; 录入校验：门诊已扫累计、住院/仓库验证、失败弹窗处置
; 住院弹窗处置：abort=应回滚；force=信息不匹配已点「是」继续；空=无相关弹窗
UI_DetectAndHandleFailDialog() {
	; “信息确认”：信息不匹配时默认「是」强制继续注入（勿当失败回滚）
	if (hwnd := WinExist("信息确认")) {
		win := "ahk_id " hwnd
		txt := ""
		try txt := WinGetText(win)
		if UI_IsIptInfoMismatchConfirm(txt) {
			Log_Debug("ui.dialog.info_confirm", "信息不匹配确认框：是", Map("txtLen", StrLen(txt)))
			if UI_SendDialogKey(win, "Y", 0x59)
				return "force"
			; 未点上但不阻塞：若框已自行关闭也算继续
			if !WinExist("信息确认")
				return "force"
			Log_Debug("ui.dialog.info_confirm_fail", "强制「是」未生效", Map())
		}
	}

	; “提示”对话框根据业务文案选择确认或取消
	if (hwnd := WinExist("提示")) {
		win := "ahk_id " hwnd
		txt := ""
		try txt := WinGetText(win)

		if InStr(txt, "重复的追溯码")
			&& InStr(txt, "不能录入") {

			Log_Debug("ui.dialog.dup_code", "重复追溯码提示：Enter", Map("txtLen", StrLen(txt)))
			UI_SendDialogKey(win, "{Enter}", 0x0D)
			return "abort"
		}

		if InStr(txt, "物资")
			&& InStr(txt, "追溯码扫码数量")
			&& InStr(txt, "是否继续新增") {

			Log_Debug("ui.dialog.qty_mismatch", "数量不符提示：Esc", Map("txtLen", StrLen(txt)))
			UI_SendDialogKey(win, "{Esc}", 0x1B)
			return "abort"
		}
	}

	return ""
}

; 住院「信息确认」：信息不匹配 / 对应不符 且询问是否继续
UI_IsIptInfoMismatchConfirm(txt) {
	t := Trim("" txt)
	if (t = "")
		return false
	if !(InStr(t, "是否继续") || InStr(t, "是否仍继续"))
		return false
	return InStr(t, "信息不匹配") || InStr(t, "不匹配") || InStr(t, "不符")
}

; 激活对话框后发键；成功或对话框已消失返回 true
UI_SendDialogKey(win, sendKey, vk) {
	try WinActivate(win)
	catch
		return !WinExist(win)
	try {
		if !WinWaitActive(win, , 0.5)
			return !WinExist(win)
	} catch {
		return !WinExist(win)
	}
	Sleep(40)
	try SendInput(sendKey)
	catch {
		try ControlSend(sendKey, , win)
		catch {
			try {
				PostMessage(0x100, vk, 0, , win)
				PostMessage(0x101, vk, 0, , win)
			} catch {
				return !WinExist(win)
			}
		}
	}
	Sleep(60)
	return true
}

; 短轮询住院弹窗：命中 force/abort 立即返回，避免对「是」连发
UI_PollIptDialogs(maxMs := 280, interval := 12) {
	t0 := A_TickCount
	while (A_TickCount - t0 < maxMs) {
		r := UI_DetectAndHandleFailDialog()
		if (r = "abort" || r = "force")
			return r
		Sleep(interval)
	}
	return ""
}

