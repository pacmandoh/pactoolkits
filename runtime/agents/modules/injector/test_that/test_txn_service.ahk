#Requires AutoHotkey v2.0
#SingleInstance Force
; 预留提交、预留回滚、bySpec 拆零计划、整盒预留；对照预留返回体（非目录 quantity）
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\log.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\pac_api.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\..\src\parse_clipboard.ahk"
#Include "%A_ScriptDir%\..\src\pg_exec.ahk"
#Include "%A_ScriptDir%\_env.ahk"

Log_Startup("InjectorTest")
cfg := Test_InitPacApi()
clientId := Test_ClientId(cfg)
drugId := Test_DrugId(cfg)
spec := Test_Spec(cfg)
skips := []

idx := Test_GetQuantity(drugId, spec)
if !idx["ok"]
	Test_Fail(idx.Has("message") ? idx["message"] : "[目录] 读取单盒数量失败")
dbQty := idx["qty"]
if (dbQty < 2)
	Test_Fail("[目录] 单盒数量过小，无法拆零计划`nquantity=" dbQty "`n" drugId "`n" spec)

; A：拆零 1 粒提交；二次提交应失败
txnA := Util_TxnId()
resA := Txn_ReservePick(txnA, clientId, drugId, spec, 1, "", "")
Test_MustReserve("A", resA, 1)
commitA := Txn_Commit(txnA)
if !(commitA.Has("ok") && commitA["ok"])
	Test_Fail("[A] 提交失败`n" (commitA.Has("message") ? commitA["message"] : ""))
againA := Txn_Commit(txnA)
if (againA.Has("ok") && againA["ok"])
	Test_Fail("[A] 已提交事务再次提交不应成功")

; B：拆零 1 粒回滚；回滚后提交应失败
txnB := Util_TxnId()
resB := Txn_ReservePick(txnB, clientId, drugId, spec, 1, "", "")
Test_MustReserve("B", resB, 1)
rbB := Txn_Rollback(txnB)
if !(rbB.Has("ok") && rbB["ok"])
	Test_Fail("[B] 回滚失败`n" (rbB.Has("message") ? rbB["message"] : ""))
commitB := Txn_Commit(txnB)
if (commitB.Has("ok") && commitB["ok"])
	Test_Fail("[B] 已回滚事务提交不应成功")

; C：bySpec 拆零计划（qty = 2*单盒 + 1 → remNeed=1）后回滚
txnC := Util_TxnId()
byC := Map("splitFlag", "是", "qty", dbQty * 2 + 1)
resC := Txn_ReservePick(txnC, clientId, drugId, spec, 0, "", "", byC, "", 0, "rem")
Test_MustReserve("C", resC, 1)
if (Util_ToInt(resC.Has("rem_need") ? resC["rem_need"] : 0, 0) != 1)
	Test_Fail("[C] rem_need 应为 1`n实际=" (resC.Has("rem_need") ? resC["rem_need"] : ""))
if (Util_ToInt(resC.Has("whole_n") ? resC["whole_n"] : 0, 0) != 0)
	Test_Fail("[C] rem 模式 whole_n 应为 0")
rbC := Txn_Rollback(txnC)
if !(rbC.Has("ok") && rbC["ok"])
	Test_Fail("[C] 回滚失败`n" (rbC.Has("message") ? rbC["message"] : ""))

; D：整盒 1 行预留后回滚（库中需有 remain=qty 的整盒）
txnD := Util_TxnId()
resD := Txn_ReserveAlloc(txnD, clientId, drugId, spec, 1, 0, "", false, "full")
if !(resD.Has("ok") && resD["ok"]) {
	skips.Push("D 整盒预留：" (resD.Has("message") ? resD["message"] : "失败"))
} else {
	if (Util_ToInt(resD.Has("whole_n") ? resD["whole_n"] : 0, 0) != 1)
		Test_Fail("[D] whole_n 应为 1")
	rbD := Txn_Rollback(txnD)
	if !(rbD.Has("ok") && rbD["ok"])
		Test_Fail("[D] 回滚失败`n" (rbD.Has("message") ? rbD["message"] : ""))
}

; 库存不足
resFail := Txn_ReservePick(Util_TxnId(), clientId, drugId, spec, 999999, "", "")
if (resFail.Has("ok") && resFail["ok"] && !(resFail.Has("skip") && resFail["skip"]))
	Test_Fail("[库存不足] 预留 999999 不应成功")

skipTxt := ""
if (skips.Length > 0) {
	skipTxt := "`n`n跳过`n"
	for _, s in skips
		skipTxt .= "- " s "`n"
}

Test_Ok("[信息] 预留/提交/回滚通过`n`n"
	. "client=" clientId "`n药品=" drugId "`n规格=" spec "`n单盒=" dbQty "`n"
	. "A 已提交 1 粒" skipTxt)

Test_MustReserve(tag, r, expectTake) {
	if !(IsObject(r) && r.Has("ok") && r["ok"])
		Test_Fail("[" tag "] 预留失败`n" (IsObject(r) && r.Has("message") ? r["message"] : ""))
	if (r.Has("skip") && r["skip"])
		Test_Fail("[" tag "] 预留被跳过`n" (r.Has("message") ? r["message"] : ""))
	take := Util_ToInt(r.Has("req_qty_effective") ? r["req_qty_effective"] : 0, 0)
	if (take != expectTake)
		Test_Fail("[" tag "] sumTake 不符`n期望=" expectTake "`n实际=" take)
}
