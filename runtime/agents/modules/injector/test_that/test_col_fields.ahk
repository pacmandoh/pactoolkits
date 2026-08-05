#Requires AutoHotkey v2.0
#SingleInstance Force
; ColFields 配置模型回归（严格边界）
; 覆盖：规范化 / 读配置门控 / settings.json 默认 / 表头命中 / bySpec 按 id 取值
; 不覆盖：DB / 事务预留 / 选码策略 / 仓库指纹 / 剪贴板与 UI 网格（解析仅喂静态 TSV）

#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\..\src\parse_clipboard.ahk"
#Include "%A_ScriptDir%\..\src\util_config.ahk"

global g_pass := 0
global g_fail := 0

Main()
ExitApp (g_fail > 0 ? 1 : 0)

Main() {
	; --- 1. 规范化（Parse_Normalize*）---
	Test_Normalize_RejectsNonArray()
	Test_Normalize_DropsInvalidItems()
	Test_Normalize_HeadersRules()
	Test_Normalize_BoolDefaultsAndCoercion()
	Test_Normalize_RuntimeShape_NoLockLabel()
	Test_Normalize_Idempotent()

	; --- 2. 读配置门控（Util_CfgGetColFields / AppWin / OneOf）---
	Test_CfgGetColFields_Rejects()
	Test_CfgGetColFields_AcceptsMixedValid()
	Test_CfgGetAppWin_StringList()
	Test_CfgGetOneOf_CodePickPolicy()

	; --- 3. 出厂 defaults（settings.json 原始语义 + 加载后运行时）---
	Test_SettingsJson_ShipShape()
	Test_SettingsJson_LoadViaCfgGet()

	; --- 4. 读值辅助（By_Get / MatchHeader）---
	Test_MatchHeader()
	Test_ByGet()

	; --- 5. 静态文本解析（仅验证 ColFields → bySpec[id]，不接 MSFX/DB）---
	Test_Parse_BySpecKeysAndOptional()
	Test_Parse_AsIntCoerce()
	Test_Parse_HeaderMissAndEmptyFields()

	body := "ColFields 配置模型`n"
		. "pass=" g_pass "  fail=" g_fail "`n"
		. "边界：无 DB/事务/选码/剪贴板 UI"
	MsgBox body, "Injector ColFields", (g_fail > 0 ? 0x10 : 0x40)
}

; ========== helpers ==========

Pass(name) {
	global g_pass
	g_pass++
}

Fail(name, detail := "") {
	global g_fail
	g_fail++
	MsgBox "[FAIL] " name (detail != "" ? "`n" detail : ""), "ColFields", 0x10
}

Assert(cond, name, detail := "") {
	if cond
		Pass(name)
	else
		Fail(name, detail)
}

AssertTrue(cond, name) {
	Assert(!!cond, name)
}

AssertFalse(cond, name) {
	Assert(!cond, name)
}

AssertEq(a, b, name) {
	if (a = b)
		Pass(name)
	else
		Fail(name, "expected=`n" b "`nactual=`n" a)
}

FieldOf(arr, id) {
	for _, f in arr {
		if (f["id"] = id)
			return f
	}
	return 0
}

HasId(arr, id) {
	return IsObject(FieldOf(arr, id))
}

; ========== 1. Normalize ==========

Test_Normalize_RejectsNonArray() {
	AssertEq(Parse_NormalizeColFields("").Length, 0, "normalize:string → []")
	AssertEq(Parse_NormalizeColFields(Map()).Length, 0, "normalize:Map → []")
	AssertEq(Parse_NormalizeColFields(0).Length, 0, "normalize:0 → []")
	AssertEq(Parse_NormalizeColFields([]).Length, 0, "normalize:[] → []")
}

Test_Normalize_DropsInvalidItems() {
	out := Parse_NormalizeColFields([
		"plain",
		42,
		[],
		Map(),
		Map("id", "", "headers", ["A"]),
		Map("id", "   ", "headers", ["A"]),
		Map("id", "ok", "headers", []),
		Map("id", "ok2", "headers", ["", "  "]),
		Map("id", "keep", "headers", ["名"]),
	])
	AssertEq(out.Length, 1, "normalize:only one valid kept")
	AssertEq(out[1]["id"], "keep", "normalize:kept id")
}

