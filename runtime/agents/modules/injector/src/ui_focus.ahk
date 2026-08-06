; 聚焦/命中/HWND 工具（Delphi + DevExpress）
;
;   1) 网格 FocusGrid — TcxGridSite2 → 类名+序位 HWND → ControlFocus(HWND)；缓存 winHwnd|类|序
;   2) 编辑框 FocusClassNN — TMemo 等 → 精确 ClassNN
;   3) 命中 FindAncestor/MouseOn — 父链完整 ClassNN（禁止去尾数字当序位）
global __UI_FAST_CTRL_CACHE := Map()
; 网格 site：key = hwndWin|baseClass|n，IsWindow 失败即丢弃
global __UI_GRID_SITE_CACHE := Map()

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

	hwndCtrl := UI_GetCtrlHwndByClassNN(classNN, "ahk_id " hwndWin)
	if !hwndCtrl
		return 0

	__UI_FAST_CTRL_CACHE[key] := hwndCtrl
	return hwndCtrl
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

; --- (1) 网格 FocusGrid：序 + HWND ---
; 配置 ClassNN 形如 TcxGridSite2：尾数=同 Win 类名的第 N 个；勿对输入框走此路径
UI_FocusGridClassNN(classNN, win := "A", control := true) {
	nn := Trim("" classNN)
	if (nn = "") {
		Log_Debug("ui.focus_grid.skip", "网格 ClassNN 为空")
		return ""
	}

	win := Util_NormalizeWin(win)
	base := RegExReplace(nn, "\d+$", "")
	if (base != "" && base != nn) {
		ordText := SubStr(nn, StrLen(base) + 1)
		ord := Util_ToInt(ordText, 0)
		if (ord > 0) {
			h := UI_FocusTarget(base, ord, win, control)
			if !h
				Log_Debug("ui.focus_grid.fail", "网格序位聚焦失败", Map(
					"nn", nn, "base", base, "ord", ord, "win", win
				))
			return h
		}
	}

	h := UI_FocusClassNN(nn, win, control)
	if !h
		Log_Debug("ui.focus_grid.fallback_fail", "网格无序位退回精确 ClassNN 仍失败", Map("nn", nn, "win", win))
	return h
}

UI_TryCopyGridClassNNText(classNN, win := "A", control := true) {
	win := Util_NormalizeWin(win)
	if !WinExist(win) {
		Log_Debug("ui.copy_grid.miss_win", "复制网格时目标窗不存在", Map("nn", classNN))
		return ""
	}
	if (UI_FocusGridClassNN(classNN, win, control) = "") {
		Log_Debug("ui.copy_grid.focus_fail", "复制网格前聚焦失败", Map("nn", classNN, "win", win))
		return ""
	}

	old := ClipboardAll()
	A_Clipboard := ""

	try SendInput("^c")
	catch {
		try A_Clipboard := old
		Log_Debug("ui.copy_grid.send_fail", "网格 ^c 投递失败", Map("nn", classNN))
		return ""
	}

	if !ClipWait(0.25) {
		try A_Clipboard := old
		Log_Debug("ui.copy_grid.clip_timeout", "网格复制剪贴板超时", Map("nn", classNN))
		return ""
	}
	txt := A_Clipboard
	try A_Clipboard := old

	return txt
}

; 按 Win32 类名枚举第 n 个 HWND（与配置 ClassNN 尾序对齐）
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

; 网格 site HWND 缓存：key = hwndWin|className|n；IsWindow 失败则剔缓存并重枚举
UI_GetCachedGridSiteHwnd(className, nSite, win := "A") {
	global __UI_GRID_SITE_CACHE
	win := Util_NormalizeWin(win)
	hwndWin := 0
	try hwndWin := WinGetID(win)
	catch
		return 0
	if !hwndWin
		return 0

	key := hwndWin "|" className "|" nSite
	if (__UI_GRID_SITE_CACHE.Has(key)) {
		h := __UI_GRID_SITE_CACHE[key]
		if (DllCall("IsWindow", "Ptr", h, "Int"))
			return h
		__UI_GRID_SITE_CACHE.Delete(key)
	}

	hwndSite := UI_GetNthCtrlHwndByClass(className, nSite, "ahk_id " hwndWin)
	if !hwndSite
		return 0
	__UI_GRID_SITE_CACHE[key] := hwndSite
	return hwndSite
}

