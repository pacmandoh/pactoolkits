# PacToolkits API (ASP.NET)

`apps/api-asp` 的 HTTP 宿主（`PacToolkits.Api`）：具名客户端用 API Key 散列换 JWT；授权、健康检查（PostgreSQL 与 schema）、LISTEN 写入 SSE 变更流；域路由挂到 Application 与 Infrastructure。

架构与迁移原则见 [docs/architecture/api.md](../../docs/architecture/api.md)。

## 布局

```text
apps/api-asp/
  README.md
  scripts/          # 本地密钥与启动脚本（不进发布物）
  src/              # 工程与源码
```

## 鉴权

```text
POST /v1/auth/token   Header X-Api-Key: <plaintext>，换 JWT
GET  /v1/ping         Authorization: Bearer <jwt>（需 scope read）
GET  /v1/system/info  Bearer（需 system.status；含 contractVersion）
GET  /v1/system/status Bearer（需 system.status；database 与 schema 诊断）
GET  /v1/changes/*    Bearer（需 scope read）
GET  /health          匿名探活；仅 status；200=可用、503=不可用
```

| 项 | 说明 |
|----|------|
| Clients | `Auth:Clients:<id>`：`ApiKeyHash`（SHA-256 hex）、`Enabled`、`Scopes` |
| Key | 明文只在客户端；服务端只比散列 |
| JWT | HMAC-SHA256；`Auth:Jwt:SigningKey` ≥32；**改密钥须重启** |
| Policy | `read` / `write` / `system.status` |
| Header | `Auth:HeaderName`（默认 `X-Api-Key`） |

## 本地开发

密钥写在 **`apps/api-asp/.env.asp`**（gitignore）：

```bash
./apps/api-asp/scripts/gen-dev-secrets.sh
# 写密钥、后台起 API，并校验 health、换票与 ping
./apps/api-asp/scripts/wire-local.sh
```

`.env.asp` 含：

| 键 | 用途 |
|----|------|
| `PAC_API_KEY` | 本地 curl 换票明文 Key（API 进程不读明文，只读散列） |
| `Auth__Clients__dev__ApiKeyHash` | 服务端比对的 Key 散列 |
| `Auth__Jwt__SigningKey` | JWT HMAC |
| `Postgres__Host` / `Port` / `Database` / `Username` / `Password` | 覆盖 `appsettings` 的 Postgres 节；`Password` 须本机手填 |

`gen-dev-secrets.sh` 与 `wire-local.sh` 会复用已有 Auth，并补齐缺省 Postgres（与 `appsettings.json` 对齐）；`--force` 只换 Auth，Postgres 手改仍保留。  
配置覆盖顺序：`appsettings.json`，再 `appsettings.{Environment}.json`，再环境变量。  
默认监听 `http://127.0.0.1:5080`。

## 生产部署

```bash
dotnet publish apps/api-asp/src/PacToolkits.Api.csproj -c Release -o /opt/pactoolkits/api --no-restore
```

生产密钥：**`/etc/pactoolkits/.env.asp`**（`chmod 600`），systemd `EnvironmentFile=`。  
公网只经 Nginx HTTPS 反代本机 5080；勿把明文 Key 写进服务端配置。

```bash
# /etc/pactoolkits/.env.asp（示例）
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
Auth__Clients__site-a__ApiKeyHash=<sha256-hex>
Auth__Clients__site-a__Enabled=true
Auth__Clients__site-a__Scopes__0=read
Auth__Clients__site-a__Scopes__1=write
Auth__Clients__site-a__Scopes__2=system.status
Auth__Jwt__SigningKey=<≥32 random>
Postgres__Password=<secret>
```

生成散列：`printf '%s' "$PLAINTEXT_KEY" | openssl dgst -sha256 -hex`

## 路由

| 方法 | 路径 | 鉴权 | 说明 |
|------|------|------|------|
| GET | `/health` | 否 | 匿名探活，仅 `status`；SchemaBounds 同时拦业务 IDb |
| POST | `/v1/auth/token` | API Key（限流） | 换 JWT |
| GET | `/v1/ping` | JWT `read` | 校验 JWT 与 read scope |
| GET | `/v1/system/info` | JWT `system.status` | 静态：product、构建用 apiVersion、协议 SemVer contractVersion |
| GET | `/v1/system/status` | JWT `system.status` | 连库与 schema 诊断 |
| GET | `/v1/changes/watermarks` | JWT `read` | 变更水位快照 |
| GET | `/v1/changes/stream` | JWT `read` | SSE 变更流；JWT `exp` 时服务端关闭 |

DI 组装入口：`AddPacToolkitsApi`。注册全量 Application 与 Infrastructure；Desktop 专属 Store 与 MSFX 外呼由宿主适配（无本地配置文件；MSFX HTTP 未接）。

版本与 schema：`export-version.sh` 从清单 `components.api`（version、contractVersion、minDbSchema、maxDbSchema）写出 `Version.g.props`、`ApiContract.g.cs`、`SchemaBounds.g.cs` 以及 appsettings 的 SchemaBounds。

## 测试

自动化：

```bash
dotnet build tests/PacToolkits.Api.Tests/PacToolkits.Api.Tests.csproj -c Release --no-restore -v minimal
dotnet test tests/PacToolkits.Api.Tests/PacToolkits.Api.Tests.csproj -c Release --no-restore --no-build -v minimal
```

连本机 PostgreSQL 的手动回归脚本（鉴权、变更流；有 psql 时校验 NOTIFY）：

```bash
./apps/api-asp/scripts/run-api-manual-regression.sh
# API 已在跑时可：
./apps/api-asp/scripts/run-api-manual-regression.sh --skip-wire
```
