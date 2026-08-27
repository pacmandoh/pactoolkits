# Agents

项目 skill 在 `.agents/skills/`。Cursor 与 Codex 按需加载 `SKILL.md`。Cloud Agent 只读仓库里的 skill，读不到本机全局目录。

**第一方 `pac-*`：** 本仓库改 `SKILL.md` 和附带的 scripts、references。写或改这些 skill 时先读 skill-creator 和 pac-comments。不要默认跑 evals，除非用户要求。

**第三方：** 用 `npx skills add` 装进同一目录，和仓库根的 `skills-lock.json` 一起提交。升级只用 `npx skills update`，不要手改它们的 `SKILL.md`。

grill-with-docs 和 domain-modeling 会想在仓库根写 `CONTEXT.md`、在 `docs/adr/` 写 ADR。领域用词先看现有的 `docs/architecture/`，不要另起一份术语表，除非用户明确要。

每轮闸门（always-apply）：

- `.cursor/rules/pac-naming-gate.mdc` — 在 `apps/`、`packages/` 写新符号前先 debit
- `.cursor/rules/pac-dotnet-build-gate.mdc` — `dotnet` 与格式脚本必须 `required_permissions: ["all"]`

任务对上某个 skill 时读对应 `SKILL.md`。细则只写在 skill 里。
