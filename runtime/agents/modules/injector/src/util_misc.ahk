; 杂项：ID/SQL/剪贴板/环境信息；dotenv 轻量解析（仅 test_that 读 .env.local，生产走 --config）
Util_TxnId() {
	r := Random(10000, 99999)
	return FormatTime(, "yyyyMMddHHmmss") "_" r
}

Util_EscapeSQL(s) {
	return StrReplace(s, "'", "''")
}

Util_ToInt(v, default := 0) {
	s := Trim(v)
	return RegExMatch(s, "^-?\d+$") ? (s + 0) : default
}

; 限制 SQL 文本长度，避免诊断信息遮蔽主要错误
Util_ShortSQL(sql, maxLen := 1200) {
	if (StrLen(sql) <= maxLen)
		return sql
	return SubStr(sql, 1, maxLen) "`n... (truncated, len=" StrLen(sql) ")"
}

Util_ReadUtf8(path) {
	txt := FileRead(path, "UTF-8")
	if (SubStr(txt, 1, 1) = Chr(0xFEFF))
		txt := SubStr(txt, 2)
	return txt
}

Util_InitRuntimeInfo(versionInfo := "") {
	v := (IsObject(versionInfo) && versionInfo.Has("moduleVersion")) ? versionInfo["moduleVersion"] : Module_ReadVersion()["moduleVersion"]
	ip := Util_GetPrimaryIPv4()
	osName := Util_GetOSName()

	return Map(
		"moduleVersion", v,
		"ip", ip,
		"osName", osName,
		"versionTag", "agents-" v "+ahk-" A_AhkVersion,
		"clientId", A_ComputerName "|" A_UserName "|ip=" ip "|os=" osName "|ver=" "agents-" v "+ahk-" A_AhkVersion
	)
}

Util_GetVersionTag() {
	global RuntimeInfo
	if (IsSet(RuntimeInfo) && Type(RuntimeInfo) = "Map" && RuntimeInfo.Has("versionTag"))
		return RuntimeInfo["versionTag"]
	v := Module_ReadVersion()
	return "agents-" v["moduleVersion"] "+ahk-" A_AhkVersion
}

Util_LoadDotEnv(path) {
	env := Map()

	full := Util_PathFull(path)
	if !FileExist(full)
		return env

	txt := FileRead(full, "UTF-8")

	; 移除 UTF-8 BOM，避免首个配置键解析失败
	if (SubStr(txt, 1, 1) = Chr(0xFEFF))
		txt := SubStr(txt, 2)

	dq := Chr(34)  ; "
	sq := "'"      ; '

	for _, line in StrSplit(txt, "`n") {
		line := Trim(line, "`r`t ")

		if (line = "" || SubStr(line, 1, 1) = "#")
			continue

		; 接受 shell 使用的 export KEY=VALUE 形式
		if (SubStr(line, 1, 7) = "export ")
			line := Trim(SubStr(line, 8))

		; 空值仍属于有效配置，因此仅按第一个等号分隔
		if !RegExMatch(line, "^\s*([^=]+?)\s*=\s*(.*)\s*$", &m)
			continue

		key := Trim(m[1])
		val := Trim(m[2])

		; 仅移除引号外的行尾注释，保留值内部的井号
		if (val != "") {
			inQ := ""
			out := ""
			Loop Parse val {
				ch := A_LoopField
				if (inQ = "") {
					if (ch = dq || ch = sq) {
						inQ := ch
						out .= ch
						continue
					}
					if (ch = "#")
						break
					out .= ch
				} else {
					out .= ch
					if (ch = inQ)
						inQ := ""
				}
			}
			val := Trim(out)
		}

		if ((SubStr(val, 1, 1) = dq && SubStr(val, -1) = dq)
			|| (SubStr(val, 1, 1) = sq && SubStr(val, -1) = sq)) {
			val := SubStr(val, 2, -1)
		}

		parsed := Util_TryParseJson(val)
		if IsObject(parsed)
			env[key] := parsed
		else
			env[key] := val
	}

	return env
}

; 环境变量值里的 JSON 对象/数组 → Map/Array
Util_TryParseJson(val) {
	v := Trim(val)
	if (v = "")
		return ""
	ch := SubStr(v, 1, 1)
	if (ch != "{" && ch != "[")
		return ""
	try {
		return JSON.parse(v)
	} catch {
		return ""
	}
}

Util_WithClipboard(tempText, fn) {
	; 临时使用剪贴板后必须恢复原内容，避免自动化修改用户数据
	old := ClipboardAll()
	try {
		A_Clipboard := tempText
		ClipWait(0.4)
		return fn.Call()
	} finally {
		try A_Clipboard := old
	}
}

Util_GetPrimaryIPv4() {
	; 使用首个非回环 IPv4 生成客户端标识；无法获取时保留空值
	try {
		for ip in SysGetIPAddresses() {
			ip := Trim("" ip)
			if (ip = "" || InStr(ip, "127.") = 1)
				continue
			return ip
		}
	}
	return ""
}

Util_GetOSName() {
	; 优先使用系统产品名称，读取失败时使用 AutoHotkey 提供的系统版本
	try {
		key := "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion"
		name := RegRead(key, "ProductName", "")
		name := Trim("" name)
		if (name != "")
			return name
	}
	return A_OSVersion
}
