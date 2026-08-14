; 仓库任务：领取/防重/明细/状态回写/事件与结算（PacAPI）

Msfx_ClaimInjectTaskByTarget(clientId, drugId, spec) {
	r := PacApi_Post("/v1/injector/msfx/claim", Map(
		"clientId", clientId, "drugId", drugId, "spec", spec
	))
	if !r["ok"]
		return Map("ok", false, "level", "Error", "message", "[PacAPI]`n" (r.Has("err") ? r["err"] : r["message"]))
	body := r["body"]
	tasks := []
	apiTasks := IsObject(body) && body.Has("tasks") && IsObject(body["tasks"]) ? body["tasks"] : []
	for _, row in apiTasks {
		tasks.Push(Map(
			"task_id", Util_ToInt(row.Has("taskId") ? row["taskId"] : 0),
			"bill_id", Util_ToInt(row.Has("billId") ? row["billId"] : 0),
			"source_bill_code", row.Has("sourceBillCode") ? row["sourceBillCode"] : "",
			"mapped_drug_id", row.Has("mappedDrugId") ? row["mappedDrugId"] : "",
			"mapped_spec", row.Has("mappedSpec") ? row["mappedSpec"] : "",
			"total_codes", Util_ToInt(row.Has("totalCodes") ? row["totalCodes"] : 0)
		))
	}
	return Map("ok", true, "tasks", tasks)
}

Msfx_HasWarehouseSuccessTask(warehouseBillNo, drugId, spec, rowFingerprint := "") {
	path := "/v1/injector/msfx/warehouse-success"
		. "?warehouseBillNo=" PacApi_UrlEncode(warehouseBillNo)
		. "&drugId=" PacApi_UrlEncode(drugId)
		. "&spec=" PacApi_UrlEncode(spec)
	if (Trim(rowFingerprint) != "")
		path .= "&rowFingerprint=" PacApi_UrlEncode(rowFingerprint)
	r := PacApi_Get(path)
	if !r["ok"]
		return Map("ok", false, "level", "Error", "message", "[PacAPI]`n" (r.Has("err") ? r["err"] : r["message"]))
	body := r["body"]
	exists := IsObject(body) && body.Has("exists") && body["exists"]
	return Map("ok", true, "exists", exists)
}

Msfx_GetPendingTaskCodes(taskId) {
	tid := Util_ToInt(taskId, 0)
	if (tid <= 0)
		return Map("ok", false, "level", "Error", "message", "[PacAPI]`ntask_id 非法")
	r := PacApi_Get("/v1/injector/msfx/tasks/" tid "/pending-codes")
	if !r["ok"]
		return Map("ok", false, "level", "Error", "message", "[PacAPI]`n" (r.Has("err") ? r["err"] : r["message"]))
	rows := []
	apiRows := IsObject(r["body"]) ? r["body"] : []
	for _, row in apiRows {
		rows.Push(Map(
			"seq", Util_ToInt(row.Has("seq") ? row["seq"] : 0),
			"leaf_code", row.Has("leafCode") ? row["leafCode"] : "",
			"staging_id", Util_ToInt(row.Has("stagingId") ? row["stagingId"] : 0),
			"l1", row.Has("l1") ? row["l1"] : "",
			"l2", row.Has("l2") ? row["l2"] : "",
			"l3", row.Has("l3") ? row["l3"] : "",
			"l4", row.Has("l4") ? row["l4"] : "",
			"l5", row.Has("l5") ? row["l5"] : ""
		))
	}
	return Map("ok", true, "rows", rows)
}

Msfx_BatchUpdateTaskCodes(taskId, leafCodes, status, verifyResult := "", errMsg := "") {
	tid := Util_ToInt(taskId, 0)
	if (tid <= 0)
		return Map("ok", false, "err", "task_id 非法")
	if !IsObject(leafCodes) || (leafCodes.Length = 0)
		return Map("ok", true)
	r := PacApi_Post("/v1/injector/msfx/tasks/" tid "/codes", Map(
		"leafCodes", leafCodes, "status", status,
		"verifyResult", verifyResult, "errMsg", errMsg
	))
	return r["ok"] ? Map("ok", true) : Map("ok", false, "err", r.Has("err") ? r["err"] : r["message"])
}

Msfx_BatchUpdateStagingCodes(stagingIds, codeStatus, errMsg := "") {
	if !IsObject(stagingIds) || (stagingIds.Length = 0)
		return Map("ok", true)
	r := PacApi_Post("/v1/injector/msfx/staging/status", Map(
		"stagingIds", stagingIds, "codeStatus", codeStatus, "errMsg", errMsg
	))
	return r["ok"] ? Map("ok", true) : Map("ok", false, "err", r.Has("err") ? r["err"] : r["message"])
}

Msfx_UpdateTaskCode(taskId, leafCode, status, verifyResult := "", errMsg := "") {
	return Msfx_BatchUpdateTaskCodes(taskId, [leafCode], status, verifyResult, errMsg)
}

Msfx_UpdateStagingCodeStatus(stagingId, codeStatus, errMsg := "") {
	sid := Util_ToInt(stagingId, 0)
	if (sid <= 0)
		return Map("ok", true)
	return Msfx_BatchUpdateStagingCodes([sid], codeStatus, errMsg)
}

Msfx_InsertEvent(taskId, stage, level, msg, leafCode := "") {
	tid := Util_ToInt(taskId, 0)
	if (tid <= 0)
		return Map("ok", false, "level", "Error", "message", "[PacAPI]`ntask_id 非法")
	r := PacApi_Post("/v1/injector/msfx/tasks/" tid "/events", Map(
		"stage", stage, "level", level, "message", msg, "leafCode", leafCode
	))
	if !r["ok"]
		return Map("ok", false, "level", "Error", "message", "[PacAPI]`n" (r.Has("err") ? r["err"] : r["message"]))
	return Map("ok", true)
}

Msfx_FinalizeInjectTask(taskId, errMsg := "", warehouseBillNo := "", rowFingerprint := "") {
	tid := Util_ToInt(taskId, 0)
	if (tid <= 0)
		return Map("ok", false, "level", "Error", "message", "[PacAPI]`ntask_id 非法")
	r := PacApi_Post("/v1/injector/msfx/tasks/" tid "/finalize", Map(
		"errMsg", errMsg, "warehouseBillNo", warehouseBillNo, "rowFingerprint", rowFingerprint
	))
	if !r["ok"]
		return Map("ok", false, "level", "Error", "message", "[PacAPI]`n" (r.Has("err") ? r["err"] : r["message"]))
	rows := []
	body := r["body"]
	apiRows := IsObject(body) && body.Has("rows") && IsObject(body["rows"]) ? body["rows"] : []
	for _, row in apiRows {
		rows.Push([
			row.Has("taskId") ? row["taskId"] : 0,
			row.Has("taskStatus") ? row["taskStatus"] : "",
			row.Has("successCodes") ? row["successCodes"] : 0,
			row.Has("failedCodes") ? row["failedCodes"] : 0,
			row.Has("totalCodes") ? row["totalCodes"] : 0
		])
	}
	return Map("ok", true, "rows", rows)
}
