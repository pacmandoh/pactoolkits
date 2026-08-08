; 追溯码事务支持预留、提交和回滚
; rem 模式：只扣拆零粒；full 模式：整盒行（remain=qty）+ 拆零粒 同 txn
; 计划：txn_plan.ahk（用 A_LineFile，测试只 #Include pg_exec 也能解析）
#Include "%A_LineFile%\..\txn_plan.ahk"

global __PG := Map(
	"conn", 0,
	"in_txn", false
)

; injectMode: "rem" | "full"；有 bySpec 时走计划，否则 reqQty 为拆零粒直接预留（测试）
Txn_ReservePick(txnId, clientId, drugId, spec, reqQty, opt, ipt, bySpec := 0, cls := "", alreadyScanned := 0, injectMode := "rem") {
	isOpt := (Trim("" opt) != "" && cls = opt)
	injectMode := StrLower(Trim("" injectMode))
	if (injectMode != "full")
		injectMode := "rem"

	wholeN := 0
	remNeed := Util_ToInt(reqQty, 0)

	if IsObject(bySpec) {
		plan := Txn_PlanPick(bySpec, injectMode, isOpt, drugId, spec, alreadyScanned)
		if !plan["ok"]
			return plan
		if (plan.Has("skip") && plan["skip"])
			return plan
		wholeN := Util_ToInt(plan["wholePick"], 0)
		remNeed := Util_ToInt(plan["remNeed"], 0)
		Log_Debug("txn.reserve.plan", "采用计划", Map(
			"txn", txnId, "injectMode", injectMode, "wholeN", wholeN, "remNeed", remNeed
		))
	}

	if (wholeN <= 0 && remNeed <= 0) {
		return Map("ok", true, "skip", true, "level", "Info",
			"message", "[跳过取码] 无需预留", "need", 0, "codes", [], "items", [])
	}

	return Txn_ReserveAlloc(txnId, clientId, drugId, spec, wholeN, remNeed, cls, isOpt, injectMode)
}

