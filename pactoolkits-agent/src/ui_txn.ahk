; ================== UI 模块 ==================
;
global __UI_FAST_CTRL_CACHE := Map()
; 只在指定场景才粘贴
; 规则：
; 1) 校验 ahk_exe = 互慧软件.exe
; 2) 住院：class 命中 + 标题包含“追溯码录入” -> 粘贴到配置化住院输入控件
; 3) 门诊：class 命中 + 页面文本包含“门诊处方发药” -> 粘贴到配置化门诊输入控件
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

    ; ===== 住院：追溯码录入 =====
    if (cls = ipt && InStr(ttl, "追溯码录入")) {
        return Ui_Paste_Impl(win, iptInputClassNN, text, false)
    } else if (cls = opt) {
		return Ui_Paste_Impl(win, optInputClassNN, text, true)
	} else {
		return Map("ok", false, "level", "ERR", "type", "[界面错误]", "why", "请在门诊或住院录入窗口进行操作", "reason", "class mismatch")
	}
}

UI_Paste_Warehouse(text, inputClassNN, win := "A") {
    ; 仓库极速通道：缓存控件句柄 + 直写输入框 + Enter keydown。
    return UI_Paste_WarehouseFast(text, inputClassNN, win)
}

UI_Paste_WarehouseFast(text, inputClassNN, win := "A") {
    win := Util_NormalizeWin(win)
    if !WinExist(win)
        return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "目标窗口不存在或已关闭")

    hwndCtrl := UI_GetCachedCtrlHwnd(inputClassNN, win)
    if !hwndCtrl
        return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "获取当前窗口 hwnd 失败", "reason", "control not found", "ctrl", inputClassNN)

    ; 直写控件文本（比剪贴板粘贴更快）
    okSet := false
    try {
        ; WM_SETTEXT
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
        return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "写入输入框失败")

    ; 仓库高速通道固定节拍（硬编码）。
    keydownDelay := 8
    if (keydownDelay > 0)
        Sleep(keydownDelay)

    ; 按既有住院行为，仅发 keydown（该窗口链路实际仅 PostMessage 可稳定生效）。
    ; lParam 传 1（repeat=1），避免部分控件把 0 视为异常键消息。
    PostMessage(0x0100, 0x0D, 1, , "ahk_id " hwndCtrl)
    ; 极短提交让步：降低高吞吐下 UI 消息拥挤导致的首尾错位概率。
    Sleep(1)
    return Map("ok", true, "ctrl", inputClassNN, "enter", false)
}

UI_PrepareWarehouseFastTarget(inputClassNN, win := "A") {
    win := Util_NormalizeWin(win)
    if !WinExist(win)
        return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "目标窗口不存在或已关闭")

    if !WinActive(win) {
        try WinActivate(win)
        catch
            return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "无法激活目标窗口，可能已切换/关闭")
        try WinWaitActive(win, , 0.6)
        catch
            return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "目标窗口未就绪，无法注入")
    }

    hwndCtrl := UI_FocusClassNN(inputClassNN, win, true)
    if !hwndCtrl
        return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "获取当前窗口 hwnd 失败", "reason", "control not found", "ctrl", inputClassNN)
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
        ; IsWindow(h)
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
        return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "目标窗口不存在或已关闭")

    hwndCtrl := Util_GetCtrlHwndByClassNN(classNN, winTitle)
    if !hwndCtrl
        return Map(
			"ok", false, "level", "ERR", "type", "[窗口错误]", "why", "获取当前窗口 hwnd 失败", 
			"reason", "control not found", "ctrl", classNN
		)

    if !WinActive(winTitle) {
        try WinActivate(winTitle)
        catch
            return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "无法激活目标窗口，可能已切换/关闭")
        try WinWaitActive(winTitle, , 0.5)
        catch
            return Map("ok", false, "level", "ERR", "type", "[窗口错误]", "why", "目标窗口未就绪，无法注入")
    }


	oldClip := ClipboardAll()
	try {
		A_Clipboard := text
		if !ClipWait(0.6)
			return Map(
				"ok", false, "level", "WARN", "type", "[解析错误]", 
				"why", "等待超时：`n - 请确认是否选中列表中相应药品", 
				"reason", "ClipWait timeout"
			)

		; 1) 粘贴
		SendMessage(0x0302, 0, 0, , "ahk_id " hwndCtrl)

		if (doEnter) {
			Sleep(22)
			PostMessage(0x0100, 0x0D, 1, , "ahk_id " hwndCtrl)
			PostMessage(0x0101, 0x0D, 0xC0000001, , "ahk_id " hwndCtrl)
		} else {
			Sleep(16)
			PostMessage(0x0100, 0x0D, 1, , "ahk_id " hwndCtrl)
			; PostMessage(0x0101, 0x0D, 0, , "ahk_id " hwndCtrl)
		}
	} finally {
		try A_Clipboard := oldClip
	}

	return Map("ok", true, "ctrl", classNN, "enter", doEnter)
}

