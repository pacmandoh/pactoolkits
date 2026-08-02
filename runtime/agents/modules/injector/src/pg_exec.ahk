; 追溯码事务支持预留、提交和回滚；拆零仅扣减余数，完整包装不占用追溯码

global __PG := Map(
	"conn", 0,
	"in_txn", false
)

Txn_ReservePick(txnId, clientId, drugId, spec, reqQty, opt, ipt, bySpec := 0, cls := "") {
	need := reqQty
	codes := []
	items := []

	; 仅拆零业务需要预留追溯码，扣减量按单盒数量的余数计算
	if IsObject(bySpec) {
		splitFlag := bySpec.Has("拆零标签||拆零") ? Trim(bySpec["拆零标签||拆零"]) : ""
		qtyVal := bySpec.Has("数量") ? bySpec["数量"] : ""

		; 非拆零业务不得从追溯池预留记录
		if (splitFlag = "否") {
			return Map("ok", true, "skip", true, "level", "Info", "message", "[跳过取码]`n未拆零药物", "need", 0, "codes", [], "items", [])
		}

		qtyN := 0
		if IsInteger(qtyVal)
			qtyN := qtyVal
		else if RegExMatch(Trim(qtyVal), "^\d+$")
			qtyN := Integer(qtyVal)

		if (qtyN <= 0) {
			return Map("ok", false, "level", "Warn", "message", "[解析错误]`n数量无效：" qtyVal)
		}

		; 单盒数量以 drug_index.qty 为准，用于计算拆零余数
		qDbQty := ""
			. "SELECT qty FROM drug_index "
			. "WHERE drug_id='" Util_EscapeSQL(drugId) "' "
			. "  AND spec='" Util_EscapeSQL(spec) "' "
			. "LIMIT 1;"
		rr := DB_Query(qDbQty)
		if !rr["ok"] {
			return Map("ok", false, "level", "Error", "message", "[查询错误]`n读取药品索引中单盒数量失败：`n" rr["err"])
		}
		if (rr["rows"].Length = 0) {
			return Map(
				"ok", false, "level", "Warn", "message", "[查询错误]`n药品索引未配置该药品规格（无法计算拆零余数）：`n" "药品=" drugId "`n规格=" spec
			)
		}

		dbQty := Util_ToInt(rr["rows"][1][1])
		if (dbQty <= 0) {
			return Map("ok", false, "level", "Error", "message", "[查询错误]`n药品索引中单盒数量非法：" dbQty)
		}

		rem := Mod(qtyN, dbQty)

		; 余数为零表示完整包装，不预留追溯码
		if (rem = 0) {
			return Map("ok", true, "skip", true, "level", "Info", "message", "[跳过取码]`n整包装（数量为整包整数倍，单盒数量=" dbQty "）"
				, "need", 0, "codes", [], "items", [], "qty", qtyN, "dbQty", dbQty)
		}

		; 拆零业务的需求量必须使用余数，不能使用原始行数量
		need := rem
		reqQty := need
	}

	; 单条 CTE 使用 SKIP LOCKED 并保持索引顺序，降低并发预留冲突
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"]
		return rOpen

	conn := __PG["conn"]

	escTxn := Util_EscapeSQL(txnId)
	escClient := Util_EscapeSQL(clientId)
	escDrug := Util_EscapeSQL(drugId)
	escSpec := Util_EscapeSQL(spec)

	bigSQL := ""
		. "WITH "
		. "params AS ("
		. "  SELECT "
		. "    '" escTxn "'::text    AS txn_id, "
		. "    '" escClient "'::text AS client_id, "
		. "    '" escDrug "'::text   AS drug_id, "
		. "    '" escSpec "'::text   AS spec, "
		. "    " (need + 0) "::int     AS need"
		. "), "
		; 可用性统计与候选选择共用一次 trace_pool 扫描
		. "stats AS ("
		. "  SELECT "
		. "    count(*)::int AS cnt_any, "
		. "    count(*) FILTER (WHERE tp.status=1 AND tp.remain > 0)::int AS cnt_usable, "
		. "    COALESCE(sum(tp.remain) FILTER (WHERE tp.status=1 AND tp.remain > 0), 0)::int AS sum_usable_remain "
		. "  FROM trace_pool tp, params p "
		. "  WHERE tp.drug_id = p.drug_id "
		. "    AND tp.spec    = p.spec "
		. "), "
		; ORDER BY 必须与 idx_trace_pick_ultra 完全一致，确保查询计划使用该索引
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
		; 共用 WINDOW 定义以避免重复排序
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
		; 当前行扣减量由需求量和累计可用量共同确定
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
		; 仅在总可用量满足需求时写入，并为不足场景返回可区分的失败原因
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
		; 只有完整满足需求时才扣减剩余数量
		. "upd AS ("
		. "  UPDATE trace_pool tp "
		. "  SET remain = tp.remain - a.take_qty "
		. "  FROM alloc a, guard g "
		. "  WHERE g.ok = 1 "
		. "    AND tp.id = a.id "
		. "    AND a.take_qty > 0 "
		. "  RETURNING a.seq, tp.id AS pool_id, a.trace_code, a.take_qty"
		. "), "
		; PENDING 事务仅在预留成功后写入，并通过 upsert 保证幂等
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
		; 事务明细仅在成功路径重写，避免失败时留下不完整记录
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

	; 使用 ADO 显式事务保证预留数据与事务记录同时提交或回滚
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
			return Map(
				"ok", false, "level", r["level"],
				"message", "[预留错误]`n预留 SQL 执行异常：`n" detail,
				"reason", "SQL_ERROR", "err", detail
			)
		}

		if (r["rows"].Length = 0) {
			conn.RollbackTrans()
			__PG["in_txn"] := false
			return Map("ok", false, "level", "Error", "message", "[预留错误]`n未返回任何结果行", "reason", "NO_RESULT")
		}

		; 查询列顺序是跨 ADO 读取的固定契约，修改 SQL 时必须同步此处索引
		status := r["rows"][1][1]
		reason := r["rows"][1][2]

		if (status = "FAIL") {
			; 业务失败即使没有写入也显式回滚，保持统一事务语义
			conn.RollbackTrans()
			__PG["in_txn"] := false

			; 失败原因区分无索引、无可用码、总量不足以及并发或限制冲突
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
				"ok", false, "level", "Warn", "message", "[预留错误]`n" msg "`n预留数量=" need "`n规格=" spec "`n药品=" drugId,
				"reason", reason, "need", need, "codes", [], "items", [],
				"skip", false)
		}

		conn.CommitTrans()
		__PG["in_txn"] := false

	} catch as e {
		try conn.RollbackTrans()
		__PG["in_txn"] := false
		return Map("ok", false, "level", "Error", "message", "[预留错误]`n`n" e.Message, "reason", "EXCEPTION", "err", e.Message)
	}

	; 住院与门诊使用不同的返回码集合策略
	if (cls = "") {
		; 未提供窗口类时使用当前活动窗口，以保持现有调用兼容
		ctx := Util_CaptureWin("A")
		cls := ctx["cls"]
	}

	lastCode := ""
	for _, row in r["rows"] {
		; 查询列顺序是跨 ADO 读取的固定契约，修改 SQL 时必须同步此处索引
		poolId := Util_ToInt(row[4])
		code := row[5]
		take := Util_ToInt(row[6])

		if (poolId <= 0 || take <= 0 || code = "")
			continue

		items.Push(Map("pool_id", poolId, "take", take, "code", code))

		; 住院流程仅使用最后一个码，门诊流程使用全部预留码
		if (cls = ipt) {
			lastCode := code
		} else {
			codes.Push(code)
		}
	}

	if (cls = ipt && lastCode != "")
		codes := [lastCode]

	return Map(
		"ok", true, "level", "Info", "message", "[预留成功]`n需扣=" reqQty "，码数=" codes.Length,
		"skip", false, "codes", codes, "items", items,
		"req_qty_effective", reqQty
	)
}

