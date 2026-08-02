; 门诊与住院窗口的追溯码注入、结果验证和失败弹窗处理
global __UI_FAST_CTRL_CACHE := Map()
; 仅允许向已识别的门诊或住院录入窗口写入，避免影响无关窗口
; HotIf 已限制目标进程，此处继续按窗口类和标题选择配置的输入控件
; 住院窗口还必须包含“追溯码录入”标题，门诊窗口仅按窗口类识别
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

UI_GetCachedCtrlHwnd(classNN, win := "A") {
	global __UI_FAST_CTRL_CACHE
	win := Util_NormalizeWin(win)
	hwndWin := 0
	try hwndWin := WinGetID(win)
	catch
		return 0
	if !hwndWin
		return 0

	key := hwndWin "|" classNN
	if (__UI_FAST_CTRL_CACHE.Has(key)) {
		h := __UI_FAST_CTRL_CACHE[key]
		; 缓存的 HWND 可能随窗口重建失效，使用前必须验证
		if (DllCall("IsWindow", "Ptr", h, "Int"))
			return h
		__UI_FAST_CTRL_CACHE.Delete(key)
	}

	hwndCtrl := Util_GetCtrlHwndByClassNN(classNN, "ahk_id " hwndWin)
	if !hwndCtrl
		return 0

	__UI_FAST_CTRL_CACHE[key] := hwndCtrl
	return hwndCtrl
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
			try got := ControlGetText(, "ahk_id " hwndCtrl)
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

UI_PostClick(hwndCtrl, x := 30, y := 40) {
	; 合成鼠标消息使用控件客户区坐标，不使用屏幕坐标
	static WM_LBUTTONDOWN := 0x0201
	static WM_LBUTTONUP := 0x0202
	static MK_LBUTTON := 0x0001
	if !hwndCtrl || !DllCall("IsWindow", "Ptr", hwndCtrl, "Int")
		return
	lParam := (y << 16) | (x & 0xFFFF)

	; 合成点击用于获取焦点；窗已关时勿冒 AHK 原生报错
	try PostMessage(WM_LBUTTONDOWN, MK_LBUTTON, lParam, , "ahk_id " hwndCtrl)
	catch {
	}
	try PostMessage(WM_LBUTTONUP, 0, lParam, , "ahk_id " hwndCtrl)
	catch {
	}
}

; 读回输入框文本，确认贴码已落入控件（Trim 后全等，或控件仅多尾空白）
UI_CtrlTextMatches(hwndCtrl, classNN, win, expect) {
	want := Trim("" expect)
	if (want = "")
		return false
	got := ""
	if (hwndCtrl && DllCall("IsWindow", "Ptr", hwndCtrl, "Int")) {
		try got := ControlGetText(, "ahk_id " hwndCtrl)
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

UI_TryCopyClassNNText(classNN, win := "A", control := true) {
	if (UI_FocusClassNN(classNN, win, control) = "")
		return ""

	old := ClipboardAll()
	A_Clipboard := ""

	SendInput("^c")

	if !ClipWait(0.25) {
		A_Clipboard := old
		return ""
	}
	txt := A_Clipboard
	A_Clipboard := old

	return txt
}

UI_TryCopyGridClassNNText(classNN, win := "A", control := true) {
	win := Util_NormalizeWin(win)
	if !WinExist(win)
		return ""
	if (UI_FocusClassNN(classNN, win, control) = "")
		return ""

	old := ClipboardAll()
	A_Clipboard := ""

	try SendInput("^c")
	catch {
		try A_Clipboard := old
		return ""
	}

	if !ClipWait(0.25) {
		try A_Clipboard := old
		return ""
	}
	txt := A_Clipboard
	try A_Clipboard := old

	return txt
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

; 沿父链按完整 ClassNN 查找（勿用去尾数字拆类名：SysListView321 ≠ SysListView+321）
UI_FindAncestorByClassNN(hwnd, targetNN, maxDepth := 40) {
	nnTarget := Trim("" targetNN)
	if (nnTarget = "" || !hwnd)
		return 0
	h := hwnd
	Loop maxDepth {
		if !h
			return 0
		nn := ""
		try nn := ControlGetClassNN(h)
		catch
			nn := ""
		if (nn = nnTarget)
			return h
		h := DllCall("user32\GetParent", "ptr", h, "ptr")
	}
	return 0
}

UI_GetWindowRect(hwnd, &x, &y, &w, &h) {
	x := 0, y := 0, w := 0, h := 0
	if !hwnd
		return false
	rc := Buffer(16, 0)
	if !DllCall("user32\GetWindowRect", "ptr", hwnd, "ptr", rc, "int")
		return false
	left := NumGet(rc, 0, "int")
	top := NumGet(rc, 4, "int")
	right := NumGet(rc, 8, "int")
	bottom := NumGet(rc, 12, "int")
	x := left
	y := top
	w := right - left
	h := bottom - top
	return true
}

UI_CaptureGridClickAnchor(targetNN, win := "A") {
	win := Util_NormalizeWin(win)
	MouseGetPos &sx, &sy, &winHwnd, &ctrlHwnd, 2
	return UI_CaptureGridClickAnchorFromPoint(targetNN, Map("ok", true, "screenX", sx, "screenY", sy), win, ctrlHwnd)
}

UI_CaptureGridClickAnchorFromPoint(targetNN, clickPoint, win := "A", ctrlHwnd := 0) {
	win := Util_NormalizeWin(win)
	if !IsObject(clickPoint)
		return Map("ok", false)
	sx := clickPoint.Has("screenX") ? Integer(clickPoint["screenX"]) : 0
	sy := clickPoint.Has("screenY") ? Integer(clickPoint["screenY"]) : 0

	h0 := ctrlHwnd
	if !h0 {
		h0 := DllCall("user32\WindowFromPoint", "Int64", (sy << 32) | sx, "Ptr")
	}
	if !h0
		return Map("ok", false)

	nnTarget := Trim("" targetNN)
	hSite := UI_FindAncestorByClassNN(h0, nnTarget)
	if !hSite
		return Map("ok", false)

	x := 0, y := 0, w := 0, h := 0
	if !UI_GetWindowRect(hSite, &x, &y, &w, &h)
		return Map("ok", false)

	relY := sy - y
	if (relY < 0)
		relY := 0
	rowHeightPx := 24
	rowSlot := Floor(relY / rowHeightPx)

	return Map(
		"ok", true,
		"screenX", sx,
		"screenY", sy,
		"relY", relY,
		"rowSlot", rowSlot,
		"targetNN", nnTarget
	)
}

UI_MouseOnClassNN(targetNN, win := "A") {
	win := Util_NormalizeWin(win)
	MouseGetPos &sx, &sy, &winHwnd, &ctrlHwnd, 2

	; 优先使用 AHK 已解析的控件句柄，以保持与实际命中目标一致
	h0 := ctrlHwnd
	if !h0 {
		; 控件句柄不可用时通过指针位置解析目标窗口
		h0 := DllCall("user32\WindowFromPoint"
			, "Int64", (sy << 32) | sx, "Ptr")
	}
	if !h0
		return false

	; 点击落在网格子控件上时沿父链匹配完整 ClassNN
	return !!UI_FindAncestorByClassNN(h0, targetNN)
}