; optCtx：门诊 Map(alreadyScanned, drugId, spec, qty, anchor)
; optGridClassNN：门诊点回 ClassNN
UI_WaitConfirm(codes, timeoutMs, opt, ipt, optGridClassNN, iptVerifyGridClassNN, win, iptSawForce, optCtx := "") {
	t0 := A_TickCount

	win := Util_NormalizeWin(win)
	; 用户在贴码后、校验前关掉录入窗：勿对已死窗 WinGetClass
	if !WinExist(win) {
		if iptSawForce {
			Log_Debug("ui.confirm.ipt_closed_pre", "校验前窗已关且贴码阶段已 force", Map(
				"codes", IsObject(codes) ? codes.Length : 0, "elapsedMs", 0
			))
			return Map("ok", true, "closed", true)
		}
		Log_Debug("ui.confirm.ipt_closed_pre", "校验前窗已关且无受理信号", Map("elapsedMs", 0))
		return Map(
			"ok", false, "level", "Warn",
			"message", "[录入验证错误] 录入窗口已关闭，无法确认是否注入成功",
			"reason", "win_closed_unconfirmed"
		)
	}

	cls := ""
	try cls := WinGetClass(win)
	catch
		cls := ""
	Log_Debug("ui.confirm.start", "开始录入校验", Map(
		"cls", cls, "codes", IsObject(codes) ? codes.Length : 0,
		"timeoutMs", timeoutMs, "iptSawForce", iptSawForce,
		"optGridNn", optGridClassNN, "iptVerifyNn", iptVerifyGridClassNN,
		"hasOptCtx", IsObject(optCtx)
	))

	; 门诊：点回目标行后只认已扫数量
	if (cls = opt) {
		return UI_WaitConfirm_Opt(
			codes, timeoutMs, optGridClassNN, win,
			optCtx, t0
		)
	}

	; 住院流程要求验证区域出现全部注入码，并处理可能的失败弹窗
	if (cls = ipt) {
		delay := 15
		lastTickLog := 0
		lastHit := -1
		sawForce := !!iptSawForce
		hit := 0

		while (A_TickCount - t0 < timeoutMs) {
			dlg := UI_PollIptDialogs(180)
			if (dlg = "abort") {
				Log_Debug("ui.confirm.ipt_dialog", "住院失败弹窗触发回退", Map("elapsedMs", A_TickCount - t0))
				return Map("ok", false, "level", "Warn", "message", "[录入验证错误] 重复的追溯码/超过对应需要追溯码条数，将自动回退库存")
			}
			if (dlg = "force")
				sawForce := true

			; 用户很快关掉录入窗：仅已强制「是」或验证区已见码时视为成功，否则回滚
			if !WinExist(win) {
				if (sawForce || hit > 0) {
					Log_Debug("ui.confirm.ipt_closed", "用户关窗且已有受理信号", Map(
						"codes", codes.Length, "sawForce", sawForce, "hit", hit, "elapsedMs", A_TickCount - t0
					))
					return Map("ok", true, "closed", true)
				}
				Log_Debug("ui.confirm.ipt_closed_unconfirmed", "用户关窗但未确认成功", Map(
					"elapsedMs", A_TickCount - t0
				))
				return Map(
					"ok", false, "level", "Warn",
					"message", "[录入验证错误] 录入窗口已关闭，无法确认是否注入成功",
					"reason", "win_closed_unconfirmed"
				)
			}

			txt := UI_TryCopyGridClassNNText(iptVerifyGridClassNN, win)
			hit := 0
			if (txt != "") {
				allOk := true
				for c in codes {
					if InStr(txt, c)
						hit++
					else
						allOk := false
				}
				if allOk {
					Log_Debug("ui.confirm.ipt_ok", "住院校验通过", Map(
						"codes", codes.Length, "hit", hit, "elapsedMs", A_TickCount - t0
					))
					return Map("ok", true)
				}
			}

			if (hit != lastHit || A_TickCount - lastTickLog >= 500) {
				lastTickLog := A_TickCount
				lastHit := hit
				Log_Debug("ui.confirm.ipt_tick", "住院校验等待", Map(
					"need", codes.Length, "hit", hit, "txtLen", StrLen(txt),
					"dlg", dlg, "elapsedMs", A_TickCount - t0
				))
			}

			Sleep(delay)
			if (delay < 120)
				delay += 15
		}
		Log_Debug("ui.confirm.ipt_fail", "住院校验超时", Map(
			"codes", codes.Length, "lastHit", lastHit, "elapsedMs", A_TickCount - t0
		))
		return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n住院窗口录入追溯码验证失败，未实际扫码成功")
	}

	Log_Debug("ui.confirm.unknown", "未知窗口类", Map("cls", cls))
	return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n未知窗口，请一直保持在相应扫码窗口")
}

