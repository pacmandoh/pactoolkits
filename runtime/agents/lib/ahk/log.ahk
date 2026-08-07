; 模块 JSON Lines 日志 → {logsRoot}\agents\modules\<Id>\
; logsRoot 默认 %LocalAppData%\PacToolkits\logs，可由 Desktop Logging.LogDirectory 覆盖
; 规格对齐 packages/logger：门控、按日滚动、保留清理；字段 ts/level/module/event/message/version/context?/exception?
; Ahk2Exe 工作目录是编译器目录，不能裸 #Include（会落到 A_WorkingDir）
#Include "%A_LineFile%\..\JSON.ahk"
#Include "%A_LineFile%\..\path.ahk"
#Include "%A_LineFile%\..\args.ahk"

global Log_ModuleId := ""
global Log_Version := ""
; 日志根目录：空 = LocalAppData\PacToolkits\logs；可由 Desktop 配置 Logging.LogDirectory 覆盖
global Log_Root := ""
; 默认与 Desktop / Host Logging 一致
global Log_Enabled := true
global Log_MinLevel := "Error"
global Log_RetentionDays := 14
global Log_MaxFileSizeMb := 20
global Log_LastCleanupTick := 0
global Log_CleanupIntervalMs := 6 * 3600 * 1000
; 与 packages/logger LogFiles.MaxShardsPerDay 对齐：单日分片异常多时停止探测
global Log_MaxShardsPerDay := 100000

Log_Init(moduleId, version := "") {
	global Log_ModuleId, Log_Version
	Log_ModuleId := Trim(moduleId)
	Log_Version := Trim(version)
}

; 模块入口共用：登记模块 ID，并按 --config 对齐 Desktop 日志根
Log_Startup(moduleId, version := "") {
	Log_Init(moduleId, version)
	Log_ApplyDesktopArgs()
}

; 从启动参数读取 Desktop --config（忽略缺失/解析失败）
Log_ApplyDesktopArgs() {
	try {
		cfg := Args_GetValue("--config")
		if (cfg != "")
			Log_ApplyDesktopConfig(cfg)
	} catch {
	}
}

; 应用 --module-settings 门控；返回规范化路径，缺参或文件不存在返回 ""
Log_TryApplyModuleSettingsArg() {
	try {
		ms := Args_GetValue("--module-settings")
		if (ms = "")
			return ""
		full := Util_PathFull(ms)
		if (full = "" || !FileExist(full))
			return ""
		Log_ApplyFromPath(full)
		return full
	} catch {
		return ""
	}
}

; enabled / minimumLevel / retentionDays / maxFileSizeMb
Log_Configure(enabled, minimumLevel := "Error", retentionDays := 14, maxFileSizeMb := 20) {
	global Log_Enabled, Log_MinLevel, Log_RetentionDays, Log_MaxFileSizeMb
	Log_Enabled := !!enabled
	Log_MinLevel := Log_CanonicalLevel(minimumLevel)
	Log_RetentionDays := Log_ClampInt(retentionDays, 14, 1, 180)
	Log_MaxFileSizeMb := Log_ClampInt(maxFileSizeMb, 20, 1, 200)
}

; 从模块 settings.json 根对象应用日志控制；缺键保持默认
Log_ApplySettings(settings) {
	enabled := true
	level := "Error"
	retention := 14
	maxMb := 20
	if IsObject(settings) {
		if settings.Has("LogEnabled")
			enabled := Log_ToBool(settings["LogEnabled"])
		if settings.Has("LogMinimumLevel")
			level := settings["LogMinimumLevel"]
		if settings.Has("LogRetentionDays")
			retention := settings["LogRetentionDays"]
		if settings.Has("LogMaxFileSizeMb")
			maxMb := settings["LogMaxFileSizeMb"]
	}
	Log_Configure(enabled, level, retention, maxMb)
}

; 启动早期即可调用，使 UI_Fail 路径也遵守 LogEnabled
Log_ApplyFromPath(path) {
	path := Trim("" path)
	if (path = "" || !FileExist(path))
		return false
	try {
		txt := FileRead(path, "UTF-8")
		; 与 Util_ReadUtf8 一致：剥离 BOM，避免首键解析失败后静默退回默认门控
		if (SubStr(txt, 1, 1) = Chr(0xFEFF))
			txt := SubStr(txt, 2)
		Log_ApplySettings(JSON.parse(txt))
		return true
	} catch {
		return false
	}
}

