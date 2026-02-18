#Requires AutoHotkey v2.0
#SingleInstance Force
#Include "%A_ScriptDir%\..\src\ui_txn.ahk"
#Include "%A_ScriptDir%\..\src\utils.ahk"
Persistent
SetTitleMatchMode 2

#HotIf WinActive("ahk_exe 互慧软件.exe")
F1::UI_FocusTarget("TMemo", 1)
F2::UI_FocusTarget("TMemo", 2)
F4::{ 
	i := UI_TryCopyListText("TcxGridSite", 1)
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