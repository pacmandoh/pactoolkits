# PacToolkits API

`apps/api-asp`（`PacToolkits.Api`）是站点业务 HTTP 宿主：鉴权、健康检查、变更流、域用例路由。业务数据走 Application 与 Infrastructure 访问 PostgreSQL。HTTP 按**用例级命令**暴露，不按 Repository 方法机械映射

## 职责边界

| 层                        | 做什么                                                                     | 不做什么                           |
| ------------------------- | -------------------------------------------------------------------------- | ---------------------------------- |
| `apps/api-asp`            | HTTP、鉴权/授权、限流、ProblemDetails、审计字段、LISTEN 写入 SSE、端点装配 | 不写 SQL、不做业务计算             |
| `packages/application`    | 与 Desktop 共用的用例与访问策略                                            | 不引用 ASP.NET / Avalonia / Npgsql |
| `packages/infrastructure` | Pg 连接与仓储                                                              | 不定义 HTTP 协议                   |

依赖方向：API 依赖 Application 与 Infrastructure（见 [layering.md](./layering.md)）

## 鉴权与授权

```text
POST /v1/auth/token   Header X-Api-Key，换短期 Bearer JWT
POST /v1/auth/unlock/verify  Bearer write；敏感操作口令
业务路由               Header Authorization: Bearer <jwt>
GET  /health          匿名探活；仅 status ok/unavailable
GET  /v1/system/info  Bearer system.status；product、apiVersion、contractVersion
GET  /v1/system/status  Bearer system.status；database 与 schema 诊断
GET  /v1/dashboard/*  Bearer read；snapshot / transactions / trends / entries / abnormal
GET  /v1/catalog/*    Bearer read；client-ids / drug-ids / drugs/specs|quantity|deprecated
GET/PUT/DELETE /v1/drugs*  Bearer read|write；检索、保存、删除、主键修复
POST /v1/trace-codes/*  Bearer write；check-existing / submit
GET/POST /v1/inventory/*  Bearer read|write；库存分页，以及批量编辑与改派
GET/POST /v1/msfx/*       Bearer read|write；看板、游标、映射、注入、AutoRun 入库与跑锁（码上放心 HTTP 由 Desktop 直连）
POST /v1/injector/*       Bearer read|write；贴码事务与仓库任务
```