; wholeN：整盒行数（remain=qty）；remNeed：拆零粒
; 码选取：门诊全部；住院 full 整盒全部，拆零侧仅末码；住院 rem 仅末码
Txn_ReserveAlloc(txnId, clientId, drugId, spec, wholeN, remNeed, cls, isOpt := false, injectMode := "rem") {
	codes := []
	items := []
	t0 := A_TickCount
	wholeN := Max(0, Util_ToInt(wholeN, 0))
	remNeed := Max(0, Util_ToInt(remNeed, 0))
	Log_Debug("txn.reserve.begin", "预留开始", Map(
		"txn", txnId, "drugId", drugId, "spec", spec,
		"wholeN", wholeN, "remNeed", remNeed, "cls", cls,
		"isOpt", isOpt, "injectMode", injectMode
	))

	rOpen := PG_EnsureOpen()
	if !rOpen["ok"]
		return rOpen

	conn := __PG["conn"]
	escTxn := Util_EscapeSQL(txnId)
	escClient := Util_EscapeSQL(clientId)
	escDrug := Util_EscapeSQL(drugId)
	escSpec := Util_EscapeSQL(spec)

	; 两段锁：整盒 remain=qty；拆零 remain>0 排除已锁整盒，优先已拆记录
	bigSQL := ""
		. "WITH "
		. "params AS ("
		. "  SELECT "
		. "    '" escTxn "'::text    AS txn_id, "
		. "    '" escClient "'::text AS client_id, "
		. "    '" escDrug "'::text   AS drug_id, "
		. "    '" escSpec "'::text   AS spec, "
		. "    " (wholeN + 0) "::int AS whole_n, "
		. "    " (remNeed + 0) "::int AS rem_need"
		. "), "
		. "stats AS ("
		. "  SELECT "
		. "    count(*)::int AS cnt_any, "
		. "    count(*) FILTER (WHERE tp.status=1 AND tp.remain > 0)::int AS cnt_usable, "
		. "    count(*) FILTER (WHERE tp.status=1 AND tp.remain = tp.qty)::int AS cnt_whole, "
		. "    COALESCE(sum(tp.remain) FILTER (WHERE tp.status=1 AND tp.remain > 0), 0)::int AS sum_usable_remain "
		. "  FROM trace_pool tp, params p "
		. "  WHERE tp.drug_id = p.drug_id "
		. "    AND tp.spec    = p.spec "
		. "), "
		. "whole_locked AS MATERIALIZED ("
		. "  SELECT tp.id "
		. "  FROM trace_pool tp, params p "
		. "  WHERE p.whole_n > 0 "
		. "    AND tp.drug_id = p.drug_id "
		. "    AND tp.spec    = p.spec "
		. "    AND tp.status  = 1 "
		. "    AND tp.remain  = tp.qty "
		. "  ORDER BY "
		. "    tp.in_date ASC, "
		. "    COALESCE(tp.last_used, 'epoch'::timestamptz) ASC, "
		. "    tp.id ASC "
		. "  FOR UPDATE SKIP LOCKED "
		. "  LIMIT (SELECT whole_n FROM params)"
		. "), "
		. "whole_alloc AS ("
		. "  SELECT "
		. "    tp.id, tp.trace_code, tp.remain AS take_qty, "
		. "    row_number() OVER ("
		. "      ORDER BY tp.in_date ASC, COALESCE(tp.last_used, 'epoch'::timestamptz) ASC, tp.id ASC"
		. "    )::int AS seq "
		. "  FROM trace_pool tp "
		. "  JOIN whole_locked w ON w.id = tp.id"
		. "), "
		. "rem_locked AS MATERIALIZED ("
		. "  SELECT tp.id "
		. "  FROM trace_pool tp, params p "
		. "  WHERE p.rem_need > 0 "
		. "    AND tp.drug_id = p.drug_id "
		. "    AND tp.spec    = p.spec "
		. "    AND tp.status  = 1 "
		. "    AND tp.remain  > 0 "
		. "    AND NOT EXISTS (SELECT 1 FROM whole_locked w WHERE w.id = tp.id) "
		. "  ORDER BY "
		. "    ((tp.remain < tp.qty) IS TRUE) DESC, "
		. "    tp.in_date ASC, "
		. "    COALESCE(tp.last_used, 'epoch'::timestamptz) ASC, "
		. "    tp.id ASC "
		. "  FOR UPDATE SKIP LOCKED "
		. "  LIMIT LEAST(GREATEST((SELECT rem_need FROM params)*2, 50), 5000)"
		. "), "
		. "rem_picked AS ("
		. "  SELECT "
		. "    tp.id, tp.trace_code, tp.remain, tp.qty, "
		. "    row_number() OVER w AS seq, "
		. "    sum(tp.remain) OVER w AS cum_remain "
		. "  FROM trace_pool tp "
		. "  JOIN rem_locked l ON l.id = tp.id "
		. "  WINDOW w AS ("
		. "    ORDER BY "
		. "      ((tp.remain < tp.qty) IS TRUE) DESC, "
		. "      tp.in_date ASC, "
		. "      COALESCE(tp.last_used, 'epoch'::timestamptz) ASC, "
		. "      tp.id ASC"
		. "  )"
		. "), "
		. "rem_alloc AS ("
		. "  SELECT "
		. "    id, trace_code, seq, remain, "
		. "    GREATEST("
		. "      LEAST("
		. "        remain, "
		. "        (SELECT rem_need FROM params) - (cum_remain - remain)"
		. "      ), "
		. "      0"
		. "    )::int AS take_qty "
		. "  FROM rem_picked"
		. "), "
		. "whole_sum AS ("
		. "  SELECT COALESCE(count(*),0)::int AS n, COALESCE(sum(take_qty),0)::int AS sum_take FROM whole_alloc"
		. "), "
		. "rem_sum AS ("
		. "  SELECT COALESCE(sum(take_qty),0)::int AS sum_take FROM rem_alloc"
		. "), "
		. "guard AS ("
		. "  SELECT "
		. "    CASE "
		. "      WHEN ws.n = (SELECT whole_n FROM params) "
		. "       AND rs.sum_take = (SELECT rem_need FROM params) THEN 1 "
		. "      ELSE 0 "
		. "    END AS ok, "
		. "    CASE "
		. "      WHEN ws.n = (SELECT whole_n FROM params) "
		. "       AND rs.sum_take = (SELECT rem_need FROM params) THEN 'OK' "
		. "      WHEN s.cnt_any = 0 THEN 'NO_ENTRY' "
		. "      WHEN (SELECT whole_n FROM params) > 0 AND s.cnt_whole < (SELECT whole_n FROM params) THEN 'NO_AVAILABLE' "
		. "      WHEN (SELECT rem_need FROM params) > 0 AND s.cnt_usable = 0 THEN 'NO_AVAILABLE' "
		. "      WHEN (SELECT rem_need FROM params) > 0 "
		. "       AND (s.sum_usable_remain - COALESCE(ws.sum_take, 0)) < (SELECT rem_need FROM params) "
		. "        THEN 'INSUFFICIENT_TOTAL' "
		. "      ELSE 'CONCURRENCY_OR_LIMIT' "
		. "    END AS reason, "
		. "    (ws.sum_take + rs.sum_take)::int AS req_qty "
		. "  FROM stats s, whole_sum ws, rem_sum rs"
		. "), "
		. "upd_whole AS ("
		. "  UPDATE trace_pool tp "
		. "  SET remain = tp.remain - a.take_qty "
		. "  FROM whole_alloc a, guard g "
		. "  WHERE g.ok = 1 "
		. "    AND tp.id = a.id "
		. "    AND a.take_qty > 0 "
		. "  RETURNING a.seq, tp.id AS pool_id, a.trace_code, a.take_qty"
		. "), "
		. "upd_rem AS ("
		. "  UPDATE trace_pool tp "
		. "  SET remain = tp.remain - a.take_qty "
		. "  FROM rem_alloc a, guard g "
		. "  WHERE g.ok = 1 "
		. "    AND tp.id = a.id "
		. "    AND a.take_qty > 0 "
		. "  RETURNING a.seq + (SELECT whole_n FROM params), tp.id AS pool_id, a.trace_code, a.take_qty"
		. "), "
		. "upd AS ("
		. "  SELECT * FROM upd_whole "
		. "  UNION ALL "
		. "  SELECT * FROM upd_rem"
		. "), "
		. "ins_txn AS ("
		. "  INSERT INTO trace_txn(txn_id, client_id, drug_id, spec, req_qty, status) "
		. "  SELECT p.txn_id, p.client_id, p.drug_id, p.spec, g.req_qty, 'PENDING' "
		. "  FROM params p, guard g "
		. "  WHERE g.ok = 1 "
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

	try {
		if !__PG["in_txn"] {
			conn.BeginTrans()
			__PG["in_txn"] := true
		}

		r := DB_Query(bigSQL)

		if !r["ok"] {
			conn.RollbackTrans()
			__PG["in_txn"] := false
			detail := r.Has("message") ? r["message"] : r["err"]
			Log_Debug("txn.reserve.sql_fail", "预留 SQL 异常", Map(
				"txn", txnId, "wholeN", wholeN, "remNeed", remNeed, "elapsedMs", A_TickCount - t0
			))
			return Map(
				"ok", false, "level", r["level"],
				"message", "[预留错误]`n预留 SQL 执行异常：`n" detail,
				"reason", "SQL_ERROR", "err", detail
			)
		}

		if (r["rows"].Length = 0) {
			conn.RollbackTrans()
			__PG["in_txn"] := false
			Log_Debug("txn.reserve.no_result", "预留无结果行", Map("txn", txnId))
			return Map("ok", false, "level", "Error", "message", "[预留错误]`n未返回任何结果行", "reason", "NO_RESULT")
		}

		status := r["rows"][1][1]
		reason := r["rows"][1][2]

		if (status = "FAIL") {
			conn.RollbackTrans()
			__PG["in_txn"] := false
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
					msg := reason
			}
			Log_Debug("txn.reserve.guard_fail", "预留 guard 失败", Map(
				"txn", txnId, "reason", reason, "wholeN", wholeN, "remNeed", remNeed,
				"drugId", drugId, "spec", spec, "elapsedMs", A_TickCount - t0
			))
			return Map(
				"ok", false, "level", "Warn",
				"message", "[预留错误]`n" msg "`n整盒数=" wholeN "`n拆零粒=" remNeed "`n规格=" spec "`n药品=" drugId,
				"reason", reason, "need", wholeN + remNeed, "codes", [], "items", [],
				"skip", false)
		}

		conn.CommitTrans()
		__PG["in_txn"] := false

	} catch as e {
		try conn.RollbackTrans()
		__PG["in_txn"] := false
		Log_Debug("txn.reserve.exception", "预留异常", Map("txn", txnId, "err", e.Message))
		return Map("ok", false, "level", "Error", "message", "[预留错误]`n`n" e.Message, "reason", "EXCEPTION", "err", e.Message)
	}

	; 组装贴码列表（items 全量；贴码：门诊全码；住院 full 整盒全+拆零末码；住院 rem/纯拆零末码）
	lastRemCode := ""
	lastAnyCode := ""
	sumTake := 0
	for _, row in r["rows"] {
		; 列：status, reason, seq, pool_id, trace_code, take_qty
		seq := Util_ToInt(row[3])
		poolId := Util_ToInt(row[4])
		code := row[5]
		take := Util_ToInt(row[6])

		if (poolId <= 0 || take <= 0 || code = "")
			continue

		sumTake += take
		items.Push(Map("pool_id", poolId, "take", take, "code", code, "seq", seq))
		lastAnyCode := code

		if isOpt {
			codes.Push(code)
			continue
		}
		if (injectMode = "full" && wholeN > 0) {
			if (seq > 0 && seq <= wholeN)
				codes.Push(code)
			else
				lastRemCode := code
		}
	}

	if !isOpt {
		if (injectMode = "full" && wholeN > 0) {
			if (lastRemCode != "")
				codes.Push(lastRemCode)
		} else if (lastAnyCode != "") {
			codes := [lastAnyCode]
		}
	}

	tails := []
	for _, c in codes
		tails.Push((StrLen(c) <= 4) ? c : SubStr(c, -3))
	Log_Debug("txn.reserve.ok", "预留成功", Map(
		"txn", txnId, "wholeN", wholeN, "remNeed", remNeed, "sumTake", sumTake,
		"codes", codes.Length, "items", items.Length, "cls", cls,
		"isOpt", isOpt, "injectMode", injectMode, "codeTails", tails,
		"elapsedMs", A_TickCount - t0
	))
	return Map(
		"ok", true, "level", "Info", "message", "[预留成功]`n整盒=" wholeN "，拆零粒=" remNeed "，码数=" codes.Length,
		"skip", false, "codes", codes, "items", items,
		"req_qty_effective", sumTake, "whole_n", wholeN, "rem_need", remNeed
	)
}