; 从 Desktop 配置读取 Logging.LogDirectory 作为全端日志根
Log_ApplyDesktopConfig(path) {
	global Log_Root
	path := Trim("" path)
	if (path = "" || !FileExist(path))
		return false
	try {
		txt := FileRead(path, "UTF-8")
		if (SubStr(txt, 1, 1) = Chr(0xFEFF))
			txt := SubStr(txt, 2)
		root := JSON.parse(txt)
		if !(IsObject(root) && root.Has("Logging") && IsObject(root["Logging"])) {
			Log_Root := ""
			return true
		}
		logging := root["Logging"]
		dir := logging.Has("LogDirectory") ? Trim("" logging["LogDirectory"]) : ""
		Log_Root := dir
		return true
	} catch {
		return false
	}
}

Log_ResolveRoot() {
	global Log_Root
	r := Trim("" Log_Root)
	if (r = "")
		; AHK v2 无 A_LocalAppData 内置，读 LocalAppData 环境变量
		return EnvGet("LocalAppData") "\PacToolkits\logs"
	; 与 AgentsLogPaths.ResolveRoot 对齐：绝对路径 + 去掉尾部分隔符
	full := Util_PathFull(r)
	return RegExReplace(full, "[\\/]+$", "")
}

Log_Dir() {
	global Log_ModuleId
	id := Log_ModuleId != "" ? Log_ModuleId : "unknown"
	return Log_ResolveRoot() "\agents\modules\" id
}

; 与 packages/logger LogLevel.Canonical 对齐（大小写与 warning/err 别名）
Log_CanonicalLevel(level) {
	switch StrLower(Trim("" level)) {
		case "debug":
			return "Debug"
		case "info":
			return "Info"
		case "warn", "warning":
			return "Warn"
		case "error", "err":
			return "Error"
		case "fatal":
			return "Fatal"
		default:
			return "Error"
	}
}

Log_LevelRank(level) {
	switch Log_CanonicalLevel(level) {
		case "Debug":
			return 0
		case "Info":
			return 1
		case "Warn":
			return 2
		case "Error":
			return 3
		case "Fatal":
			return 4
		default:
			return 3
	}
}

Log_Write(level, event, message, context := unset, exception := unset) {
	global Log_Enabled, Log_MinLevel, Log_MaxFileSizeMb
	if !Log_Enabled
		return
	if (Log_LevelRank(level) < Log_LevelRank(Log_MinLevel))
		return

	dir := Log_Dir()
	try DirCreate(dir)

	; AHK Map 枚举无稳定插入序；手写键序对齐 C# JsonLogRecord
	line := Log_BuildLine(level, event, message, context?, exception?)
	; 目录已按模块分开，文件名仅用日期
	file := Log_ResolvePath(dir, Log_MaxFileSizeMb)
	try FileAppend(line "`n", file, "UTF-8")
	Log_MaybeCleanup(dir)
}

; 固定键序：ts → level → module → event → message → version → context? → exception?
; 标量勿调 JSON.stringify（顶层只接受 Array/Map/Object，String 会 OwnProps 报错）
Log_BuildLine(level, event, message, context := unset, exception := unset) {
	global Log_ModuleId, Log_Version
	s := "{"
		. '"ts":' Log_JsonString(Log_Ts())
		. ',"level":' Log_JsonString(Log_CanonicalLevel(level))
		. ',"module":' Log_JsonString(Log_ModuleId != "" ? Log_ModuleId : "unknown")
		. ',"event":' Log_JsonString(Trim(event))
		. ',"message":' Log_JsonString("" message)
		. ',"version":' Log_JsonString("" Log_Version)
	if IsSet(context) && IsObject(context)
		s .= ',"context":' JSON.stringify(context, 0)
	if IsSet(exception) && IsObject(exception)
		s .= ',"exception":' JSON.stringify(exception, 0)
	return s "}"
}