UI_DetectAndHandleFailDialog() {

    ; ========= 信息确认：按 Y =========
    if (hwnd := WinExist("信息确认")) {
        win := "ahk_id " hwnd
        txt := WinGetText(win)

        if InStr(txt, "对应")
        && InStr(txt, "不符")
        && InStr(txt, "是否继续") {

            try {
                ControlSend("y", , win)
            } catch {
                ; VK_Y = 0x59
                PostMessage(0x100, 0x59, 0, , win) ; WM_KEYDOWN
                PostMessage(0x101, 0x59, 0, , win) ; WM_KEYUP
            }
            return false
        }
    }

    ; ========= 提示：Enter / Esc =========
    if (hwnd := WinExist("提示")) {
        win := "ahk_id " hwnd
        txt := WinGetText(win)

        ; ---- 重复追溯码：Enter ----
        if InStr(txt, "重复的追溯码")
        && InStr(txt, "不能录入") {

            try {
                ControlSend("{Enter}", , win)
            } catch {
                ; VK_RETURN = 0x0D
                PostMessage(0x100, 0x0D, 0, , win)
                PostMessage(0x101, 0x0D, 0, , win)
            }
            return true
        }

        ; ---- 数量不符：Esc ----
        if InStr(txt, "物资")
        && InStr(txt, "追溯码扫码数量")
        && InStr(txt, "是否继续新增") {

            try {
                ControlSend("{Esc}", , win)
            } catch {
                ; VK_ESCAPE = 0x1B
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

	; ========= 门诊 =========
	if (cls = opt) {
		needN := codes.Length

		colSpecs := ["追溯码"]
		intCols  := []

		; memoCtl := "TMemo1"  ; 门诊软区/提示区
		; lastMemo := ""       ; 避免每次都提示相同内容

		while (A_TickCount - t0 < timeoutMs) {	
            txt := UI_TryCopyGridClassNNText(optVerifyGridClassNN, win)
			p := Parse_TargetInfo(colSpecs, ipt, intCols, txt, win, iptParseGridClassNN)

			; 0) 强验证：解析“追溯码”列，判断已扫N码
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
		return Map("ok", false, "level", "ERR", "type", "[录入验证错误]", "why", "门诊窗口录入追溯码验证失败，未实际扫码成功")
	}

    ; ========= 住院 =========
    if (cls = ipt) {	
		ipt := Map("codes", codes, "gridN", 1)

		while (A_TickCount - t0 < timeoutMs) {
			if UI_RunBurst(UI_DetectAndHandleFailDialog)
				return Map("ok", false, "level", "WARN", "type", "[录入验证错误]", "why", "重复的追溯码/超过对应需要追溯码条数，将自动回退库存")

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
		return Map("ok", false, "level", "ERR", "type", "[录入验证错误]", "why", "住院窗口录入追溯码验证失败，未实际扫码成功")
    }

    return Map("ok", false, "level", "ERR", "type", "[录入验证错误]", "why", "未知窗口，请一直保持在相应扫码窗口")
}

UI_WaitConfirm_Warehouse(codes, timeoutMs, verifyGridClassNN, win := "A") {
    t0 := A_TickCount
    delay := 20

    if !IsObject(codes) || (codes.Length = 0)
        return Map("ok", false, "level", "WARN", "type", "[录入验证错误]", "why", "仓库验证缺少待验证码")

    target := Trim(codes[1])
    if (target = "")
        return Map("ok", false, "level", "WARN", "type", "[录入验证错误]", "why", "仓库验证目标码为空")

    while (A_TickCount - t0 < timeoutMs) {
        if UI_RunBurst(UI_DetectAndHandleFailDialog)
            return Map("ok", false, "level", "WARN", "type", "[录入验证错误]", "why", "仓库窗口出现错误提示，已终止本次注入")

        txt := UI_TryCopyGridClassNNText(verifyGridClassNN, win)
        if (txt != "" && InStr(txt, target))
            return Map("ok", true)

        Sleep(delay)
        if (delay < 140)
            delay += 20
    }

    return Map("ok", false, "level", "ERR", "type", "[录入验证错误]", "why", "仓库窗口首条注入验证失败，未匹配到目标码")
}

UI_PostClick(hwndCtrl, x := 30, y := 40) {
    ; x,y 是控件客户区坐标
    static WM_LBUTTONDOWN := 0x0201
    static WM_LBUTTONUP   := 0x0202
    static MK_LBUTTON     := 0x0001
    lParam := (y << 16) | (x & 0xFFFF)

    ; 让控件认为自己被点了（不移动鼠标）
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
    parts := Util_ParseClassNN(nn)
    if (parts["ord"] > 0)
        return UI_FocusTarget(parts["base"], parts["ord"], win, control)

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

    try WinActivate(winId)
    catch
        return ""
    try WinWaitActive(winId, , 1)
    catch
        return ""

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

UI_GetOptScannedCount(verifyGridClassNN, win := "A") {
    txt := UI_TryCopyGridClassNNText(verifyGridClassNN, win)
    if (txt = "")
        return -1
    return UI_Parse_MaxScanned(txt)
}

UI_WaitOptScannedCount(targetN, verifyGridClassNN, timeoutMs := 450, win := "A") {
    t0 := A_TickCount
    delay := 10

    while (A_TickCount - t0 < timeoutMs) {
        n := UI_GetOptScannedCount(verifyGridClassNN, win)
        if (n >= targetN)
            return Map("ok", true, "count", n, "elapsed", A_TickCount - t0)

        Sleep(delay)
        if (delay < 25)
            delay += 5
    }

    return Map("ok", false, "count", UI_GetOptScannedCount(verifyGridClassNN, win), "elapsed", A_TickCount - t0)
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

UI_MouseOnClassNN(targetNN, win := "A") {
    win := Util_NormalizeWin(win)
    MouseGetPos &sx, &sy, &winHwnd, &ctrlHwnd, 2

    ; 1) 优先从 ctrlHwnd 起步（最贴近真实命中）
    h0 := ctrlHwnd
    if !h0 {
        ; 兜底：WindowFromPoint
        h0 := DllCall("user32\WindowFromPoint"
            , "Int64", (sy<<32)|sx, "Ptr")
    }
    if !h0
        return false

    ; 2) 向上爬到目标 ClassNN 对应的基类控件
    parts := Util_ParseClassNN(targetNN)
    baseClass := parts["base"]
    if (baseClass = "")
        return false

    hSite := UI_FindAncestorByClass(h0, baseClass)
    if !hSite
        return false

    ; 3) 拿这个基类控件的 ClassNN 做最终比对
    nn := ""
    try nn := ControlGetClassNN(hSite)
    catch
        nn := ""

    return (nn = targetNN)
}

UI_Err(text, title := "追溯码自动化") {
	return MsgBox(text, title, 0x40000 | 0x1000)
}
