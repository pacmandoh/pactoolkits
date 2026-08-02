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

	if (cls = ipt && InStr(ttl, "追溯码录入")) {
		return Ui_Paste_Impl(win, iptInputClassNN, text, false)
	} else if (cls = opt) {
		return Ui_Paste_Impl(win, optInputClassNN, text, true)
	} else {
		return Map("ok", false, "level", "Error", "message", "[界面错误]`n请在门诊或住院录入窗口进行操作", "reason", "class mismatch")
	}
}

UI_Paste_Warehouse(text, inputClassNN, win := "A") {
	; 仓库高吞吐路径缓存 HWND，通过 WM_SETTEXT 写入并仅发送 keydown
	return UI_Paste_WarehouseFast(text, inputClassNN, win)
}

UI_Paste_WarehouseFast(text, inputClassNN, win := "A") {
	win := Util_NormalizeWin(win)
	if !WinExist(win)
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n目标窗口不存在或已关闭")

	hwndCtrl := UI_GetCachedCtrlHwnd(inputClassNN, win)
	if !hwndCtrl
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n获取当前窗口 hwnd 失败", "reason", "control not found", "ctrl", inputClassNN)

	; 直接写入可避免剪贴板竞争，适用于连续仓库任务
	okSet := false
	try {
		; 使用 WM_SETTEXT 避免模拟键盘输入受焦点状态影响
		SendMessage(0x000C, 0, StrPtr(text), , "ahk_id " hwndCtrl)
		okSet := true
	} catch {
		try {
			ControlSetText(text, inputClassNN, win)
			okSet := true
		} catch {
		}
	}
	if !okSet
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n写入输入框失败")

	; keydown 前延迟是窗口消息时序契约，过短会导致首尾记录错位
	keydownDelay := 8
	if (keydownDelay > 0)
		Sleep(keydownDelay)

	; 该窗口链路仅 PostMessage 的 keydown 能稳定生效，与住院流程保持一致
	; lParam 使用 repeat=1，避免部分控件拒绝零值键消息
	PostMessage(0x0100, 0x0D, 1, , "ahk_id " hwndCtrl)
	; 短暂让出执行权可降低高吞吐时的窗口消息积压和记录错位
	Sleep(1)
	return Map("ok", true, "ctrl", inputClassNN, "enter", false)
}

UI_PrepareWarehouseFastTarget(inputClassNN, win := "A") {
	win := Util_NormalizeWin(win)
	if !WinExist(win)
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n目标窗口不存在或已关闭")

	if !WinActive(win) {
		try WinActivate(win)
		catch
			return Map("ok", false, "level", "Error", "message", "[窗口错误]`n无法激活目标窗口，可能已切换/关闭")
		try WinWaitActive(win, , 0.6)
		catch
			return Map("ok", false, "level", "Error", "message", "[窗口错误]`n目标窗口未就绪，无法注入")
	}

	hwndCtrl := UI_FocusClassNN(inputClassNN, win, true)
	if !hwndCtrl
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n获取当前窗口 hwnd 失败", "reason", "control not found", "ctrl", inputClassNN)
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
	winTitle := Util_NormalizeWin(winTitle)
	if !WinExist(winTitle)
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n目标窗口不存在或已关闭")

	hwndCtrl := Util_GetCtrlHwndByClassNN(classNN, winTitle)
	if !hwndCtrl
		return Map(
			"ok", false, "level", "Error", "message", "[窗口错误]`n获取当前窗口 hwnd 失败",
			"reason", "control not found", "ctrl", classNN
		)

	try WinActivate(winTitle)
	catch
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n无法激活目标窗口，可能已切换/关闭")
	try WinWaitActive(winTitle, , 1)
	catch
		return Map("ok", false, "level", "Error", "message", "[窗口错误]`n目标窗口未就绪，无法注入")

	oldClip := ClipboardAll()
	try {
		A_Clipboard := text
		if !ClipWait(0.6)
			return Map(
				"ok", false, "level", "Warn", "message", "[解析错误]`n等待超时：`n - 请确认是否选中列表中相应药品",
				"reason", "ClipWait timeout"
			)

		SendMessage(0x0302, 0, 0, , "ahk_id " hwndCtrl)

		if (doEnter) {
			Sleep(50)
			PostMessage(0x0100, 0x0D, 0, , "ahk_id " hwndCtrl)
			PostMessage(0x0101, 0x0D, 0, , "ahk_id " hwndCtrl)
		} else {
			Sleep(50)
			PostMessage(0x0100, 0x0D, 0, , "ahk_id " hwndCtrl)
		}
	} finally {
		try A_Clipboard := oldClip
	}

	return Map("ok", true, "ctrl", classNN, "enter", doEnter)
}

