#Requires AutoHotkey v2.0
#SingleInstance Force
; 取码策略、码归一、leaf/staging 分组
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\..\src\msfx_code.ahk"

global g_pass := 0
global g_fail := 0

Main()
ExitApp (g_fail > 0 ? 1 : 0)

Main() {
	item := Map("l1", "A", "l2", "", "l3", "C", "l4", "", "l5", "E", "leaf_code", "LEAF")
	AssertEq(Msfx_SelectInjectCode(item, "MAX_LEVEL"), "E", "select:MAX prefers l5")
	AssertEq(Msfx_SelectInjectCode(item, "MIN_LEVEL"), "A", "select:MIN prefers l1")
	AssertEq(Msfx_SelectInjectCode(Map("leaf_code", "X"), "MAX_LEVEL"), "X", "select:fallback leaf")

	AssertEq(Msfx_NormalizeCode("  12 34-5678  "), "12345678", "norm:digits >=8")
	AssertEq(Msfx_NormalizeCode("ab12"), "ab12", "norm:short keeps trimmed")
	AssertEq(Msfx_NormalizeCode("a`nb c"), "abc", "norm:strips space newline")

	grouped := Msfx_GroupLeafCodes([
		Map("leaf_code", "11111111"),
		Map("leaf_code", "11 111111"),
		Map("leaf_code", ""),
		Map("leaf_code", "22222222"),
	])
	AssertEq(grouped.Length, 2, "group leaf:dedupe length")
	AssertEq(grouped[1], "11111111", "group leaf:[1]")
	AssertEq(grouped[2], "22222222", "group leaf:[2]")

	sids := Msfx_GroupStagingIds([
		Map("staging_id", 3),
		Map("staging_id", 3),
		Map("staging_id", 0),
		Map("staging_id", 8),
	])
	AssertEq(sids.Length, 2, "group staging:length")
	AssertEq(sids[1], 3, "group staging:[1]")
	AssertEq(sids[2], 8, "group staging:[2]")

	MsgBox "msfx_code`npass=" g_pass "  fail=" g_fail, "Injector Code", (g_fail > 0 ? 0x10 : 0x40)
}

AssertEq(a, b, name) {
	global g_pass, g_fail
	if (a = b) {
		g_pass++
		return
	}
	g_fail++
	MsgBox "[FAIL] " name "`nexpected=" b "`nactual=" a, "msfx_code", 0x10
}