; 提交操作将任务状态从 PENDING 转换为 COMMITTED
Txn_Commit(txnId) {
	Log_Debug("txn.commit.begin", "提交开始", Map("txn", txnId))
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"] {
		Log_Debug("txn.commit.db_closed", "提交时库未开", Map("txn", txnId))
		return rOpen
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
			Log_Debug("txn.commit.sql_fail", "提交 SQL 失败", Map("txn", txnId))
			return r
		}
		if (r["rows"].Length != 1) {
			conn.RollbackTrans()
			__PG["in_txn"] := false
			Log_Debug("txn.commit.not_pending", "无 PENDING 可提交", Map("txn", txnId, "rows", r["rows"].Length))
			return Map("ok", false, "level", "Error", "message", "[提交错误]`n提交减扣失败：`n没有在 PENDING 状态的预留事务")
		}

		conn.CommitTrans()
		__PG["in_txn"] := false
		Log_Debug("txn.commit.ok", "提交成功", Map("txn", txnId))
		return Map("ok", true, "level", "Info", "message", "[提交成功]")

	} catch as e {
		try conn.RollbackTrans()
		__PG["in_txn"] := false
		Log_Debug("txn.commit.exception", "提交异常", Map("txn", txnId, "err", e.Message))
		return Map("ok", false, "level", "Error", "message", "[提交错误]`n" e.Message, "err", e.Message)
	}
}