UI_DetectAndHandleFailDialog() {

	; “信息确认”对话框要求在信息不符时仍继续，因此发送 Y
	if (hwnd := WinExist("信息确认")) {
		win := "ahk_id " hwnd
		txt := WinGetText(win)

		if InStr(txt, "对应")
			&& InStr(txt, "不符")
			&& InStr(txt, "是否继续") {

			try {
				ControlSend("y", , win)
			} catch {
				; 对目标确认框发送 Y 键虚拟键码
				PostMessage(0x100, 0x59, 0, , win) ; WM_KEYDOWN
				PostMessage(0x101, 0x59, 0, , win) ; WM_KEYUP
			}
			return false
		}
	}

	; “提示”对话框根据业务文案选择确认或取消
	if (hwnd := WinExist("提示")) {
		win := "ahk_id " hwnd
		txt := WinGetText(win)

		; 重复追溯码提示使用 Enter 关闭
		if InStr(txt, "重复的追溯码")
			&& InStr(txt, "不能录入") {

			try {
				ControlSend("{Enter}", , win)
			} catch {
				; 对目标确认框发送 Enter 虚拟键码
				PostMessage(0x100, 0x0D, 0, , win)
				PostMessage(0x101, 0x0D, 0, , win)
			}
			return true
		}

		; 数量不符时使用 Escape 取消继续新增
		if InStr(txt, "物资")
			&& InStr(txt, "追溯码扫码数量")
			&& InStr(txt, "是否继续新增") {

			try {
				ControlSend("{Esc}", , win)
			} catch {
				; 对目标确认框发送 Escape 虚拟键码
				PostMessage(0x100, 0x1B, 0, , win)
				PostMessage(0x101, 0x1B, 0, , win)
			}
			return true
		}
	}

	return false
}

UI_RunBurst(fn, maxMs := 250, interval := 10) {
	t0 := A_TickCount
	while (A_TickCount - t0 < maxMs) {
		if fn.Call()
			return true
		Sleep(interval)
	}
	return false
}

UI_WaitConfirm(codes, timeoutMs, opt, ipt, optVerifyGridClassNN, iptVerifyGridClassNN, iptParseGridClassNN, win := "A") {
	t0 := A_TickCount
	delay := 15

	win := Util_NormalizeWin(win)
	cls := WinGetClass(win)

	; 门诊流程通过“已扫 N 码”文本验证实际写入数量
	if (cls = opt) {
		needN := codes.Length

		colSpecs := ["追溯码"]
		intCols := []

		while (A_TickCount - t0 < timeoutMs) {
			txt := UI_TryCopyGridClassNNText(optVerifyGridClassNN, win)
			p := Parse_TargetInfo(colSpecs, ipt, intCols, txt, win, iptParseGridClassNN)

			; “追溯码”列的已扫数量达到需求量后才视为成功
			if (IsObject(p) && p.Has("ok") && p["ok"]) {
				v := p["bySpec"].Has("追溯码") ? Trim(p["bySpec"]["追溯码"]) : ""
				if (v != "") {
					n := 0
					if RegExMatch(v, "已扫\s*(\d+)\s*码", &m)
						n := Integer(m[1])
					else if RegExMatch(v, "(\d+)", &m2)
						n := Integer(m2[1])

					if (n >= needN)
						return Map("ok", true)
				}
			}

			Sleep(delay)
			if (delay < 120)
				delay += 15
		}
		return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n门诊窗口录入追溯码验证失败，未实际扫码成功")
	}

	; 住院流程要求验证区域出现全部注入码，并处理可能的失败弹窗
	if (cls = ipt) {
		ipt := Map("codes", codes, "gridN", 1)

		while (A_TickCount - t0 < timeoutMs) {
			if UI_RunBurst(UI_DetectAndHandleFailDialog)
				return Map("ok", false, "level", "Warn", "message", "[录入验证错误]`n重复的追溯码/超过对应需要追溯码条数，将自动回退库存")

			txt := UI_TryCopyGridClassNNText(iptVerifyGridClassNN, win)
			if (txt != "") {
				allOk := true
				for c in ipt["codes"] {
					if !InStr(txt, c) {
						allOk := false
						break
					}
				}
				if allOk
					return Map("ok", true)
			}

			Sleep(delay)
			if (delay < 120)
				delay += 15
		}
		return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n住院窗口录入追溯码验证失败，未实际扫码成功")
	}

	return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n未知窗口，请一直保持在相应扫码窗口")
}