; 提交操作将任务状态从 PENDING 转换为 COMMITTED
Txn_Commit(txnId) {
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"]
		return rOpen

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
			return r
		}
		if (r["rows"].Length != 1) {
			conn.RollbackTrans()
			__PG["in_txn"] := false
			return Map("ok", false, "level", "Error", "message", "[提交错误]`n提交减扣失败：`n没有在 PENDING 状态的预留事务")
		}

		conn.CommitTrans()
		__PG["in_txn"] := false
		return Map("ok", true, "level", "Info", "message", "[提交成功]")

	} catch as e {
		try conn.RollbackTrans()
		__PG["in_txn"] := false
		return Map("ok", false, "level", "Error", "message", "[提交错误]`n" e.Message, "err", e.Message)
	}
}

; 回滚仅恢复仍处于 PENDING 状态的事务
Txn_Rollback(txnId) {
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"]
		return rOpen

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
			return r
		}

		restored := (r["rows"].Length > 0) ? Util_ToInt(r["rows"][1][1]) : 0
		if (restored = 0) {
			conn.RollbackTrans()
			__PG["in_txn"] := false
			return Map("ok", false, "level", "Error", "message", "[回滚错误]`n回滚减扣失败：`n没有在 PENDING 状态的预留事务")
		}

		conn.CommitTrans()
		__PG["in_txn"] := false
		return Map("ok", true, "level", "Info", "message", "[回滚成功]", "restored_rows", restored)

	} catch as e {
		try conn.RollbackTrans()
		__PG["in_txn"] := false
		return Map("ok", false, "level", "Error", "message", "[回滚错误]`n" e.Message, "err", e.Message)
	}
}

; 超时 PENDING 恢复用于回补异常退出前已预留的库存
Txn_CleanupPending(timeoutMinutes := 10, maxBatch := 200) {
	; 缺少 created_at 时使用 txn_id 的时间前缀判断超时，以兼容旧表结构
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"]
		return rOpen

	mins := Util_ToInt(timeoutMinutes, 10)
	if (mins < 1)
		mins := 1

	cutoff := FormatTime(DateAdd(A_Now, -mins, "Minutes"), "yyyyMMddHHmmss")

	; 优先使用 created_at，旧表缺少该列时改用 txn_id 时间前缀
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
			. "  AND created_at < (now() - (" (mins + 0) " * interval '1 minute')) "
			. "ORDER BY created_at, txn_id "
			. "LIMIT " (maxBatch + 0) ";"
	} else {
		sql := ""
			. "SELECT txn_id "
			. "FROM trace_txn "
			. "WHERE status='PENDING' "
			. "  AND left(txn_id,14) < '" Util_EscapeSQL(cutoff) "' "
			. "ORDER BY txn_id "
			. "LIMIT " (maxBatch + 0) ";"
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