- **客户端**：`Auth:Clients` 按 id 配置；JWT `sub` / `client_id` 为稳定 client id（不是数组下标）。ClientId 以 ASCII 字母或数字起头，其后可为字母/数字/`._-`，不得含空白。Desktop 与 Agents 用不同 client：Desktop 存 `PacApi.ApiKey`；Agents 用独立换票客户端（惯例 id `agents`），Desktop 只存 `PacApi.AgentsApiKey`，勿把 Desktop Key 传给 Host
- **API Key**：服务端只存 `ApiKeyHash`（SHA-256 hex）；明文仅创建时交给客户端；同一散列不得分给多个 ClientId
- **JWT**：HMAC-SHA256；`Auth:Jwt:SigningKey` 变更后须**重启**进程（不支持运行中轮换密钥）
- **Scope / Policy**：`read` / `write` / `system.status`；端点 `.RequireAuthorization(...)`；配置出现未知 scope 则启动失败
- **Clients 启动校验**：Production 至少要有一个 Enabled client（合法 hash，且至少一个已知 scope）；`dev` 等非 Production 允许空 `Clients`
- **换票**：失败统一 401；不区分 Key 不存在 / 错误 / 已禁用；日志不记明文 Key
- **换票限流**：按 `RemoteIpAddress` 固定窗 30 次/分钟。多终端走同一台机器转发或 NAT 出口时共用额度
- **敏感操作口令**：`Auth:UnlockPasswordHash`（SHA-256 hex）；`POST /v1/auth/unlock/verify`（`write`）204 通过，未配置 `unlock_not_configured`，口令错 `unlock_mismatch`（400）。明文不进配置与请求日志
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
- 状态码：参数 400、未认证/无权限 401/403、不存在 404、状态冲突 409、限流 429、库或 schema 不可用 503
- 药品保存业务成功或 Blocked\* 返回 200，body 带 `outcome`
- OCC（保存、删除、主键修复）409 带 `currentVersion`；删除须带 query `expectedVersion`
- 删除时若被追溯码池、执行事务或码上放心映射引用：409（`code=conflict`），无 `currentVersion`
- 纠错预览计入码上放心映射；改主键时映射改到目标规格。单盒数量变更拦截只看追溯码池与执行事务
- 库存批量编辑若有冲突：HTTP 409 ProblemDetails（`code=conflict`），扩展字段 `conflicts` 为冲突行；Desktop 捕获 `PacApiConflictException` 转成批结果
- 写命令（POST/PUT/DELETE）要求头 `X-Command-Id`（非空 UUID）
- 会改库的写走持久化 `ICommandDedup`（`PgCommandDedup`，表 `api_command_dedup`）。Claim、业务写与 Complete 同一库事务；失败回滚后可同 CommandId 重试
- 身份是 clientId、operation、commandId；digest 对不上为 PayloadMismatch
- 查重、预览类 POST 需要 CommandId，但不 Complete。`MemoryCommandDedup` 只给测试用，不算业务写幂等
- 扫码 `submit` 与库存批量编辑、按码改派为用例级事务：插入与流水同提交；批量冲突时本批不落库
- Desktop `PacApiClient`：Resilience 与 401 换票重放仅用于 GET/HEAD；写命令不重试、不因 401 重放。401 且 Bearer 对应当前缓存票时清票；换票失败后缓存为空；显式 `CommandId` 不得为 `Guid.Empty`
- `ApiProblems` 构造业务 ProblemDetails（含 `TryGetCommandId`）；换票失败多为框架最小 401；换票限流 429 带统一 `ApiProblem` 与 `Retry-After`
- 生产不返回内部路径与敏感配置

## 变更流

数据变更用 `pg_notify('pactoolkits_change', topic)`

Desktop：变更水位用 `ApiChangeWatermark`（SSE 与 watermarks）。Dashboard、药品目录、药品索引、扫码、库存、MSFX 库侧同步分别走 `ApiDashboard`、`ApiLookupCatalog`、`ApiDrugIndex`、`ApiScanCode`、`ApiInventory`、`ApiSync`。AutoRun 入库与跑锁走 `ApiMsfxPull` / `ApiMsfxIngest` 等 HTTP 适配；码上放心 HTTP 由 Desktop `MsfxApiClient` 直连。Shell 可用性走 `IApiAvailabilityService`（探测 `/v1/system/info` 与 `/v1/system/status`）。Settings 站点业务项（MSFX 接入凭据、客户端别名、追溯码规则）与码上放心 HTTP 在 Desktop 本地配置；别名来源走 `GET /v1/catalog/client-ids`

Desktop 侧 `PacApiClient`（`Services/Infrastructure/Api/`）用 `IHttpClientFactory` 注册四类 HttpClient：换票（短超时、无 JWT）、普通 API（短超时、JWT、仅 GET 走 Resilience）、可用性探测（短超时、JWT、无 Resilience）、SSE（长连接与 JWT）。请求带 W3C `traceparent`（客户端 span 名 `pacapi.http`）。401 且 Bearer 对应当前缓存票时清票；GET/HEAD 换票后重放（并发换票进锁复用，不连续请求 `/token`）；写命令不重放。共享 HTTP DTO 在 `packages/application/DTOs/Api/`

命名（同目录 `Services/Infrastructure/Api/`）：

| 前缀      | 职责                                                                               | 例                                                  |
| --------- | ---------------------------------------------------------------------------------- | --------------------------------------------------- |
| `PacApi*` | 连 `PacToolkits.Api` 宿主的传输与协议：客户端、选项、异常、JWT/trace、协议门禁、DI | `PacApiClient`、`PacApiOptions`、`PacApiException`  |
| `Api*`    | 用 `PacApiClient` 实现 Application 抽象的域适配，以及 Shell 可用性探测             | `ApiDashboard`、`ApiSync`、`ApiAvailabilityService` |