; 与 thqby JSON.ES String 分支同转义，供 Log_BuildLine 标量字段使用
Log_JsonString(value) {
	s := "" value
	s := StrReplace(s, "\", "\\")
	s := StrReplace(s, "`t", "\t")
	s := StrReplace(s, "`r", "\r")
	s := StrReplace(s, "`n", "\n")
	s := StrReplace(s, "`b", "\b")
	s := StrReplace(s, "`f", "\f")
	s := StrReplace(s, "`v", "\v")
	s := StrReplace(s, '"', '\"')
	return '"' s '"'
}

Log_ResolvePath(dir, maxFileSizeMb) {
	; 达 MaxFileSizeMb 后递增 N（无固定 9 封顶），避免末片无限膨胀
	global Log_MaxShardsPerDay
	date := FormatTime(, "yyyy-MM-dd")
	maxBytes := Max(1, Integer(maxFileSizeMb)) * 1024 * 1024
	i := 0
	while (i < Log_MaxShardsPerDay) {
		name := i = 0 ? date ".log" : date "." i ".log"
		path := dir "\" name
		if !FileExist(path)
			return path
		try size := FileGetSize(path)
		catch
			return path
		if (size < maxBytes)
			return path
		i += 1
	}
	return dir "\" date "." Log_MaxShardsPerDay ".log"
}

Log_MaybeCleanup(dir) {
	global Log_LastCleanupTick, Log_CleanupIntervalMs, Log_RetentionDays
	now := A_TickCount
	elapsed := now - Log_LastCleanupTick
	; TickCount 回绕时 elapsed 为负，也触发一次清理
	if (Log_LastCleanupTick != 0 && elapsed >= 0 && elapsed < Log_CleanupIntervalMs)
		return
	Log_LastCleanupTick := now
	Log_CleanupExpired(dir, Log_RetentionDays)
}

Log_CleanupExpired(dir, retentionDays) {
	if (dir = "" || !DirExist(dir))
		return
	days := Log_ClampInt(retentionDays, 14, 1, 180)
	; 截到本地日 00:00，避免同一天内因时分秒边界误删/误留
	threshold := SubStr(DateAdd(A_Now, -days, "Days"), 1, 8) "000000"
	Loop Files, dir "\*.log", "F" {
		try {
			mtime := FileGetTime(A_LoopFileFullPath, "M")
			if (mtime != "" && mtime < threshold)
				FileDelete(A_LoopFileFullPath)
		} catch {
			; 清理失败不得影响写日志主路径
		}
	}
}

Log_Debug(event, message, context := unset) {
	if IsSet(context)
		Log_Write("Debug", event, message, context)
	else
		Log_Write("Debug", event, message)
}

Log_Info(event, message, context := unset) {
	if IsSet(context)
		Log_Write("Info", event, message, context)
	else
		Log_Write("Info", event, message)
}

Log_Warn(event, message, context := unset) {
	if IsSet(context)
		Log_Write("Warn", event, message, context)
	else
		Log_Write("Warn", event, message)
}

Log_Error(event, message, context := unset) {
	if IsSet(context)
		Log_Write("Error", event, message, context)
	else
		Log_Write("Error", event, message)
}

Log_Fatal(event, message, context := unset) {
	if IsSet(context)
		Log_Write("Fatal", event, message, context)
	else
		Log_Write("Fatal", event, message)
}

Log_Ts() {
	; 本地时间 ISO 风格；与 Desktop DateTimeOffset 字符串可人工对齐
	return FormatTime(, "yyyy-MM-ddTHH:mm:ss")
}

Log_ToBool(v) {
	if (v = true || v = 1)
		return true
	if (v = false || v = 0)
		return false
	s := StrLower(Trim("" v))
	if (s = "1" || s = "true" || s = "yes" || s = "on")
		return true
	if (s = "0" || s = "false" || s = "no" || s = "off" || s = "")
		return false
	return !!v
}

Log_ClampInt(value, default, min, max) {
	try n := Integer(value)
	catch
		return default
	if (n <= 0)
		return default
	if (n < min)
		return min
	if (n > max)
		return max
	return n
}
