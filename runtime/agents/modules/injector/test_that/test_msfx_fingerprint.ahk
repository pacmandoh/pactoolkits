#Requires AutoHotkey v2.0
#SingleInstance Force
; 仓库行指纹：缺字段为空；可选 slot / batch
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\..\src\parse_clipboard.ahk"
#Include "%A_ScriptDir%\..\src\msfx_task.ahk"

global g_pass := 0
global g_fail := 0

Main()
ExitApp (g_fail > 0 ? 1 : 0)

Main() {
	by := Map("qty", 12, "unit", "盒", "batchNo", "B1")
	fp := Msfx_BuildWarehouseRowFingerprint(by, "BILL-1", "药A", "0.5g")
	AssertEq(fp, "bill=BILL-1|drug=药A|spec=0.5g|qty=12|unit=盒|batch=B1", "fp:with batch")

	byNoBatch := Map("qty", 12, "unit", "盒")
	fp2 := Msfx_BuildWarehouseRowFingerprint(byNoBatch, "BILL-1", "药A", "0.5g")
	AssertEq(fp2, "bill=BILL-1|drug=药A|spec=0.5g|qty=12|unit=盒", "fp:no batch")

	slot := Map("ok", true, "rowSlot", "3")
	fp3 := Msfx_BuildWarehouseRowFingerprint(byNoBatch, "BILL-1", "药A", "0.5g", slot)
	AssertEq(fp3, "bill=BILL-1|drug=药A|spec=0.5g|qty=12|unit=盒|slot=3", "fp:slot")

	AssertEq(Msfx_BuildWarehouseRowFingerprint(0, "BILL", "药", "规"), "", "fp:non-object")
	AssertEq(Msfx_BuildWarehouseRowFingerprint(Map("qty", 1), "BILL", "药", "规"), "", "fp:missing unit")
	AssertEq(Msfx_BuildWarehouseRowFingerprint(Map("qty", 1, "unit", "盒"), "", "药", "规"), "", "fp:empty bill")

	ws := Map("qty", "  12  ", "unit", "盒  装")
	fpWs := Msfx_BuildWarehouseRowFingerprint(ws, "  BILL  1 ", "药", "规")
	AssertEq(fpWs, "bill=BILL 1|drug=药|spec=规|qty=12|unit=盒 装", "fp:collapse whitespace")

	MsgBox "fingerprint`npass=" g_pass "  fail=" g_fail, "Injector Fingerprint", (g_fail > 0 ? 0x10 : 0x40)
}

AssertEq(a, b, name) {
	global g_pass, g_fail
	if (a = b) {
		g_pass++
		return
	}
	g_fail++
	MsgBox "[FAIL] " name "`nexpected=`n" b "`nactual=`n" a, "fingerprint", 0x10
}