UI_FocusTarget(className, nSite := 1, win := "A", control := true) {
	win := Util_NormalizeWin(win)
	hwndWin := 0
	try hwndWin := WinGetID(win)
	catch {
		Log_Debug("ui.focus_target.miss_win", "无法解析目标窗", Map("class", className, "n", nSite, "win", win))
		return ""
	}
	if !hwndWin {
		Log_Debug("ui.focus_target.miss_win", "目标窗 ID 为空", Map("class", className, "n", nSite, "win", win))
		return ""
	}

	winId := "ahk_id " hwndWin
	hwndSite := UI_GetCachedGridSiteHwnd(className, nSite, winId)
	if !hwndSite {
		Log_Debug("ui.focus_target.miss_site", "未找到第 N 个网格类控件", Map(
			"class", className, "n", nSite, "hwndWin", hwndWin
		))
		return ""
	}
	if !DllCall("IsWindow", "Ptr", hwndSite, "Int") {
		Log_Debug("ui.focus_target.dead_hwnd", "网格 site HWND 已失效", Map(
			"class", className, "n", nSite, "hwnd", hwndSite
		))
		return ""
	}

	if !WinActive(winId) {
		try WinActivate(winId)
		catch {
			Log_Debug("ui.focus_target.activate_fail", "无法激活网格窗", Map("hwndWin", hwndWin))
			return ""
		}
		try WinWaitActive(winId, , 0.3)
		catch {
			Log_Debug("ui.focus_target.wait_fail", "网格窗未就绪", Map("hwndWin", hwndWin))
			return ""
		}
	}

	if (control) {
		; ControlFocus(HWND)：cxGrid 上比 ControlFocus("TcxGridSiteN") 可靠
		try ControlFocus(hwndSite, winId)
		catch {
			Log_Debug("ui.focus_target.focus_fail", "ControlFocus(HWND) 失败", Map(
				"class", className, "n", nSite, "hwnd", hwndSite
			))
			return ""
		}
	} else {
		try DllCall("SetFocus", "Ptr", hwndSite)
		catch {
			Log_Debug("ui.focus_target.setfocus_fail", "SetFocus 失败", Map("hwnd", hwndSite))
			return ""
		}
		try UI_PostClick(hwndSite, 30, 40)
		catch {
			Log_Debug("ui.focus_target.click_fail", "合成点击失败", Map("hwnd", hwndSite))
			return ""
		}
	}
	return hwndSite
}

; (2) 编辑框等：精确 ClassNN（网格请用 UI_FocusGridClassNN）
UI_FocusClassNN(classNN, win := "A", control := true) {
	nn := Trim("" classNN)
	if (nn = "") {
		Log_Debug("ui.focus_nn.skip", "ClassNN 为空")
		return ""
	}

	win := Util_NormalizeWin(win)
	if !WinExist(win) {
		Log_Debug("ui.focus_nn.miss_win", "目标窗不存在", Map("nn", nn, "win", win))
		return ""
	}

	hwndCtrl := 0
	try hwndCtrl := ControlGetHwnd(nn, win)
	catch {
		Log_Debug("ui.focus_nn.get_hwnd_fail", "ControlGetHwnd 抛错", Map("nn", nn, "win", win))
		return ""
	}
	if !hwndCtrl {
		Log_Debug("ui.focus_nn.miss_ctrl", "精确 ClassNN 未找到控件", Map("nn", nn, "win", win))
		return ""
	}
	if !DllCall("IsWindow", "Ptr", hwndCtrl, "Int") {
		Log_Debug("ui.focus_nn.dead_hwnd", "控件 HWND 已失效", Map("nn", nn, "hwnd", hwndCtrl))
		return ""
	}

	if !WinActive(win) {
		try WinActivate(win)
		catch {
			Log_Debug("ui.focus_nn.activate_fail", "无法激活目标窗", Map("nn", nn, "win", win))
			return ""
		}
		try WinWaitActive(win, , 0.3)
		catch {
			Log_Debug("ui.focus_nn.wait_fail", "目标窗未就绪", Map("nn", nn, "win", win))
			return ""
		}
	}

	if (control) {
		try ControlFocus(nn, win)
		catch {
			Log_Debug("ui.focus_nn.focus_fail", "ControlFocus(ClassNN) 失败", Map("nn", nn, "hwnd", hwndCtrl))
			return ""
		}
	} else {
		try DllCall("SetFocus", "Ptr", hwndCtrl)
		catch {
			Log_Debug("ui.focus_nn.setfocus_fail", "SetFocus 失败", Map("nn", nn, "hwnd", hwndCtrl))
			return ""
		}
		try UI_PostClick(hwndCtrl, 30, 40)
		catch {
			Log_Debug("ui.focus_nn.click_fail", "合成点击失败", Map("nn", nn, "hwnd", hwndCtrl))
			return ""
		}
	}
	return hwndCtrl
}

UI_GetCtrlHwndByClassNN(classNN, win := "A") {
	nn := Trim("" classNN)
	if (nn = "")
		return 0

	win := Util_NormalizeWin(win)
	if !WinExist(win)
		return 0

	hwndCtrl := 0
	try hwndCtrl := ControlGetHwnd(nn, win)
	catch
		return 0
	if !hwndCtrl
		return 0
	if !DllCall("IsWindow", "Ptr", hwndCtrl, "Int")
		return 0
	return hwndCtrl
}

; --- (2) 编辑框等：精确 ClassNN（UI_FocusClassNN）---
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

; --- (3) 命中：祖先完整 ClassNN（指针下控件向上找，不剥尾数字）---
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

	; (3) 命中：优先 AHK 指针下控件 HWND，再父链比完整 ClassNN
	h0 := ctrlHwnd
	if !h0 {
		h0 := DllCall("user32\WindowFromPoint"
			, "Int64", (sy << 32) | sx, "Ptr")
	}
	if !h0
		return false

	return !!UI_FindAncestorByClassNN(h0, targetNN)
}
