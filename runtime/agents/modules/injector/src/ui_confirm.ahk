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

; optTargetScanned：门诊累计已扫目标（必填 >0）；iptSawForce：贴码阶段已点过「信息不匹配：是」
UI_WaitConfirm(codes, timeoutMs, opt, ipt, optVerifyGridClassNN, iptVerifyGridClassNN, iptParseGridClassNN, win, optTargetScanned, iptSawForce) {
	t0 := A_TickCount
	delay := 15
	lastTickLog := 0

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
			"message", "[录入验证错误]`n录入窗口已关闭，无法确认是否注入成功",
			"reason", "win_closed_unconfirmed"
		)
	}

	cls := ""
	try cls := WinGetClass(win)
	catch
		cls := ""
	Log_Debug("ui.confirm.start", "开始录入校验", Map(
		"cls", cls, "codes", IsObject(codes) ? codes.Length : 0,
		"timeoutMs", timeoutMs, "optTargetScanned", optTargetScanned, "iptSawForce", iptSawForce,
		"optVerifyNn", optVerifyGridClassNN, "iptVerifyNn", iptVerifyGridClassNN
	))

	; 门诊：HIS「已扫 N 码」按码累计；跨码时码数≠拆零余数（粒），目标必须由调用方显式传入
	if (cls = opt) {
		needN := Util_ToInt(optTargetScanned, 0)
		if (needN <= 0) {
			Log_Debug("ui.confirm.opt_bad_target", "门诊缺少累计已扫目标", Map(
				"optTargetScanned", optTargetScanned, "codes", IsObject(codes) ? codes.Length : 0
			))
			return Map("ok", false, "level", "Error",
				"message", "[录入验证错误]`n门诊校验缺少累计已扫目标")
		}

		colSpecs := ["追溯码"]
		intCols := []
		lastGot := -1

		while (A_TickCount - t0 < timeoutMs) {
			txt := UI_TryCopyGridClassNNText(optVerifyGridClassNN, win)
			gotN := -1
			cell := ""
			if RegExMatch(txt, "已扫\s*\d+\s*码") {
				gotN := UI_Parse_MaxScanned(txt)
			} else {
				p := Parse_TargetInfo(colSpecs, ipt, intCols, txt, win, iptParseGridClassNN, true)
				if (IsObject(p) && p.Has("ok") && p["ok"]) {
					v := p["bySpec"].Has("追溯码") ? Trim(p["bySpec"]["追溯码"]) : ""
					cell := v
					if (v != "" && RegExMatch(v, "已扫\s*(\d+)\s*码", &m))
						gotN := Integer(m[1])
				}
			}

			if (gotN >= needN) {
				Log_Debug("ui.confirm.opt_ok", "门诊校验通过", Map(
					"needN", needN, "gotN", gotN, "elapsedMs", A_TickCount - t0, "cell", cell
				))
				return Map("ok", true)
			}

			if (gotN != lastGot || A_TickCount - lastTickLog >= 500) {
				lastTickLog := A_TickCount
				lastGot := gotN
				Log_Debug("ui.confirm.opt_tick", "门诊校验等待", Map(
					"needN", needN, "gotN", gotN,
					"cell", cell, "txtLen", StrLen(txt), "elapsedMs", A_TickCount - t0
				))
			}

			Sleep(delay)
			if (delay < 120)
				delay += 15
		}
		Log_Debug("ui.confirm.opt_fail", "门诊校验超时", Map(
			"needN", needN, "lastGot", lastGot, "elapsedMs", A_TickCount - t0
		))
		return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n门诊窗口录入追溯码验证失败，未实际扫码成功")
	}

	; 住院流程要求验证区域出现全部注入码，并处理可能的失败弹窗
	if (cls = ipt) {
		ipt := Map("codes", codes, "gridN", 1)
		lastHit := -1
		sawForce := !!iptSawForce
		hit := 0

		while (A_TickCount - t0 < timeoutMs) {
			dlg := UI_PollIptDialogs(180)
			if (dlg = "abort") {
				Log_Debug("ui.confirm.ipt_dialog", "住院失败弹窗触发回退", Map("elapsedMs", A_TickCount - t0))
				return Map("ok", false, "level", "Warn", "message", "[录入验证错误]`n重复的追溯码/超过对应需要追溯码条数，将自动回退库存")
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
					"message", "[录入验证错误]`n录入窗口已关闭，无法确认是否注入成功",
					"reason", "win_closed_unconfirmed"
				)
			}

			txt := UI_TryCopyGridClassNNText(iptVerifyGridClassNN, win)
			hit := 0
			if (txt != "") {
				allOk := true
				for c in ipt["codes"] {
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

UI_WaitConfirm_Warehouse(codes, timeoutMs, verifyGridClassNN, win := "A") {
	t0 := A_TickCount
	delay := 20
	lastTickLog := 0

	if !IsObject(codes) || (codes.Length = 0) {
		Log_Debug("ui.confirm.wh_empty", "仓库验证缺码", Map())
		return Map("ok", false, "level", "Warn", "message", "[录入验证错误]`n仓库验证缺少待验证码")
	}

	target := Trim(codes[1])
	if (target = "") {
		Log_Debug("ui.confirm.wh_empty", "仓库验证目标码为空", Map())
		return Map("ok", false, "level", "Warn", "message", "[录入验证错误]`n仓库验证目标码为空")
	}

	codeTail := (StrLen(target) <= 4) ? target : SubStr(target, -3)
	Log_Debug("ui.confirm.wh_start", "仓库首条校验", Map(
		"verifyNn", verifyGridClassNN, "codeLen", StrLen(target), "codeTail", codeTail, "timeoutMs", timeoutMs
	))

	while (A_TickCount - t0 < timeoutMs) {
		dlg := UI_PollIptDialogs(180)
		if (dlg = "abort") {
			Log_Debug("ui.confirm.wh_dialog", "仓库失败弹窗", Map("elapsedMs", A_TickCount - t0))
			return Map("ok", false, "level", "Warn", "message", "[录入验证错误]`n仓库窗口出现错误提示，已终止本次注入")
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

UI_Parse_MaxScanned(txt) {
	max := 0
	pos := 1
	while RegExMatch(txt, "已扫\s*(\d+)\s*码", &m, pos) {
		n := Integer(m[1])
		if (n > max)
			max := n
		pos := m.Pos + m.Len
	}
	return max
}

; 读门诊验证网格当前「已扫 N 码」；读不到返回 -1（勿当 0，以免误预留满额）
UI_ReadOptScannedCount(verifyNn, win := "A", iptCls := "", parseNn := "") {
	win := Util_NormalizeWin(win)
	txt := UI_TryCopyGridClassNNText(verifyNn, win)
	if (txt = "")
		return -1
	if RegExMatch(txt, "已扫\s*\d+\s*码")
		return UI_Parse_MaxScanned(txt)

	; 全文无「已扫」时再试追溯码列（未扫过可能仍无文案）
	if (iptCls != "" || parseNn != "") {
		p := Parse_TargetInfo(["追溯码"], iptCls, [], txt, win, parseNn, true)
		if (IsObject(p) && p.Has("ok") && p["ok"]) {
			v := p["bySpec"].Has("追溯码") ? Trim(p["bySpec"]["追溯码"]) : ""
			if (v != "" && RegExMatch(v, "已扫\s*(\d+)\s*码", &m))
				return Integer(m[1])
			; 追溯码列为空/无已扫文案：视为尚未扫过
			if (v = "" || !RegExMatch(v, "\d"))
				return 0
		}
	}

	; 网格有内容但无「已扫」模式：无法判断，拒绝猜测
	return -1
}
