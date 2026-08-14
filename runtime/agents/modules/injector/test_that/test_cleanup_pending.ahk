#Requires AutoHotkey v2.0
#SingleInstance Force
; 超时 PENDING 回补
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\log.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\pac_api.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\..\src\pg_exec.ahk"
#Include "%A_ScriptDir%\_env.ahk"

Log_Startup("InjectorTest")
Test_InitPacApi()

r := Txn_CleanupPending(10, 200)
if !(r.Has("ok") && r["ok"])
	Test_Fail("[cleanup] 失败`n" (r.Has("message") ? r["message"] : ""))

cleaned := r.Has("cleaned") ? Util_ToInt(r["cleaned"], 0) : 0
Test_Ok("[信息] txn/cleanup 通过`n`ncleaned=" cleaned "`n（未满超时的 PENDING 不会被清理）")