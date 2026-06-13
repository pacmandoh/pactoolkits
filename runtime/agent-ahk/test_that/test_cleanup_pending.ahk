#Requires AutoHotkey v2.0
#SingleInstance Force
#Include "%A_ScriptDir%\..\src\pg_exec.ahk"
#Include "%A_ScriptDir%\..\src\db_txn.ahk"
#Include "%A_ScriptDir%\..\src\utils.ahk"

global Cfg := IsSet(Cfg) ? Cfg : Map()
global Cfg := Util_LoadDotEnv(A_ScriptDir "\..\.env.local")

global TEST_DRUG   := "盐酸氨基葡萄糖胶囊"
global TEST_SPEC   := "0.75g*60粒"
global TEST_CLIENT := A_ComputerName "\" A_UserName

global TIMEOUT_MIN := 1    ; 自愈阈值（分钟）。脚本会制造 5 分钟前的 txn，必然超时
global LIMIT_N      := 200 ; 一次最多清理多少条 PENDING

Main()

Main() {
    MsgBox "[信息] AHK 版本: " A_AhkVersion "`n"
        . "[信息] AHK 位数: " (A_PtrSize=8 ? "64-bit" : "32-bit")

    ; 0) 连接自检
    ping := Ping_DB()
    if !ping["ok"] {
        MsgBox "[连接错误] 连接失败`n`n" ping["err"]
        ExitApp 1
    }
    MsgBox "[信息] 连接成功`n`n" ping["text"]

    ; 1) 基础检查：drug_index 是否存在
    chk := DB_Query("SELECT qty FROM drug_index WHERE drug_id='" Util_EscapeSQL(TEST_DRUG) "' AND spec='" Util_EscapeSQL(TEST_SPEC) "' LIMIT 1;")
    if !chk["ok"] {
        MsgBox "[SQL 错误] 查询 drug_index 失败`n" chk["err"]
        ExitApp 1
    }
    if (chk["rows"].Length = 0) {
        MsgBox "[查询错误] drug_index 未找到测试药品规格`n药品=" TEST_DRUG "`n规格=" TEST_SPEC
        ExitApp 1
    }

    ; 2) 测试前库存概况（sum(remain>0)）
    beforeSum := GetAvailRemainSum()
    ShowPoolSummary("[信息] 测试前库存概况")

    ; 3) 制造“超时 PENDING”
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

    ; 4) 执行自愈清理
    rr := Txn_CleanupPending(TIMEOUT_MIN, LIMIT_N)

    ; rr 可能是 void 或 Map。这里不强制检查 rr["ok"]，只看最终库状态
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

; ----------------- DB ping -----------------

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

; ----------------- helpers -----------------

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
