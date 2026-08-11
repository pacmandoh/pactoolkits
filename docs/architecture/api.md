# PacToolkits API

`apps/api-asp`（`PacToolkits.Api`）是站点业务 HTTP 宿主：鉴权、健康检查、变更流、域用例路由。业务数据经 Application 与 Infrastructure 访问 PostgreSQL。HTTP 按**用例级命令**暴露，不按 Repository 方法机械映射。

**目标**：Desktop 业务数据与变更消费只经本 API，不再依赖 `packages/infrastructure` / 本机 Pg。

## 职责边界

| 层                        | 做什么                                                                     | 不做什么                           |
| ------------------------- | -------------------------------------------------------------------------- | ---------------------------------- |
| `apps/api-asp`            | HTTP、鉴权/授权、限流、ProblemDetails、审计字段、LISTEN 写入 SSE、端点装配 | 不写 SQL、不做业务计算             |
| `packages/application`    | 与 Desktop 共用的用例与访问策略                                            | 不引用 ASP.NET / Avalonia / Npgsql |
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
- **Clients 启动校验**：Production 至少要有一个 Enabled client（合法 hash，且至少一个已知 scope）；`dev` 等非 Production 允许空 `Clients`
- **换票**：失败统一 401；不区分 Key 不存在 / 错误 / 已禁用；日志不记明文 Key
- **换票限流**：按 `RemoteIpAddress` 固定窗 30 次/分钟。多终端经同一机器转发或 NAT 出口时共用额度；上线前用真实中转拓扑验证（约 10 台同时启动换票，观察是否 429）
- **协议版本**：`GET /v1/system/info` 返回 `contractVersion`（协议 SemVer，来自清单 `components.api.contractVersion`，export 为 `ApiContract.Version`）。客户端用该字段判断协议是否兼容；`apiVersion` 只标识进程构建（清单 `components.api.version`），不参与协议判断

## 健康与错误

- `/health`：匿名探活，只返回 `status`（`ok` 或 `unavailable`）；进程、PostgreSQL 与 `SchemaBounds` 通过为 **200**，否则 **503**；探针短缓存并 single-flight（约 3s）；不暴露连接串、schema 版本、账号
- `GET /v1/system/status`（JWT `system.status`）：`database`、`schema`、`schemaVersion`、`reason`
- `SchemaBounds`：
  - 启动时校验 `MinDbSchema` / `MaxDbSchema` 为发布用 `X.Y.Z`，且 `min <= max`
  - `IDbAccessGuard` 默认 `schema_bounds:not_ready`；`SchemaBoundsAccessHost` 在接受请求前完成首检
  - schema 兼容才放行业务库访问；schema 不合或库不可达均为 **503**
  - 中间件拦默认 `/v1` 业务路由（含 `changes/*`）；不拦 `/health`、换票、`/v1/ping`、`/v1/system/*`
- 错误体：业务路径 ProblemDetails 含 `status` / `code` / `title` / `detail` / `traceId`；OCC 另带 `currentVersion`；限流或短暂不可用可带 `Retry-After`
- 状态码：参数 400、未认证/无权限 401/403、不存在 404、OCC/状态冲突 409、限流 429、库或 schema 不可用 503
- 写命令（POST/PUT/DELETE）要求头 `X-Command-Id`（非空 UUID）
- 幂等：可靠业务键，或持久化 `ICommandDedup`（Claim、Complete、Release）。命令身份为 `clientId`、`operation`、`commandId`；`requestDigest` 不一致为 `PayloadMismatch`。宿主默认不注册 `ICommandDedup`；`MemoryCommandDedup` 仅测试或显式注入，不满足业务写幂等
- Desktop `PacApiClient`：Resilience 与 401 换票重放仅用于 GET/HEAD；写命令不重试、不因 401 重放。401 且 Bearer 对应当前缓存票时清票；换票失败后缓存为空；显式 `CommandId` 不得为 `Guid.Empty`
- `ApiProblems` 构造业务 ProblemDetails（含 `TryGetCommandId`）；换票失败多为框架最小 401；换票限流 429 带统一 `ApiProblem` 与 `Retry-After`
- 生产不返回内部路径与敏感配置

