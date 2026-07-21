# PacToolkits Desktop（Avalonia）

当前正式桌面客户端。业务页面、配置、更新与诊断入口；通过 `packages/application` 调用用例，数据库实现在 `packages/infrastructure`。Agents 启停由 `AgentsRuntime` 控制（Host + Injector），见 [Agents 运行时架构](../../docs/architecture/agents.md)。

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
- [Desktop 状态模型](../../docs/architecture/desktop-state.md)
- [Agents 运行时](../../docs/architecture/agents.md)
- [发布与更新](../../docs/operations/release-flow.md)
