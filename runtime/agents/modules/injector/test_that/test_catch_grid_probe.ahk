#Requires AutoHotkey v2.0
#SingleInstance Force
#Warn
Persistent
CoordMode "Mouse", "Screen"

; 热键
~RButton::Probe_Win32()
F2::Probe_UIA()
; 枚举窗口下所有子控件
F3::
{
    hwnd := WinGetID("A")
    list := ""
    for hChild in WinGetControlsHwnd("ahk_id " hwnd) {
        cls := WinGetClass("ahk_id " hChild)
        list .= Format("0x{:X}  {}\n", hChild, cls)
    }
    MsgBox list
}
Esc::ExitApp

; -----------------------
; Win32 级别：从坐标反查 HWND / Class / Text
; -----------------------
Probe_Win32() {
    MouseGetPos &sx, &sy, &winHwnd, &ctrlHwnd, 2

    hwndPt := HwndFromPoint(sx, sy) ; 不依赖 ctrlHwnd
    clsPt  := GetClass(hwndPt)
    txtPt  := GetWndText(hwndPt)
    pidPt  := GetPid(hwndPt)
	try { 
		clsnnPt := ControlGetClassNN(hwndPt) 
	} catch {
		clsnnPt := ""
	}

    exe   := winHwnd ? WinGetProcessName("ahk_id " winHwnd) : ""
    wcls  := winHwnd ? WinGetClass("ahk_id " winHwnd) : ""
    title := winHwnd ? WinGetTitle("ahk_id " winHwnd) : ""

    out :=
    (
        "=== 基础信息 ===`n"
        "Mouse(Screen): " sx ", " sy "`n`n"
        "AHK WinHwnd: " (winHwnd ? Format("0x{:X}", winHwnd) : "(none)") "`n"
        "AHK CtrlHwnd: " (ctrlHwnd ? Format("0x{:X}", ctrlHwnd) : "(none)") "`n`n"

        "WinTitle: " title "`n"
        "WinClass: " wcls "`n"
        "EXE: " exe "`n`n"

        "---- WindowFromPoint ----`n"
        "Hwnd@Point: " (hwndPt ? Format("0x{:X}", hwndPt) : "(none)") "`n"
        "Class@Point: " (clsPt != "" ? clsPt : "(empty)") "`n"
		"ClassNN@Point: " (clsnnPt != "" ? clsnnPt : "(empty)") "`n"
        "Text@Point: " (txtPt != "" ? txtPt : "(empty)") "`n"
        "PID@Point: " pidPt "`n"
    )
    MsgBox out
}

; -----------------------
; UIA 级别：用坐标抓 UIA 元素（ProgID失败则CLSID兜底）
; -----------------------
Probe_UIA() {
    MouseGetPos &sx, &sy
    try {
        uia := UIA_Create()

        pt := UIA_Point(sx, sy)
        el := uia.ElementFromPoint(pt)

        name  := SafeGet(() => el.CurrentName, "")
        aid   := SafeGet(() => el.CurrentAutomationId, "")
        ctype := SafeGet(() => el.CurrentControlType, 0)
        cls   := SafeGet(() => el.CurrentClassName, "")
        h     := SafeGet(() => el.CurrentNativeWindowHandle, 0)

        val := ""
        try {
            vp := el.GetCurrentPattern(10002) ; UIA_ValuePatternId
            val := SafeGet(() => vp.CurrentValue, "")
        } catch {
            val := ""
        }

        out :=
        (
            "=== F2 UIA ElementFromPoint ===`n"
            "Point(Screen): " sx ", " sy "`n`n"
            "Name: " name "`n"
            "AutomationId: " aid "`n"
            "ControlType: " ctype "`n"
            "ClassName: " cls "`n"
            "NativeHwnd: " (h ? Format("0x{:X}", h) : "(none)") "`n"
            "ValuePattern: " (val != "" ? val : "(no/empty)") "`n`n"
            "说明：`n"
            "- 如果能看到有意义的 Name/Value/ControlType，说明 UIA 可用`n"
            "- 如果这里依然失败/全空，多半是自绘控件或系统缺 UIA 组件"
        )
        MsgBox out
    } catch as e {
        MsgBox "UIA 失败：`n" e.Message "`n`n"
    }
}

; 创建 UIAutomation：ProgID 失败 -> CLSID 兜底
UIA_Create() {
    try return ComObject("UIAutomationClient.CUIAutomation")
    catch {
        ; CUIAutomation CLSID
        return ComObject("{FF48DBA4-60EF-4201-AA87-54103EEF594E}")
    }
}

; -----------------------
; Win32 helpers
; -----------------------
HwndFromPoint(x, y) {
    pt := Buffer(8, 0)
    NumPut("Int", x, pt, 0)
    NumPut("Int", y, pt, 4)
    return DllCall("user32\WindowFromPoint", "Int64", NumGet(pt, 0, "Int64"), "Ptr")
}

GetClass(hwnd) {
    if !hwnd
        return ""
    buf := Buffer(256, 0)
    DllCall("user32\GetClassNameW", "Ptr", hwnd, "Ptr", buf, "Int", 128)
    return StrGet(buf, "UTF-16")
}

GetWndText(hwnd) {
    if !hwnd
        return ""
    len := DllCall("user32\GetWindowTextLengthW", "Ptr", hwnd, "Int")
    if (len <= 0)
        return ""
    buf := Buffer((len + 1) * 2, 0)
    DllCall("user32\GetWindowTextW", "Ptr", hwnd, "Ptr", buf, "Int", len + 1)
    return StrGet(buf, "UTF-16")
}

GetPid(hwnd) {
    if !hwnd
        return 0
    pid := 0
    DllCall("user32\GetWindowThreadProcessId", "Ptr", hwnd, "UInt*", &pid)
    return pid
}

SafeGet(fn, fallback := "") {
    try { 
		return fn.Call() 
	} catch { 
		return fallback 
	}
}

UIA_Point(x, y) {
    pt := Buffer(8, 0)
    NumPut("Int", x, pt, 0)
    NumPut("Int", y, pt, 4)
    return pt
}