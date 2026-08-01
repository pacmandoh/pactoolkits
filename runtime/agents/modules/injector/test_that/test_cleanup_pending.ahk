#Requires AutoHotkey v2.0
#SingleInstance Force
#Include "%A_ScriptDir%\..\src\pg_exec.ahk"
#Include "%A_ScriptDir%\..\src\db_txn.ahk"
#Include "%A_ScriptDir%\..\src\utils.ahk"

global Cfg := IsSet(Cfg) ? Cfg : Map()
global Cfg := Util_LoadDotEnv(A_ScriptDir "\..\.env.local")

global TEST_DRUG := "盐酸氨基葡萄糖胶囊"
global TEST_SPEC := "0.75g*60粒"
global TEST_CLIENT := A_ComputerName "\" A_UserName

global TIMEOUT_MIN := 1    ; 自愈阈值（分钟）；脚本会伪造 5 分钟前 txn 以必超时
global LIMIT_N := 200 ; 单次最多清理的 PENDING 条数

Main()

Main() {
	MsgBox "[信息] AHK 版本: " A_AhkVersion "`n"
		. "[信息] AHK 位数: " (A_PtrSize = 8 ? "64-bit" : "32-bit")

	; 前置条件：数据库连接可用
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

	; 记录恢复前的可用库存基线
	beforeSum := GetAvailRemainSum()
	ShowPoolSummary("[信息] 测试前库存概况")

	; 构造已超时的 PENDING 事务以触发恢复流程
	oldTs := DateAdd(A_Now, -5, "Minutes")
	txnId := FormatTime(oldTs, "yyyyMMddHHmmss") "_" Random(1000, 9999)

	need := 3
	r := Txn_ReservePick(txnId, TEST_CLIENT, TEST_DRUG, TEST_SPEC, need, "", "", 0)
	if !IsObject(r) || !r.Has("ok") || !r["ok"] {
		MsgBox "[预留错误] Reserve 失败`n"
			. "txn=" txnId "`n"
			. (IsObject(r) && r.Has("err") ? r["err"] : "")
		ExitApp 1
	}
	if (r.Has("skip") && r["skip"]) {
		MsgBox "[信息] Reserve 被跳过（本次无法验证自愈）`nwhy=" (r.Has("why") ? r["why"] : "")
		ExitApp 1
	}

	AssertTxnStatus(txnId, "PENDING", "[信息] Reserve 后状态校验(PENDING)")

	afterReserveSum := GetAvailRemainSum()
	ShowPoolSummary("[信息] Reserve 后库存概况")

	if (afterReserveSum >= beforeSum) {
		MsgBox "[断言失败] Reserve 后库存未减少（无法验证回补）`n"
			. "beforeSum=" beforeSum "`n"
			. "afterReserveSum=" afterReserveSum "`n"
			. "txn=" txnId
		ExitApp 1
	}

	; 执行恢复后以事务和库存的最终状态作为判定依据
	rr := Txn_CleanupPending(TIMEOUT_MIN, LIMIT_N)

	; 返回标志不是恢复完成的充分条件，测试以事务和库存状态为准
	AssertTxnStatus(txnId, "ROLLED_BACK", "[信息] 自愈后状态校验(ROLLED_BACK)")

	afterCleanupSum := GetAvailRemainSum()
	ShowPoolSummary("[信息] 自愈后库存概况")

	if (afterCleanupSum != beforeSum) {
		MsgBox "[断言失败] 自愈回补不一致`n"
			. "beforeSum=" beforeSum "`n"
			. "afterReserveSum=" afterReserveSum "`n"
			. "afterCleanupSum=" afterCleanupSum "`n"
			. "txn=" txnId
		ExitApp 1
	}

	MsgBox "自愈清理超时 PENDING 测试通过`n`n"
		. "txn=" txnId "`n"
		. "remain_sum(before/reserve/cleanup)=" beforeSum "/" afterReserveSum "/" afterCleanupSum

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

GetAvailRemainSum() {
	sql := ""
		. "SELECT coalesce(sum(remain) FILTER (WHERE remain > 0), 0) "
		. "FROM trace_pool "
		. "WHERE drug_id='" Util_EscapeSQL(TEST_DRUG) "' "
		. "  AND spec='" Util_EscapeSQL(TEST_SPEC) "';"

	r := DB_Query(sql)
	if !r["ok"] {
		MsgBox "[SQL 错误] GetAvailRemainSum 查询失败`n" r["err"]
		ExitApp 1
	}
	return r["rows"][1][1]
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
	if (r["rows"].Length = 0) {
		MsgBox title "`n(no row)"
		return
	}
	row := r["rows"][1]
	MsgBox title "`n"
		. "avail_rows(remain>0)=" row[1] "`n"
		. "avail_total_remain=" row[2] "`n"
		. "total_rows=" row[3]
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
