#Requires AutoHotkey v2.0
#SingleInstance Force
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\..\src\util_scene.ahk"
#Include "%A_ScriptDir%\..\src\ui_focus.ahk"
Persistent
SetTitleMatchMode 2

#HotIf WinActive("ahk_exe 互慧软件.exe")
F1:: UI_FocusClassNN("TMemo1")
F2:: UI_FocusClassNN("TMemo2")
F4:: {
	i := UI_TryCopyClassNNText("TcxGridSite1")
	MsgBox i
}
F3::
{
	hwndWin := WinGetID("A")
	out := "Active Win: 0x" Format("{:X}", hwndWin) "`n`n"

	for h in WinGetControlsHwnd("ahk_id " hwndWin) {
		cls := WinGetClass(h)
		if (cls = "TcxGridSite") {
			classnn := ControlGetClassNN(h)
			out .= Format("hwnd=0x{:X}  ClassNN={}`n", h, classnn)
		}
	}

	MsgBox out
}
#HotIf