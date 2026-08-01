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

global ModuleSettingsPath := GetArgValue("--module-settings")
global ModuleSettingsJson := ""

ClearReady()
OnExit(ClearReady)

if (ModuleSettingsPath = "" || !FileExist(ModuleSettingsPath)) {
	MsgBox("缺少有效的 --module-settings 参数", "Scanner - 启动自检", "Iconx")
	ExitApp
}

try ModuleSettingsJson := FileRead(ModuleSettingsPath, "UTF-8")
catch as err {
	MsgBox("读取 Scanner 配置失败：`n" err.Message, "Scanner - 启动自检", "Iconx")
	ExitApp
}

if (Trim(ModuleSettingsJson) = "") {
	MsgBox("Scanner 配置不能为空", "Scanner - 启动自检", "Iconx")
	ExitApp
}

MarkReady()
ToolTip("AHK Scanner 已启动`n按 Ctrl+Alt+F8 测试")
SetTimer(() => ToolTip(), -1800)

^!F8:: {
	global ModuleSettingsPath, ModuleSettingsJson
	summary := SubStr(ModuleSettingsJson, 1, 500)
	MsgBox(
		"Scanner 运行正常`n`n配置路径：`n" ModuleSettingsPath "`n`n配置摘要：`n" summary,
		"Scanner 测试",
		"Iconi"
	)
}

GetArgValue(name) {
	for index, arg in A_Args {
		if (arg = name && index < A_Args.Length)
			return A_Args[index + 1]

		prefix := name "="
		if (InStr(arg, prefix) = 1)
			return SubStr(arg, StrLen(prefix) + 1)
	}

	return ""
}

MarkReady() {
	path := A_ScriptDir "\module.ready"
	try FileDelete(path)
	FileAppend("ok`n", path, "UTF-8")
}

ClearReady(*) {
	try FileDelete(A_ScriptDir "\module.ready")
}
