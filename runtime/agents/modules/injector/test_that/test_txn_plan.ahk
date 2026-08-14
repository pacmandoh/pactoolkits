#Requires AutoHotkey v2.0
#SingleInstance Force
; Txn_PlanPick：整包 skip/full、拆零余数、已扫、WHOLE_BOX_PENDING
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\log.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\pac_api.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\..\src\parse_clipboard.ahk"
#Include "%A_ScriptDir%\..\src\txn_plan.ahk"
#Include "%A_ScriptDir%\_env.ahk"

global g_pass := 0
global g_fail := 0
global Cfg := Map(
	"OPT_PACK_UNITS", Map("盒", true),
	"OPT_PIECE_UNITS", Map("片", true, "粒", true)
)

Log_Startup("InjectorTest")
Main()
ExitApp (g_fail > 0 ? 1 : 0)

Main() {
	Test_BadQty()
	Test_RemPackWholeSkip()
	Test_FullPackWhole()
	Test_OptRemPackWholeSkip()

	splitNote := ""
	loaded := Test_LoadEnvLocal()
	if !loaded["ok"] {
		splitNote := "拆零计划跳过：无 .env.local"
	} else {
		Test_ApplyEnv(loaded["cfg"])
		api := PacApi_InitFromEnv()
		tok := api["ok"] ? PacApi_EnsureToken() : api
		if !(tok.Has("ok") && tok["ok"]) {
			splitNote := "拆零计划跳过：PacAPI 换票失败"
		} else {
			RunSplitPlans(loaded["cfg"])
		}
	}

	MsgBox "Txn_PlanPick`npass=" g_pass "  fail=" g_fail
		. (splitNote != "" ? "`n" splitNote : ""),
		"Injector Plan", (g_fail > 0 ? 0x10 : 0x40)
}

RunSplitPlans(cfg) {
	drugId := Test_DrugId(cfg)
	spec := Test_Spec(cfg)
	idx := Test_GetQuantity(drugId, spec)
	if !idx["ok"] {
		Fail("split:catalog", idx.Has("message") ? idx["message"] : "")
		return
	}
	dbQty := idx["qty"]
	if (dbQty < 2) {
		Fail("split:dbQty", "单盒数量过小 quantity=" dbQty)
		return
	}

	qty := dbQty * 2 + 1
	by := Map("splitFlag", "是", "qty", qty, "unit", "片")

	rem := Txn_PlanPick(by, "rem", false, drugId, spec, 0)
	AssertTrue(rem["ok"] && !rem["skip"], "ipt rem:ok")
	AssertEq(rem["wholePick"], 0, "ipt rem:wholePick=0")
	AssertEq(rem["remNeed"], 1, "ipt rem:remNeed=1")

	pending := Txn_PlanPick(by, "rem", true, drugId, spec, 0)
	AssertFalse(pending["ok"], "opt rem:WHOLE_BOX_PENDING")
	AssertEq(pending.Has("reason") ? pending["reason"] : "", "WHOLE_BOX_PENDING", "opt rem:reason")

	afterWhole := Txn_PlanPick(by, "rem", true, drugId, spec, 2)
	AssertTrue(afterWhole["ok"] && !afterWhole["skip"], "opt rem after wholes:ok")
	AssertEq(afterWhole["remNeed"], 1, "opt rem after wholes:remNeed=1")

	optSkip := Txn_PlanPick(by, "rem", true, drugId, spec, 3)
	AssertTrue(optSkip["ok"] && optSkip["skip"], "opt rem already split-scanned:skip")

	full := Txn_PlanPick(by, "full", false, drugId, spec, 0)
	AssertTrue(full["ok"] && !full["skip"], "full:ok")
	AssertEq(full["wholePick"], 2, "full:wholePick=2")
	AssertEq(full["remNeed"], 1, "full:remNeed=1")

	fullRem := Txn_PlanPick(by, "full", false, drugId, spec, 2)
	AssertTrue(fullRem["ok"] && !fullRem["skip"], "full after wholes:ok")
	AssertEq(fullRem["wholePick"], 0, "full after wholes:wholePick=0")
	AssertEq(fullRem["remNeed"], 1, "full after wholes:remNeed=1")

	fullSkip := Txn_PlanPick(by, "full", false, drugId, spec, 3)
	AssertTrue(fullSkip["ok"] && fullSkip["skip"], "full already covered:skip")

	exact := Txn_PlanPick(Map("splitFlag", "是", "qty", dbQty * 2), "rem", false, drugId, spec, 0)
	AssertTrue(exact["ok"] && exact["skip"], "rem exact packs:skip")
}

Test_BadQty() {
	r := Txn_PlanPick(Map("splitFlag", "是", "qty", 0), "rem", false, "药", "规", 0)
	AssertFalse(r["ok"], "qty=0 fail")
}

Test_RemPackWholeSkip() {
	r := Txn_PlanPick(Map("splitFlag", "否", "qty", 3), "rem", false, "药", "规", 0)
	AssertTrue(r["ok"] && r["skip"], "ipt rem packWhole:skip")
	AssertEq(r["wholePick"], 0, "ipt rem packWhole:wholePick=0")
}

Test_FullPackWhole() {
	r := Txn_PlanPick(Map("splitFlag", "否", "qty", 3), "full", false, "药", "规", 0)
	AssertTrue(r["ok"] && !r["skip"], "ipt full packWhole:ok")
	AssertEq(r["wholePick"], 3, "ipt full packWhole:wholePick=3")
	AssertEq(r["remNeed"], 0, "ipt full packWhole:remNeed=0")
}

Test_OptRemPackWholeSkip() {
	r := Txn_PlanPick(Map("qty", 2, "unit", "盒"), "rem", true, "药", "规", 0)
	AssertTrue(r["ok"] && r["skip"], "opt rem 盒:skip")
}

AssertTrue(cond, name) {
	if cond
		Pass(name)
	else
		Fail(name)
}

AssertFalse(cond, name) {
	AssertTrue(!cond, name)
}

AssertEq(a, b, name) {
	if (a = b)
		Pass(name)
	else
		Fail(name, "expected=" b "`nactual=" a)
}

Pass(name) {
	global g_pass
	g_pass++
}

Fail(name, detail := "") {
	global g_fail
	g_fail++
	MsgBox "[FAIL] " name (detail != "" ? "`n" detail : ""), "Txn_PlanPick", 0x10
}
