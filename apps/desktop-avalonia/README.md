# PacToolkits Desktop（Avalonia）

PacToolkits Desktop 提供业务页面、配置、更新和诊断能力。ViewModel 通过 `packages/application` 调用用例；域数据走 PacAPI。`AgentsRuntime` 负责 Host 与模块的运行控制。

## 构建

```bash
cd apps/desktop-avalonia/src
dotnet build -c Release --no-restore
```

解决方案入口（仓库根）：

```bash
dotnet build PacToolkits.sln -c Release --no-restore
```

## 文档

- [模块概览](./docs/overview.md)
- [日志事件](./docs/log-events.md)
- [分层与依赖](../../docs/architecture/layering.md)
- [Desktop 目录与 namespace](../../docs/architecture/desktop-layout.md)
- [Desktop 状态模型](../../docs/architecture/desktop-state.md)
- [Agents 运行时](../../docs/architecture/agents.md)
- [发布与更新](../../docs/operations/release-flow.md)
