# Desktop（Avalonia）概览

Desktop 承载业务交互、配置、审计、更新和 Agents 运行控制。ViewModel 通过 `packages/application` 调用业务用例；域数据走 PacAPI。

## 主要职责

- 仪表盘与总览
- 药品信息维护
- 追溯码录入与扫描
- 库存总览与库存调整
- 码上放心联调、拉取、映射、任务审计
- 码上放心账单监视补偿，以及任务弃用、映射回退、合并和拆分
- Agents Host 与模块的配置、启停和状态展示
- 设置、更新、日志与诊断

## 主要目录

相对 `apps/desktop-avalonia/src/`（业务代码）：

- `Views/`、`ViewModels/`
- `Services/Infrastructure/`、`Services/Presentation/`、`Services/Integration/`、`Services/Workspace/`
- `Styles/`、`Controls/`、`Behaviors/`、`Converters/`

## 代表页面与入口

- `Dashboard`、`DrugIndex`、`InventoryOverview`、`ScanCode`、`MsfxLink`
- `Settings`（含 `Settings.Agents` 自动化集成）
- `MainWindow`（标题栏 Agents 状态与启停）

## 技术栈

- Avalonia **12**
- ShadUI 0.2.x
- CommunityToolkit.Mvvm
- Lucide.Avalonia
- Velopack
- 域数据：PacAPI (ASP.NET)
- 项目引用：`PacToolkits.Application`、`PacToolkits.Agents.Contracts`、`PacToolkits.Logger`

## UI 状态

连接状态、页面可用性和区块空状态的分层模型见 [Desktop 状态模型](../../../docs/architecture/desktop-state.md)。

键盘焦点、临时浮层与业务工作集（K / T / W）的分工与手势约定见 [焦点与工作集模型](./focus-model.md)。

## 相关文档

- [分层与依赖规则](../../../docs/architecture/layering.md)
- [Agents 运行时架构](../../../docs/architecture/agents.md)
- [Desktop 状态模型](../../../docs/architecture/desktop-state.md)
- [焦点与工作集模型](./focus-model.md)
- [日志事件](./log-events.md)
- [发布与更新](../../../docs/operations/release-flow.md)