; 回滚仅恢复仍处于 PENDING 状态的事务
Txn_Rollback(txnId) {
	Log_Debug("txn.rollback.begin", "回滚开始", Map("txn", txnId))
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"] {
		Log_Debug("txn.rollback.db_closed", "回滚时库未开", Map("txn", txnId))
		return rOpen
	}

	conn := __PG["conn"]
	escTxn := Util_EscapeSQL(txnId)

	; 先以状态转换取得回滚权，再恢复库存，避免并发重复回补
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
			Log_Debug("txn.rollback.sql_fail", "回滚 SQL 失败", Map("txn", txnId))
			return r
		}

		restored := (r["rows"].Length > 0) ? Util_ToInt(r["rows"][1][1]) : 0
		if (restored = 0) {
			conn.RollbackTrans()
			__PG["in_txn"] := false
			Log_Debug("txn.rollback.not_pending", "无 PENDING 可回滚", Map("txn", txnId))
			return Map("ok", false, "level", "Error", "message", "[回滚错误]`n回滚减扣失败：`n没有在 PENDING 状态的预留事务")
		}

		conn.CommitTrans()
		__PG["in_txn"] := false
		Log_Debug("txn.rollback.ok", "回滚成功", Map("txn", txnId, "restored", restored))
		return Map("ok", true, "level", "Info", "message", "[回滚成功]", "restored_rows", restored)

	} catch as e {
		try conn.RollbackTrans()
		__PG["in_txn"] := false
		Log_Debug("txn.rollback.exception", "回滚异常", Map("txn", txnId, "err", e.Message))
		return Map("ok", false, "level", "Error", "message", "[回滚错误]`n" e.Message, "err", e.Message)
	}
}

