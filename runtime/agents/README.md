# Agents 运行时

本目录包含 Agents Host、自动化模块和 AHK 模块模板。

```text
runtime/agents/
  host/                  .NET Host，发布为 Agents.exe
  modules/               参与发布构建的模块源码
  templates/ahk-module/  新建 AHK 模块的起始模板
  docs/                   模块说明
```

## 运行模型

Agents 的发布单元由一个常驻 Host 和多个独立模块进程组成：

- Desktop 启动 Host，并通过控制文件下发 Host 与模块命令
- Host 发现 `Modules/<Id>/module.json`，按 `entry.win-x64` 启动模块
- 模块自检完成后创建 `module.ready`
- Desktop 结合进程状态和 `module.ready` 展示模块状态

Host 启动后不会自行挂载模块。Desktop 会根据 `Agents.Modules[id].Enabled` 挂载已启用模块，也可以在运行期间单独启动或停止模块。

## 发布布局

```text
Agents/
  Agents.exe
  ReleaseManifest.json
  host.control
  Modules/
    <Id>/
      module.json
      settings.json
      settings.schema.json
      module.control
      module.ready
      <Entry>.exe
```

`settings.json` 是随模块发布的默认配置。Desktop 首次使用模块时，将其复制到 `{ConfigDir}/agents/modules/<Id>/settings.json`；后续设置修改仅写入用户配置。

## 模块发现与更新

Desktop 和 Host 都会定期扫描模块描述文件，因此运行期间可以发现新增或移除的模块目录。发现模块不等于启动模块。

Desktop 还会监视正在运行的入口二进制：

- Host 二进制更新后重启 Host
- 模块二进制更新后只重启对应模块
- 未运行模块更新后不自动启动

文件变化须连续两次观测稳定后才会触发重启，以避开复制过程中的临时状态。

## 本地验证

在 Desktop 输出目录生成与安装包一致的 Agents 目录布局：

```bash
./scripts/run-desktop-with-agents.sh --configuration Release --stage-only
```

macOS 和 Linux 无法编译或运行 Windows AHK 模块；脚本仍会复制 Host、模块描述文件、默认设置和 schema，用于验证发现与设置页行为。

验证已由 Windows CI 生成的 Agents staging：

```bash
./scripts/release-agents.sh \
  --artifact-dir artifacts/agents/win-x64 \
  --skip-upload \
  --dry-run
```

## 新建模块

复制 [`templates/ahk-module/`](templates/ahk-module/) 后，按模板说明更新模块 ID、入口、图标、默认设置和发布清单。模板目录不参与运行时模块发现或 CI 编译。

## 相关文档

- [Agents 运行时架构](../../docs/architecture/agents.md)
- [Injector 模块](./docs/injector.md)
- [发布流程](../../docs/operations/release-flow.md)
- [脚本工具](../../scripts/docs/tooling.md)
