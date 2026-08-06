; 仓库任务 SQL：领取/防重/明细/状态回写/事件与结算
global __MSFX_COL := Map()

Msfx_ClaimInjectTaskByTarget(clientId, drugId, spec) {
	escClient := Util_EscapeSQL(clientId)
	escDrug := Util_EscapeSQL(drugId)
	escSpec := Util_EscapeSQL(spec)

	sql := ""
		. "SELECT task_id, bill_id, source_bill_code, mapped_drug_id, mapped_spec, total_codes "
		. "FROM msfx_claim_inject_task_by_target('"
		. escClient "', '"
		. escDrug "', '"
		. escSpec "');"
	r := DB_Query(sql)
	if !r["ok"] {
		return Map("ok", false, "level", "Error", "message", "[SQL 错误]`n" r["err"])
	}

	tasks := []
	for _, row in r["rows"] {
		tasks.Push(Map(
			"task_id", Util_ToInt(row[1]),
			"bill_id", Util_ToInt(row[2]),
			"source_bill_code", row[3],
			"mapped_drug_id", row[4],
			"mapped_spec", row[5],
			"total_codes", Util_ToInt(row[6])
		))
	}

	return Map("ok", true, "tasks", tasks)
}

Msfx_HasWarehouseSuccessTask(warehouseBillNo, drugId, spec, rowFingerprint := "") {
	escBill := Util_EscapeSQL(warehouseBillNo)
	escDrug := Util_EscapeSQL(drugId)
	escSpec := Util_EscapeSQL(spec)
	escFp := Util_EscapeSQL(rowFingerprint)
	sql := ""
		. "SELECT msfx_has_warehouse_success_task('"
		. escBill "', '"
		. escDrug "', '"
		. escSpec "', NULLIF('" escFp "', '')) AS has_success;"
	r := DB_Query(sql)
	if !r["ok"]
		return Map("ok", false, "level", "Error", "message", "[SQL 错误]`n" r["err"])
	exists := false
	if (r["rows"].Length > 0) {
		v := StrUpper(Trim("" r["rows"][1][1]))
		exists := (v = "TRUE" || v = "T" || v = "1")
	}
	return Map("ok", true, "exists", exists)
}

Msfx_GetPendingTaskCodes(taskId) {
	escTask := Util_ToInt(taskId, 0)
	if (escTask <= 0)
		return Map("ok", false, "level", "Error", "message", "[SQL 错误]`ntask_id 非法")

	codeCol := Msfx_GetTaskCodeColumnName()
	sql := ""
		. "SELECT "
		. "  tc.seq, tc." codeCol ", COALESCE(tc.staging_id, 0), "
		. "  COALESCE(s.source_code_level_1, ''), "
		. "  COALESCE(s.source_code_level_2, ''), "
		. "  COALESCE(s.source_code_level_3, ''), "
		. "  COALESCE(s.source_code_level_4, ''), "
		. "  COALESCE(s.source_code_level_5, '') "
		. "FROM msfx_inject_task_code tc "
		. "LEFT JOIN msfx_code_staging s ON s.id = tc.staging_id "
		. "WHERE tc.task_id = " escTask " "
		. "  AND tc.status = 'PENDING' "
		. "ORDER BY tc.seq, tc." codeCol ";"
	r := DB_Query(sql)
	if !r["ok"] {
		return Map("ok", false, "level", "Error", "message", "[SQL 错误]`n" r["err"])
	}

	rows := []
	for _, row in r["rows"] {
		rows.Push(Map(
			"seq", Util_ToInt(row[1]),
			"leaf_code", row[2],
			"staging_id", Util_ToInt(row[3]),
			"l1", row[4],
			"l2", row[5],
			"l3", row[6],
			"l4", row[7],
			"l5", row[8]
		))
	}
	return Map("ok", true, "rows", rows)
}

Msfx_BatchUpdateTaskCodes(taskId, leafCodes, status, verifyResult := "", errMsg := "") {
	escTask := Util_ToInt(taskId, 0)
	if (escTask <= 0)
		return Map("ok", false, "err", "task_id 非法")
	if !IsObject(leafCodes) || (leafCodes.Length = 0)
		return Map("ok", true)

	codeCol := Msfx_GetTaskCodeColumnName()
	inList := Msfx_SqlListQuoted(leafCodes)
	if (inList = "")
		return Map("ok", true)

	escStatus := Util_EscapeSQL(status)
	escVerify := Util_EscapeSQL(verifyResult)
	escErr := Util_EscapeSQL(errMsg)
	sql := ""
		. "UPDATE msfx_inject_task_code "
		. "SET status='" escStatus "', "
		. "    injected_at = CASE WHEN '" escStatus "' = 'INJECTED' THEN now() ELSE injected_at END, "
		. "    verify_result = NULLIF('" escVerify "', ''), "
		. "    err_msg = NULLIF('" escErr "', '') "
		. "WHERE task_id=" escTask " AND " codeCol " IN (" inList ");"
	return DB_Exec(sql)
}

Msfx_BatchUpdateStagingCodes(stagingIds, codeStatus, errMsg := "") {
	if !IsObject(stagingIds) || (stagingIds.Length = 0)
		return Map("ok", true)

	inList := Msfx_SqlListInts(stagingIds)
	if (inList = "")
		return Map("ok", true)

	escStatus := Util_EscapeSQL(codeStatus)
	escErr := Util_EscapeSQL(errMsg)
	sql := ""
		. "UPDATE msfx_code_staging "
		. "SET code_status='" escStatus "', "
		. "    verified_at = CASE WHEN '" escStatus "' = 'VERIFIED' THEN now() ELSE verified_at END, "
		. "    err_msg = NULLIF('" escErr "', ''), "
		. "    updated_at = now() "
		. "WHERE id IN (" inList ");"
	return DB_Exec(sql)
}

