# Desktop（Avalonia）概览

桌面端是业务操作中心：页面交互、配置、审计、更新与 Agents 运行时联动。ViewModel 经 `packages/application` 访问业务；数据库实现在 `packages/infrastructure`。

## 主要职责

- 仪表盘与总览
- 药品信息维护
- 追溯码录入与扫描
- 库存总览与库存调整
- 码上放心联调、拉取、映射、任务审计
- 码上放心 bill watch 补偿、任务弃用 / 回退映射 / 合并 / 拆分
- **Agents Host + Modules** 的配置与启停（非「单独 AHK 进程」）
- 设置、更新、日志与诊断

## 主要目录

相对 `apps/desktop-avalonia/src/`（业务代码）：

- `Views/`、`ViewModels/`
- `Services/Application/`、`Services/Infrastructure/`、`Services/Presentation/`、`Services/Integration/`
- `Styles/`、`Controls/`、`Behaviors/`、`Converters/`

## 代表页面 / 入口

- `Dashboard`、`DrugIndex`、`InventoryOverview`、`ScanCode`、`MsfxLink`
- `Settings`（含 `Settings.Agents` 自动化集成）
- `MainWindow`（标题栏 Agents 状态与启停）

## 技术栈

- Avalonia **12**
- ShadUI 0.2.x
- CommunityToolkit.Mvvm
- Lucide.Avalonia
- Velopack
- 项目引用：`PacToolkits.Application`、`PacToolkits.Infrastructure`、`PacToolkits.Agents.Contracts`

## UI 状态

连接 / 页面可用性 / 区块空态三层模型见 [Desktop 状态模型](../../../docs/architecture/desktop-state.md)。

## 相关文档

- [分层与依赖规则](../../../docs/architecture/layering.md)
- [Agents 运行时架构](../../../docs/architecture/agents.md)
- [Desktop 状态模型](../../../docs/architecture/desktop-state.md)
- [日志事件](./log-events.md)
- [发布与更新](../../../docs/operations/release-flow.md)
