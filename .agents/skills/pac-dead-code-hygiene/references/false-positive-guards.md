# 假阳性门（删之前读）

符号可以看起来已死，但仍必需。标 **delete** 之前，适用的复选框都要勾完。

## ViewModel 属性

| 看起来已死 | 仍有效，当 |
|------------|------------|
| `Views/**/*.axaml` 零命中 | 用在 `.axaml.cs` code-behind |
| Views 零命中 | 用在单元测试（`tests/PacToolkits.Desktop.Tests`） |
| Views 零命中 | 只在同一 VM 的 partial 里引用（命令 CanExecute、reload 闸门） |
| 属性名零命中 | 经 `[ObservableProperty]` 生成名绑定 — 搜 PascalCase，不要搜 `_field` |
| 零命中 | 列在 `GetNotifiableCommands()` 里给 `NotifyCommands(...)` 刷新 |
| 零命中 | 父 VM 或嵌套行模板用代码读，不是 `{Binding}` |

```bash
# 属性在 Views 之外用过吗？
rg "PropertyName" apps/desktop-avalonia/src tests --glob "*.{cs,axaml}"
rg "PropertyNameCommand" apps/desktop-avalonia/src/Views -g "*.axaml"
rg "GetNotifiableCommands" apps/desktop-avalonia/src/ViewModels -g "*YourViewModel*"
```

## RelayCommand

| 看起来已死 | 仍有效，当 |
|------------|------------|
| AXAML 没有命令名 | 代码里调用（`FooCommand.Execute(...)`） |
| 不在 Views | 在 `GetNotifiableCommands()` 数组里 |
| 不在 Views | Shell/顶栏经 `AppPageBase` 上的动态命令绑定 |

```bash
rg "YourCommand" apps/desktop-avalonia/src --glob "*.{cs,axaml}"
```

## DI 与反射接线

| 看起来已死 | 仍有效，当 |
|------------|------------|
| 没有 `GetRequiredService<T>()` | 经构造注入（grep 构造参数类型） |
| 没有显式 resolve | 在 `AddPageViewModels()` 注册 — 所有 `AppPageBase` 子类自动注册 |
| 没有显式 resolve | 作为 `IEnumerable<AppPageBase>` 注入 `MainWindowViewModel` |
| 接口未用 | 默认接口成员；裁之前核对全部实现者 |
| Desktop 服务未用 | 只经 `IDialogService` / `IToastService` 入口 resolve |

```bash
rg "IYourService|YourService" apps packages tests -g "*.cs" \
  --glob '!**/ServiceCollectionExtensions.cs' \
  --glob '!**/ServiceRegistration.cs'
```

## 视图与命名约定

| 看起来已死 | 仍有效，当 |
|------------|------------|
| 视图从未被显式引用 | `AppViews` 按命名约定把 `FooViewModel` 映射到 `FooView` |
| 页面 VM 侧栏 grep 没有 | `ShowInSidebar == false` 但经导航命令打开（Settings、About） |
| 对话框视图 AXAML 未用 | 在 `DialogManager.Register<View, ViewModel>()` 注册，经 `IDialogService` 显示 |

```bash
rg "class FooView" apps/desktop-avalonia/src/Views
rg "ShowInSidebar|SidebarRoute" apps/desktop-avalonia/src/ViewModels/Pages/FooViewModel.cs
```

## DTO 与 Application 成员

| 看起来已死 | 仍有效，当 |
|------------|------------|
| Service 里从未读 DTO 字段 | 序列化进 JSON/日志；外部 API 规范的一部分 |
| Repo 方法只在测试里用 | 测试在记录预期行为 — 确认后 **方法和测试一起**删 |
| `internal` 类型零跨程序集引用 | 经测试项目 `InternalsVisibleTo` 使用 |
| 公开辅助只在单文件用 | 候选改 `private`，不是删 — 除非真不可达 |

## AXAML 样式与资源

| 看起来已死 | 仍有效，当 |
|------------|------------|
| 样式 class 不在 `Styles/` Selector | 动态设置：code-behind 里 `Classes.Add("Token")` |
| `StaticResource` 键只在 `App.axaml` | 经隐式样式间接使用 |
| Converter 键 Views 零命中 | 用在 `Styles/**/*.axaml`，不只 `Views/` |

```bash
rg "Token" apps/desktop-avalonia/src --glob "*.{axaml,cs}"
rg "YourConverterKey" apps/desktop-avalonia/src/Styles -g "*.axaml"
```

## Agent、数据库、运行时

| 看起来已死 | 仍有效，当 |
|------------|------------|
| `runtime/agents/**` 零 C# 引用 | 运行时由 AHK injector 加载 |
| SQL 文件 grep 零命中 | 列在 `database/postgres/` 引导 README 或部署脚本 |
| 类型只出现在 JSON 配置 | 多态反序列化或 agent 协议 schema |

---

## 删除前清单（每条发现复制一份）

```
[ ] 全库 rg（apps、packages、tests、scripts、.github）
[ ] 查过 code-behind 和 VM partial
[ ] 查过测试程序集
[ ] 查过 DI/反射注册路径
[ ] 查过 GetNotifiableCommands / 动态绑定
[ ] 查过 Styles/ 和 App.axaml（不只 Views）
[ ] 确认不是架构漂移（是则交给 pac-architecture）
```