Test_Normalize_HeadersRules() {
	; headers 必须是 Array；字符串不是 Array → 丢弃整项
	outStr := Parse_NormalizeColFields([Map("id", "x", "headers", "物资名称")])
	AssertEq(outStr.Length, 0, "normalize:headers string rejected")

	outMap := Parse_NormalizeColFields([Map("id", "x", "headers", Map(1, "A"))])
	AssertEq(outMap.Length, 0, "normalize:headers Map rejected")

	out := Parse_NormalizeColFields([Map("id", "x", "headers", [" 物资名称 ", "", " 药品名称 "])])
	AssertEq(out.Length, 1, "normalize:headers array ok")
	AssertEq(out[1]["headers"].Length, 2, "normalize:trim skip empty headers")
	AssertEq(out[1]["headers"][1], "物资名称", "normalize:header[1] trimmed")
	AssertEq(out[1]["headers"][2], "药品名称", "normalize:header[2] trimmed")
}

Test_Normalize_BoolDefaultsAndCoercion() {
	dflt := Parse_NormalizeColFields([Map("id", "a", "headers", ["H"])])[1]
	AssertTrue(dflt["required"], "normalize:required default true")
	AssertFalse(dflt["asInt"], "normalize:asInt default false")

	; Util_ToBool：数字非 0 / 1、true/yes/on；false/0/空串等为 false
	cases := [
		Map("in", true, "req", true, "as", true),
		Map("in", false, "req", false, "as", false),
		Map("in", 1, "req", true, "as", true),
		Map("in", 0, "req", false, "as", false),
		Map("in", "true", "req", true, "as", true),
		Map("in", "YES", "req", true, "as", true),
		Map("in", "on", "req", true, "as", true),
		Map("in", "false", "req", false, "as", false),
		Map("in", "no", "req", false, "as", false),
		Map("in", "", "req", false, "as", false),
	]
	for _, c in cases {
		f := Parse_NormalizeColFields([Map(
			"id", "b", "headers", ["H"],
			"required", c["in"], "asInt", c["in"]
		)])[1]
		AssertEq(f["required"], c["req"], "normalize:required←" c["in"])
		AssertEq(f["asInt"], c["as"], "normalize:asInt←" c["in"])
	}
}

Test_Normalize_RuntimeShape_NoLockLabel() {
	; 运行时 Map 仅 id/headers/required/asInt；label/locked 不进规范化结果
	f := Parse_NormalizeColFields([Map(
		"id", "drugName",
		"label", "药品名称",
		"headers", ["物资名称"],
		"required", true,
		"asInt", false,
		"locked", true
	)])[1]
	AssertTrue(IsObject(f), "normalize:object")
	AssertTrue(f.Has("id") && f.Has("headers") && f.Has("required") && f.Has("asInt"), "normalize:has runtime keys")
	AssertFalse(f.Has("label"), "normalize:no label")
	AssertFalse(f.Has("locked"), "normalize:no locked")
}

Test_Normalize_Idempotent() {
	raw := [
		Map("id", "qty", "label", "数量", "headers", ["数量"], "required", true, "asInt", true, "locked", true),
		Map("id", "unit", "headers", ["单位"], "required", false, "asInt", false),
	]
	once := Parse_NormalizeColFields(raw)
	twice := Parse_NormalizeColFields(once)
	AssertEq(once.Length, twice.Length, "normalize:idempotent length")
	AssertEq(once[1]["id"], twice[1]["id"], "normalize:idempotent id0")
	AssertEq(once[1]["asInt"], twice[1]["asInt"], "normalize:idempotent asInt")
	AssertEq(once[2]["required"], twice[2]["required"], "normalize:idempotent required")
	AssertEq(once[1]["headers"][1], twice[1]["headers"][1], "normalize:idempotent header")
}

; ========== 2. CfgGet ==========

