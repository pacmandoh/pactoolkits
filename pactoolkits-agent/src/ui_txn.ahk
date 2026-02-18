; ================== UI 模块 ==================
;
; 只在指定场景才粘贴
; 规则：
; 1) 校验 ahk_exe = 互慧软件.exe
; 2) 住院：class 命中 + 标题包含“追溯码录入” -> 粘贴到 TEdit1
; 3) 门诊：class 命中 + 页面文本包含“门诊处方发药” -> 粘贴到 TMemo2
UI_Paste_ByPolicy(
	text,
	opt,
	ipt,
	win := "A"
) {
    win := Util_NormalizeWin(win)
    cls := WinGetClass(win)
    ttl := WinGetTitle(win)

    ; ===== 住院：追溯码录入 =====
    if (cls = ipt && InStr(ttl, "追溯码录入")) {
        return Ui_Paste_Impl(win, "TEdit1", text, false)
    } else if (cls = opt) {
		return Ui_Paste_Impl(win, "TMemo2", text, true)
	} else {
		return Map("ok", false, "level", "ERR", "type", "[界面错误]", "why", "请在门诊或住院录入窗口进行操作", "reason", "class mismatch")
	}
}

UI_Paste_Impl(winTitle, classNN, text, doEnter := true) {
    try hwndCtrl := ControlGetHwnd(classNN, winTitle)
    catch
        return Map(
			"ok", false, "level", "ERR", "type", "[窗口错误]", "why", "获取当前窗口 hwnd 失败", 
			"reason", "control not found", "ctrl", classNN
		)

    WinActivate(winTitle)
    WinWaitActive(winTitle, , 1)


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
			Sleep(50)
			PostMessage(0x0100, 0x0D, 0, , "ahk_id " hwndCtrl)
			PostMessage(0x0101, 0x0D, 0, , "ahk_id " hwndCtrl)
		} else {
			Sleep(50)
			PostMessage(0x0100, 0x0D, 0, , "ahk_id " hwndCtrl)
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

UI_WaitConfirm(codes, timeoutMs, opt, ipt, classNN, win := "A") {
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
			p := Parse_TargetInfo(colSpecs, ipt, intCols, "", win)

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

			txt := UI_TryCopyListText(classNN, ipt["gridN"], win)
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

UI_PostClick(hwndCtrl, x := 30, y := 40) {
    ; x,y 是控件客户区坐标（你可以调 y 来点不同的行）
    static WM_LBUTTONDOWN := 0x0201
    static WM_LBUTTONUP   := 0x0202
    static MK_LBUTTON     := 0x0001
    lParam := (y << 16) | (x & 0xFFFF)

    ; 让控件认为自己被点了（不移动鼠标）
    PostMessage(WM_LBUTTONDOWN, MK_LBUTTON, lParam, , "ahk_id " hwndCtrl)
    PostMessage(WM_LBUTTONUP, 0, lParam, , "ahk_id " hwndCtrl)
}

UI_TryCopyListText(classNN, nSite := 1, win := "A", control := true) {
    win := Util_NormalizeWin(win)

	UI_FocusTarget(classNN, nSite, win, control)
	
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

; TODO FIX: 在报错信息提示未点击时，切换过窗口会概率被 ahk 直接 throw 出错误
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
		; 无鼠标点击逻辑：
		try ControlFocus(hwndSite, winId)
		catch
			return ""
	} else {
		; 鼠标点击逻辑：
		try DllCall("SetFocus", "Ptr", hwndSite)
		catch
			return ""
		try UI_PostClick(hwndSite, 30, 40)
		catch
			return ""
	}
	return hwndSite
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

    ; 2) 向上爬到 TcxGridSite
    hSite := UI_FindAncestorByClass(h0, "TcxGridSite")
    if !hSite
        return false

    ; 3) 拿这个 Site 的 ClassNN
    nn := ""
    try { 
		nn := ControlGetClassNN(hSite) 
	} catch { 
		nn := "" 
	}

    return (nn = targetNN)
}

UI_Err(text, title := "追溯码自动化") {
	return MsgBox(text, title, 0x40000 | 0x1000)
}