## 变更流

数据变更经 `pg_notify('pactoolkits_change', topic)`。

**Desktop 现状**：业务页与变更流经本机 Infrastructure（`ChangeWatermarkService` 的 LISTEN 与 watermark 轮询），不经 API SSE。Desktop 与 API 可同时 LISTEN 同一 channel（PostgreSQL 允许多会话）。

**Desktop 目标**：业务查询/写与变更消费只经 API；不再引用 Infrastructure、不再本机 LISTEN。同一域查询/写与变更消费须走同一路径。

Desktop 侧 `PacApiClient`（`Services/Infrastructure/Api/`）经 `IHttpClientFactory` 注册三类命名客户端：换票（短超时、无 JWT）、普通 API（短超时、JWT、仅 GET 走 Resilience）、SSE（长连接与 JWT）。出站带 W3C `traceparent`（客户端 span 名 `pacapi.http`）。401 且 Bearer 对应当前缓存票时清票；GET/HEAD 换票后重放（并发换票进锁复用，不连打 `/token`）；写命令不重放。共享 HTTP DTO 在 `packages/application/DTOs/Api/`。

异常约定：

- `GetJsonAsync` / `PostJsonAsync` / `PutJsonAsync` / `DeleteAsync`、`EnsureSuccessAsync`、换票：非 2xx 或不可达时抛 `PacApiException`（`code`、`traceId`、可选 `currentVersion`；不可达 `transport`；超时 `timeout`）
- HTTP 409 为 `PacApiConflictException`（含 `currentVersion`）
- 写方法带非空 `X-Command-Id`；业务 JSON、错误正文与换票响应受正文大小上限（超出 `response_too_large`）
- 调用方取消原样抛出；超时与断网为 `PacApiException`
- 裸 `SendAsync`、`SendSseAsync` 返回 `HttpResponseMessage`，由调用方 `EnsureSuccessAsync` 或自读状态（如 `ApiChangeWatermark`）
- 不向外抛裸 `HttpRequestException`

`ApiChangeWatermark` 实现经 SSE + watermarks 的 `IChangeWatermarkService`；Desktop DI 注册的是本机 `ChangeWatermarkService`。SSE 的 `ready`（含重连）与 `change` 都会再 GET watermarks 补 version；第一次见到的 topic 只有 `change` 才刷页，避免冷启动连环刷新。唤醒容量为 1，在锁内合并。`TopicChanged` 按订阅者隔离，单页异常不拖死其它 topic。经 PacApi 消费变更的页面须保留突发合并、编辑中暂缓刷新、Stale、恢复后自动刷新（见 [desktop-state.md](./desktop-state.md)）。

**API 侧**：

```text
Pg NOTIFY
  API PostgresNotifyListener 写入 ChangeBus
  SSE  GET /v1/changes/stream
  GET  /v1/changes/watermarks
```

- SSE：`ready`、`change`、`heartbeat`；单 `client_id` 最多 2 条并发流；订阅通道有界，落后时丢旧 topic
- LISTEN 侧按 topic 记 pending，经短合并窗再 `Publish`（NOTIFY 可合并或丢弃；**version 以 watermark 为准**）
- SSE 在 JWT `exp` 时由服务端关闭；客户端换票后重连，并 GET watermarks 补偿（勿只信 SSE 推送）
- `Changes:ListenEnabled`：是否启 LISTEN（测试可关）

## 审计

请求日志至少含：W3C `traceId`、`spanId`（若有）、每请求 `requestId`（`TraceIdentifier`）、`clientId`（若有）、方法与路径、HTTP 状态、耗时。禁止记录 API Key、JWT、数据库密码、完整连接串。

