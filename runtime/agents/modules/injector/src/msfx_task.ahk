; 码上放心仓库任务：解析行、防重、贴码验证与节拍（SQL/取码见 msfx_sql / msfx_code）
Msfx_RunWarehouseTaskFlow(timeoutMs, parseGridClassNN, verifyGridClassNN, inputClassNN, colFields, iptCls, win := "A", clickAnchor := "") {
	win := Util_NormalizeWin(win)
	flowT0 := A_TickCount
	Log_Debug("msfx.flow.start", "仓库流程开始", Map(
		"parseNn", parseGridClassNN, "verifyNn", verifyGridClassNN, "inputNn", inputClassNN,
		"timeoutMs", timeoutMs
	))
	pre := Util_WarehouseSoftCheck(win)
	if !pre["ok"] {
		Log_Debug("msfx.flow.soft_fail", "仓库软校验失败", Map(
			"message", pre.Has("message") ? pre["message"] : "", "elapsedMs", A_TickCount - flowT0
		))
		return pre
	}
	headerLine := pre.Has("header_line") ? Trim(pre["header_line"]) : ""

	p := Parse_TargetInfo(colFields, iptCls, "", win, parseGridClassNN)
	if !p["ok"] {
		Log_Debug("msfx.flow.parse_fail", "仓库解析失败", Map("elapsedMs", A_TickCount - flowT0))
		return p
	}

	by := p["bySpec"]
	drugId := Trim("" By_Get(by, "drugName"))
	spec := Trim("" By_Get(by, "drugSpec"))
	warehouseBillNo := Trim("" By_Get(by, "billNo"))
	baseRowFingerprint := Msfx_BuildWarehouseRowFingerprint(by, warehouseBillNo, drugId, spec)
	rowFingerprint := baseRowFingerprint
	Log_Debug("msfx.flow.parsed", "仓库行已解析", Map(
		"drugId", drugId, "spec", spec, "bill", warehouseBillNo,
		"fp", baseRowFingerprint
	))
	if (drugId = "" || spec = "") {
		return Map("ok", false, "level", "Warn", "message", "[解析错误]`n仓库模式解析结果缺少关键字段`n药品名称=" drugId "`n规格=" spec)
	}
	if (warehouseBillNo = "") {
		return Map("ok", false, "level", "Warn", "message", "[解析错误]`n仓库模式解析结果缺少任务标识（billNo）")
	}
	if (Trim(baseRowFingerprint) = "") {
		return Map("ok", false, "level", "Warn", "message", "[仓库任务校验]`n仓库行指纹生成失败，已停止执行以避免错误防重`n单据号=" warehouseBillNo "`n药品=" drugId "`n规格=" spec)
	}

	global RuntimeInfo
	clientId := (IsSet(RuntimeInfo) && Type(RuntimeInfo) = "Map" && RuntimeInfo.Has("clientId"))
		? RuntimeInfo["clientId"]
		: (A_ComputerName "|" A_UserName "|ip=" Util_GetPrimaryIPv4() "|os=" Util_GetOSName() "|ver=" Util_GetVersionTag())

	dup := Msfx_HasWarehouseSuccessTask(warehouseBillNo, drugId, spec, baseRowFingerprint)
	if !dup["ok"]
		return dup
	if dup["exists"] {
		Log_Debug("msfx.flow.dup", "命中防重，尝试行槽指纹", Map("bill", warehouseBillNo, "fp", baseRowFingerprint))
		if (IsObject(clickAnchor) && clickAnchor.Has("ok") && clickAnchor["ok"] && clickAnchor.Has("rowSlot")) {
			; 左键已采集 rowSlot，直接用于防重消歧（不再屏幕坐标二次换算）
			slotRowFingerprint := Msfx_BuildWarehouseRowFingerprint(by, warehouseBillNo, drugId, spec, clickAnchor)
			Log_Debug("msfx.flow.slot_fp", "行槽指纹", Map(
				"base", baseRowFingerprint, "slot", slotRowFingerprint,
				"anchorOk", true, "rowSlot", clickAnchor["rowSlot"]
			))
			if (slotRowFingerprint != "" && slotRowFingerprint != baseRowFingerprint) {
				dup2 := Msfx_HasWarehouseSuccessTask(warehouseBillNo, drugId, spec, slotRowFingerprint)
				if !dup2["ok"]
					return dup2
				if !dup2["exists"] {
					rowFingerprint := slotRowFingerprint
					dup := dup2
				}
			}
		}
	}
	if dup["exists"] {
		fpTip := (rowFingerprint != "") ? ("`n行指纹=" rowFingerprint) : ""
		Log_Debug("msfx.flow.dup_block", "防重拦截", Map("bill", warehouseBillNo, "fp", rowFingerprint))
		return Map(
			"ok", false,
			"level", "Warn", "message", "[仓库任务校验]`n当前单据该药品规格已存在成功记录，已阻止重复注入`n单据号=" warehouseBillNo "`n药品=" drugId "`n规格=" spec fpTip
		)
	}

	claim := Msfx_ClaimInjectTaskByTarget(clientId, drugId, spec)
	if !claim["ok"] {
		Log_Debug("msfx.flow.claim_fail", "领取任务失败", Map("drugId", drugId, "spec", spec))
		return claim
	}

	tasks := claim["tasks"]
	Log_Debug("msfx.flow.claim", "领取任务", Map("tasks", tasks.Length, "drugId", drugId, "spec", spec))
	if (tasks.Length = 0)
		return Map("ok", true, "skip", true, "level", "Info", "message", "[仓库任务]`n未找到匹配任务`n药品=" drugId "`n规格=" spec)
	UI_Tip("[仓库模式] 已匹配任务 1 条，开始注入…", 1000)

	policy := Cfg["CODE_PICK_POLICY"]
	lastErr := ""
	hasErr := false
	for _, task in tasks {
		taskId := task["task_id"]
		Log_Debug("msfx.flow.task", "执行仓库任务", Map("taskId", taskId, "policy", policy, "fp", rowFingerprint))
		r := Msfx_RunOneWarehouseTask(taskId, policy, timeoutMs, verifyGridClassNN, inputClassNN, win, headerLine, warehouseBillNo, rowFingerprint)
		if !r["ok"] {
			lastErr := r["message"]
			if (r.Has("level") && r["level"] = "Error")
				hasErr := true
			Log_Debug("msfx.flow.task_fail", "仓库任务失败", Map(
				"taskId", taskId, "level", r.Has("level") ? r["level"] : "",
				"message", lastErr
			))
		}
	}

	if (lastErr != "")
		return Map("ok", false, "level", hasErr ? "Error" : "Warn", "message", lastErr)

	Log_Info("msfx.flow.done", "仓库流程完成", Map(
		"tasks", tasks.Length, "bill", warehouseBillNo, "drugId", drugId, "spec", spec,
		"elapsedMs", A_TickCount - flowT0
	))
	return Map(
		"ok", true, "level", "Info", "message", "[仓库任务完成]`n已处理任务数=" tasks.Length "`n单据号=" warehouseBillNo "`n药品=" drugId "`n规格=" spec
	)
}

