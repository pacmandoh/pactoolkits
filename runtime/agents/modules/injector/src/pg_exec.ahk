; 追溯码预留/提交/回滚：拆零只扣余数，整包装跳过

global __PG := Map(
    "conn", 0,
    "in_txn", false
)

Txn_ReservePick(txnId, clientId, drugId, spec, reqQty, opt, ipt, bySpec := 0, cls := "") {
    need  := reqQty
    codes := []
    items := []

    ; 前置：只处理拆零=是，且只扣相对单盒量的余数
    if IsObject(bySpec) {
        splitFlag := bySpec.Has("拆零标签||拆零") ? Trim(bySpec["拆零标签||拆零"]) : ""
        qtyVal    := bySpec.Has("数量") ? bySpec["数量"] : ""

        ; 拆零=否不取码，避免整盒场景误扣池
        if (splitFlag = "否") {
            return Map("ok", true, "skip", true, "type", "[跳过取码]", "why", "未拆零药物", "need", 0, "codes", [], "items", [])
        }

        qtyN := 0
        if IsInteger(qtyVal)
            qtyN := qtyVal
        else if RegExMatch(Trim(qtyVal), "^\d+$")
            qtyN := Integer(qtyVal)

        if (qtyN <= 0) {
            return Map("ok", false, "level", "WARN", "type", "[解析错误]", "why", "数量无效：" qtyVal)
        }

        ; 单盒量来自 drug_index.qty，用于算拆零余数
        qDbQty := ""
            . "SELECT qty FROM drug_index "
            . "WHERE drug_id='" Util_EscapeSQL(drugId) "' "
            . "  AND spec='" Util_EscapeSQL(spec) "' "
            . "LIMIT 1;"
        rr := DB_Query(qDbQty)
        if !rr["ok"] {
            return Map("ok", false, "level", "ERR", "type", "[查询错误]", "why", "读取药品索引中单盒数量失败：`n" rr["err"])
        }
        if (rr["rows"].Length = 0) {
            return Map(
				"ok", false, "level", "WARN", "type", "[查询错误]", 
				"why", "药品索引未配置该药品规格（无法计算拆零余数）：`n" "药品=" drugId "`n规格=" spec
			)
        }

        dbQty := Util_ToInt(rr["rows"][1][1])
        if (dbQty <= 0) {
            return Map("ok", false, "level", "ERR", "type", "[查询错误]", "why", "药品索引中单盒数量非法：" dbQty)
        }

        rem := Mod(qtyN, dbQty)

        ; 余数=0 视为整包装：不取码不扣库
        if (rem = 0) {
            ; UI_Tip("跳过取码：整包装（数量为整包整数倍，单盒数量=" dbQty "）", 1200)
            return Map("ok", true, "skip", true, "type", "[跳过取码]", "why", "整包装（数量为整包整数倍，单盒数量=" dbQty "）"
                , "need", 0, "codes", [], "items", [], "qty", qtyN, "dbQty", dbQty)
        }

        ; 拆零场景 need 改为余数，而非整行数量
        need := rem
        reqQty := need
    }

    ; 单条 CTE 锁行挑选：SKIP LOCKED + 与索引同序，降低并发冲突
    rOpen := PG_EnsureOpen()
    if !rOpen["ok"] {
        return Map("ok", false, "level", "ERR", "type", rOpen["type"], "why", "数据库链接失败：`n" rOpen["err"])
    }

    conn := __PG["conn"]

    escTxn    := Util_EscapeSQL(txnId)
    escClient := Util_EscapeSQL(clientId)
    escDrug   := Util_EscapeSQL(drugId)
    escSpec   := Util_EscapeSQL(spec)

	bigSQL := ""
		. "WITH "
		. "params AS ("
		. "  SELECT "
		. "    '" escTxn "'::text    AS txn_id, "
		. "    '" escClient "'::text AS client_id, "
		. "    '" escDrug "'::text   AS drug_id, "
		. "    '" escSpec "'::text   AS spec, "
		. "    " (need+0) "::int     AS need"
		. "), "

		; 只扫一次 trace_pool 做可用性统计
		. "stats AS ("
		. "  SELECT "
		. "    count(*)::int AS cnt_any, "
		. "    count(*) FILTER (WHERE tp.status=1 AND tp.remain > 0)::int AS cnt_usable, "
		. "    COALESCE(sum(tp.remain) FILTER (WHERE tp.status=1 AND tp.remain > 0), 0)::int AS sum_usable_remain "
		. "  FROM trace_pool tp, params p "
		. "  WHERE tp.drug_id = p.drug_id "
		. "    AND tp.spec    = p.spec "
		. "), "

		; ORDER BY 必须与 idx_trace_pick_ultra 完全一致，否则无法吃到索引
		. "locked AS MATERIALIZED ("
		. "  SELECT tp.id "
		. "  FROM trace_pool tp, params p "
		. "  WHERE tp.drug_id = p.drug_id "
		. "    AND tp.spec    = p.spec "
		. "    AND tp.status  = 1 "
		. "    AND tp.remain  > 0 "
		. "  ORDER BY "
		. "    ((tp.remain < tp.qty) IS TRUE) DESC, "
		. "    tp.in_date ASC, "
		. "    COALESCE(tp.last_used, 'epoch'::timestamptz) ASC, "
		. "    tp.id ASC "
		. "  FOR UPDATE SKIP LOCKED "
		. "  LIMIT LEAST(GREATEST((SELECT need FROM params)*2, 50), 5000)"
		. "), "

		; WINDOW 只定义一次，避免重复排序开销
		. "picked AS ("
		. "  SELECT "
		. "    tp.id, tp.trace_code, tp.remain, tp.qty, "
		. "    row_number() OVER w AS seq, "
		. "    sum(tp.remain) OVER w AS cum_remain "
		. "  FROM trace_pool tp "
		. "  JOIN locked l ON l.id = tp.id "
		. "  WINDOW w AS ("
		. "    ORDER BY "
		. "      ((tp.remain < tp.qty) IS TRUE) DESC, "
		. "      tp.in_date ASC, "
		. "      COALESCE(tp.last_used, 'epoch'::timestamptz) ASC, "
		. "      tp.id ASC"
		. "  )"
		. "), "

		; 按 need 与 cum_remain 切分当前行 take_qty
		. "alloc AS ("
		. "  SELECT "
		. "    id, trace_code, seq, remain, "
		. "    GREATEST("
		. "      LEAST("
		. "        remain, "
		. "        (SELECT need FROM params) - (cum_remain - remain)"
		. "      ), "
		. "      0"
		. "    )::int AS take_qty "
		. "  FROM picked"
		. "), "

		. "take_sum AS ("
		. "  SELECT COALESCE(sum(take_qty),0)::int AS sum_take FROM alloc"
		. "), "

		; guard：凑够 need 才写库；否则细分失败原因供上层提示
		. "guard AS ("
		. "  SELECT "
		. "    CASE WHEN ts.sum_take = (SELECT need FROM params) THEN 1 ELSE 0 END AS ok, "
		. "    CASE "
		. "      WHEN ts.sum_take = (SELECT need FROM params) THEN 'OK' "
		. "      WHEN s.cnt_any = 0 THEN 'NO_ENTRY' "
		. "      WHEN s.cnt_usable = 0 THEN 'NO_AVAILABLE' "
		. "      WHEN s.sum_usable_remain < (SELECT need FROM params) THEN 'INSUFFICIENT_TOTAL' "
		. "      ELSE 'CONCURRENCY_OR_LIMIT' "
		. "    END AS reason "
		. "  FROM stats s, take_sum ts"
		. "), "

		; 仅 guard.ok=1 时扣 remain
		. "upd AS ("
		. "  UPDATE trace_pool tp "
		. "  SET remain = tp.remain - a.take_qty "
		. "  FROM alloc a, guard g "
		. "  WHERE g.ok = 1 "
		. "    AND tp.id = a.id "
		. "    AND a.take_qty > 0 "
		. "  RETURNING a.seq, tp.id AS pool_id, a.trace_code, a.take_qty"
		. "), "

		; 仅成功路径写 PENDING txn（upsert）
		. "ins_txn AS ("
		. "  INSERT INTO trace_txn(txn_id, client_id, drug_id, spec, req_qty, status) "
		. "  SELECT txn_id, client_id, drug_id, spec, need, 'PENDING' "
		. "  FROM params, guard "
		. "  WHERE guard.ok = 1 "
		. "  ON CONFLICT (txn_id) DO UPDATE "
		. "    SET client_id    = EXCLUDED.client_id, "
		. "        drug_id      = EXCLUDED.drug_id, "
		. "        spec         = EXCLUDED.spec, "
		. "        req_qty      = EXCLUDED.req_qty, "
		. "        status       = 'PENDING', "
		. "        committed_at = NULL "
		. "  WHERE trace_txn.status = 'PENDING' "
		. "  RETURNING txn_id"
		. "), "

		; 仅成功路径重写 txn items，避免失败半写入
		. "del_items AS ("
		. "  DELETE FROM trace_txn_item "
		. "  WHERE (SELECT ok FROM guard)=1 "
		. "    AND txn_id = (SELECT txn_id FROM ins_txn) "
		. "  RETURNING 1"
		. "), "

		. "ins_items AS ("
		. "  INSERT INTO trace_txn_item(txn_id, pool_id, take_qty, trace_code) "
		. "  SELECT (SELECT txn_id FROM ins_txn), u.pool_id, u.take_qty, u.trace_code "
		. "  FROM upd u "
		. "  WHERE (SELECT ok FROM guard)=1 "
		. "  RETURNING pool_id, trace_code, take_qty"
		. ") "
		
		. "SELECT "
		. "  CASE WHEN g.ok=1 THEN 'OK' ELSE 'FAIL' END AS status, "
		. "  g.reason, "
		. "  u.seq, u.pool_id, u.trace_code, u.take_qty "
		. "FROM guard g "
		. "LEFT JOIN upd u ON g.ok=1 "
		. "ORDER BY u.seq NULLS FIRST;"

    ; ADO 显式事务：失败统一 Rollback，成功再 Commit
    try {
        if !__PG["in_txn"] {
            conn.BeginTrans()
            __PG["in_txn"] := true
        }

        r := DB_Query(bigSQL)

        if !r["ok"] {
            conn.RollbackTrans()
            __PG["in_txn"] := false
            return Map(
				"ok", false, "level", "ERR", "type", r["type"], 
				"why", "预留 SQL 执行异常：`n" r["err"], "reason", "SQL_ERROR", "err", r["err"]
			)
        }

        if (r["rows"].Length = 0) {
            conn.RollbackTrans()
            __PG["in_txn"] := false
            return Map("ok", false, "level", "ERR", "type", "[预留错误]", "why", "未返回任何结果行", "reason", "NO_RESULT")
        }

        ; 行布局：[status, reason, seq, pool_id, trace_code, take_qty]
        status := r["rows"][1][1]
        reason := r["rows"][1][2]

        if (status = "FAIL") {
            ; 业务 FAIL 虽无写入，仍 Rollback，保持“失败即回滚”语义
            conn.RollbackTrans()
            __PG["in_txn"] := false

            ; 业务 reason：NO_ENTRY / NO_AVAILABLE / INSUFFICIENT_TOTAL / CONCURRENCY_OR_LIMIT
            msg := ""
            switch reason {
                case "NO_ENTRY":
                    msg := "未录入追溯码：追溯池中不存在该药品的任何记录"
                case "NO_AVAILABLE":
                    msg := "追溯码库存不足，请及时录入"
                case "INSUFFICIENT_TOTAL":
                    msg := "追溯码库存不足，请及时录入"
                case "CONCURRENCY_OR_LIMIT":
                    msg := "并发抢占或 LIMIT 截断：请重试"
                default:
                    msg := "`n" reason
            }

            return Map(
				"ok", false, "level", "WARN", "type", "[预留错误]", 
				"why", msg "`n预留数量=" need "`n规格=" spec "`n药品=" drugId, 
				"reason", reason, "need", need, "codes", [], "items", [], 
				"skip", false)
        }

        conn.CommitTrans()
        __PG["in_txn"] := false

    } catch as e {
        try conn.RollbackTrans()
        __PG["in_txn"] := false
        return Map("ok", false, "level", "ERR", "type", "[预留错误]", "why", "`n" e.Message, "reason", "EXCEPTION", "err", e.Message)
    }

    ; 组装 codes/items；住院与门诊返回码策略不同
	if (cls = "") {
		; 未传 cls 时退化为当前活动窗口，兼容旧调用
		ctx := Util_CaptureWin("A")
		cls := ctx["cls"]
	}


    lastCode := ""
    for _, row in r["rows"] {
        ; 行布局：[status, reason, seq, pool_id, trace_code, take_qty]
        poolId := Util_ToInt(row[4])
        code   := row[5]
        take   := Util_ToInt(row[6])

        if (poolId <= 0 || take <= 0 || code = "")
            continue

        items.Push(Map("pool_id", poolId, "take", take, "code", code))

		; 住院只回最后一码；门诊回全部 codes
        if (cls = ipt) {
            lastCode := code
        } else {
            codes.Push(code)
        }
    }

    if (cls = ipt && lastCode != "")
        codes := [ lastCode ]

    ; UI_Tip("[预留成功] 需扣=" reqQty "，码数=" codes.Length, 1200)
    return Map(
		"ok", true, "type", "[预留成功]", 
		"why", "需扣=" reqQty "，码数=" codes.Length, 
		"skip", false, "codes", codes, "items", items, 
		"req_qty_effective", reqQty
	)
}


