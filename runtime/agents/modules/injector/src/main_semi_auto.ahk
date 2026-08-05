; 半自动流程在用户选定目标行后由热键执行拆零药追溯码注入
; 预留成功后若 UI 注入或验证失败必须回滚，避免库存长期占用

Semi_Auto_Fill(opt, ipt, colSpecs, intCols, timeoutMs, optParseGridClassNN, iptParseGridClassNN, iptVerifyGridClassNN, optInputClassNN, iptInputClassNN, win := "A", clickAnchor := "") {
	global RuntimeInfo
	flowT0 := A_TickCount
	win := Util_NormalizeWin(win)
	cls := WinGetClass(win)
	ttl := ""
	try ttl := WinGetTitle(win)
	clientId := (IsSet(RuntimeInfo) && Type(RuntimeInfo) = "Map" && RuntimeInfo.Has("clientId"))
		? RuntimeInfo["clientId"]
		: A_ComputerName

	mode := ""
	if (cls = ipt) {
		mode := "住院"
	} else if (cls = opt) {
		mode := "门诊"
	} else {
		Log_Debug("semi_auto.scene_miss", "窗口不在允许场景", Map("cls", cls, "ttl", ttl, "opt", opt, "ipt", ipt))
		return Map("ok", false, "level", "Warn", "message", "[界面错误]`n当前窗口不在允许场景内`nclass=" cls)
	}

	parseGridClassNN := (cls = ipt) ? iptParseGridClassNN : optParseGridClassNN
	inputClassNN := (cls = ipt) ? iptInputClassNN : optInputClassNN

	if (mode = "门诊") {
		if !(IsObject(clickAnchor) && clickAnchor.Has("ok") && clickAnchor["ok"]
			&& clickAnchor.Has("restoreOk") && clickAnchor["restoreOk"]) {
			Log_Debug("semi_auto.anchor_unusable", "门诊锚点不可用", Map(
				"anchorOk", IsObject(clickAnchor) && clickAnchor.Has("ok") && clickAnchor["ok"],
				"restoreOk", IsObject(clickAnchor) && clickAnchor.Has("restoreOk") && clickAnchor["restoreOk"]
			))
			return Map("ok", false, "level", "Warn",
				"message", "[界面错误] 门诊校验缺少可用的网格点击锚点，已中止")
		}
	}

	Log_Debug("semi_auto.start", "半自动开始", Map(
		"mode", mode, "cls", cls, "ttl", ttl,
		"parseNn", parseGridClassNN, "inputNn", inputClassNN,
		"timeoutMs", timeoutMs, "clientId", clientId
	))

	p := Parse_TargetInfo(colSpecs, ipt, intCols, "", win, parseGridClassNN)

	if !p["ok"] {
		Log_Debug("semi_auto.parse_fail", p["message"], Map(
			"mode", mode, "reason", p.Has("reason") ? p["reason"] : "",
			"elapsedMs", A_TickCount - flowT0
		))
		return p
	}

	by := p["bySpec"]
	drugId := by.Has("物资名称||药品名称") ? Trim(by["物资名称||药品名称"]) : ""
	spec := by.Has("规格||药品规格") ? Trim(by["规格||药品规格"]) : ""
	splitFlag := by.Has("拆零标签||拆零") ? Trim(by["拆零标签||拆零"]) : ""
	qtyVal := by.Has("数量") ? by["数量"] : ""
	Log_Debug("semi_auto.fields", "关键字段", Map(
		"mode", mode, "drugId", drugId, "spec", spec, "split", splitFlag, "qty", qtyVal
	))
	if (drugId = "" || spec = "") {
		Log_Debug("semi_auto.fields_miss", "缺药品名或规格", Map("drugId", drugId, "spec", spec))
		return Map("ok", false, "level", "Warn", "message", "[解析错误]`n解析结果缺少关键字段`n药品名称=" drugId "`n规格=" spec)
	}

	txnId := Util_TxnId()
	Log_Debug("semi_auto.reserve", "开始预留", Map("txn", txnId, "drugId", drugId, "spec", spec, "mode", mode))

	; 门诊：药品行取已扫（供确认累计）；必须有追溯码列；空/无数字 → 0；已扫 N → N；有数字但非已扫文案 → 拒绝
	alreadyScanned := 0
	if (mode = "门诊") {
		if !by.Has("追溯码") {
			Log_Debug("semi_auto.trace_col_miss", "门诊缺少追溯码列", Map(
				"txn", txnId, "elapsedMs", A_TickCount - flowT0
			))
			return Map("ok", false, "level", "Warn",
				"message", "[解析错误] 门诊药品行缺少「追溯码」列，已中止")
		}
		traceCell := Trim(by["追溯码"], " `t`r`n")
		if (traceCell != "" && RegExMatch(traceCell, "已扫\s*(\d+)\s*码", &mScan)) {
			alreadyScanned := Integer(mScan[1])
		} else if (traceCell != "" && RegExMatch(traceCell, "\d")) {
			Log_Debug("semi_auto.scanned_unread", "追溯码格无法判定已扫", Map(
				"txn", txnId, "traceLen", StrLen(traceCell),
				"elapsedMs", A_TickCount - flowT0
			))
			return Map("ok", false, "level", "Warn",
				"message", "[界面错误] 无法读取门诊「已扫 N 码」，已中止以免超量注入")
		}
		Log_Debug("semi_auto.scanned", "门诊已扫基数", Map(
			"txn", txnId, "alreadyScanned", alreadyScanned, "traceLen", StrLen(traceCell)
		))
	}

	r := Txn_ReservePick(txnId, clientId, drugId, spec, 0, opt, ipt, by, cls, alreadyScanned)

	if !r["ok"] {
		Log_Debug("semi_auto.reserve_fail", r.Has("message") ? r["message"] : "预留失败", Map(
			"txn", txnId, "reason", r.Has("reason") ? r["reason"] : "",
			"elapsedMs", A_TickCount - flowT0
		))
		return r
	}
	if (r["skip"]) {
		Log_Debug("semi_auto.skip", r.Has("message") ? r["message"] : "跳过", Map(
			"txn", txnId, "elapsedMs", A_TickCount - flowT0
		))
		return Map("ok", true, "skip", true, "level", r["level"], "message", r["message"], "focusClassNN", inputClassNN)
	}

	codes := r.Has("codes") ? r["codes"] : []
	if (codes.Length = 0) {
		Log_Debug("semi_auto.codes_empty", "预留成功但码为空", Map("txn", txnId))
		Txn_Rollback(txnId)
		return Map("ok", false, "level", "Error", "message", "[预留错误]`n预留成功但追溯码异常并且为空")
	}

	tails := []
	for _, c in codes
		tails.Push((StrLen(c) <= 4) ? c : SubStr(c, -3))
	Log_Debug("semi_auto.codes", "预留码就绪", Map(
		"txn", txnId, "count", codes.Length, "codeTails", tails,
		"drugId", drugId, "spec", spec
	))

	iptSawForce := false
	for i, code in codes {
		Log_Debug("semi_auto.paste", "粘贴追溯码", Map(
			"txn", txnId, "idx", i, "codes", codes.Length,
			"codeLen", StrLen(code), "codeTail", (StrLen(code) <= 4) ? code : SubStr(code, -3)
		))
		pr := UI_Paste_ByPolicy(code, opt, ipt, optInputClassNN, iptInputClassNN, win)
		if (!pr["ok"]) {
			Log_Debug("semi_auto.paste_fail", pr.Has("message") ? pr["message"] : "贴码失败", Map(
				"txn", txnId, "idx", i, "elapsedMs", A_TickCount - flowT0
			))
			Txn_Rollback(txnId)
			return Map("ok", false, "level", pr["level"], "message", "注入失败（第" i "条）：`n" pr["message"])
		}
		if (mode = "住院") {
			dlg := UI_PollIptDialogs(450)
			if (dlg = "abort") {
				Log_Debug("semi_auto.paste_dialog_abort", "贴码后失败弹窗", Map("txn", txnId, "idx", i))
				Txn_Rollback(txnId)
				return Map("ok", false, "level", "Warn", "message", "[录入验证错误] 重复的追溯码/超过对应需要追溯码条数，将自动回退库存")
			}
			if (dlg = "force")
				iptSawForce := true
		}
	}

	optCtx := ""
	if (mode = "门诊") {
		optCtx := Map(
			"alreadyScanned", alreadyScanned,
			"drugId", drugId,
			"spec", spec,
			"qty", Util_ToInt(qtyVal, 0),
			"anchor", clickAnchor
		)
	}
	Log_Debug("semi_auto.confirm_begin", "进入录入校验", Map(
		"txn", txnId, "codes", codes.Length, "alreadyScanned", alreadyScanned,
		"iptSawForce", iptSawForce,
		"timeoutMs", timeoutMs, "parseNn", parseGridClassNN
	))
	wc := UI_WaitConfirm(codes, timeoutMs, opt, ipt, parseGridClassNN, iptVerifyGridClassNN, win, iptSawForce, optCtx)
	Log_Debug("semi_auto.confirm", wc["ok"] ? "校验通过" : "校验失败", Map(
		"txn", txnId,
		"ok", wc["ok"],
		"message", wc.Has("message") ? wc["message"] : "",
		"codes", codes.Length,
		"alreadyScanned", alreadyScanned,
		"elapsedMs", A_TickCount - flowT0
	))

	if !wc["ok"] {
		Log_Debug("semi_auto.rollback", "校验失败回滚", Map("txn", txnId))
		Txn_Rollback(txnId)
		return wc
	}

	rc := Txn_Commit(txnId)
	if !(rc is Map) {
		Log_Debug("semi_auto.commit_bad", "提交返回异常", Map("txn", txnId))
		return Map("ok", false, "level", "Error", "message", "[提交错误]`n未知执行错误")
	}

	if (!rc["ok"]) {
		Log_Debug("semi_auto.commit_fail", rc.Has("message") ? rc["message"] : "提交失败", Map("txn", txnId))
		return rc
	}

	Log_Info("semi_auto.done", "半自动完成", Map(
		"txn", txnId, "mode", mode, "drugId", drugId, "spec", spec,
		"codes", codes.Length, "elapsedMs", A_TickCount - flowT0
	))
	UI_Tip("[半自动注入完成] " drugId " / " spec "（" mode "）", 1500)
	return Map("ok", true, "focusClassNN", inputClassNN)
}
