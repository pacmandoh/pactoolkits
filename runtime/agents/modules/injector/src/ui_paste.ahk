; 追溯码贴码：门诊/住院策略、仓库快写、写入校验与回车
; 仅允许向已识别的录入窗口写入；住院标题须含「追溯码录入」
UI_Paste_ByPolicy(
	text,
	opt,
	ipt,
	optInputClassNN,
	iptInputClassNN,
	win := "A"
) {
	win := Util_NormalizeWin(win)
	cls := WinGetClass(win)
	ttl := WinGetTitle(win)
	codeLen := StrLen(text)
	codeTail := (codeLen <= 4) ? text : SubStr(text, -3)

	if (cls = ipt && InStr(ttl, "追溯码录入")) {
		Log_Debug("ui.paste", "住院贴码", Map(
			"inputNn", iptInputClassNN, "codeLen", codeLen, "codeTail", codeTail, "ttl", ttl, "cls", cls
		))
		return Ui_Paste_Impl(win, iptInputClassNN, text, false)
	}
	if (cls = ipt) {
		Log_Debug("ui.paste.ipt_title_miss", "住院类但标题无追溯码录入", Map(
			"ttl", ttl, "cls", cls, "need", "追溯码录入", "codeLen", codeLen
		))
		return Map("ok", false, "level", "Error", "message", "[界面错误]`n请在门诊或住院录入窗口进行操作", "reason", "ipt title mismatch")
	}
	if (cls = opt) {
		Log_Debug("ui.paste", "门诊贴码", Map(
			"inputNn", optInputClassNN, "codeLen", codeLen, "codeTail", codeTail, "ttl", ttl, "cls", cls
		))
		return Ui_Paste_Impl(win, optInputClassNN, text, true)
	}
	Log_Debug("ui.paste.miss", "贴码窗口类不匹配", Map("cls", cls, "ttl", ttl, "opt", opt, "ipt", ipt, "codeLen", codeLen))
	return Map("ok", false, "level", "Error", "message", "[界面错误]`n请在门诊或住院录入窗口进行操作", "reason", "class mismatch")
}

UI_Paste_Warehouse(text, inputClassNN, win := "A") {
	; 仓库高吞吐路径缓存 HWND，通过 WM_SETTEXT 写入并仅发送 keydown
	return UI_Paste_WarehouseFast(text, inputClassNN, win)
}