; 超时 PENDING 恢复用于回补异常退出前已预留的库存
Txn_CleanupPending(timeoutMinutes := 10, maxBatch := 200) {
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"]
		return rOpen

	mins := Util_ToInt(timeoutMinutes, 10)
	if (mins < 1)
		mins := 1

	sql := ""
		. "SELECT txn_id "
		. "FROM trace_txn "
		. "WHERE status='PENDING' "
		. "  AND created_at < (now() - (" (mins + 0) " * interval '1 minute')) "
		. "ORDER BY created_at, txn_id "
		. "LIMIT " (maxBatch + 0) ";"

	r := DB_Query(sql)
	if !r["ok"] {
		Log_Debug("txn.cleanup.query_fail", "清理 PENDING 查询失败", Map("mins", mins))
		return r
	}

	cleaned := 0
	for _, row in r["rows"] {
		txnId := row[1]
		if (Trim(txnId) = "")
			continue
		rr := Txn_Rollback(txnId)
		if (IsObject(rr) && rr.Has("ok") && rr["ok"])
			cleaned++
	}
	if (cleaned > 0 || r["rows"].Length > 0)
		Log_Debug("txn.cleanup.done", "清理 PENDING 完成", Map(
			"found", r["rows"].Length, "cleaned", cleaned, "mins", mins
		))
	return Map("ok", true, "cleaned", cleaned)
}