Test_CfgGetColFields_Rejects() {
	ok := false
	err := ""

	Util_CfgGetColFields(Map(), "ColFields", &ok, &err)
	AssertFalse(ok, "CfgGet:missing key → fail")
	AssertTrue(InStr(err, "缺少"), "CfgGet:missing key message")

	Util_CfgGetColFields(Map("ColFields", "not-array"), "ColFields", &ok, &err)
	AssertFalse(ok, "CfgGet:non-array → fail")
	AssertTrue(InStr(err, "数组"), "CfgGet:non-array message")

	Util_CfgGetColFields(Map("ColFields", []), "ColFields", &ok, &err)
	AssertFalse(ok, "CfgGet:empty array → fail")

	Util_CfgGetColFields(Map("ColFields", ["x", Map("id", "a")]), "ColFields", &ok, &err)
	AssertFalse(ok, "CfgGet:all invalid after normalize → fail")

	Util_CfgGetColFields(Map("ColFields", [
		Map("id", "drugName", "headers", ["A"]),
		Map("id", "drugName", "headers", ["B"]),
	]), "ColFields", &ok, &err)
	AssertFalse(ok, "CfgGet:duplicate id → fail")
	AssertTrue(InStr(err, "重复"), "CfgGet:duplicate message")
	AssertTrue(InStr(err, "drugName"), "CfgGet:duplicate names id")
}

Test_CfgGetColFields_AcceptsMixedValid() {
	ok := false
	err := ""
	fields := Util_CfgGetColFields(Map("ColFields", [
		Map("id", "bad"),  ; no headers → drop
		Map("id", "drugName", "headers", ["物资名称", "药品名称"], "required", true, "asInt", false, "locked", true),
		Map("id", "qty", "headers", ["数量"], "required", true, "asInt", true),
	]), "ColFields", &ok, &err)
	AssertTrue(ok, "CfgGet:mixed valid ok" (err != "" ? " err=" err : ""))
	AssertEq(fields.Length, 2, "CfgGet:drops invalid keeps valid")
	dn := FieldOf(fields, "drugName")
	AssertTrue(IsObject(dn), "CfgGet:has drugName")
	AssertEq(dn["headers"].Length, 2, "CfgGet:headers preserved")
	AssertTrue(FieldOf(fields, "qty")["asInt"], "CfgGet:asInt on qty")
}

Test_CfgGetAppWin_StringList() {
	ok := false
	err := ""
	set := Util_CfgGetAppWin(Map("AppWin", ["互慧软件.exe", "ProjectMain.exe", "", "  "]), "AppWin", &ok, &err)
	AssertTrue(ok, "AppWin:stringList ok")
	AssertTrue(IsObject(set) && Type(set) = "Map", "AppWin:returns Map")
	if IsObject(set) {
		AssertTrue(set.Has("互慧软件.exe"), "AppWin:has 互慧")
		AssertTrue(set.Has("ProjectMain.exe"), "AppWin:has ProjectMain")
		AssertFalse(set.Has(""), "AppWin:skips empty")
	}

	Util_CfgGetAppWin(Map("AppWin", Map("互慧软件.exe", 1)), "AppWin", &ok, &err)
	AssertFalse(ok, "AppWin:object Map rejected (stringList only)")

	Util_CfgGetAppWin(Map("AppWin", []), "AppWin", &ok, &err)
	AssertFalse(ok, "AppWin:empty array fail")
}

Test_CfgGetOneOf_CodePickPolicy() {
	ok := false
	err := ""
	v := Util_CfgGetOneOf(Map("CodePickPolicy", "MAX_LEVEL"), "CodePickPolicy", ["max_level", "min_level"], &ok, &err)
	AssertTrue(ok, "CodePick:ok")
	AssertEq(v, "max_level", "CodePick:lowercased")
	AssertEq(StrUpper(v), "MAX_LEVEL", "CodePick:upper for runtime Cfg")

	Util_CfgGetOneOf(Map("CodePickPolicy", "weird"), "CodePickPolicy", ["max_level", "min_level"], &ok, &err)
	AssertFalse(ok, "CodePick:invalid rejected")
}

; ========== 3. settings.json ==========