UI_Paste_WarehouseFast(text, inputClassNN, win := "A") {
	win := Util_NormalizeWin(win)
	t0 := A_TickCount
	codeLen := StrLen(text)
	if !WinExist(win) {
		Log_Debug("ui.wh_paste.miss_win", "仓库快写目标窗不存在", Map("ctrl", inputClassNN, "codeLen", codeLen))
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n目标窗口不存在或已关闭")
	}

	hwndCtrl := UI_GetCachedCtrlHwnd(inputClassNN, win)
	if !hwndCtrl {
		Log_Debug("ui.wh_paste.miss_ctrl", "仓库快写控件未找到", Map("ctrl", inputClassNN, "codeLen", codeLen))
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n获取当前窗口 hwnd 失败", "reason", "control not found", "ctrl", inputClassNN)
	}
	if !DllCall("IsWindow", "Ptr", hwndCtrl, "Int") {
		Log_Debug("ui.wh_paste.dead_hwnd", "仓库控件 HWND 已失效", Map("ctrl", inputClassNN))
		return Map("ok", false, "level", "Warn", "message", "[窗口错误]`n写入输入框失败（窗口可能已关闭）", "reason", "win_closed")
	}

	; 直接写入可避免剪贴板竞争，适用于连续仓库任务
	okSet := false
	via := ""
	try {
		; 使用 WM_SETTEXT 避免模拟键盘输入受焦点状态影响
		SendMessage(0x000C, 0, StrPtr(text), , "ahk_id " hwndCtrl)
		okSet := true
		via := "WM_SETTEXT"
	} catch {
		try {
			ControlSetText(text, inputClassNN, win)
			okSet := true
			via := "ControlSetText"
		} catch {
		}
	}
	if !okSet {
		Log_Debug("ui.wh_paste.write_fail", "仓库快写失败", Map("ctrl", inputClassNN, "hwnd", hwndCtrl, "codeLen", codeLen))
		return Map("ok", false, "level", "Warn", "message", "[窗口错误]`n写入输入框失败（窗口可能已关闭）", "reason", "win_closed")
	}

	; keydown 前延迟是窗口消息时序契约，过短会导致首尾记录错位
	keydownDelay := 8
	if (keydownDelay > 0)
		Sleep(keydownDelay)

	; 该窗口链路仅 PostMessage 的 keydown 能稳定生效，与住院流程保持一致
	; lParam 使用 repeat=1，避免部分控件拒绝零值键消息
	if !DllCall("IsWindow", "Ptr", hwndCtrl, "Int") {
		Log_Debug("ui.wh_paste.dead_before_enter", "回车前控件已失效", Map("ctrl", inputClassNN, "via", via))
		return Map("ok", false, "level", "Warn", "message", "[窗口错误]`n写入输入框失败（窗口可能已关闭）", "reason", "win_closed")
	}
	try PostMessage(0x0100, 0x0D, 1, , "ahk_id " hwndCtrl)
	catch {
		Log_Debug("ui.wh_paste.enter_fail", "仓库回车投递失败", Map("ctrl", inputClassNN, "hwnd", hwndCtrl))
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n仓库回车投递失败", "reason", "enter_fail")
	}
	; 短暂让出执行权可降低高吞吐时的窗口消息积压和记录错位
	Sleep(1)
	Log_Debug("ui.wh_paste.ok", "仓库快写完成", Map(
		"ctrl", inputClassNN, "hwnd", hwndCtrl, "via", via,
		"codeLen", codeLen, "elapsedMs", A_TickCount - t0
	))
	return Map("ok", true, "ctrl", inputClassNN)
}

UI_PrepareWarehouseFastTarget(inputClassNN, win := "A") {
	win := Util_NormalizeWin(win)
	if !WinExist(win) {
		Log_Debug("ui.wh_prep.miss_win", "仓库准备目标窗不存在", Map("ctrl", inputClassNN))
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n目标窗口不存在或已关闭")
	}

	if !WinActive(win) {
		try WinActivate(win)
		catch {
			Log_Debug("ui.wh_prep.activate_fail", "无法激活仓库窗", Map("ctrl", inputClassNN))
			return Map("ok", false, "level", "Error", "message", "[窗口错误]`n无法激活目标窗口，可能已切换/关闭")
		}
		try WinWaitActive(win, , 0.6)
		catch {
			Log_Debug("ui.wh_prep.wait_fail", "仓库窗未就绪", Map("ctrl", inputClassNN))
			return Map("ok", false, "level", "Error", "message", "[窗口错误]`n目标窗口未就绪，无法注入")
		}
	}

	hwndCtrl := UI_FocusClassNN(inputClassNN, win, true)
	if !hwndCtrl {
		Log_Debug("ui.wh_prep.miss_ctrl", "仓库输入框聚焦失败", Map("ctrl", inputClassNN))
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n获取当前窗口 hwnd 失败", "reason", "control not found", "ctrl", inputClassNN)
	}
	Log_Debug("ui.wh_prep.ok", "仓库输入框就绪", Map("ctrl", inputClassNN, "hwnd", hwndCtrl))
	return Map("ok", true)
}

