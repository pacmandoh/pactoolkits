; 模块入口启动门控（显式调用，无顶层副作用）
; Ahk2Exe 工作目录是编译器目录，不能裸 #Include（会落到 A_WorkingDir）
#Include "%A_LineFile%\..\log.ahk"
#Include "%A_LineFile%\..\ui.ahk"
#Include "%A_LineFile%\..\path.ahk"
#Include "%A_LineFile%\..\args.ahk"
#Include "%A_LineFile%\..\JSON.ahk"

; 打包基底要求 64 位；失败则 ExitApp
Arch_Require64(title) {
	if (A_PtrSize != 4)
		return
	if (A_IsCompiled) {
		UI_Fail(
			"startup.arch32",
			"当前为 32 位打包程序，无法运行`n请使用 64 位 AutoHotkey 基底重新打包后再启动",
			title
		)
		ExitApp
	}
	Run('"C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe" "' A_ScriptFullPath '"')
	ExitApp
}

; 要求有效 --module-settings：应用日志门控并读出非空 JSON 文本；失败则 ExitApp
Settings_RequireJson(&path, &json, title) {
	path := Log_TryApplyModuleSettingsArg()
	if (path = "") {
		UI_Fail("startup.settings_missing", "缺少有效的 --module-settings 参数", title)
		ExitApp
	}

	try json := FileRead(path, "UTF-8")
	catch as err {
		UI_Fail("startup.settings_read_fail", "读取模块配置失败：`n" err.Message, title)
		ExitApp
	}

	if (SubStr(json, 1, 1) = Chr(0xFEFF))
		json := SubStr(json, 2)

	if (Trim(json) = "") {
		UI_Fail("startup.settings_empty", "模块配置不能为空", title)
		ExitApp
	}
}

; 要求 cfg Map 含非空标量键（对象值仅检查 Has）；失败则 ExitApp
Cfg_RequireKeys(cfg, keys, title) {
	missing := []
	for _, k in keys {
		if !cfg.Has(k) {
			missing.Push(k)
			continue
		}
		v := cfg[k]
		if (!IsObject(v) && Trim("" v) = "")
			missing.Push(k)
	}
	if (missing.Length = 0)
		return

	join := ""
	for i, kk in missing
		join .= (i = 1 ? kk : "`n - " kk)
	UI_Fail(
		"startup.config_missing",
		"配置缺失：`n - " join "`n`n请检查 --config 指向的统一配置文件",
		title,
		Map("keys", join)
	)
	ExitApp
}

; 版本/身份仅来自当前模块 module.json，避免误用 Host；损坏时为 unknown / 空
Module_ReadVersion() {
	global VersionInfo
	if (IsSet(VersionInfo) && Type(VersionInfo) = "Map"
		&& VersionInfo.Has("moduleVersion")
		&& VersionInfo.Has("id")
		&& VersionInfo.Has("displayName"))
		return VersionInfo

	info := Map("moduleVersion", "unknown", "id", "", "displayName", "")
	moduleMetaPath := Util_PathFull(A_ScriptDir "\module.json")
	if !FileExist(moduleMetaPath)
		return info

	try {
		txt := FileRead(moduleMetaPath, "UTF-8")
		if (SubStr(txt, 1, 1) = Chr(0xFEFF))
			txt := SubStr(txt, 2)
		meta := JSON.parse(txt)
		if !IsObject(meta)
			return info
		if (meta.Has("version"))
			info["moduleVersion"] := meta["version"]
		if (meta.Has("id"))
			info["id"] := "" meta["id"]
		if (meta.Has("displayName") && Trim("" meta["displayName"]) != "")
			info["displayName"] := "" meta["displayName"]
		else if (info["id"] != "")
			info["displayName"] := info["id"]
	} catch {
	}
	return info
}

; 日志 module 字段优先用 module.json id
Module_LogId() {
	info := Module_ReadVersion()
	if (info.Has("id") && Trim(info["id"]) != "")
		return info["id"]
	if (info.Has("displayName") && Trim(info["displayName"]) != "")
		return info["displayName"]
	return "Module"
}

; UI 弹窗标题：displayName（缺省回退 id）；suffix 非空则拼「名称 - suffix」
Module_UiTitle(suffix := "") {
	info := Module_ReadVersion()
	name := ""
	if (info.Has("displayName") && Trim(info["displayName"]) != "")
		name := info["displayName"]
	else if (info.Has("id") && Trim(info["id"]) != "")
		name := info["id"]
	else
		name := "Module"
	if (suffix = "")
		return name
	return name " - " suffix
}
