---
name: pac-comments
description: >-
  PacToolkits 注释与文案（C#、AHK、shell、docs/**、AGENTS.md、pac-* skill）。
  加/改/审注释、C# XML summary、TODO/FIXME/HACK/NOTE、#region，或写工程文档、
  AGENTS.md、skill 正文时加载。根 README 是对外介绍，不要按注释那套去压短。
  注释用中文；XML summary 仅 C#。纯改注释不动逻辑、命名、格式。
---

# Comments（PacToolkits）

**只写为什么。** 注释讲业务规则、设计原因、代码本身说不出的约束 — 不要复述下一行已经写明的事。能靠名字表明用途的就不要注释。

**大业务代码库：** 不要追求 `/// <summary>` 全覆盖。按下面 Must / May / Do not。改注释时择缺补漏 — 不要给每个类型、成员批量生成 summary。

**范围：** **所有代码文件**（C#、AutoHotkey、shell、PowerShell、workflow YAML 等）。新注释全跟。纯改注释时只动注释（不改逻辑、命名、格式）。有价值的业务/设计说明留下；删掉废话和机械句；不要发明空注释。

**不要**改不可变 SQL 迁移里的注释（`database/postgres/migrations/**`）— 发布按内容哈希，改了就不能动。

---

## 语言

- 注释用 **简体中文**
- 既定英文术语原样保留（不要硬译）：MVVM、DI、UI、ObservableCollection、CancellationToken、ConfigureAwait、Task、MSFX、API、SQL、HTTP、JSON、Host、Injector、DataGrid 等
- **句末不要 `。`。** 中文注释结尾不加句号；句中用 `。` 分句可以；注释（或每一行写完的注释）末尾去掉

```text
Good: 返回 UI 线程更新 ObservableCollection
Bad:  返回用户界面线程更新可观察集合。
Good: 这是一句，这是第二句。但是这是结尾
Bad:  这是一句，这是第二句。但是这是结尾。
```

各语言行注释标记（标记后面的正文同一套规则）：

| 语言 | 行注释 |
|------|--------|
| C# | `//`、`///` |
| AutoHotkey | `;` |
| shell / YAML | `#` |
| PowerShell | `#` |

机器指令不要翻译或改写：shebang、`# shellcheck …`、`#Requires`、`#region` 标签（C# region 名保持下面列出的英文）。

---

## 文案（注释、工程文档、pac-* skill）

代码注释、`docs/**`、`pac-*` skill：书面汉语，按结果写现在的设计。
根 `README.md` / `README.zh-CN.md` 是对外介绍，产品表述（例如「一体化工具套件」）留着，不要按注释那套去压短。

拒的是 **整类** 问题，不是禁词表。**本 skill 的例子只示意；不是穷举。** 判法同 **pac-naming**。不要用「业界通称」「教材译法」「Fowler 译本」给坏译开脱。

| 症状（类） | 改成 |
|------------|------|
| 英文习语按字填成汉字 | 用书面中文写这件事，或直接留英文产品词 |
| 把按字翻译的词当成标准术语（合同、宿主、夹具、门面、气味、非平凡） | 约束/规范、进程名、测试类、入口、迹象、有实际逻辑；中文文档里不这样写就不要留 |
| 口语指令（再搞一套、塞进、抄一页、摆、别、瞎删、手搓） | 另行实现、放入/写入、对照现有页实现、放置、不要、未经确认不要删、自行绘制 |
| 「库」当 database | PostgreSQL、DB 或 PacAPI |
| Unicode 箭头当流程胶水（`→` `←` `⇒`） | 用动词：依赖 / 换 / 转 / 则 / 返回；长链拆句或列表 |
| 标点堆概念（`A + B + C`、一长串 `/` 叠词） | 顿号、「与」、分句；一句一件事 |
| 括号里贴职责标签，或把一段话压成口号 | 名字已说明职责就不要括号；要解释就写完整句子 |
| 空强调 / 假具体（「真 Pg」「黄金路径」） | 写场景和条件（本机 PostgreSQL、正式发布路径…） |
| 给既定英文产品词造中文 | JWT、SSE、LISTEN、Host、DI、fixture… 保持英文 |
| 迁移步骤、验证已删除的旧路径 | 只写当前设计结果 |

「同一套逻辑」可以留（表示同一组规则）。引擎正规能力不要写成「不规范」（用「不直观的行为」）。
算术、SemVer build metadata、代码（`n + 1`）、字段名拼接（`minDbSchema+maxDbSchema`）不算「堆概念」。

普通标点用 ASCII `-` / `:` / `/` 即可。Mermaid 边在语法要求时可以用 `-->`；节点标签里不要放装饰性 `→`。