; 门诊校验：点回目标行后，轮询已扫至 alreadyScanned+codes（只认「已扫 N 码」）
UI_WaitConfirm_Opt(codes, timeoutMs, gridClassNN, win, optCtx, t0) {
	if !IsObject(optCtx) {
		Log_Debug("ui.confirm.opt_no_ctx", "门诊缺少目标上下文", Map())
		return Map("ok", false, "level", "Error",
			"message", "[录入验证错误]`n门诊校验缺少目标行上下文，无法安全验证")
	}

	alreadyScanned := optCtx.Has("alreadyScanned") ? Util_ToInt(optCtx["alreadyScanned"], 0) : 0
	wantDrug := optCtx.Has("drugId") ? Trim(optCtx["drugId"]) : ""
	wantSpec := optCtx.Has("spec") ? Trim(optCtx["spec"]) : ""
	wantQty := optCtx.Has("qty") ? Util_ToInt(optCtx["qty"], 0) : 0
	anchor := optCtx.Has("anchor") ? optCtx["anchor"] : ""
	codeCount := IsObject(codes) ? codes.Length : 0
	needN := alreadyScanned + codeCount

	if (wantDrug = "" || wantSpec = "") {
		Log_Debug("ui.confirm.opt_no_identity", "门诊缺少行身份", Map(
			"drugId", wantDrug, "spec", wantSpec
		))
		return Map("ok", false, "level", "Error",
			"message", "[录入验证错误]`n门诊校验缺少药品行身份，无法安全验证")
	}

	if (needN <= 0) {
		Log_Debug("ui.confirm.opt_bad_target", "门诊缺少累计已扫目标", Map(
			"alreadyScanned", alreadyScanned, "codes", codeCount
		))
		return Map("ok", false, "level", "Error",
			"message", "[录入验证错误]`n门诊校验缺少累计已扫目标")
	}

	if !(IsObject(anchor) && anchor.Has("ok") && anchor["ok"] && anchor.Has("restoreOk") && anchor["restoreOk"]) {
		Log_Debug("ui.confirm.opt_restore_fail", "锚点不可用于点回", Map(
			"needN", needN,
			"anchorOk", IsObject(anchor) && anchor.Has("ok") && anchor["ok"],
			"restoreOk", IsObject(anchor) && anchor.Has("restoreOk") && anchor["restoreOk"],
			"cx", IsObject(anchor) && anchor.Has("clientX") ? anchor["clientX"] : "",
			"cy", IsObject(anchor) && anchor.Has("clientY") ? anchor["clientY"] : ""
		))
		return Map("ok", false, "level", "Error",
			"message", "[录入验证错误]`n门诊校验缺少可用的网格点击锚点")
	}

	Log_Debug("ui.confirm.opt_snapshot", "门诊校验快照", Map(
		"needN", needN, "alreadyScanned", alreadyScanned, "codes", codeCount,
		"drugId", wantDrug, "spec", wantSpec, "qty", wantQty,
		"cx", anchor["clientX"], "cy", anchor["clientY"]
	))

	; 先点回（失败可再试一次），成功后只轮询复制读已扫，不再连点
	maxRestore := 2
	restoreUsed := 0
	restored := false
	delay := 15
	while (restoreUsed < maxRestore) {
		rr := UI_RestoreGridClick(anchor, gridClassNN, win)
		restoreUsed += 1
		if (IsObject(rr) && rr.Has("ok") && rr["ok"]) {
			restored := true
			break
		}
		Log_Debug("ui.confirm.opt_restore_fail", "点回失败", Map(
			"reason", IsObject(rr) && rr.Has("reason") ? rr["reason"] : "",
			"restoreUsed", restoreUsed, "elapsedMs", A_TickCount - t0
		))
		Sleep(delay)
		if (delay < 120)
			delay += 15
	}
	if !restored {
		Log_Debug("ui.confirm.opt_restore_exhausted", "点回次数用尽", Map(
			"restoreUsed", restoreUsed, "needN", needN, "elapsedMs", A_TickCount - t0
		))
		return Map("ok", false, "level", "Error",
			"message", "[录入验证错误]`n门诊校验点回目标行失败")
	}

	lastGot := -1
	lastTickLog := 0
	delay := 15

	; 门诊校验用 colFields（optCtx 优先，否则 Cfg.COL_FIELDS）
	srcFields := []
	if (IsObject(optCtx) && optCtx.Has("colFields"))
		srcFields := optCtx["colFields"]
	else if (IsSet(Cfg) && IsObject(Cfg) && Cfg.Has("COL_FIELDS"))
		srcFields := Cfg["COL_FIELDS"]
	confirmFields := Parse_NormalizeColFields(srcFields)
	if (confirmFields.Length = 0) {
		Log_Debug("ui.confirm.opt_fields_empty", "门诊校验列映射为空", Map(
			"hasOptCtx", IsObject(optCtx),
			"hasCfg", IsSet(Cfg) && IsObject(Cfg) && Cfg.Has("COL_FIELDS")
		))
		return Map("ok", false, "level", "Error",
			"message", "[录入验证错误]`n门诊校验缺少列映射配置")
	}

	while (A_TickCount - t0 < timeoutMs) {
		txt := UI_TryCopyGridClassNNText(gridClassNN, win)
		p := Parse_TargetInfo(confirmFields, "", txt, win, "", true)
		if !(IsObject(p) && p.Has("ok") && p["ok"]) {
			Log_Debug("ui.confirm.opt_candidates", "点回后解析失败", Map(
				"lines", StrSplit(Trim(txt), "`n").Length, "txtLen", StrLen(txt),
				"reason", IsObject(p) && p.Has("reason") ? p["reason"] : "invalid_result",
				"header", UI_ConfirmLinePreview(txt, 1),
				"firstRow", UI_ConfirmLinePreview(txt, 2),
				"anchorSiteHwnd", IsObject(anchor) && anchor.Has("siteHwnd") ? anchor["siteHwnd"] : 0,
				"restoreUsed", restoreUsed, "elapsedMs", A_TickCount - t0
			))
			Sleep(delay)
			if (delay < 120)
				delay += 15
			continue
		}

		by := p["bySpec"]
		gotDrug := Trim("" By_Get(by, "drugName"))
		gotSpec := Trim("" By_Get(by, "drugSpec"))
		gotQty := Util_ToInt(By_Get(by, "qty"), 0)
		if (gotDrug = "" || gotDrug != wantDrug || gotSpec = "" || gotSpec != wantSpec || gotQty != wantQty) {
			Log_Debug("ui.confirm.opt_row_mismatch", "点回行身份不匹配", Map(
				"wantDrug", wantDrug, "gotDrug", gotDrug,
				"wantSpec", wantSpec, "gotSpec", gotSpec,
				"wantQty", wantQty, "gotQty", gotQty,
				"elapsedMs", A_TickCount - t0
			))
			Sleep(delay)
			if (delay < 120)
				delay += 15
			continue
		}

		gotN := -1
		traceCell := Trim("" By_Get(by, "traceCode"))
		if (traceCell != "" && RegExMatch(traceCell, "已扫\s*(\d+)\s*码", &mScan))
			gotN := Integer(mScan[1])

		if (gotN >= needN) {
			Log_Debug("ui.confirm.opt_ok", "门诊校验通过", Map(
				"needN", needN, "gotN", gotN, "alreadyScanned", alreadyScanned,
				"drugId", wantDrug, "spec", wantSpec, "qty", wantQty,
				"restoreUsed", restoreUsed, "elapsedMs", A_TickCount - t0
			))
			return Map("ok", true)
		}

		if (gotN != lastGot || A_TickCount - lastTickLog >= 500) {
			lastTickLog := A_TickCount
			lastGot := gotN
			Log_Debug("ui.confirm.opt_tick", "门诊校验等待", Map(
				"needN", needN, "gotN", gotN, "alreadyScanned", alreadyScanned,
				"cellLen", StrLen(traceCell), "txtLen", StrLen(txt),
				"restoreUsed", restoreUsed,
				"elapsedMs", A_TickCount - t0
			))
		}

		Sleep(delay)
		if (delay < 120)
			delay += 15
	}

	Log_Debug("ui.confirm.opt_fail", "门诊校验超时", Map(
		"needN", needN, "lastGot", lastGot, "alreadyScanned", alreadyScanned,
		"restoreUsed", restoreUsed, "elapsedMs", A_TickCount - t0
	))
	return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n门诊窗口录入追溯码验证失败，未实际扫码成功")
}

