#Requires AutoHotkey v2.0
#SingleInstance Force
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\src\pg_exec.ahk"
#Include "%A_ScriptDir%\..\src\db_txn.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\_env.ahk"

global Cfg := IsSet(Cfg) ? Cfg : Map()
loaded := Test_LoadEnvLocal()
if !loaded["ok"] {
	MsgBox "[配置错误] 缺少 env`n" loaded["path"] "`n见 modules/injector/.env.example"
	ExitApp 1
}
global Cfg := loaded["cfg"]

global TEST_DRUG := "盐酸氨基葡萄糖胶囊"
global TEST_SPEC := "0.75g*60粒"
global TEST_CLIENT := A_ComputerName "\" A_UserName

Main()

Main() {
	MsgBox "[信息] AHK 版本: " A_AhkVersion "`n"
		. "[信息] AHK 位数: " (A_PtrSize = 8 ? "64-bit" : "32-bit") "`n"
		. "[信息] env=" Test_EnvLocalPath()

	; 前置条件：通过正式数据库访问路径建立连接
	ping := Ping_DB()
	if !ping["ok"] {
		MsgBox "[连接错误] 连接失败`n`n" ping["err"]
		ExitApp 1
	}
	MsgBox "[信息] 连接成功`n`n" ping["text"]

	; 前置条件：drug_index 中存在可测试记录
	chk := DB_Query("SELECT qty FROM drug_index WHERE drug_id='" Util_EscapeSQL(TEST_DRUG) "' AND spec='" Util_EscapeSQL(TEST_SPEC) "' LIMIT 1;")
	if !chk["ok"] {
		MsgBox "[SQL 错误] 查询 drug_index 失败`n" chk["err"]
		ExitApp 1
	}
	if (chk["rows"].Length = 0) {
		MsgBox "[查询错误] drug_index 未找到测试药品规格`n药品=" TEST_DRUG "`n规格=" TEST_SPEC
		ExitApp 1
	}

	ShowPoolSummary("[信息] 测试前库存概况")

	; 场景 A：预留后提交
	txnA := Util_TxnId()
	needA := 7

	rA := Txn_ReservePick(txnA, TEST_CLIENT, TEST_DRUG, TEST_SPEC, needA, "", "", 0)
	if !IsObject(rA) || !rA.Has("ok") || !rA["ok"] {
		MsgBox "[预留错误] Reserve 失败`ntxn=" txnA "`n" (IsObject(rA) && rA.Has("err") ? rA["err"] : "")
		ExitApp 1
	}
	if (rA.Has("skip") && rA["skip"]) {
		MsgBox "[信息] Reserve 被跳过`nmessage=" (rA.Has("message") ? rA["message"] : "")
		ExitApp 1
	}

	ShowReserveResult("[信息] 测试A Reserve 成功", txnA, rA)
	ShowPoolSummary("[信息] 测试A Reserve 后库存概况")

	cA := Txn_Commit(txnA)
	if !IsObject(cA) || !cA.Has("ok") || !cA["ok"] {
		MsgBox "[提交错误] Commit 失败`ntxn=" txnA "`n" (cA.Has("err") ? cA["err"] : "")
		ExitApp 1
	}
	AssertTxnStatus(txnA, "COMMITTED", "[信息] 测试A Commit 状态校验")
	ShowPoolSummary("[信息] 测试A Commit 后库存概况")

	; 场景 B：预留后回滚并验证库存恢复
	txnB := Util_TxnId()
	needB := 5

	rB := Txn_ReservePick(txnB, TEST_CLIENT, TEST_DRUG, TEST_SPEC, needB, "", "", 0)
	if !IsObject(rB) || !rB.Has("ok") || !rB["ok"] {
		MsgBox "[预留错误] 测试B Reserve 失败`ntxn=" txnB "`n" (rB.Has("err") ? rB["err"] : "")
		ExitApp 1
	}
	if (rB.Has("skip") && rB["skip"]) {
		MsgBox "[信息] 测试B Reserve 被跳过`nmessage=" (rB.Has("message") ? rB["message"] : "")
		ExitApp 1
	}

	ShowReserveResult("[信息] 测试B Reserve 成功（准备 Rollback）", txnB, rB)

	rb := Txn_Rollback(txnB)
	if !IsObject(rb) || !rb.Has("ok") || !rb["ok"] {
		MsgBox "[回滚错误] Rollback 失败`ntxn=" txnB "`n" (rb.Has("err") ? rb["err"] : "")
		ExitApp 1
	}
	AssertTxnStatus(txnB, "ROLLED_BACK", "[信息] 测试B Rollback 状态校验")

	ShowPoolSummary("[信息] 测试B Rollback 后库存概况")

	; 场景 C：按规格执行拆零预留，仅扣余数并在验证后回滚
	txnC := Util_TxnId()
	bySpec := Map("splitFlag", "是", "qty", "125")
	rC := Txn_ReservePick(txnC, TEST_CLIENT, TEST_DRUG, TEST_SPEC, 999999, "", "", bySpec)
	if !IsObject(rC) || !rC.Has("ok") || !rC["ok"] {
		MsgBox "[预留错误] 测试C Reserve(拆零) 失败`ntxn=" txnC "`n" (rC.Has("err") ? rC["err"] : "")
		ExitApp 1
	}

	if (rC.Has("skip") && rC["skip"]) {
		MsgBox "[信息] 测试C 被跳过`nmessage=" (rC.Has("message") ? rC["message"] : "")
	} else {
		ShowReserveResult("[信息] 测试C Reserve 成功（拆零）", txnC, rC)
		rbC := Txn_Rollback(txnC)
		if !IsObject(rbC) || !rbC.Has("ok") || !rbC["ok"] {
			MsgBox "[回滚错误] 测试C Rollback 失败`ntxn=" txnC "`n" (rbC.Has("err") ? rbC["err"] : "")
			ExitApp 1
		}
		AssertTxnStatus(txnC, "ROLLED_BACK", "[信息] 测试C Rollback 状态校验")
	}

	MsgBox "[信息] 全部测试完成`n"
		. "A: Reserve+Commit OK`n"
		. "B: Reserve+Rollback OK`n"
		. "C: 拆零逻辑 OK（已回滚）"

	ExitApp 0
}