; Commit：PENDING → COMMITTED
Txn_Commit(txnId) {
    rOpen := PG_EnsureOpen()
    if !rOpen["ok"] {
        return Map("ok", false, "type", rOpen["type"], "why", "数据库链接失败：`n" rOpen["err"])
    }

    conn := __PG["conn"]
    escTxn := Util_EscapeSQL(txnId)

    sql := ""
        . "UPDATE trace_txn "
        . "SET status='COMMITTED', committed_at=now() "
        . "WHERE txn_id='" escTxn "' AND status='PENDING' "
        . "RETURNING txn_id;"

    try {
        if !__PG["in_txn"] {
            conn.BeginTrans()
            __PG["in_txn"] := true
        }

        r := DB_Query(sql)
        if !r["ok"] {
            conn.RollbackTrans()
            __PG["in_txn"] := false
            return Map("ok", false, "type", r["type"], "why", r["err"])
        }
        if (r["rows"].Length != 1) {
            conn.RollbackTrans()
            __PG["in_txn"] := false
            return Map("ok", false, "type", "[提交错误]", "why", "提交减扣失败：`n没有在 PENDING 状态的预留事务")
        }

        conn.CommitTrans()
        __PG["in_txn"] := false
        return Map("ok", true, "type", "[提交成功]")

    } catch as e {
        try conn.RollbackTrans()
        __PG["in_txn"] := false
        return Map("ok", false, "type", "[提交错误]", "why", e.Message)
    }
}