UI_Paste_Impl(winTitle, classNN, text, doEnter := true) {
	; doEnter：true=门诊(KEYDOWN+KEYUP)；false=住院/仓库稳写(仅 KEYDOWN，勿发 KEYUP)
	winTitle := Util_NormalizeWin(winTitle)
	t0 := A_TickCount
	codeLen := StrLen(text)
	codeTail := (codeLen <= 4) ? text : SubStr(text, -3)
	if !WinExist(winTitle) {
		Log_Debug("ui.paste_impl.miss_win", "目标窗不存在", Map("win", winTitle, "ctrl", classNN, "codeLen", codeLen))
		return Map("ok", false, "level", "Warn", "message", "[窗口错误]`n目标窗口不存在或已关闭", "reason", "win_closed")
	}

	; 必须先聚焦输入框：仅 WinActivate 时焦点常仍在网格，WM_PASTE 偶发“无异常但不写入”
	hwndCtrl := UI_FocusClassNN(classNN, winTitle, true)
	if !hwndCtrl {
		closed := !WinExist(winTitle)
		Log_Debug("ui.paste_impl.miss_ctrl", closed ? "窗已关闭无控件" : "输入框聚焦失败", Map(
			"ctrl", classNN, "win", winTitle, "codeLen", codeLen, "closed", closed
		))
		return Map(
			"ok", false,
			"level", "Warn",
			"message", closed
				? "[窗口错误]`n录入窗口已关闭，无法继续注入"
			: "[窗口错误]`n无法聚焦追溯码输入框",
			"reason", closed ? "win_closed" : "focus_fail",
			"ctrl", classNN
		)
	}

	if !WinExist(winTitle) || !DllCall("IsWindow", "Ptr", hwndCtrl, "Int") {
		Log_Debug("ui.paste_impl.closed_before_send", "发送前窗/控件已失效", Map("ctrl", classNN, "hwnd", hwndCtrl))
		return Map("ok", false, "level", "Warn", "message", "[窗口错误]`n录入窗口已关闭，无法继续注入", "reason", "win_closed")
	}

	oldClip := ClipboardAll()
	via := ""
	try {
		A_Clipboard := text
		if !ClipWait(0.6) {
			Log_Debug("ui.paste_impl.clip_timeout", "贴码剪贴板超时", Map("ctrl", classNN, "codeLen", codeLen))
			return Map(
				"ok", false, "level", "Warn", "message", "[解析错误]`n等待超时：`n - 请确认是否选中列表中相应药品",
				"reason", "ClipWait timeout"
			)
		}

		; 写入后读回核对；未写入绝不发 Enter（避免 HIS「码不对」）
		written := false

		try {
			SendMessage(0x0302, 0, 0, , "ahk_id " hwndCtrl) ; WM_PASTE
			Sleep(30)
			if UI_CtrlTextMatches(hwndCtrl, classNN, winTitle, text) {
				written := true
				via := "WM_PASTE"
			} else {
				Log_Debug("ui.paste_impl.paste_no_take", "WM_PASTE 未落入控件", Map(
					"ctrl", classNN, "hwnd", hwndCtrl, "codeLen", codeLen, "codeTail", codeTail
				))
			}
		} catch as e {
			Log_Debug("ui.paste_impl.send_fail", "WM_PASTE 失败", Map(
				"ctrl", classNN, "hwnd", hwndCtrl, "err", e.Message
			))
		}

		if !written {
			try {
				ControlSetText(text, classNN, winTitle)
				Sleep(20)
				if UI_CtrlTextMatches(hwndCtrl, classNN, winTitle, text) {
					written := true
					via := "ControlSetText"
				}
			} catch as e {
				Log_Debug("ui.paste_impl.settext_fail", "ControlSetText 失败", Map(
					"ctrl", classNN, "err", e.Message
				))
			}
		}

		if !written && DllCall("IsWindow", "Ptr", hwndCtrl, "Int") {
			try {
				SendMessage(0x000C, 0, StrPtr(text), , "ahk_id " hwndCtrl) ; WM_SETTEXT
				Sleep(20)
				if UI_CtrlTextMatches(hwndCtrl, classNN, winTitle, text) {
					written := true
					via := "WM_SETTEXT"
				}
			} catch as e {
				Log_Debug("ui.paste_impl.settext_msg_fail", "WM_SETTEXT 失败", Map(
					"ctrl", classNN, "hwnd", hwndCtrl, "err", e.Message
				))
			}
		}

		if !written {
			got := ""
			try got := ControlGetText(hwndCtrl)
			catch {
				try got := ControlGetText(classNN, winTitle)
				catch
					got := ""
			}
			Log_Debug("ui.paste_impl.verify_fail", "贴码未写入控件，跳过 Enter", Map(
				"ctrl", classNN, "hwnd", hwndCtrl, "codeLen", codeLen, "codeTail", codeTail,
				"gotLen", StrLen(got), "gotTail", (StrLen(got) <= 4) ? got : SubStr(got, -3),
				"closed", !WinExist(winTitle)
			))
			return Map(
				"ok", false, "level", "Warn",
				"message", "[窗口错误]`n追溯码未写入输入框，已中止回车以免误提交",
				"reason", "paste_verify_fail", "ctrl", classNN
			)
		}

		if !DllCall("IsWindow", "Ptr", hwndCtrl, "Int") {
			Log_Debug("ui.paste_impl.closed_after_write", "写入后控件已失效", Map("ctrl", classNN, "via", via))
			return Map("ok", false, "level", "Warn", "message", "[窗口错误]`n录入窗口已关闭，无法继续注入", "reason", "win_closed")
		}

		; doEnter 两种模式必须分开（实测契约，禁止合并成同一种回车）：
		;   true  → 门诊：KEYDOWN + KEYUP
		;   false → 住院/仓库稳写：仅 KEYDOWN（禁止补发 KEYUP）
		; 仅在读回确认写入成功后才回车；回车失败（含控件已死）一律失败，由调用方回滚
		Sleep(50)
		try {
			if (doEnter) {
				PostMessage(0x0100, 0x0D, 0, , "ahk_id " hwndCtrl)
				PostMessage(0x0101, 0x0D, 0, , "ahk_id " hwndCtrl)
			} else {
				PostMessage(0x0100, 0x0D, 0, , "ahk_id " hwndCtrl)
			}
		} catch as e {
			alive := DllCall("IsWindow", "Ptr", hwndCtrl, "Int")
			Log_Debug("ui.paste_impl.enter_fail", "回车投递失败", Map(
				"ctrl", classNN, "hwnd", hwndCtrl, "err", e.Message, "via", via,
				"doEnter", doEnter, "alive", alive
			))
			return Map(
				"ok", false, "level", "Warn",
				"message", alive
					? "[窗口错误]`n回车投递失败"
				: "[窗口错误]`n回车时录入窗口已关闭，无法确认是否提交",
				"reason", alive ? "enter_fail" : "win_closed"
			)
		}
	} finally {
		try A_Clipboard := oldClip
	}

	Log_Debug("ui.paste_impl.ok", "贴码完成", Map(
		"ctrl", classNN, "hwnd", hwndCtrl, "doEnter", doEnter, "via", via,
		"codeLen", codeLen, "codeTail", codeTail, "elapsedMs", A_TickCount - t0
	))
	return Map("ok", true, "ctrl", classNN, "doEnter", doEnter, "via", via)
}

; 读回输入框文本，确认贴码已落入控件（Trim 后全等，或控件仅多尾空白）
UI_CtrlTextMatches(hwndCtrl, classNN, win, expect) {
	want := Trim("" expect)
	if (want = "")
		return false
	got := ""
	if (hwndCtrl && DllCall("IsWindow", "Ptr", hwndCtrl, "Int")) {
		try got := ControlGetText(hwndCtrl)
		catch
			got := ""
	}
	if (got = "") {
		try got := ControlGetText(classNN, win)
		catch
			got := ""
	}
	got := Trim(got, " `t`r`n")
	if (got = want)
		return true
	; 少数控件读回带不可见后缀，允许极短超额且包含完整码
	return (InStr(got, want) = 1 && StrLen(got) <= StrLen(want) + 2)
}