Msfx_SqlListQuoted(items) {
	out := []
	seen := Map()
	for _, x in items {
		v := Msfx_NormalizeCode(x)
		if (v = "" || seen.Has(v))
			continue
		seen[v] := true
		out.Push("'" Util_EscapeSQL(v) "'")
	}
	return out.Length > 0 ? Msfx_Join(out, ",") : ""
}

Msfx_SqlListInts(items) {
	out := []
	seen := Map()
	for _, x in items {
		n := Util_ToInt(x, 0)
		if (n <= 0 || seen.Has(n))
			continue
		seen[n] := true
		out.Push(n)
	}
	return out.Length > 0 ? Msfx_Join(out, ",") : ""
}

Msfx_Join(arr, sep := ",") {
	if !IsObject(arr) || (arr.Length = 0)
		return ""
	s := ""
	for i, v in arr
		s .= (i = 1 ? "" : sep) v
	return s
}

Msfx_UpdateTaskCode(taskId, leafCode, status, verifyResult := "", errMsg := "") {
	escTask := Util_ToInt(taskId, 0)
	escLeaf := Util_EscapeSQL(leafCode)
	escStatus := Util_EscapeSQL(status)
	escVerify := Util_EscapeSQL(verifyResult)
	escErr := Util_EscapeSQL(errMsg)

	codeCol := Msfx_GetTaskCodeColumnName()
	sql := ""
		. "UPDATE msfx_inject_task_code "
		. "SET status='" escStatus "', "
		. "    injected_at = CASE WHEN '" escStatus "' = 'INJECTED' THEN now() ELSE injected_at END, "
		. "    verify_result = NULLIF('" escVerify "', ''), "
		. "    err_msg = NULLIF('" escErr "', '') "
		. "WHERE task_id=" escTask " AND " codeCol "='" escLeaf "';"
	return DB_Exec(sql)
}

Msfx_UpdateStagingCodeStatus(stagingId, codeStatus, errMsg := "") {
	sid := Util_ToInt(stagingId, 0)
	if (sid <= 0)
		return Map("ok", true)

	escStatus := Util_EscapeSQL(codeStatus)
	escErr := Util_EscapeSQL(errMsg)
	sql := ""
		. "UPDATE msfx_code_staging "
		. "SET code_status='" escStatus "', "
		. "    verified_at = CASE WHEN '" escStatus "' = 'VERIFIED' THEN now() ELSE verified_at END, "
		. "    err_msg = NULLIF('" escErr "', ''), "
		. "    updated_at = now() "
		. "WHERE id=" sid ";"
	return DB_Exec(sql)
}

Msfx_InsertEvent(taskId, stage, level, msg, leafCode := "") {
	tid := Util_ToInt(taskId, 0)
	if (tid <= 0)
		return Map("ok", false, "level", "Error", "message", "[SQL 错误]`ntask_id 非法")

	escStage := Util_EscapeSQL(stage)
	escLevel := Util_EscapeSQL(level)
	escMsg := Util_EscapeSQL(msg)
	escLeaf := Util_EscapeSQL(leafCode)

	codeCol := Msfx_GetEventCodeColumnName()
	sql := ""
		. "INSERT INTO msfx_inject_event(task_id, " codeCol ", stage, level, message) "
		. "VALUES ("
		. tid ", "
		. "NULLIF('" escLeaf "', ''), "
		. "'" escStage "', "
		. "'" escLevel "', "
		. "'" escMsg "'"
		. ");"
	return DB_Exec(sql)
}

Msfx_FinalizeInjectTask(taskId, errMsg := "", warehouseBillNo := "", rowFingerprint := "") {
	tid := Util_ToInt(taskId, 0)
	if (tid <= 0)
		return Map("ok", false, "level", "Error", "message", "[SQL 错误]`ntask_id 非法")

	escErr := Util_EscapeSQL(errMsg)
	escBill := Util_EscapeSQL(warehouseBillNo)
	escFp := Util_EscapeSQL(rowFingerprint)
	sql := ""
		. "SELECT task_id, task_status, success_codes, failed_codes, total_codes "
		. "FROM msfx_finalize_inject_task(" tid ", NULLIF('" escErr "', ''), NULLIF('" escBill "', ''), NULLIF('" escFp "', ''));"
	r := DB_Query(sql)
	if !r["ok"]
		return Map("ok", false, "level", "Error", "message", "[SQL 错误]`n" r["err"])
	return Map("ok", true, "rows", r["rows"])
}

Msfx_GetTaskCodeColumnName() {
	global __MSFX_COL
	if (__MSFX_COL.Has("task_code_col"))
		return __MSFX_COL["task_code_col"]

	col := Msfx_DetectColumn("msfx_inject_task_code", "leaf_code") ? "leaf_code" : "trace_code"
	__MSFX_COL["task_code_col"] := col
	return col
}

Msfx_GetEventCodeColumnName() {
	global __MSFX_COL
	if (__MSFX_COL.Has("event_code_col"))
		return __MSFX_COL["event_code_col"]

	col := Msfx_DetectColumn("msfx_inject_event", "leaf_code") ? "leaf_code" : "trace_code"
	__MSFX_COL["event_code_col"] := col
	return col
}

Msfx_DetectColumn(tableName, colName) {
	escTable := Util_EscapeSQL(tableName)
	escCol := Util_EscapeSQL(colName)
	sql := ""
		. "SELECT 1 "
		. "FROM information_schema.columns "
		. "WHERE table_schema='public' "
		. "  AND table_name='" escTable "' "
		. "  AND column_name='" escCol "' "
		. "LIMIT 1;"
	r := DB_Query(sql)
	return (r["ok"] && r["rows"].Length > 0)
}