改 `docs/**` 或 **pac-*** skill 时同一套。不要去改非 `pac-*` 的第三方 skill。不要按这套去改根 README 的产品表述。

---

## 哪些该写（C# 与行内）

按下面三类，不要给所有符号写文档。

### Must write

- **有职责的类型** — 有实际逻辑的 public（以及重要的 internal）class / record：短 `/// <summary>` 写清管什么 / 不管什么
- **公开接口** — 一句约束
- **复杂业务规则** — 口径、顺序、并发、unlock/session、reload 闸门等。优先入口短 `///`，或旁边 `//` 写为什么
- **SQL / MSFX / 特殊协议** — 不直观的协议或引擎行为、双列、upsert 技巧（`xmax`）、LISTEN 池、为何建这条索引、外部 BizCode 字符串等

### May write

- **Service / Repository** — 可选的短职责与边界（只访问数据、不算业务等）。类型名已经说清、边界也不意外时跳过
- **公开入口方法** 行为不能从名字看出来时（`LoadAsync`、`RefreshAsync` …）— 一小段，不是 changelog

### Do not write

- 属性（含 `[ObservableProperty]` / CommunityToolkit 生成成员）
- 没有额外逻辑的 getter / setter
- MVVM Toolkit / 源生成、只为绑定存在的 partial
- 私有方法名已经表明用途的
- 只复述符号的机械 summary（`获取数据`、`设置数据`、`初始化`、`更新` …）

枚举：值带有从名字看不出的业务含义时才写一句；否则跳过。

---

## `/// <summary>` — **仅 C#**

其它语言 **不要** XML 文档 / 「summary」块。不要发明模仿 `/// <summary>` 的文件头。

C# 的 summary 是 IDE 阅读辅助（不随产品发布）。按上面三类 — 只留能补充信息的。

Bad（复述职责清单，不要仿照）：

```csharp
/// <summary>
/// Dashboard 页面 ViewModel
///
/// 负责：
/// - 药品统计
/// - 趋势分析
/// - 数据刷新
/// </summary>
```

Good：

```csharp
/// <summary>
/// 查询当前库存
///
/// 不包含已停用药品
/// KPI 按 Display（别名）聚合，勿按 Raw 重算
/// </summary>
```

`remarks` 只给说不清的业务规则 — 不要逐步 changelog。

---

## 行内 `//`

优先 **为什么**（MSFX/Postgres 里不直观的行为、口径、线程、顺序、兼容、性能、依赖限制）。显然的赋值不要注释。

```csharp
// DataGrid 半选(null) 不回写全选，避免把 indeterminate 当成 false
if (isChecked is null)
{
    return;
}

// 已停用且库存为 0 的药品不参与统计

// 这里必须先取消旧请求，避免刷新过快导致数据显示错乱

// MSFX BizCode=7 / 文案含 App Call Limited 视为限流
```

```csharp
// Bad: // 自增
// Bad: // 设置值
```

单条注释大约 ≤ 4 行；更长通常该抽方法。

---

## 标记

| 形式 | 模式 |
|------|------|
| TODO | `// TODO(username): …` — 如 `// TODO(pacmandoh): 支持分页查询` |
| FIXME | `// FIXME: …` — 如 `// FIXME: MSFX 偶发返回重复出库明细` |
| HACK | `// HACK: …` — 如 `// HACK: 第三方接口存在 Bug，暂时绕过` |
| NOTE | 长期有效的规则可以多行：`// NOTE:` 后续续行 |

---

## `#region`

不要用 region 折叠普通成员。宁愿拆类型。

**允许：** `Constructor`、`Commands`、`Event Handlers`、`IDisposable`、`Generated`

**禁止：** `Properties`、`Methods`、`Utils`、`Test` 以及同类目录折叠

---

## 优先解释

- 为什么这样设计 / 为什么必须留下 / 为什么必须走这条路
- MSFX / Postgres 耦合与业务口径
- 为什么还留着这条路径、线程、性能、依赖限制

**不要**解释控制流复述。

---

## 纯改注释清单

1. 留下有价值的业务和设计说明
2. 删掉复述代码的注释
3. 删掉机械 summary（尤其属性 / 没有额外逻辑的私有成员 / Toolkit 生成成员）
4. 不要补低信息注释，也不要批量填缺 summary
5. 中文注释去掉句末 `。`（句中 `。` 可以）
6. 硬译、口语指令、箭头当流程、概念堆叠按上文改。不要动根 README 的产品表述
7. 不改逻辑、命名、格式 — 只动注释

不要把这些规则再写入 `docs/` — 只放本 skill