Msfx_RunOneWarehouseTask(taskId, policy, timeoutMs, verifyGridClassNN, inputClassNN, win, headerLine := "", warehouseBillNo := "", rowFingerprint := "") {
	taskT0 := A_TickCount
	Log_Debug("msfx.task.start", "单任务开始", Map(
		"taskId", taskId, "policy", policy, "bill", warehouseBillNo, "fp", rowFingerprint
	))
	Msfx_InsertEvent(taskId, "PARSE", "INFO", "仓库任务开始执行")
	if (Trim(headerLine) != "") {
		line := headerLine
		if (StrLen(line) > 240)
			line := SubStr(line, 1, 240) "..."
		Msfx_InsertEvent(taskId, "PARSE", "INFO", "仓库软校验表头: " line)
	}

	rowsRes := Msfx_GetPendingTaskCodes(taskId)
	if !rowsRes["ok"] {
		Log_Debug("msfx.task.codes_fail", "读明细失败", Map("taskId", taskId))
		Msfx_FinalizeInjectTask(taskId, "读取任务明细失败")
		return rowsRes
	}

	codeRows := rowsRes["rows"]
	Log_Debug("msfx.task.codes", "待注入明细", Map("taskId", taskId, "rows", codeRows.Length))
	if (codeRows.Length = 0) {
		Msfx_InsertEvent(taskId, "PARSE", "WARN", "任务无待处理明细")
		Msfx_FinalizeInjectTask(taskId, "任务无待处理明细")
		return Map("ok", false, "level", "Warn", "message", "[仓库任务错误] 任务无待注入明细")
	}

	succ := 0
	fail := 0
	injectRuns := 0
	firstVerified := false
	isMinPolicy := (StrUpper(Trim(policy)) = "MIN_LEVEL")
	groups := []
	groupPos := Map()
	noCodeLeafs := []
	noCodeStaging := []
	; MIN_LEVEL 高吞吐路径累计成功结果后批量回写，减少数据库往返
	minFirstLeafs := []
	minFirstStaging := []
	minSoftLeafs := []
	minSoftStaging := []

	; 先按取码策略计算目标码，再按目标码分组，避免同码重复注入
	for _, item in codeRows {
		leafCode := item["leaf_code"]
		stagingId := item["staging_id"]
		injectCode := Msfx_NormalizeCode(Msfx_SelectInjectCode(item, policy))

		if (Trim(injectCode) = "") {
			fail++
			noCodeLeafs.Push(leafCode)
			if (stagingId > 0)
				noCodeStaging.Push(stagingId)
			continue
		}

		if !groupPos.Has(injectCode) {
			idx := groups.Length + 1
			groupPos[injectCode] := idx
			groups.Push(Map("inject_code", injectCode, "items", []))
		}
		groups[groupPos[injectCode]]["items"].Push(item)
	}

	if (noCodeLeafs.Length > 0) {
		u1 := Msfx_BatchUpdateTaskCodes(taskId, noCodeLeafs, "FAILED", "", "取码策略结果为空")
		if !u1["ok"] {
			Msfx_FinalizeInjectTask(taskId, "更新失败明细状态失败")
			return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u1["err"])
		}
		u2 := Msfx_BatchUpdateStagingCodes(noCodeStaging, "FAILED", "取码策略结果为空")
		if !u2["ok"] {
			Msfx_FinalizeInjectTask(taskId, "更新 staging 失败状态失败")
			return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u2["err"])
		}
		Msfx_InsertEvent(taskId, "PARSE", "ERR", "取码策略结果为空，明细数=" noCodeLeafs.Length)
	}

	totalGroups := groups.Length
	Log_Debug("msfx.task.groups", "取码分组完成", Map(
		"taskId", taskId, "groups", totalGroups, "noCode", noCodeLeafs.Length, "policy", policy
	))
	prep := UI_PrepareWarehouseFastTarget(inputClassNN, win)
	if !prep["ok"] {
		why := prep.Has("message") ? prep["message"] : "仓库窗口准备失败"
		Log_Debug("msfx.task.prep_fail", why, Map("taskId", taskId))
		Msfx_FinalizeInjectTask(taskId, "仓库窗口准备失败")
		return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" why)
	}

	for gIdx, grp in groups {
		injectCode := grp["inject_code"]
		items := grp["items"]
		itemCount := items.Length
		leafCodes := Msfx_GroupLeafCodes(items)
		stagingIds := Msfx_GroupStagingIds(items)
		injectRuns++
		codeTail := (StrLen(injectCode) <= 4) ? injectCode : SubStr(injectCode, -3)

		; 首条记录使用完整注入与强验证，确认窗口链路对齐后再进入高吞吐路径
		useStableInject := !firstVerified
		if (useStableInject || gIdx <= 2 || Mod(gIdx, 40) = 0)
			Log_Debug("msfx.task.inject", "注入一组", Map(
				"taskId", taskId, "gIdx", gIdx, "groups", totalGroups,
				"items", itemCount, "stable", useStableInject,
				"codeLen", StrLen(injectCode), "codeTail", codeTail
			))
		if useStableInject
			pr := UI_Paste_Impl(win, inputClassNN, injectCode, false)
		else
			pr := UI_Paste_Warehouse(injectCode, inputClassNN, win)
		if !pr["ok"] {
			why := pr.Has("message") ? pr["message"] : "仓库窗口注入失败"
			Log_Debug("msfx.task.inject_fail", why, Map(
				"taskId", taskId, "gIdx", gIdx, "codeTail", codeTail, "items", itemCount
			))
			fail += itemCount
			u1 := Msfx_BatchUpdateTaskCodes(taskId, leafCodes, "FAILED", "", why)
			if !u1["ok"] {
				Msfx_FinalizeInjectTask(taskId, "更新注入失败状态失败")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u1["err"])
			}
			u2 := Msfx_BatchUpdateStagingCodes(stagingIds, "FAILED", why)
			if !u2["ok"] {
				Msfx_FinalizeInjectTask(taskId, "更新 staging 注入失败状态失败")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u2["err"])
			}
			Msfx_InsertEvent(taskId, "INJECT", "ERR", "注入失败，码=" injectCode "，影响明细=" itemCount "，原因=" why)
			Msfx_ApplyWarehouseBurstPacing(gIdx, totalGroups)
			continue
		}

		verifyResult := "SOFT_OK"
		verifyOk := true
		if !firstVerified {
			; 仓库验证窗口结构与住院一致，沿用 TcxGridSite 第 1 个网格验证首条
			firstTimeout := (timeoutMs < 3500) ? 3500 : timeoutMs
			Log_Debug("msfx.task.first_verify", "首条强校验", Map("taskId", taskId, "timeoutMs", firstTimeout, "codeTail", codeTail))
			wc := UI_WaitConfirm_Warehouse([injectCode], firstTimeout, verifyGridClassNN, win)
			verifyOk := wc["ok"]
			verifyResult := verifyOk ? "FIRST_OK" : "FIRST_FAIL"
			Log_Debug("msfx.task.first_verify_result", verifyOk ? "首条通过" : "首条失败", Map(
				"taskId", taskId, "result", verifyResult,
				"message", wc.Has("message") ? wc["message"] : ""
			))
			if verifyOk {
				firstVerified := true
				; 首条验证会将焦点移至验证区域，进入后续循环前必须恢复输入框焦点
				rePrep := UI_PrepareWarehouseFastTarget(inputClassNN, win)
				if !rePrep["ok"] {
					why := rePrep.Has("message") ? rePrep["message"] : "仓库窗口准备失败"
					Msfx_FinalizeInjectTask(taskId, "仓库窗口准备失败")
					return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" why)
				}
			}
		}

		if !verifyOk {
			why := wc.Has("message") ? wc["message"] : "仓库窗口验证失败"
			fail += itemCount
			u1 := Msfx_BatchUpdateTaskCodes(taskId, leafCodes, "FAILED", verifyResult, why)
			if !u1["ok"] {
				Msfx_FinalizeInjectTask(taskId, "更新验证失败状态失败")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u1["err"])
			}
			u2 := Msfx_BatchUpdateStagingCodes(stagingIds, "FAILED", why)
			if !u2["ok"] {
				Msfx_FinalizeInjectTask(taskId, "更新 staging 验证失败状态失败")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u2["err"])
			}
			Msfx_InsertEvent(taskId, "VERIFY", "ERR", "验证失败，码=" injectCode "，影响明细=" itemCount "，原因=" why)

			; 首条验证失败表示窗口链路未对齐，必须终止任务以避免后续数据错位
			if !firstVerified {
				Msfx_FinalizeInjectTask(taskId, "首条验证失败，任务已终止以避免错位注入")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n首条注入验证失败，已终止任务，避免后续错位")
			}

			Msfx_ApplyWarehouseBurstPacing(gIdx, totalGroups)
			continue
		}

		succ += itemCount
		if isMinPolicy {
			if (verifyResult = "FIRST_OK") {
				Msfx_ArrayAppend(minFirstLeafs, leafCodes)
				Msfx_ArrayAppend(minFirstStaging, stagingIds)
			} else {
				Msfx_ArrayAppend(minSoftLeafs, leafCodes)
				Msfx_ArrayAppend(minSoftStaging, stagingIds)
			}
			Msfx_ApplyWarehouseBurstPacing(gIdx, totalGroups)
			continue
		}

		u1 := Msfx_BatchUpdateTaskCodes(taskId, leafCodes, "INJECTED", verifyResult, "")
		if !u1["ok"] {
			Msfx_FinalizeInjectTask(taskId, "更新注入成功状态失败")
			return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u1["err"])
		}
		u2 := Msfx_BatchUpdateStagingCodes(stagingIds, "VERIFIED", "")
		if !u2["ok"] {
			Msfx_FinalizeInjectTask(taskId, "更新 staging 成功状态失败")
			return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u2["err"])
		}
		msg := (itemCount > 1) ? "注入并验证成功（同码合并）" : "注入并验证成功"
		Msfx_InsertEvent(taskId, "INJECT", "INFO", msg "，码=" injectCode "，影响明细=" itemCount)
		Msfx_ApplyWarehouseBurstPacing(gIdx, totalGroups)
	}

	if isMinPolicy {
		if (minFirstLeafs.Length > 0) {
			u1 := Msfx_BatchUpdateTaskCodes(taskId, minFirstLeafs, "INJECTED", "FIRST_OK", "")
			if !u1["ok"] {
				Msfx_FinalizeInjectTask(taskId, "MIN_LEVEL 批量更新首组成功状态失败")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u1["err"])
			}
			u2 := Msfx_BatchUpdateStagingCodes(minFirstStaging, "VERIFIED", "")
			if !u2["ok"] {
				Msfx_FinalizeInjectTask(taskId, "MIN_LEVEL 批量更新首组 staging 成功状态失败")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u2["err"])
			}
		}
		if (minSoftLeafs.Length > 0) {
			u1 := Msfx_BatchUpdateTaskCodes(taskId, minSoftLeafs, "INJECTED", "SOFT_OK", "")
			if !u1["ok"] {
				Msfx_FinalizeInjectTask(taskId, "MIN_LEVEL 批量更新后续成功状态失败")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u1["err"])
			}
			u2 := Msfx_BatchUpdateStagingCodes(minSoftStaging, "VERIFIED", "")
			if !u2["ok"] {
				Msfx_FinalizeInjectTask(taskId, "MIN_LEVEL 批量更新后续 staging 成功状态失败")
				return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n" u2["err"])
			}
		}
		Msfx_InsertEvent(taskId, "INJECT", "INFO", "MIN_LEVEL 极速注入完成，注入次数=" injectRuns "，成功明细=" succ "，失败明细=" fail)
	}

	finalErr := ""
	if (fail > 0) {
		if (succ = 0)
			finalErr := "全部失败: " fail "/" codeRows.Length
		else
			finalErr := "部分失败: " fail "/" codeRows.Length
	}
	finalizeBillNo := (fail = 0 && succ > 0) ? warehouseBillNo : ""
	finalizeRowFingerprint := (fail = 0 && succ > 0) ? rowFingerprint : ""
	fr := Msfx_FinalizeInjectTask(taskId, finalErr, finalizeBillNo, finalizeRowFingerprint)
	if !fr["ok"] {
		Log_Debug("msfx.task.finalize_fail", "任务结算失败", Map("taskId", taskId, "message", fr.Has("message") ? fr["message"] : ""))
		return Map("ok", false, "level", "Error", "message", "[仓库任务错误]`n任务结算失败：`n" fr["message"])
	}

	Log_Debug("msfx.task.done", "单任务完成", Map(
		"taskId", taskId, "runs", injectRuns, "succ", succ, "fail", fail,
		"elapsedMs", A_TickCount - taskT0
	))
	return Map("ok", true, "level", "Info", "message", "[仓库任务完成]`ntask_id=" taskId "`n注入次数=" injectRuns "`n成功=" succ "`n失败=" fail)
}

