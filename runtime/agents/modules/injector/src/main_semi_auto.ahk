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
		return Map("ok", false, "level", "WARN", "type", "[界面错误]", "why", "当前窗口不在允许场景内`nclass=" cls)
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
		return Map("ok", false, "level", "WARN", "type", "[解析错误]", "why", "解析结果缺少关键字段`n药品名称=" drugId " 规格=" spec)
	}

	txnId := Util_TxnId()

	r := Txn_ReservePick(txnId, clientId, drugId, spec, 0, opt, ipt, by, cls)

	if !r["ok"] {
		return r
	}
	if (r["skip"]) {
		; 跳过原因和焦点恢复由模块入口统一处理，避免重复提示
		return Map("ok", true, "skip", true, "type", r["type"], "why", r["why"], "focusClassNN", inputClassNN)
	}

	codes := r.Has("codes") ? r["codes"] : []
	if (codes.Length = 0) {
		Txn_Rollback(txnId)
		return Map("ok", false, "level", "ERR", "type", "[预留错误]", "why", "预留成功但追溯码异常并且为空")
	}

	debugOptMulti := (cls = opt && codes.Length > 1)
	if (debugOptMulti) {
		Util_LogLine(
			"OPT_MULTI"
			. " | start"
			. " | txn=" txnId
			. " | t=" (A_TickCount - flowT0) "ms"
			. " | drug=" drugId
			. " | spec=" spec
			. " | codes=" codes.Length
			. " | input=" inputClassNN
			. " | verify=" verifyGridClassNN
		)
	}

	for i, code in codes {
		if (debugOptMulti) {
			Util_LogLine(
				"OPT_MULTI"
				. " | paste_begin"
				. " | txn=" txnId
				. " | idx=" i "/" codes.Length
				. " | t=" (A_TickCount - flowT0) "ms"
				. " | len=" StrLen(code)
			)
		}
		pr := UI_Paste_ByPolicy(code, opt, ipt, optInputClassNN, iptInputClassNN, win)
		if (!pr["ok"]) {
			if (debugOptMulti) {
				Util_LogLine(
					"OPT_MULTI"
					. " | paste_fail"
					. " | txn=" txnId
					. " | idx=" i "/" codes.Length
					. " | t=" (A_TickCount - flowT0) "ms"
					. " | why=" StrReplace(pr["why"], "`n", " | ")
				)
			}
			; 库存已经预留，注入失败时必须执行事务回滚
			Txn_Rollback(txnId)
			return Map("ok", false, "level", "ERR", "type", pr["type"], "why", "注入失败（第" i "条）：`n" pr["why"])
		}
		if (debugOptMulti) {
			Util_LogLine(
				"OPT_MULTI"
				. " | paste_ok"
				. " | txn=" txnId
				. " | idx=" i "/" codes.Length
				. " | t=" (A_TickCount - flowT0) "ms"
			)
		}
	}

	wc := UI_WaitConfirm(codes, timeoutMs, opt, ipt, optVerifyGridClassNN, iptVerifyGridClassNN, iptParseGridClassNN, win)
	if (debugOptMulti) {
		Util_LogLine(
			"OPT_MULTI"
			. " | final_confirm"
			. " | txn=" txnId
			. " | t=" (A_TickCount - flowT0) "ms"
			. " | ok=" (wc["ok"] ? 1 : 0)
			. (wc["ok"] ? "" : (" | why=" StrReplace(wc["why"], "`n", " | ")))
		)
	}

	if !wc["ok"] {
		Txn_Rollback(txnId)
		return wc
	}

	rc := Txn_Commit(txnId)
	if !(rc is Map) {
		return Map("ok", false, "level", "ERR", "type", "[提交错误]", "why", "未知执行错误")
	}

	if (!rc["ok"]) {
		return rc
	}

	UI_Tip("[半自动注入完成] " drugId " / " spec "（" mode "）", 1500)
	return Map("ok", true)
}