; Rollback：仅 PENDING 回补 remain
Txn_Rollback(txnId) {
    rOpen := PG_EnsureOpen()
    if !rOpen["ok"] {
        return Map("ok", false, "type", rOpen["type"], "why", "数据库链接失败：`n" rOpen["err"])
    }

    conn := __PG["conn"]
    escTxn := Util_EscapeSQL(txnId)

    ; 先 PENDING→ROLLED_BACK 占住 txn，再回补库存，避免并发双回补
    sql := ""
        . "WITH tx AS ("
        . "  UPDATE trace_txn "
        . "  SET status='ROLLED_BACK' "
        . "  WHERE txn_id='" escTxn "' AND status='PENDING' "
        . "  RETURNING txn_id"
        . "), "
        . "upd AS ("
        . "  UPDATE trace_pool tp "
        . "  SET remain = tp.remain + tti.take_qty "
        . "  FROM trace_txn_item tti, tx "
        . "  WHERE tti.txn_id = tx.txn_id "
        . "    AND tti.pool_id = tp.id "
        . "  RETURNING tp.id"
        . ") "
        . "SELECT count(*)::int FROM upd;"

    try {
        if !__PG["in_txn"] {
            conn.BeginTrans()
            __PG["in_txn"] := true
        }

        r := DB_Query(sql)
        if !r["ok"] {
            conn.RollbackTrans()
            __PG["in_txn"] := false
            return Map("ok", false, "type", r["type"], "why", r["err"])
        }

        restored := (r["rows"].Length > 0) ? Util_ToInt(r["rows"][1][1]) : 0
        if (restored = 0) {
            conn.RollbackTrans()
            __PG["in_txn"] := false
            return Map("ok", false, "type", "[回滚错误]", "why", "回滚减扣失败：`n没有在 PENDING 状态的预留事务")
        }

        conn.CommitTrans()
        __PG["in_txn"] := false
        return Map("ok", true, "type", "[回滚成功]", "restored_rows", restored)

    } catch as e {
        try conn.RollbackTrans()
        __PG["in_txn"] := false
        return Map("ok", false, "type", "[回滚错误]", "why", e.Message)
    }
}

