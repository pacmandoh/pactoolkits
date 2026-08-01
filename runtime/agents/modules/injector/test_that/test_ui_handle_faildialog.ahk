#Requires AutoHotkey v2.0
#SingleInstance Force

F8:: UI_DetectAndHandleFailDialog()

UI_DetectAndHandleFailDialog() {
	if WinExist("信息确认") {
		txt := WinGetText("信息确认")
		if InStr(txt, "对应") && InStr(txt, "不符") && InStr(txt, "是否继续") {
			WinActivate("信息确认")
			SendInput("Y")
			MsgBox("[识别成功] 已识别住院报错窗口")
			return false
		}
	}

	if WinExist("提示") {
		txt := WinGetText("提示")
		if InStr(txt, "重复的追溯码") && InStr(txt, "不能录入") {
			WinActivate("提示")
			SendInput("{Enter}")
			MsgBox("[识别成功] 已识别住院报错窗口")
			return true
		}

		if InStr(txt, "该物资") && InStr(txt, "已扫追溯码条数与数量一致") && InStr(txt, "是否继续新增") {
			WinActivate("提示")
			SendInput("{Esc}")
			MsgBox("[识别成功] 已识别住院报错窗口")
			return true
		}
	}
	MsgBox("[识别错误] 未识别住院报错窗口")
	return false
}