Msfx_ApplyWarehouseBurstPacing(groupIndex, totalGroups) {
	; 每处理 N 组后短暂让出执行权，降低窗口消息积压并稳定长序列吞吐
	if (groupIndex >= totalGroups)
		return

	; 吞吐节拍集中由此常量控制，避免在循环中分散调整 Sleep
	burstN := 40
	pauseMs := 2

	if (burstN <= 0 || pauseMs <= 0)
		return
	if (Mod(groupIndex, burstN) = 0)
		Sleep(pauseMs)
}

Msfx_BuildWarehouseRowFingerprint(bySpec, warehouseBillNo, drugId, spec, clickAnchor := "") {
	if !IsObject(bySpec)
		return ""

	billPart := Msfx_NormalizeFingerprintPart(warehouseBillNo)
	drugPart := Msfx_NormalizeFingerprintPart(drugId)
	specPart := Msfx_NormalizeFingerprintPart(spec)
	qtyPart := Msfx_NormalizeFingerprintPart(By_Get(bySpec, "qty"))
	unitPart := Msfx_NormalizeFingerprintPart(By_Get(bySpec, "unit"))
	if (billPart = "" || drugPart = "" || specPart = "" || qtyPart = "" || unitPart = "")
		return ""

	batchNo := Trim("" By_Get(bySpec, "batchNo"))
	rowSlot := ""
	if (IsObject(clickAnchor) && clickAnchor.Has("ok") && clickAnchor["ok"] && clickAnchor.Has("rowSlot"))
		rowSlot := "" clickAnchor["rowSlot"]

	return "bill=" billPart
		. "|drug=" drugPart
		. "|spec=" specPart
		. "|qty=" qtyPart
		. "|unit=" unitPart
		. ((Trim(rowSlot) != "") ? ("|slot=" Msfx_NormalizeFingerprintPart(rowSlot)) : "")
		. ((batchNo != "") ? ("|batch=" Msfx_NormalizeFingerprintPart(batchNo)) : "")
}

Msfx_NormalizeFingerprintPart(value) {
	value := Trim("" value)
	return RegExReplace(value, "\s+", " ")
}
