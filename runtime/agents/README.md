# Agents + Modules

**Agents** = 容器（安装目录 + 配置根 + `Agents.exe`）。  
**Host** = Agents 里**启停 Modules 的入口进程**（`Agents.exe`，.NET）。不是产品名，不要叫「宿主」；**容器**只指 Agents。  
**Module** = `Modules/<Id>/` 下一套可启停进程；**Injector** = 正式业务模块，**Scanner** = 模块契约测试模块。

源码：`host/`、`modules/<id>/`（小写）。发布布局：`Modules/<Id>/` + `module.json`。

新增 AHK 模块可从 [`templates/ahk-module/`](templates/ahk-module/) 复制；模板包含可运行入口、清单、默认设置与 Schema，不参与正式构建。

```text
Agents/                 ← 容器
  Agents.exe            ← Host（常驻；不自动启动模块）
  host.control          ← quit
  Modules/
    <Id>/
      module.json
      settings.json           ← 模板（随 Agents 包）
      settings.schema.json    ← Settings 自动表单
      module.control          ← start / stop
      module.ready
      <Entry>.exe             ← 模块（Desktop 写 start 后由 Host 启动）
```

## 运行时行为（摘要）

1. Desktop `Process.Start` 启动 Host，参数含 `--config <AppConfig 绝对路径>`。
2. Host 常驻：轮询 `host.control`（`quit`）与各 `Modules/<Id>/module.control`（`start` / `stop`）；**启动时不自动带上模块**。
3. Desktop 在 Host 就绪且模块启用时写入对应 `module.control=start`；模块自检通过后写 `module.ready`。
4. Host 把除控制参数外的参数转发给模块，并追加 `--module-settings <用户 settings 路径>`；**读业务配置的是模块，不是 Host**。
5. Desktop 发现运行中二进制稳定变化时，Host 变化会重启 Host，模块变化只重挂对应模块；停止态模块不会被自动拉起。

进程模型、契约与打包见 **[Agents 运行时架构](../../docs/architecture/agents.md)**。

## 文档

- [Agents 运行时架构](../../docs/architecture/agents.md)
- [Injector 执行模型与 ClassNN](./docs/injector.md)
- [发布流程（路径解析）](../../docs/operations/release-flow.md)
- [脚本工具](../../scripts/docs/tooling.md)

## 本地打包（干跑）

仓库根目录：

```bash
./scripts/release-agents.sh --skip-upload --dry-run
```

需已有 CI/本地 staging：`artifacts/agents/win-x64/`（含 `Agents.exe` 与 `Modules/<Id>/`，覆盖 manifest 全部 modules）。
