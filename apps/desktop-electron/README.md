# PacToolkits Desktop Electron（预留）

这是未来 **Nuxt + Electron Preview** 的目录占位。

## 当前状态

- **当前不参与 release**（CI / Velopack / agent 打包均不包含此目录）
- **当前不连接数据库**
- **当前不替代 Avalonia**（正式 Desktop 仍是 `apps/desktop-avalonia`）
- **当前不实现业务**——仅保留目录结构与入口占位文件

## 架构约束

后续若在此实现 Desktop 壳层，业务能力必须通过共享包暴露，而不是在 Electron 层直接访问 Postgres 或复制 Avalonia 逻辑：

| 层级 | 路径 |
|------|------|
| 用例 / 服务接口 | `packages/application` |
| Agent 协议 | `packages/agent-contracts` |
| 基础设施实现 | `packages/infrastructure` |
| 纯领域逻辑 | `packages/core` |

## 目录

```txt
apps/desktop-electron/
  app/                 # 未来 Nuxt 应用（当前为空占位）
  electron/
    main/              # Electron 主进程
    preload/           # Preload 脚本
  package.json
  README.md
```

## 本地开发（尚未启用）

依赖与启动脚本尚未接入。需要时可在此目录补充 `electron` / Nuxt 依赖后再实现 `npm run dev`。
