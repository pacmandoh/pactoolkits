# 命名扫描（提示列表）

机械 grep 只找 **候选** — 每条都要做 debit 判断。死代码脚本替代不了这一步。

## 流程

1. 列出这次改过的文件
2. 每个新符号：列 debit + 同目录最短 peer
3. 下面 grep 可选；只修 diff 里真正的堆叠

## 可选 grep（仓库根）

```bash
# 可见性或 Internal/Core 堆叠（审，不自动删）
rg '\b(private|internal)\s+\w[\w<>,\?\s]*\s+\w*(Internal|Core)(Async)?\s*\(' apps packages -g '*.cs' | rg -v ReloadCoreAsync

# 文件名重复文件夹角色
rg '/(Pages|Dialogs|DTOs)/\w+Models\.cs$|/Converters/\w+Converters\.cs$' apps packages

# 样式文件把父作用域叠进每个选择器（文件上下文已 debit）
rg 'UserControl\.\w+' apps/desktop-avalonia/src/Styles/Pages -g '*.axaml' --count-matches
```

## 报告

类型：**naming** — 位置、debit 列表、peer、动作、核对 build。
