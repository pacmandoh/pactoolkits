#Requires AutoHotkey v2.0
#SingleInstance Force
#NoTrayIcon
;@Ahk2Exe-SetCompanyName PacDocs
;@Ahk2Exe-SetCopyright PacDocs 2026. All rights reserved.
;@Ahk2Exe-Set Author, PacmanDoh
;@Ahk2Exe-SetProductName PacToolkits Module Template
;@Ahk2Exe-SetDescription PacToolkits AHK module template
;@Ahk2Exe-SetInternalName ModuleTemplate
;@Ahk2Exe-SetOrigFilename ModuleTemplate.exe
;@Ahk2Exe-SetMainIcon assets\module.ico

global ModuleSettingsPath := GetArgValue("--module-settings")
global ModuleSettingsJson := ""

ClearReady()
OnExit(ClearReady)

if (ModuleSettingsPath = "" || !FileExist(ModuleSettingsPath)) {
    MsgBox("缺少有效的 --module-settings 参数", "模块模板 - 启动自检", "Iconx")
    ExitApp
}

try ModuleSettingsJson := FileRead(ModuleSettingsPath, "UTF-8")
catch as err {
    MsgBox("读取模块配置失败：`n" err.Message, "模块模板 - 启动自检", "Iconx")
    ExitApp
}

if (Trim(ModuleSettingsJson) = "") {
    MsgBox("模块配置不能为空", "模块模板 - 启动自检", "Iconx")
    ExitApp
}

MarkReady()
ToolTip("AHK 模块模板已启动`n按 Ctrl+Alt+F8 测试")
SetTimer(() => ToolTip(), -1800)

^!F8:: {
    global ModuleSettingsPath, ModuleSettingsJson
    summary := SubStr(ModuleSettingsJson, 1, 500)
    MsgBox(
        "模块运行正常`n`n配置路径：`n" ModuleSettingsPath "`n`n配置摘要：`n" summary,
        "模块模板测试",
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
