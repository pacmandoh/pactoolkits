; 半自动流程在用户选定目标行后由热键执行拆零药追溯码注入
; 预留成功后若 UI 注入或验证失败必须回滚，避免库存长期占用

Semi_Auto_Fill(opt, ipt, colSpecs, intCols, timeoutMs, optParseGridClassNN, optVerifyGridClassNN, iptParseGridClassNN, iptVerifyGridClassNN, optInputClassNN, iptInputClassNN, win := "A") {
	global RuntimeInfo
	flowT0 := A_TickCount
	win := Util_NormalizeWin(win)
	cls := WinGetClass(win)
	clientId := (IsSet(RuntimeInfo) && Type(RuntimeInfo) = "Map" && RuntimeInfo.Has("clientId"))
		? RuntimeInfo["clientId"]
		: A_ComputerName

	mode := ""
	if (cls = ipt) {
		mode := "住院"
	} else if (cls = opt) {
		mode := "门诊"
	} else {
		return Map("ok", false, "level", "Warn", "message", "[界面错误]`n当前窗口不在允许场景内`nclass=" cls)
	}

	parseGridClassNN := (cls = ipt) ? iptParseGridClassNN : optParseGridClassNN
	verifyGridClassNN := (cls = ipt) ? iptVerifyGridClassNN : optVerifyGridClassNN
	inputClassNN := (cls = ipt) ? iptInputClassNN : optInputClassNN

	p := Parse_TargetInfo(colSpecs, ipt, intCols, "", win, parseGridClassNN)

	if !p["ok"] {
		return p
	}

	by := p["bySpec"]
	drugId := by.Has("物资名称||药品名称") ? Trim(by["物资名称||药品名称"]) : ""
	spec := by.Has("规格||药品规格") ? Trim(by["规格||药品规格"]) : ""
	if (drugId = "" || spec = "") {
		return Map("ok", false, "level", "Warn", "message", "[解析错误]`n解析结果缺少关键字段`n药品名称=" drugId " 规格=" spec)
	}

	txnId := Util_TxnId()

	r := Txn_ReservePick(txnId, clientId, drugId, spec, 0, opt, ipt, by, cls)

	if !r["ok"] {
		return r
	}
	if (r["skip"]) {
		; 跳过原因和焦点恢复由模块入口统一处理，避免重复提示
		return Map("ok", true, "skip", true, "level", r["level"], "message", r["message"], "focusClassNN", inputClassNN)
	}

	codes := r.Has("codes") ? r["codes"] : []
	if (codes.Length = 0) {
		Txn_Rollback(txnId)
		return Map("ok", false, "level", "Error", "message", "[预留错误]`n预留成功但追溯码异常并且为空")
	}

	debugOptMulti := (cls = opt && codes.Length > 1)
	if (debugOptMulti) {
		Log_Debug("opt_multi.start", "门诊多码注入开始", Map(
			"txn", txnId,
			"elapsedMs", A_TickCount - flowT0,
			"drugId", drugId,
			"spec", spec,
			"codes", codes.Length,
			"inputClassNN", inputClassNN,
			"verifyGridClassNN", verifyGridClassNN
		))
	}

	for i, code in codes {
		if (debugOptMulti) {
			Log_Debug("opt_multi.paste_begin", "开始粘贴追溯码", Map(
				"txn", txnId,
				"idx", i,
				"codes", codes.Length,
				"elapsedMs", A_TickCount - flowT0,
				"codeLen", StrLen(code)
			))
		}
		pr := UI_Paste_ByPolicy(code, opt, ipt, optInputClassNN, iptInputClassNN, win)
		if (!pr["ok"]) {
			if (debugOptMulti) {
				Log_Debug("opt_multi.paste_fail", pr["message"], Map(
					"txn", txnId,
					"idx", i,
					"codes", codes.Length,
					"elapsedMs", A_TickCount - flowT0
				))
			}
			; 库存已经预留，注入失败时必须执行事务回滚
			Txn_Rollback(txnId)
			return Map("ok", false, "level", pr["level"], "message", "注入失败（第" i "条）：`n" pr["message"])
		}
		if (debugOptMulti) {
			Log_Debug("opt_multi.paste_ok", "粘贴成功", Map(
				"txn", txnId,
				"idx", i,
				"codes", codes.Length,
				"elapsedMs", A_TickCount - flowT0
			))
		}
	}

	wc := UI_WaitConfirm(codes, timeoutMs, opt, ipt, optVerifyGridClassNN, iptVerifyGridClassNN, iptParseGridClassNN, win)
	if (debugOptMulti) {
		ctx := Map(
			"txn", txnId,
			"elapsedMs", A_TickCount - flowT0,
			"ok", wc["ok"] ? 1 : 0
		)
		if (!wc["ok"] && wc.Has("message"))
			ctx["message"] := wc["message"]
		Log_Debug("opt_multi.final_confirm", wc["ok"] ? "确认成功" : "确认失败", ctx)
	}

	if !wc["ok"] {
		Txn_Rollback(txnId)
		return wc
	}

	rc := Txn_Commit(txnId)
	if !(rc is Map) {
		return Map("ok", false, "level", "Error", "message", "[提交错误]`n未知执行错误")
	}

	if (!rc["ok"]) {
		return rc
	}

	UI_Tip("[半自动注入完成] " drugId " / " spec "（" mode "）", 1500)
	return Map("ok", true)
}