Test_SettingsJson_ShipShape() {
	path := A_ScriptDir "\..\settings.json"
	AssertTrue(FileExist(path), "settings.json exists")
	if !FileExist(path)
		return

	try {
		root := JSON.parse(Util_ReadUtf8(path))
	} catch as e {
		Fail("settings.json parse", e.Message)
		return
	}
	Assert(Type(root) = "Map", "settings.json root Map")

	; 出厂键：已迁离 ColSpecs / IntCols / WarehouseTaskIdentifier
	AssertFalse(root.Has("ColSpecs"), "ship:no ColSpecs")
	AssertFalse(root.Has("IntCols"), "ship:no IntCols")
	AssertFalse(root.Has("WarehouseTaskIdentifier"), "ship:no WarehouseTaskIdentifier")

	Assert(Type(root["AppWin"]) = "Array", "ship:AppWin is Array")
	Assert(Type(root["ColFields"]) = "Array", "ship:ColFields is Array")
	AssertEq(root["ColFields"].Length, 9, "ship:ColFields count=9")

	must := ["traceCode", "drugName", "drugSpec", "qty", "unit", "doseUnit", "splitFlag", "billNo", "batchNo"]
	for _, id in must {
		found := false
		for _, row in root["ColFields"] {
			if (Type(row) = "Map" && row.Has("id") && row["id"] = id) {
				found := true
				AssertTrue(row.Has("headers") && Type(row["headers"]) = "Array" && row["headers"].Length > 0
					, "ship:" id " headers[] non-empty")
				AssertTrue(row.Has("locked") && row["locked"] = true, "ship:" id " locked=true")
				AssertTrue(row.Has("label") && Trim("" row["label"]) != "", "ship:" id " has label")
				break
			}
		}
		AssertTrue(found, "ship:has id=" id)
	}

	; 业务约定：名称/规格/数量必填；数量整数
	for _, row in root["ColFields"] {
		if (row["id"] = "drugName" || row["id"] = "drugSpec" || row["id"] = "qty")
			AssertTrue(row["required"] = true, "ship:" row["id"] " required")
		if (row["id"] = "qty")
			AssertTrue(row["asInt"] = true, "ship:qty asInt")
	}
}

Test_SettingsJson_LoadViaCfgGet() {
	path := A_ScriptDir "\..\settings.json"
	if !FileExist(path)
		return
	try {
		root := JSON.parse(Util_ReadUtf8(path))
	} catch as e {
		Fail("settings load parse", e.Message)
		return
	}

	ok := false
	err := ""
	fields := Util_CfgGetColFields(root, "ColFields", &ok, &err)
	AssertTrue(ok, "load:CfgGetColFields ok" (err != "" ? " err=" err : ""))
	if !ok
		return
	AssertEq(fields.Length, 9, "load:runtime field count")
	; 运行时仅 id/headers/required/asInt（无 label/locked）
	for _, f in fields {
		AssertFalse(f.Has("label"), "load:no label on " f["id"])
		AssertFalse(f.Has("locked"), "load:no locked on " f["id"])
		AssertTrue(f.Has("headers") && f["headers"].Length > 0, "load:headers " f["id"])
	}
	AssertTrue(FieldOf(fields, "qty")["asInt"], "load:qty asInt")
	AssertTrue(FieldOf(fields, "drugName")["required"], "load:drugName required")
	AssertFalse(FieldOf(fields, "traceCode")["required"], "load:traceCode optional")

	set := Util_CfgGetAppWin(root, "AppWin", &ok, &err)
	AssertTrue(ok, "load:AppWin ok")
	if ok {
		AssertTrue(set.Has("互慧软件.exe"), "load:AppWin 互慧")
		AssertTrue(set.Has("ProjectMain.exe"), "load:AppWin ProjectMain")
	}

	pol := Util_CfgGetOneOf(root, "CodePickPolicy", ["max_level", "min_level"], &ok, &err)
	AssertTrue(ok, "load:CodePickPolicy ok")
	AssertEq(pol, "max_level", "load:CodePickPolicy value")
}

; ========== 4. Match / By_Get ==========

Test_MatchHeader() {
	AssertTrue(Parse_MatchHeader("物资名称", ["物资名称", "药品名称"]), "match:exact first")
	AssertTrue(Parse_MatchHeader("药品名称", ["物资名称", "药品名称"]), "match:exact second")
	AssertTrue(Parse_MatchHeader("  规格  ", ["规格"]), "match:trim text")
	AssertFalse(Parse_MatchHeader("物资", ["物资名称"]), "match:no partial")
	AssertFalse(Parse_MatchHeader("X", ["A", "B"]), "match:miss")
	AssertFalse(Parse_MatchHeader("A", []), "match:empty headers")
}

