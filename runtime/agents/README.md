# Agents + Modules

**Agents** = 容器（安装目录 + 配置根 + `Agents.exe`）。  
**Host** = Agents 里**启停 Modules 的入口进程**（`Agents.exe`，.NET）。不是产品名，不要叫「宿主」；**容器**只指 Agents。  
**Injector** = 当前唯一业务模块（AHK v2 → `Injector.exe`）；后续还可有其它模块。

源码：`host/`、`modules/<id>/`（小写）。发布布局：`Modules/<Id>/` + `module.json`。

```text
Agents/                 ← 容器
  Agents.exe            ← Host（常驻；不自动启动模块）
  Modules/
    Injector/
      module.json
      Injector.exe      ← 模块（Desktop 写入 start 后由 Host 启动）
```

## 运行时行为（摘要）

1. Desktop `Process.Start` 启动 Host，参数含 `--config <AppConfig 绝对路径>`。
2. Host 常驻并轮询 `Modules/<Id>/module.control`（`start` / `stop` / `quit`）；**启动时不自动带上模块**。
3. Desktop 在 Host 就绪且 Injector 启用时写入 `start`；Injector 自检通过后写 `module.ready`。
4. Host 把除 `--module` 外的参数转发给模块；**读配置的是 Injector，不是 Host**。

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

需已有 CI/本地 staging：`artifacts/agents/win-x64/`（含 `Agents.exe` 与 `Modules/Injector/`）。
