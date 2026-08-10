# PacToolkits API

`apps/api-asp`（`PacToolkits.Api`）是站点业务 HTTP 宿主：鉴权、健康检查、变更流、域用例路由。业务数据经 Application 与 Infrastructure 访问 PostgreSQL。Desktop 与 Agents **最终经本 API 读写业务数据**，不长期直连 Pg。

迁移按 **用例级 HTTP 命令** 推进，不是把每个 Repository 方法包成 HTTP。

## 职责边界

| 层                        | 做什么                                                                     | 不做什么                           |
| ------------------------- | -------------------------------------------------------------------------- | ---------------------------------- |
| `apps/api-asp`            | HTTP、鉴权/授权、限流、ProblemDetails、审计字段、LISTEN 写入 SSE、端点装配 | 不写 SQL、不做业务计算             |
| `packages/application`    | 与 Desktop 共用的用例与门禁                                                | 不引用 ASP.NET / Avalonia / Npgsql |
| `packages/infrastructure` | Pg 连接与仓储                                                              | 不定义 HTTP 协议                   |

依赖方向：API 依赖 Application 与 Infrastructure（见 [layering.md](./layering.md)）。

## 鉴权与授权

```text
POST /v1/auth/token   Header X-Api-Key，换短期 Bearer JWT
业务路由               Header Authorization: Bearer <jwt>
GET  /health          匿名探活；仅 status ok/unavailable
GET  /v1/system/info  Bearer system.status；product、apiVersion、contractVersion
GET  /v1/system/status  Bearer system.status；database 与 schema 诊断
```

- **客户端**：`Auth:Clients` 具名条目；JWT `sub` / `client_id` 为稳定 client id（不是数组下标）。ClientId 以 ASCII 字母或数字起头，其后可为字母/数字/`._-`，不得含空白
- **API Key**：服务端只存 `ApiKeyHash`（SHA-256 hex）；明文仅创建时交给客户端；同一散列不得分给多个 ClientId
- **JWT**：HMAC-SHA256；`Auth:Jwt:SigningKey` 变更后须**重启**进程（不支持运行中轮换密钥）
- **Scope / Policy**：`read` / `write` / `system.status`；端点 `.RequireAuthorization(...)`；配置出现未知 scope 则启动失败
- **Clients 启动校验**：Production 至少要有一个 Enabled client（合法 hash + 至少一个已知 scope）；`dev` 等非 Production 允许空 `Clients`
- **换票**：失败统一 401；不区分 Key 不存在 / 错误 / 已禁用；日志不记明文 Key
- **换票限流**：按 `RemoteIpAddress` 固定窗 30 次/分钟。多终端经同一机器转发或 NAT 出口时共用额度；上线前用真实中转拓扑验证（约 10 台同时启动换票，观察是否 429）
- **协议版本**：`GET /v1/system/info` 返回 `contractVersion`（协议 SemVer，来自清单 `components.api.contractVersion`，export 为 `ApiContract.Version`）。客户端用该字段判断协议是否兼容；`apiVersion` 只标识进程构建（清单 `components.api.version`），不参与协议判断

## 健康与错误

- `/health`：匿名探活，只返回 `status`（`ok` / `unavailable`）；进程、PostgreSQL 与 `SchemaBounds` 通过为 **200**，否则 **503**；探针短缓存 + single-flight（约 3s）；不暴露连接串、schema 版本、账号
- `GET /v1/system/status`（JWT `system.status`）：`database`、`schema`、`schemaVersion`、`reason`
- `SchemaBounds`：
  - 启动时校验 `MinDbSchema` / `MaxDbSchema` 为发布用 `X.Y.Z`，且 `min <= max`
  - `IDbAccessGuard` 默认 `schema_bounds:not_ready`；`SchemaBoundsAccessHost` 在接受请求前完成首检
  - schema 兼容才放行数据面；schema 不合或库不可达均为 **503**
  - 中间件拦默认 `/v1` 业务路由（含 `changes/*`）；不拦 `/health`、换票、`/v1/ping`、`/v1/system/*`
- 错误体：业务路径经 ProblemDetails 时常含 `status` / `code` / `title` / `traceId`；换票失败多为框架最小 401；换票限流 429 未必带统一 ApiProblem
- 生产不返回内部路径与敏感配置

