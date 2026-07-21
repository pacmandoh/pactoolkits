# PostgreSQL 数据库概览

数据库模块负责整个系统的持久化建模与任务编排，是联调、映射、建任务、执行状态回写的事实来源。

## 主要职责

- bootstrap 初始化
- 增量 migration
- verify 校验
- 部署计划与执行
- staging / mapping / inject task / audit event 相关模型维护

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
- 药品 / 规格映射
- 注入任务生成与队列顺序
- 仓库模式重复注入防护
- 任务重开、重试与结算
- 任务回退映射、合并、拆分编排
- 仓库成功任务的行指纹防重

## 相关文档

- [运维入口 README](../README.md)
- [数据库兼容与回退政策](../../../docs/operations/database-compatibility-policy.md)
- [Beta 发布政策](../../../docs/operations/beta-release-policy.md)
- [发布流程](../../../docs/operations/release-flow.md)