UI_WaitConfirm_Warehouse(codes, timeoutMs, verifyGridClassNN, win := "A") {
	t0 := A_TickCount
	delay := 20
	lastTickLog := 0

	if !IsObject(codes) || (codes.Length = 0) {
		Log_Debug("ui.confirm.wh_empty", "仓库验证缺码", Map())
		return Map("ok", false, "level", "Warn", "message", "[录入验证错误] 仓库验证缺少待验证码")
	}

	target := Trim(codes[1])
	if (target = "") {
		Log_Debug("ui.confirm.wh_empty", "仓库验证目标码为空", Map())
		return Map("ok", false, "level", "Warn", "message", "[录入验证错误] 仓库验证目标码为空")
	}

	codeTail := (StrLen(target) <= 4) ? target : SubStr(target, -3)
	Log_Debug("ui.confirm.wh_start", "仓库首条校验", Map(
		"verifyNn", verifyGridClassNN, "codeLen", StrLen(target), "codeTail", codeTail, "timeoutMs", timeoutMs
	))

	while (A_TickCount - t0 < timeoutMs) {
		dlg := UI_PollIptDialogs(180)
		if (dlg = "abort") {
			Log_Debug("ui.confirm.wh_dialog", "仓库失败弹窗", Map("elapsedMs", A_TickCount - t0))
			return Map("ok", false, "level", "Warn", "message", "[录入验证错误] 仓库窗口出现错误提示，已终止本次注入")
		}

		txt := UI_TryCopyGridClassNNText(verifyGridClassNN, win)
		if (txt != "" && InStr(txt, target)) {
			Log_Debug("ui.confirm.wh_ok", "仓库首条校验通过", Map("elapsedMs", A_TickCount - t0, "codeTail", codeTail))
			return Map("ok", true)
		}

		if (A_TickCount - lastTickLog >= 500) {
			lastTickLog := A_TickCount
			Log_Debug("ui.confirm.wh_tick", "仓库校验等待", Map(
				"txtLen", StrLen(txt),
				"elapsedMs", A_TickCount - t0, "codeTail", codeTail, "dlg", dlg
			))
		}

		Sleep(delay)
		if (delay < 140)
			delay += 20
	}

	Log_Debug("ui.confirm.wh_fail", "仓库首条校验超时", Map("elapsedMs", A_TickCount - t0, "codeTail", codeTail))
	return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n仓库窗口首条注入验证失败，未匹配到目标码")
}

; 诊断仅保留指定行的短预览；长数字统一脱敏，避免记录处方号/追溯码原文
UI_ConfirmLinePreview(txt, lineNo, maxLen := 220) {
	text := Trim("" txt, " `t`r`n")
	if (text = "")
		return ""
	lines := StrSplit(text, "`n")
	if (lineNo < 1 || lineNo > lines.Length)
		return ""
	line := StrReplace(lines[lineNo], "`r", "")
	line := StrReplace(line, "`t", " | ")
	line := RegExReplace(line, "\d{5,}", "<digits>")
	if (StrLen(line) > maxLen)
		line := SubStr(line, 1, maxLen) "…"
	return line
}
