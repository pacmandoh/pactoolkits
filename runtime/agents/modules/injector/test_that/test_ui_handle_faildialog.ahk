#Requires AutoHotkey v2.0
#SingleInstance Force

; 手工探针：F8 处置当前相关住院弹窗（语义对齐 ui_confirm.ahk，独立可跑）
F8:: {
	r := UI_DetectAndHandleFailDialog()
	MsgBox("结果=" (r = "" ? "(无)" : r))
}

UI_DetectAndHandleFailDialog() {
	if (hwnd := WinExist("信息确认")) {
		win := "ahk_id " hwnd
		txt := WinGetText(win)
		if UI_IsIptInfoMismatchConfirm(txt) {
			if UI_SendDialogKey(win, "Y", 0x59) || !WinExist("信息确认")
				return "force"
		}
	}

	if (hwnd := WinExist("提示")) {
		win := "ahk_id " hwnd
		txt := WinGetText(win)
		if InStr(txt, "重复的追溯码") && InStr(txt, "不能录入") {
			UI_SendDialogKey(win, "{Enter}", 0x0D)
			return "abort"
		}
		if InStr(txt, "物资") && InStr(txt, "追溯码扫码数量") && InStr(txt, "是否继续新增") {
			UI_SendDialogKey(win, "{Esc}", 0x1B)
			return "abort"
		}
	}
	return ""
}

UI_IsIptInfoMismatchConfirm(txt) {
	t := Trim("" txt)
	if (t = "")
		return false
	if !(InStr(t, "是否继续") || InStr(t, "是否仍继续"))
		return false
	return InStr(t, "信息不匹配") || InStr(t, "不匹配") || InStr(t, "不符")
}

UI_SendDialogKey(win, sendKey, vk) {
	try WinActivate(win)
	catch
		return !WinExist(win)
	if !WinWaitActive(win, , 0.5)
		return !WinExist(win)
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
