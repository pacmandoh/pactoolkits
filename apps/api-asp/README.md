# PacToolkits API (ASP.NET)

`apps/api-asp` 的 HTTP 宿主（`PacToolkits.Api`）：API Key → JWT 鉴权、存活与 JWT 探针。

## 布局

```text
apps/api-asp/
  README.md
  scripts/          # 本地密钥 / 启动探针（不进发布物）
  src/              # 工程与源码
```

## 鉴权

```text
POST /v1/auth/token   + Header X-Api-Key: <key>   →  { accessToken, expiresIn, … }
GET  /v1/ping         + Authorization: Bearer <jwt>
GET  /health          匿名存活
```

| 项 | 说明 |
|----|------|
| Key | `Auth:ApiKeys`；只用于换票 |
| JWT | HMAC-SHA256；`Auth:Jwt:SigningKey` ≥32 字符 |
| 有效期 | `Auth:Jwt:ExpiresMinutes`（默认 60，须 >0） |
| Header | `Auth:HeaderName`（默认 `X-Api-Key`，须非空） |

## 本地开发

密钥写在 **`apps/api-asp/.env.asp`**（gitignore；首次随机生成，其后复用）：

```bash
./apps/api-asp/scripts/gen-dev-secrets.sh
eval "$(./apps/api-asp/scripts/gen-dev-secrets.sh --export)"

# 写密钥 + 后台起 API + health/token/ping 探活
./apps/api-asp/scripts/wire-local.sh
```

配置：`appsettings.json` → `appsettings.{Environment}.json` → 环境变量。  
`.env.asp` 不由 ASP.NET 自动加载；本地需 `source` / `wire-local`，生产用 systemd `EnvironmentFile=`。  
默认监听 `http://127.0.0.1:5080`。

## 生产部署

```bash
dotnet publish apps/api-asp/src/PacToolkits.Api.csproj -c Release -o /opt/pactoolkits/api --no-restore
```

生产密钥：**`/etc/pactoolkits/.env.asp`**（`chmod 600`），systemd `EnvironmentFile=`。  
公网建议 Nginx HTTPS 反代本机 5080。

```bash
# /etc/pactoolkits/.env.asp
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
Auth__ApiKeys__0=<key>
Auth__Jwt__SigningKey=<≥32 random>
```

## 路由

| 方法 | 路径 | 鉴权 | 说明 |
|------|------|------|------|
| GET | `/health` | 否 | 存活 |
| POST | `/v1/auth/token` | API Key | 换 JWT |
| GET | `/v1/ping` | JWT | JWT 校验探针 |

路由定义在 `Endpoints/`；服务注册在 `AddPacToolkitsApi`（`Hosting/ServiceRegistration.cs`）。  
Application / Infrastructure 由域路由的 csproj 引用并在 composition root 注册。

## 测试

```bash
dotnet test tests/PacToolkits.Api.Tests/PacToolkits.Api.Tests.csproj -c Release --no-restore --no-build -v minimal
```

覆盖 health / auth / ping。