Test_ByGet() {
	by := Map("drugName", "阿莫西林", "qty", 2)
	AssertEq(By_Get(by, "drugName"), "阿莫西林", "By_Get:hit")
	AssertEq(By_Get(by, "missing", "d"), "d", "By_Get:default")
	AssertEq(By_Get(by, "", "d"), "d", "By_Get:empty id → default")
	AssertEq(By_Get(0, "drugName", "d"), "d", "By_Get:non-object → default")
	AssertEq(By_Get(by, "  drugName  "), "阿莫西林", "By_Get:trim id")
}

; ========== 5. Parse static TSV（配置驱动边界，无业务链路）==========

Test_Parse_BySpecKeysAndOptional() {
	fields := Parse_NormalizeColFields([
		Map("id", "drugName", "headers", ["物资名称", "药品名称"], "required", true, "asInt", false),
		Map("id", "drugSpec", "headers", ["规格"], "required", true, "asInt", false),
		Map("id", "qty", "headers", ["数量"], "required", true, "asInt", true),
		Map("id", "billNo", "headers", ["单据号", "当前编号"], "required", false, "asInt", false),
	])
	; 表头用别名「药品名称」；单据可选列缺失仍应成功
	tsv := "药品名称`t规格`t数量`n阿莫西林`t0.25g*24粒`t12"
	r := Parse_TargetInfo(fields, "", tsv, "A", "", true)
	AssertTrue(r["ok"], "parse:ok optional missing" (r.Has("message") ? " " r["message"] : ""))
	if !r["ok"]
		return
	by := r["bySpec"]
	AssertEq(By_Get(by, "drugName"), "阿莫西林", "parse:bySpec drugName")
	AssertEq(By_Get(by, "drugSpec"), "0.25g*24粒", "parse:bySpec drugSpec")
	AssertEq(By_Get(by, "qty"), 12, "parse:bySpec qty int")
	AssertFalse(by.Has("billNo"), "parse:optional absent not forced")
	; data 键是命中的表头字面值
	AssertTrue(r["data"].Has("药品名称"), "parse:data uses matched header text")
}

Test_Parse_AsIntCoerce() {
	fields := Parse_NormalizeColFields([
		Map("id", "drugName", "headers", ["名称"], "required", true),
		Map("id", "qty", "headers", ["数量"], "required", true, "asInt", true),
	])
	rOk := Parse_TargetInfo(fields, "", "名称`t数量`n药`t7", "A", "", true)
	AssertTrue(rOk["ok"], "parse:asInt digits ok")
	if rOk["ok"]
		AssertEq(rOk["bySpec"]["qty"], 7, "parse:asInt→integer")

	rBad := Parse_TargetInfo(fields, "", "名称`t数量`n药`tx", "A", "", true)
	AssertTrue(rBad["ok"], "parse:asInt non-digit still row-ok")
	if rBad["ok"]
		AssertEq(rBad["bySpec"]["qty"], 0, "parse:asInt non-digit → 0")
}

Test_Parse_HeaderMissAndEmptyFields() {
	fields := Parse_NormalizeColFields([
		Map("id", "drugName", "headers", ["物资名称"], "required", true),
		Map("id", "qty", "headers", ["数量"], "required", true, "asInt", true),
	])
	rMiss := Parse_TargetInfo(fields, "", "别的列`t数量`nA`t1", "A", "", true)
	AssertFalse(rMiss["ok"], "parse:missing required header → fail")
	AssertTrue(InStr(rMiss["reason"], "Header"), "parse:header miss reason")

	rEmpty := Parse_TargetInfo([], "", "物资名称`t数量`nA`t1", "A", "", true)
	AssertFalse(rEmpty["ok"], "parse:empty ColFields → fail")
	AssertTrue(InStr(rEmpty["reason"], "ColFields"), "parse:empty fields reason")

	rNoRow := Parse_TargetInfo(fields, "", "物资名称`t数量", "A", "", true)
	AssertFalse(rNoRow["ok"], "parse:header only no data → fail")
}
