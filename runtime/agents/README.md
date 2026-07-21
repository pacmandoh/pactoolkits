# Agents + modules

**Agents** = 容器（install dir + config root + `Agents.exe`）。
**Injector** = Agents 下的一个模块（以后还会有别的模块）。
**Host** = 英文角色名：Agents 里**挂载 Modules 的入口进程**（`Agents.exe`）。不是产品名，不要叫「宿主」；**容器**只指 Agents。

Icon: `host/assets/pactoolkits-agents.ico`.

Source modules live under `modules/<id>/` (lowercase). Release layout uses `Modules/<Id>/` with `module.json`.
Today only `injector` (AHK) ships as `Modules/Injector`. Host mounts it as a child on Desktop `start`
(no second tray icon); Host does not auto-mount on boot.

```text
Agents/                 ← 容器
  Agents.exe            ← Host（入口进程，挂载 Modules）
  Modules/
    Injector/
      module.json
      Injector.exe      ← 模块
```

Desktop launches Host; Host selects modules (default `Injector`, override `--module`).
Host stays resident and does **not** auto-mount: Desktop writes `Modules/<Id>/module.control`
(`start` / `stop` / `quit`) to mount, unload, or exit Host.
