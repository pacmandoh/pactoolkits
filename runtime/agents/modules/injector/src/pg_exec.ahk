; 追溯码事务支持预留、提交和回滚
; rem 模式：只扣拆零粒；full 模式：整盒行（remain=qty）+ 拆零粒 同 txn
; 计划：txn_plan.ahk（用 A_LineFile，测试只 #Include pg_exec 也能解析）
#Include "%A_LineFile%\..\txn_plan.ahk"

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

	r := PacApi_Post("/v1/injector/txn/reserve", Map(
		"txnId", txnId, "clientId", clientId, "drugId", drugId, "spec", spec,
		"wholeN", wholeN, "remNeed", remNeed
	))
	if !r["ok"] {
		Log_Debug("txn.reserve.api_fail", "预留请求失败", Map(
			"txn", txnId, "wholeN", wholeN, "remNeed", remNeed, "elapsedMs", A_TickCount - t0
		))
		return Map(
			"ok", false, "level", "Error",
			"message", "[预留错误]`n" (r.Has("message") ? r["message"] : "PacAPI 失败"),
			"reason", "API_ERROR", "err", r.Has("err") ? r["err"] : ""
		)
	}

	body := r["body"]
	if !IsObject(body)
		return Map("ok", false, "level", "Error", "message", "[预留错误]`n未返回任何结果行", "reason", "NO_RESULT")
	if !(body.Has("ok") && body["ok"]) {
		reason := body.Has("reason") ? body["reason"] : "FAIL"
		msg := body.Has("message") ? body["message"] : reason
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

	; 组装贴码列表（items 全量；贴码：门诊全码；住院 full 整盒全+拆零末码；住院 rem/纯拆零末码）
	lastRemCode := ""
	lastAnyCode := ""
	sumTake := 0
	apiItems := body.Has("items") && IsObject(body["items"]) ? body["items"] : []
	for _, row in apiItems {
		seq := Util_ToInt(row.Has("seq") ? row["seq"] : 0)
		poolId := Util_ToInt(row.Has("poolId") ? row["poolId"] : 0)
		code := row.Has("code") ? row["code"] : ""
		take := Util_ToInt(row.Has("take") ? row["take"] : 0)

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
	r := PacApi_Post("/v1/injector/txn/commit", Map("txnId", txnId))
	if !r["ok"] {
		Log_Debug("txn.commit.api_fail", "提交失败", Map("txn", txnId))
		return r
	}
	body := r["body"]
	if !(IsObject(body) && body.Has("ok") && body["ok"]) {
		Log_Debug("txn.commit.not_pending", "无 PENDING 可提交", Map("txn", txnId))
		msg := IsObject(body) && body.Has("message") ? body["message"] : "没有在 PENDING 状态的预留事务"
		return Map("ok", false, "level", "Error", "message", "[提交错误]`n提交减扣失败：`n" msg)
	}
	Log_Debug("txn.commit.ok", "提交成功", Map("txn", txnId))
	return Map("ok", true, "level", "Info", "message", "[提交成功]")
}

; 回滚仅恢复仍处于 PENDING 状态的事务
Txn_Rollback(txnId) {
	Log_Debug("txn.rollback.begin", "回滚开始", Map("txn", txnId))
	r := PacApi_Post("/v1/injector/txn/rollback", Map("txnId", txnId))
	if !r["ok"] {
		Log_Debug("txn.rollback.api_fail", "回滚失败", Map("txn", txnId))
		return r
	}
	body := r["body"]
	if !(IsObject(body) && body.Has("ok") && body["ok"]) {
		Log_Debug("txn.rollback.not_pending", "无 PENDING 可回滚", Map("txn", txnId))
		msg := IsObject(body) && body.Has("message") ? body["message"] : "没有在 PENDING 状态的预留事务"
		return Map("ok", false, "level", "Error", "message", "[回滚错误]`n回滚减扣失败：`n" msg)
	}
	restored := IsObject(body) && body.Has("restoredRows") ? Util_ToInt(body["restoredRows"], 0) : 0
	Log_Debug("txn.rollback.ok", "回滚成功", Map("txn", txnId, "restored", restored))
	return Map("ok", true, "level", "Info", "message", "[回滚成功]", "restored_rows", restored)
}

; 超时 PENDING 恢复用于回补异常退出前已预留的库存
Txn_CleanupPending(timeoutMinutes := 10, maxBatch := 200) {
	mins := Util_ToInt(timeoutMinutes, 10)
	if (mins < 1)
		mins := 1
	limit := Util_ToInt(maxBatch, 200)
	r := PacApi_Post("/v1/injector/txn/cleanup", Map("timeoutMinutes", mins, "maxBatch", limit))
	if !r["ok"] {
		Log_Debug("txn.cleanup.query_fail", "清理 PENDING 失败", Map("mins", mins))
		return r
	}
	body := r["body"]
	cleaned := IsObject(body) && body.Has("cleaned") ? Util_ToInt(body["cleaned"], 0) : 0
	if (cleaned > 0)
		Log_Debug("txn.cleanup.done", "清理 PENDING 完成", Map("cleaned", cleaned, "mins", mins))
	return Map("ok", true, "cleaned", cleaned)
}