UI_WaitConfirm_Warehouse(codes, timeoutMs, verifyGridClassNN, win := "A") {
	t0 := A_TickCount
	delay := 20

	if !IsObject(codes) || (codes.Length = 0)
		return Map("ok", false, "level", "Warn", "message", "[录入验证错误]`n仓库验证缺少待验证码")

	target := Trim(codes[1])
	if (target = "")
		return Map("ok", false, "level", "Warn", "message", "[录入验证错误]`n仓库验证目标码为空")

	while (A_TickCount - t0 < timeoutMs) {
		if UI_RunBurst(UI_DetectAndHandleFailDialog)
			return Map("ok", false, "level", "Warn", "message", "[录入验证错误]`n仓库窗口出现错误提示，已终止本次注入")

		txt := UI_TryCopyGridClassNNText(verifyGridClassNN, win)
		if (txt != "" && InStr(txt, target))
			return Map("ok", true)

		Sleep(delay)
		if (delay < 140)
			delay += 20
	}

	return Map("ok", false, "level", "Error", "message", "[录入验证错误]`n仓库窗口首条注入验证失败，未匹配到目标码")
}

UI_PostClick(hwndCtrl, x := 30, y := 40) {
	; 合成鼠标消息使用控件客户区坐标，不使用屏幕坐标
	static WM_LBUTTONDOWN := 0x0201
	static WM_LBUTTONUP := 0x0202
	static MK_LBUTTON := 0x0001
	lParam := (y << 16) | (x & 0xFFFF)

	; 合成点击用于获取焦点，不移动用户的实际鼠标指针
	PostMessage(WM_LBUTTONDOWN, MK_LBUTTON, lParam, , "ahk_id " hwndCtrl)
	PostMessage(WM_LBUTTONUP, 0, lParam, , "ahk_id " hwndCtrl)
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

UI_FocusGridClassNN(classNN, win := "A", control := true) {
	nn := Trim("" classNN)
	if (nn = "")
		return ""

	win := Util_NormalizeWin(win)
	base := RegExReplace(nn, "\d+$", "")
	if (base != "" && base != nn) {
		ordText := SubStr(nn, StrLen(base) + 1)
		ord := Util_ToInt(ordText, 0)
		if (ord > 0)
			return UI_FocusTarget(base, ord, win, control)
	}

	return UI_FocusClassNN(nn, win, control)
}

UI_TryCopyGridClassNNText(classNN, win := "A", control := true) {
	if (UI_FocusGridClassNN(classNN, win, control) = "")
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

UI_GetNthCtrlHwndByClass(className, n, win := "A") {
	win := Util_NormalizeWin(win)
	hs := ""
	try hs := WinGetControlsHwnd(win)
	catch
		return 0
	if !IsObject(hs)
		return 0

	found := 0
	for _, h in hs {
		if !h
			continue
		buf := Buffer(128, 0)
		DllCall("GetClassNameW", "Ptr", h, "Ptr", buf, "Int", 64)
		cls := StrGet(buf, "UTF-16")
		if (cls = className) {
			found += 1
			if (found = n)
				return h
		}
	}
	return 0
}

UI_FocusTarget(classNN, nSite := 1, win := "A", control := true) {
	win := Util_NormalizeWin(win)
	hwndWin := 0
	try hwndWin := WinGetID(win)
	catch
		return ""
	if !hwndWin
		return ""

	winId := "ahk_id " hwndWin
	hwndSite := UI_GetNthCtrlHwndByClass(classNN, nSite, winId)
	if !hwndSite
		return ""

	if !WinActive(winId) {
		try WinActivate(winId)
		catch
			return ""
		try WinWaitActive(winId, , 0.3)
		catch
			return ""
	}

	if (control) {
		try ControlFocus(hwndSite, winId)
		catch
			return ""
	} else {
		try DllCall("SetFocus", "Ptr", hwndSite)
		catch
			return ""
		try UI_PostClick(hwndSite, 30, 40)
		catch
			return ""
	}
	return hwndSite
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

UI_FindAncestorByClass(hwnd, className, maxDepth := 40) {
	h := hwnd
	Loop maxDepth {
		if !h
			return 0
		if (WinGetClass("ahk_id " h) = className)
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
	baseClass := RegExReplace(nnTarget, "\d+$", "")
	if (baseClass = "")
		baseClass := nnTarget
	if (baseClass = "")
		return Map("ok", false)

	hSite := UI_FindAncestorByClass(h0, baseClass)
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

	; 为兼容现有配置，先移除 ClassNN 序号，再向上查找对应基类父控件
	nnTarget := Trim("" targetNN)
	baseClass := RegExReplace(nnTarget, "\d+$", "")
	if (baseClass = "")
		baseClass := nnTarget
	if (baseClass = "")
		return false

	hSite := UI_FindAncestorByClass(h0, baseClass)
	if !hSite
		return false

	; 最终使用基类控件的完整 ClassNN 与配置目标比较
	nn := ""
	try nn := ControlGetClassNN(hSite)
	catch
		nn := ""

	return (nn = targetNN)
}