## 部署边界

- Kestrel 默认只听本机或受控内网（如 `127.0.0.1:5080`）
- 公网只经 **HTTPS** 反向代理；Forwarded Headers 仅信任环回（或部署时显式写入的 `KnownProxies` / `KnownIPNetworks`）
- Nginx 必须用 `$remote_addr` **覆盖** `X-Forwarded-For`，禁止 `$proxy_add_x_forwarded_for`（否则客户端可伪造来源 IP，绕过按来源聚合的限流）
- 代理与应用日志均不记录认证头

本地密钥、Nginx 片段与换票限流手工验证见 [API README](../../apps/api-asp/README.md)。

## 架构红线

1. 不按 Repo / SQL 机械暴露 HTTP
2. 需要事务的写操作在 API 内完整提交（客户端不跨请求拼事务）
3. 实时变更走 SSE 与 watermark；不以常规定时轮询作主路径
4. Agents / AHK 禁止通用 `execute-sql`；只走专用业务 API
5. 关键写具备幂等（CommandId + 持久化去重，或可靠业务键）；禁止只靠进程内存
6. 同一域只保留一条数据路径；禁止双写

### Desktop 业务数据

- **目标**：Desktop 不依赖 Infrastructure；业务数据与变更流只经 API，并保留页面可用性三层（见 [desktop-state.md](./desktop-state.md)）
- **现状**：页面查询、写库与变更 LISTEN 经本机 Infrastructure；经 PacApi 的域须查询/写与变更消费同侧

### 配置入口

| 节             | 用途                                                    |
| -------------- | ------------------------------------------------------- |
| `Auth:Clients` | 具名客户端、Key 散列、Enabled、Scopes                   |
| `Auth:Jwt`     | Issuer / Audience / SigningKey / TTL                    |
| `Postgres`     | 服务端库连接（环境变量覆盖密码）                        |
| `SchemaBounds` | schema 闭区间；同时约束 `/health` 与经 `IDb` 的业务读写 |
| `Changes`      | `ListenEnabled` 等变更流宿主开关                        |

DI 组装入口：`AddPacToolkitsApi`（`Hosting/ServiceRegistration.cs`）。注册全量 Application 与 Infrastructure；Desktop 专属 Store 与 MSFX 客户端由 API 宿主适配（无 Desktop 配置文件；MSFX 外呼未接）。域用例 HTTP 按域挂到已注册服务。

### Desktop 侧 PacApi（部署注入）

Desktop 访问 API 的密钥与地址由环境变量或受保护配置提供，**不要**写入普通 `AppConfigStore`。

| 变量                             | 含义                                         |
| -------------------------------- | -------------------------------------------- |
| `PACTOOLKITS_PacApi__BaseUrl`    | API 根地址（绝对 URI；非 loopback 须 HTTPS） |
| `PACTOOLKITS_PacApi__ApiKey`     | 换票用明文 Key（仅部署侧持有）               |
| `PACTOOLKITS_PacApi__HeaderName` | 可选；默认 `X-Api-Key`                       |

Options 规则：

- `BaseUrl` 与 `ApiKey` 都空：校验通过，表示未启用 PacApi
- 只配一侧：失败
- 两侧都有：须为绝对 URI；非 loopback 须 HTTPS；`HeaderName` 须是合法 HTTP field-name；`BaseUrl` 不得带 query、fragment、userinfo

`PacApiClient` 与 `PacApiContractGate` 启动时读 `IOptions<PacApiOptions>` 快照；改环境变量或配置文件不会热更新，须重启 Desktop。

App DI 在 `IReleaseVersionService` 之后注册 `IPacApiContractGate`。已配置时，业务请求进 Jwt Handler 前会先 GET `/v1/system/info`，用 Desktop 清单的 `minApiContract`、`maxApiContract` 对照 API 的 `contractVersion`；对不上就拦下业务请求。
