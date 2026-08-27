# DI 与反射接线（PacToolkits）

**注册不等于在用。** 类型可以经注入、`IEnumerable<T>`、或反射活着，而不出现显式 `GetRequiredService<T>()`。

## DI 入口（先 grep 这些）

| 文件 | 注册什么 |
|------|----------|
| `apps/desktop-avalonia/src/Composition/ServiceRegistration.cs` | UI shell、对话框、desktop 适配、`AddPageViewModels()` |
| `packages/application/Services/ServiceCollectionExtensions.cs` | `I*Service` 到 `*Service` |
| `packages/infrastructure/Database/ServiceCollectionExtensions.cs` | `IDb`、repo、PostgreSQL 基础设施 |

入口：`AddPacToolkitsUiServices()` 注册 Infrastructure、Application、UI shell，然后页面 VM。

## 页面 ViewModel（反射）

`AddPageViewModels()` 注册 desktop 程序集里 **每一个非抽象 `AppPageBase` 子类**：

```csharp
asm.GetTypes().Where(t => !t.IsAbstract && typeof(AppPageBase).IsAssignableFrom(t))
```

含义：

- 页面 VM **即使不在侧栏也会注册** — 看 `ShowInSidebar`、`SidebarRoute`、导航命令
- 删一页需要：VM + View + 该页专用样式/资源；DI 下次编译会跟上（不用手工注销）
- 孤儿页迹象：具体 `AppPageBase` 没有对应 `*View.axaml`（`AppViews` 命名：`FooViewModel` 对 `FooView`）

```bash
# 全部页面 VM
rg "class \w+ViewModel : AppPageBase" apps/desktop-avalonia/src/ViewModels/Pages -g "*.cs"
# 有对应视图吗？
ls apps/desktop-avalonia/src/Views/Pages/*View.axaml
```

## 对话框接线

在 `ServiceRegistration.AddUiShell()` 经 `DialogManager.Register<View, ViewModel>()` 注册：

| View | ViewModel |
|------|-----------|
| `InventoryUnlockDialogView` | `InventoryUnlockDialogViewModel` |
| `DrugKeyFixPreviewDialogView` | `DrugKeyFixPreviewDialogViewModel` |
| `InfoDetailDialogView` | `InfoDetailDialogViewModel` |
| `MsfxStateDetailDialogView` | `MsfxStateDetailDialogViewModel` |
| `MsfxMappingBatchDialogView` | `MsfxMappingBatchDialogViewModel` |
| `MsfxTaskSplitDialogView` | `MsfxTaskSplitDialogViewModel` |

死对话框迹象：注册了对，却没有 `IDialogService` / `DialogService.Show*` 调用链。

```bash
rg "MsfxTaskSplitDialogViewModel|Show.*MsfxTaskSplit" apps/desktop-avalonia/src -g "*.cs"
```

## 视图解析（反射）

`AppViews.TryCreateView` 按 **类型名** 把 `FooViewModel` 映射到 `FooView`（去掉 `ViewModel`，接上 `View`）。不需要显式导航引用。

```bash
rg "class FooView\b" apps/desktop-avalonia/src/Views
```

## Application 服务三角

对 `packages/application/Abstractions/` 里每个 `I*Service`：

1. **实现存在** 于 `packages/application/Services/*Service.cs`
2. **已注册** 于 `AddPacToolkitsApplication()`
3. **至少注入** 到一个 Desktop VM 或另一个 Application 服务

```bash
rg "interface IYourService" packages/application/Abstractions -g "*.cs"
rg "AddSingleton<IYourService" packages/application/Services/ServiceCollectionExtensions.cs
rg "IYourService" apps/desktop-avalonia/src/ViewModels packages/application/Services -g "*.cs"
```

## Infrastructure repo 三角

对 Abstractions 里每个 `I*Repo`：

1. **实现** 在 `packages/infrastructure/Repositories/`
2. **已注册** 于 `AddPacToolkitsInfrastructure()`
3. **被** Application `*Service` 使用（不是 Desktop VM 直接用）

```bash
rg "IYourRepo" packages/application/Services -g "*.cs"
rg "YourRepo\." packages/application/Services -g "*.cs"
```

## 仅 Desktop 的服务

`ServiceRegistration.AddApplicationServices()` 与 `AddCoreInfrastructure()` — 经 VM 或其它服务的构造注入核对 resolve：

```bash
rg "AddSingleton<IYourDesktopService" apps/desktop-avalonia/src/Composition/ServiceRegistration.cs
rg "IYourDesktopService" apps/desktop-avalonia/src -g "*.cs" \
  --glob '!**/ServiceRegistration.cs'
```

## 自动扫描

Phase A 跑附带脚本：

```bash
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-dead-di.sh
```

输出的 `WARN` 行需要按 [false-positive-guards.md](false-positive-guards.md) 在 Phase B 手工确认。