配置字段在 AppConfig 的 `PacApi`（设置页持久化）；DTO 放 `DTOs/Api/`（HTTP 契约，不绑 Desktop 类名前缀）

异常约定：

- `GetJsonAsync` / `PostJsonAsync` / `PutJsonAsync` / `DeleteAsync`、`EnsureSuccessAsync`、换票：非 2xx 或不可达时抛 `PacApiException`（`code`、`traceId`、可选 `currentVersion`；不可达 `transport`；超时 `timeout`）
- HTTP 409 为 `PacApiConflictException`（可选 `currentVersion`；库存批量 OCC 另含 `conflicts`）
- 写方法带非空 `X-Command-Id`；业务 JSON、错误正文与换票响应受正文大小上限（超出 `response_too_large`）
- 调用方取消原样抛出；超时与断网为 `PacApiException`
- 裸 `SendAsync`、`SendSseAsync` 返回 `HttpResponseMessage`，由调用方 `EnsureSuccessAsync` 或自读状态（如 `ApiChangeWatermark`）
- 不向外抛裸 `HttpRequestException`

`ApiChangeWatermark` 为 Desktop 唯一 `IChangeWatermarkService`（SSE 与 watermarks）。SSE 的 `ready`（含重连）与 `change` 都会再 GET watermarks 补 version；第一次见到的 topic 只有 `change` 才刷页，避免冷启动连环刷新。PacAPI 配置变更时 `Reset` 重绑 SSE 与本地水位，已配置则按 change 语义刷当前 topic。唤醒容量为 1，在锁内合并。`TopicChanged` 按订阅者隔离，单页异常不拖死其它 topic。消费变更的页面需要突发合并、编辑中暂缓刷新、Stale、恢复后自动刷新（见 [desktop-state.md](./desktop-state.md)）

**API 侧**：

```text
Pg NOTIFY
  API PostgresNotifyListener 写入 ChangeBus
  SSE  GET /v1/changes/stream
  GET  /v1/changes/watermarks
```

- SSE：`ready`、`change`、`heartbeat`；单 `client_id` 最多 2 条并发流；订阅通道有界，落后时丢旧 topic
- LISTEN 侧按 topic 记 pending，短合并窗后再 `Publish`（NOTIFY 可合并或丢弃；**version 以 watermark 为准**）
- SSE 在 JWT `exp` 时由服务端关闭；客户端换票后重连，并 GET watermarks 补偿（version 以 watermark 为准，不以 SSE 推送单独为准）
- `Changes:ListenEnabled`：是否启 LISTEN（测试可关）

## 审计

请求日志至少含：W3C `traceId`、`spanId`（若有）、每请求 `requestId`（`TraceIdentifier`）、`clientId`（若有）、方法与路径、HTTP 状态、耗时。禁止记录 API Key、JWT、数据库密码、完整连接串

## 部署边界

- Kestrel 默认只听本机或受控内网（如 `127.0.0.1:5080`）
- 公网只走 **HTTPS** 反向代理；Forwarded Headers 仅信任环回（或部署时显式写入的 `KnownProxies` / `KnownIPNetworks`）
- 进程切换在 API 机上用 `apps/api-asp/scripts/deploy.sh`；发布 CI 不重启服务
- Nginx 必须用 `$remote_addr` **覆盖** `X-Forwarded-For`，禁止 `$proxy_add_x_forwarded_for`（否则客户端可伪造来源 IP，绕过按来源聚合的限流）
- 代理与应用日志均不记录认证头

本地密钥与 Nginx 模板见 [API README](../../apps/api-asp/README.md)

## 约束