## 变更流

数据变更仍 `pg_notify('pactoolkits_change', topic)`。

**当前 Desktop（过渡）**：业务页与变更流仍走本机 Infrastructure：`ChangeWatermarkService` 做 LISTEN，并用 watermark 轮询，**不经过** API。Desktop 与 API 可同时 LISTEN 同一 channel（Pg 允许多会话）。

预留实现（`#if false`，不编译、无 DI）：`ApiChangeWatermark` / `PacApiClient`，供日后 Desktop 改走 API SSE。配置另行约定，不沿用现 Desktop.config。SSE `ready`（含重连）与 `change` 均 GET watermarks 补 version；`ready` 不对首见 topic 刷页，避免冷启动连环刷新。

**API 侧（已实现，可单独在本机验证）**：

```text
Pg NOTIFY
  API PostgresNotifyListener 写入 ChangeBus
  SSE  GET /v1/changes/stream
  GET  /v1/changes/watermarks
```

- SSE：`ready` / `change` / `heartbeat`；单 `client_id` 最多 2 条并发流；订阅通道有界，落后时丢旧 topic
- LISTEN 侧按 topic 记 pending + 短合并窗再 `Publish`（NOTIFY 可合并或丢弃；**version 以 watermark 为准**）
- SSE 在 JWT `exp` 时由服务端关闭；客户端换票后重连，并 GET watermarks 补偿（勿只信 SSE 推送）
- `Changes:ListenEnabled`：是否启 LISTEN（测试可关）

Desktop 将来改走 API 时：页面仍须保留突发合并、编辑中暂缓刷新、Stale、恢复后自动刷新。

## 审计

请求日志至少含：`traceId`、`clientId`（若有）、方法与路径、HTTP 状态、耗时。禁止记录 API Key、JWT、数据库密码、完整连接串。

## 部署边界

- Kestrel 默认只听本机或受控内网（如 `127.0.0.1:5080`）
- 公网只经 **HTTPS** 反向代理；Forwarded Headers 仅信任环回（或部署时显式写入的 `KnownProxies` / `KnownIPNetworks`）
- Nginx 必须用 `$remote_addr` **覆盖** `X-Forwarded-For`，禁止 `$proxy_add_x_forwarded_for`（否则客户端可伪造来源 IP，绕过按来源聚合的限流）
- 代理与应用日志均不记录认证头

本地密钥、Nginx 片段与换票限流手工验证见 [API README](../../apps/api-asp/README.md)。

## 迁移原则（其余域）

### 红线

1. 不按 Repo / SQL 机械暴露 HTTP
2. 需要事务的写操作在 API 内完整提交（客户端不跨请求拼事务）
3. 实时变更走 SSE 与 watermark；不以常规定时轮询作主路径
4. Agents / AHK 禁止通用 `execute-sql`；只走专用业务 API
5. 关键写具备幂等（CommandId / 业务键）
6. 按域单路径切换；禁止双写，也不要一次砍掉全部旧路径

### Desktop 业务数据

页面查询/写库与（当前）变更 LISTEN 均经本机 Infrastructure。变更通知与域数据改走 HTTP 时按域切换；接入时保留页面可用性三层（见 [desktop-state.md](./desktop-state.md)）。

### 配置入口

| 节             | 用途                                                    |
| -------------- | ------------------------------------------------------- |
| `Auth:Clients` | 具名客户端、Key 散列、Enabled、Scopes                   |
| `Auth:Jwt`     | Issuer / Audience / SigningKey / TTL                    |
| `Postgres`     | 服务端库连接（环境变量覆盖密码）                        |
| `SchemaBounds` | schema 闭区间；同时约束 `/health` 与经 `IDb` 的业务读写 |
| `Changes`      | `ListenEnabled` 等变更流宿主开关                        |

DI 组装入口：`AddPacToolkitsApi`（`Hosting/ServiceRegistration.cs`）。注册全量 Application 与 Infrastructure；Desktop 专属 Store 与 MSFX 客户端由 API 宿主适配（无 Desktop 配置文件；MSFX 外呼未接）。域用例 HTTP 按域挂到已注册服务。
