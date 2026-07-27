# PostgreSQL 数据库概览

PostgreSQL 组件负责系统的持久化模型和任务编排，是联调数据、映射关系、任务及执行状态的权威数据源。

## 主要职责

- schema 初始化
- 增量 migration 管理
- 结构与约束验证
- 部署计划与执行
- staging、mapping、inject task 和 audit event 相关模型维护

## 目录结构

```text
database/postgres/
  bootstrap/
  migrations/
  verify/
  scripts/
```

## 业务主题

- 上游单据与明细入库
- 追溯码 staging
- 药品与规格映射
- 注入任务生成与队列顺序
- 仓库模式重复注入防护
- 任务重开、重试与结算
- 任务回退映射、合并、拆分编排
- 仓库成功任务的行指纹防重

## 相关文档

- [运维入口 README](../README.md)
- [数据库兼容与回退规则](../../../docs/operations/database-compatibility-policy.md)
- [Beta 发布规则](../../../docs/operations/beta-release-policy.md)
- [发布流程](../../../docs/operations/release-flow.md)
