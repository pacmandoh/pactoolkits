#Requires AutoHotkey v2.0
#SingleInstance Force
; 换票与目录 quantity
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\log.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\pac_api.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\_env.ahk"

Log_Startup("InjectorTest")
cfg := Test_InitPacApi()
drugId := Test_DrugId(cfg)
spec := Test_Spec(cfg)
qty := Test_GetQuantity(drugId, spec)
if !qty["ok"]
	Test_Fail(qty.Has("message") ? qty["message"] : "[目录] quantity 失败")

Test_Ok("[信息] PacAPI 换票与目录 quantity 通过`n`n"
	. "PAC_API_BASE_URL=" EnvGet("PAC_API_BASE_URL") "`n"
	. "药品=" drugId "`n规格=" spec "`nquantity=" qty["qty"])