; 超时 PENDING 自愈：异常退出后自动回补被扣库存
Txn_CleanupPending(timeoutMinutes := 10, maxBatch := 200) {
    ; 无 created_at 时用 txn_id 前缀时间（yyyyMMddHHmmss_XXXXX）判超时
    rOpen := PG_EnsureOpen()
    if !rOpen["ok"]
        return rOpen

    mins := Util_ToInt(timeoutMinutes, 10)
    if (mins < 1)
        mins := 1

    cutoff := FormatTime(DateAdd(A_Now, -mins, "Minutes"), "yyyyMMddHHmmss")

    ; 优先 created_at；旧表无该列时回退 txn_id 前缀时间
    hasCreatedAt := false
    qCol := ""
        . "SELECT 1 "
        . "FROM information_schema.columns "
        . "WHERE table_schema='public' "
        . "  AND table_name='trace_txn' "
        . "  AND column_name='created_at' "
        . "LIMIT 1;"

    rCol := DB_Query(qCol)
    if (IsObject(rCol) && rCol.Has("ok") && rCol["ok"] && rCol["rows"].Length > 0)
        hasCreatedAt := true

    if (hasCreatedAt) {
        sql := ""
            . "SELECT txn_id "
            . "FROM trace_txn "
            . "WHERE status='PENDING' "
            . "  AND created_at < (now() - (" (mins+0) " * interval '1 minute')) "
            . "ORDER BY created_at, txn_id "
            . "LIMIT " (maxBatch+0) ";"
    } else {
        sql := ""
            . "SELECT txn_id "
            . "FROM trace_txn "
            . "WHERE status='PENDING' "
            . "  AND left(txn_id,14) < '" Util_EscapeSQL(cutoff) "' "
            . "ORDER BY txn_id "
            . "LIMIT " (maxBatch+0) ";"
    }

    r := DB_Query(sql)
    if !r["ok"]
        return r

    cleaned := 0
    for _, row in r["rows"] {
        txnId := row[1]
        if (Trim(txnId) = "")
            continue
        rr := Txn_Rollback(txnId)
        if (IsObject(rr) && rr.Has("ok") && rr["ok"])
            cleaned++
    }
    return Map("ok", true, "cleaned", cleaned, "cutoff", cutoff)
}
