# 包层扫描模式（Application / Infrastructure / Core）

Desktop 扫描之后，清理 `packages/` 或跨层追死代码时用。

## Application — Abstractions

| 信号 | 查 | 动作 |
|------|----|------|
| `I*Service.cs` 没有 `*Service.cs` | `rg "class YourService" packages/application/Services` | 补实现或删接口 |
| 接口方法从未调用 | 在 Application 和 Desktop 搜方法名 | 从接口和全部实现者删方法 |
| `DTOs/` 里 DTO 零引用 | `rg "YourDto" apps packages tests` | 删或并进兄弟 DTO |
| 枚举成员未用 | `rg "EnumValue" apps packages tests` | 删成员和 switch 臂 |

```bash
rg "^public interface I" packages/application/Abstractions -g "*.cs"
rg "AddSingleton<I" packages/application/Services/ServiceCollectionExtensions.cs
```

## Application — Services

| 信号 | 查 | 动作 |
|------|----|------|
| 服务方法从未调用 | 从 Desktop VM 和兄弟服务 grep | 删方法 |
| 服务与 Desktop 适配重复 | Desktop `Services/*/` 和 `packages/application/Services/` 同一条规则 | 并进 Application；删 desktop 副本 |
| 服务注入具体 repo 类型 | 应使用 Abstractions 的 `I*Repo` | 修注入；不是死代码 — 是漂移 |
| Mapping 填了下游从未读的 DTO 字段 | Repo / Service / VM 绑定链断了 | 在最早一层丢掉字段 |

```bash
# Desktop 用了这个服务吗？
rg "IYourService" apps/desktop-avalonia/src/ViewModels -g "*.cs"
# 重复逻辑迹象
rg "NormalizeClientId|ClassifyTransport" apps/desktop-avalonia/src/Services packages/application -g "*.cs"
```

## Infrastructure — Repositories

| 信号 | 查 | 动作 |
|------|----|------|
| Repo 没有 Service 调用方 | `rg "IYourRepo" packages/application/Services` 为空 | 删 repo 和注册 |
| Repo 方法从未调用 | 只在 Application Services 搜方法名 | 删方法 |
| 重复 SQL 片段 | 两个 repo 同一段 `WHERE`/`JOIN` | 抽共享私有辅助或合并查询 |
| Repo 只被 Desktop VM 用 | 架构违规 | 先把调用挪到 Service — 见 pac-architecture |

```bash
rg "FROM inventory" packages/infrastructure/Repositories -g "*.cs"  # 重复 SQL 示例
rg "IYourRepo" apps/desktop-avalonia/src -g "*.cs"  # 应该为空
```

## Core

| 信号 | 查 | 动作 |
|------|----|------|
| `packages/core/` 类型引用了 IO/UI | 禁止 — 不是死，是分层坏了 | 重构出去 |
| `SemVerRange` / `DbSchemaCompatibility` 未用 | 在 Application 和 Infrastructure grep | 真孤儿才删 |
| Record 计算属性从未读 | grep 属性名 | 删成员 |

Core 必须零依赖 — 这里确认零引用后的孤儿文件可以删。

## Agent.Contracts

| 信号 | 查 | 动作 |
|------|----|------|
| 协议类型零 C# 引用 | 查 `runtime/agents/` 和序列化路径 | 删之前问用户 |
| 协议里的 DTO 字段 | 可能协议还要用 | 除非升版本，否则推迟 |

```bash
rg "YourContractType" apps packages runtime tests
```

## 测试层

| 信号 | 查 | 动作 |
|------|----|------|
| 测试类对应已删生产类型 | 还能编过？ | 和生产代码一起删测试文件 |
| 测试辅助从未导入 | `rg "YourTestHelper" tests` | 删辅助 |
| 生产类型上仅测试用的公开 API | 方法只为测试存在 | 改 `internal` + `InternalsVisibleTo`，或测试和 API 一起删 |

```bash
rg "class YourServiceTests" tests -g "*.cs"
```

## 跨层重复迹象（P2 — 合并，不是快删）

| 迹象 | 规范位置 |
|------|----------|
| 3+ 个 VM 同一套空面板条件 | `SectionEmptyPolicy`、`SectionEmptyCopy` |
| VM 与 shell 同一套连接错误文案 | `DbTransportErrorClassifier`、`ConnectivityBannerFactory` |
| VM 与 Service 同一套归一化 | `InputNormalizer`、Application Service |
| 两个 repo 同一段 SQL | 单个 repo，或 Infrastructure 里共享查询构造 |