- 药品主键是 query `drugId=` 与 `spec=`（均可含 `/`）
- HTTP 按用例级命令暴露，不按 Repository / SQL 机械映射
- 需要事务的写在 API 内完整提交；客户端不跨请求拼事务
- 实时变更走 SSE 与 watermark；常规定时轮询不是主路径
- Agents / AHK 只走专用业务 API，无通用 `execute-sql`
- 关键写具备幂等：CommandId + 持久化去重，或可靠业务键；不以进程内存作唯一去重
- 同一域一条数据路径，无双写

### Desktop 业务数据

- 变更水位、Dashboard、药品目录、药品索引、扫码入库、库存、MSFX 库侧同步（`ApiSync`）与 AutoRun 入库走 PacAPI
- Shell 连接与刷新门禁跟 `IApiAvailabilityService` / `ConnectionView`
- Settings 业务配置与码上放心 HTTP 在 Desktop 直连；不走 PacAPI 落库
- 页面连接与可用性见 [desktop-state.md](./desktop-state.md)

### 配置入口

| 节                        | 用途                                                    |
| ------------------------- | ------------------------------------------------------- |
| `Auth:Clients`            | 按 id 配置的客户端、Key 散列、Enabled、Scopes           |
| `Auth:UnlockPasswordHash` | 敏感操作口令 SHA-256 hex；空则解锁校验拒绝              |
| `Auth:Jwt`                | Issuer / Audience / SigningKey / TTL                    |
| `Postgres`                | 服务端库连接（环境变量覆盖密码）                        |
| `SchemaBounds`            | schema 闭区间；同时约束 `/health` 与走 `IDb` 的业务读写 |
| `Changes`                 | `ListenEnabled` 等变更流宿主开关                        |

DI 组装入口：`AddPacToolkitsApi`（`Hosting/ServiceRegistration.cs`）。注册全量 Application 与 Infrastructure。API 宿主用 Empty/Memory 适配 Desktop 专属 Store；`IMsfxApiClient` 为 Unsupported（码上放心 HTTP 不走 API 宿主）。域用例 HTTP 按域挂到已注册服务

### Desktop 侧 PacAPI（设置页配置）

Desktop 访问 API 的地址与密钥在设置的「连接设置」写入 `AppConfigStore`（与码上放心 API 凭证同一套持久化）。保存后立刻让 `PacApiClient` 用上新配置，并 `Reset` 变更水位与 Shell 可用性探测

| 字段           | 含义                                                      |
| -------------- | --------------------------------------------------------- |
| `BaseUrl`      | API 根地址（绝对 URI；内网与本机可用 HTTP，公网须 HTTPS） |
| `ApiKey`       | Desktop 换票用明文 Key                                    |
| `AgentsApiKey` | Agents 换票明文 Key；对应环境 `PAC_API_KEY`               |
| `HeaderName`   | 可选；默认 `X-Api-Key`（设置页不暴露）                    |

规则：

- `BaseUrl` 与 `ApiKey` 都空：合法，表示未配置；此时不探测 PacAPI，`ConnectionView` 为 `NotConfigured`，变更流不启动
- 只配一侧：保存失败
- 两侧都有：须为绝对 URI；内网与本机可用 HTTP，公网须 HTTPS；`BaseUrl` 不得带 query、fragment、userinfo
- 保存后立刻生效、`Reset` 水位并立刻 `Probe`；换票 401/403 后可用性挂起自动探测，改密钥抬 `ConfigEpoch` 再探

未配置是配置态，不是 API 探测态：`IApiAvailabilityService.IsConfigured` 为假时不发 HTTP，也不把「未配置」写成 `ApiAvailabilityState`

App DI 在 `IReleaseVersionService` 之后注册 `IPacApiContractGate`。已配置时，业务请求进 Jwt Handler 前会先 GET `/v1/system/info`，用 Desktop 清单的 `minApiContract`、`maxApiContract` 对照 API 的 `contractVersion`；对不上就拦下业务请求。设置页改密钥后会 `Reset` 协议检查结果
