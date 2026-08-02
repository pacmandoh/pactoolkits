#Requires AutoHotkey v2.0
#SingleInstance Force
#NoTrayIcon
;@Ahk2Exe-SetCompanyName PacDocs
;@Ahk2Exe-SetCopyright PacDocs 2026. All rights reserved.
;@Ahk2Exe-Set Author, PacmanDoh
;@Ahk2Exe-SetProductName PacToolkits Scanner
;@Ahk2Exe-SetDescription PacToolkits scanner automation module
;@Ahk2Exe-SetInternalName Scanner
;@Ahk2Exe-SetOrigFilename Scanner.exe
;@Ahk2Exe-SetMainIcon assets\agents-scanner.ico
#Include "%A_ScriptDir%\..\..\lib\ahk\ready.ahk"
#Include "%A_ScriptDir%\..\..\lib\ahk\log.ahk"
#Include "%A_ScriptDir%\..\..\lib\ahk\ui.ahk"
#Include "%A_ScriptDir%\..\..\lib\ahk\startup.ahk"

global ModuleSettingsPath := ""
global ModuleSettingsJson := ""

global VersionInfo := Module_ReadVersion()
Log_Startup(Module_LogId(), VersionInfo["moduleVersion"])
Ready_Install()
Settings_RequireJson(&ModuleSettingsPath, &ModuleSettingsJson, Module_UiTitle("启动自检"))

Ready_Mark()
UI_Tip("AHK " Module_UiTitle() " 已启动`n按 Ctrl+Alt+F8 测试", 1800)
Log_Info("startup.ready", Module_UiTitle() " 自检通过")

^!F8:: {
	global ModuleSettingsPath, ModuleSettingsJson
	summary := SubStr(ModuleSettingsJson, 1, 500)
	MsgBox(
		Module_UiTitle() " 运行正常`n`n配置路径：`n" ModuleSettingsPath "`n`n配置摘要：`n" summary,
		Module_UiTitle() " 测试",
		"Iconi"
	)
}