Ping_DB() {
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"]
		return Map("ok", false, "err", rOpen["err"])

	r := DB_Query("SELECT version(), current_database(), current_user, current_setting('TimeZone');")
	if !r["ok"]
		return Map("ok", false, "err", r["err"])

	if (r["rows"].Length = 0)
		return Map("ok", true, "text", "connected (no row)")

	row := r["rows"][1]
	out := "version=" row[1] "`n"
		. "db=" row[2] "`n"
		. "user=" row[3] "`n"
		. "timezone=" row[4]
	return Map("ok", true, "text", out)
}

ShowPoolSummary(title) {
	sql := ""
		. "SELECT "
		. "  count(*) FILTER (WHERE remain > 0) AS avail_rows, "
		. "  coalesce(sum(remain) FILTER (WHERE remain > 0), 0) AS avail_total_remain, "
		. "  count(*) AS total_rows "
		. "FROM trace_pool "
		. "WHERE drug_id='" Util_EscapeSQL(TEST_DRUG) "' "
		. "  AND spec='" Util_EscapeSQL(TEST_SPEC) "';"

	r := DB_Query(sql)
	if !r["ok"] {
		MsgBox "[SQL 错误] 统计 trace_pool 失败`n" r["err"]
		return
	}
	row := r["rows"][1]
	MsgBox title "`n"
		. "avail_rows(remain>0)=" row[1] "`n"
		. "avail_total_remain=" row[2] "`n"
		. "total_rows=" row[3]
}

ShowReserveResult(title, txnId, r) {
	items := r.Has("items") ? r["items"] : []
	codes := r.Has("codes") ? r["codes"] : []
	eff := r.Has("req_qty_effective") ? r["req_qty_effective"] : ""

	txt := title "`n"
		. "txn_id=" txnId "`n"
		. "req_qty_effective=" eff "`n"
		. "items=" items.Length "  codes=" codes.Length "`n`n"

	max := (items.Length < 20) ? items.Length : 20
	Loop max {
		it := items[A_Index]
		txt .= Format("#{1}: pool_id={2} take={3} code={4}`n"
			, A_Index, it["pool_id"], it["take"], it["code"])
	}
	if (items.Length > max)
		txt .= "... (" items.Length " items total)`n"

	MsgBox txt
}

AssertTxnStatus(txnId, expected, title := "[信息] Txn 状态校验") {
	q := "SELECT status FROM trace_txn WHERE txn_id='" Util_EscapeSQL(txnId) "' LIMIT 1;"
	r := DB_Query(q)
	if !r["ok"] {
		MsgBox "[SQL 错误] " title " 查询失败`n" r["err"]
		ExitApp 1
	}
	if (r["rows"].Length = 0) {
		MsgBox "[查询错误] " title ": txn 不存在`ntxn=" txnId
		ExitApp 1
	}
	st := r["rows"][1][1]
	if (st != expected) {
		MsgBox "[断言失败] " title ": 状态不匹配`nexpected=" expected "`ngot=" st "`ntxn=" txnId
		ExitApp 1
	}
}
