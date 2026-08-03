; 杂项：ID/SQL/剪贴板/环境信息；dotenv 轻量解析
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

		parsedSet := Util_TryParseSet(val)
		if IsObject(parsedSet) {
			env[key] := parsedSet
		} else {
			parsedArr := Util_TryParseArray(val)
			if IsObject(parsedArr)
				env[key] := parsedArr
			else
				env[key] := val
		}
	}

	return env
}

Util_TryParseArray(val) {
	; 无法解析为数组时返回空字符串，由调用方继续尝试其他配置类型
	v := Trim(val)
	if (v = "")
		return ""

	if !RegExMatch(v, "^\[(.*)\]$", &mm)
		return ""

	inner := Trim(mm[1])
	arr := []

	if (inner = "")
		return arr

	dq := Chr(34)
	sq := "'"

	token := ""
	inQ := ""

	Loop Parse inner {
		ch := A_LoopField
		if (inQ = "") {
			if (ch = dq || ch = sq) {
				inQ := ch
				token .= ch
				continue
			}
			if (ch = ",") {
				item := Util_ArrayItemNormalize(token)
				if (item != "")
					arr.Push(item)
				token := ""
				continue
			}
			token .= ch
		} else {
			token .= ch
			if (ch = inQ)
				inQ := ""
		}
	}

	item := Util_ArrayItemNormalize(token)
	if (item != "")
		arr.Push(item)

	return arr
}

Util_ArrayItemNormalize(token) {
	item := Trim(token, "`r`t ")
	if (item = "")
		return ""

	dq := Chr(34)
	sq := "'"

	if ((SubStr(item, 1, 1) = dq && SubStr(item, -1) = dq)
		|| (SubStr(item, 1, 1) = sq && SubStr(item, -1) = sq)) {
		item := SubStr(item, 2, -1)
	}

	return item
}

; 将类 JSON 对象解析为以键表示成员的 Map，例如 APP_WIN
; 例：
;   {"互慧软件.exe":1,"ProjectMain.exe":1}
;   {'互慧软件.exe':true, 'ProjectMain.exe':true}
Util_TryParseSet(val) {
	v := Trim(val)
	if (v = "")
		return ""

	if !RegExMatch(v, "^\{(.*)\}$", &m)
		return ""

	inner := Trim(m[1])

	set := Map()
	if (inner = "")
		return set

	dq := Chr(34)  ; "
	sq := "'"      ; '

	token := ""
	inQ := ""

	; 仅按顶层逗号分隔，避免拆分引号内的内容
	Loop Parse inner {
		ch := A_LoopField

		if (inQ = "") {
			if (ch = dq || ch = sq) {
				inQ := ch
				token .= ch
				continue
			}

			if (ch = ",") {
				Util_SetConsumeToken(set, token)
				token := ""
				continue
			}

			token .= ch
		} else {
			token .= ch
			if (ch = inQ)
				inQ := ""
		}
	}

	Util_SetConsumeToken(set, token)

	return set
}

; 从形如 "key":1 或 'key':true 的成员中提取键
Util_SetConsumeToken(set, token) {
	t := Trim(token, "`r`t ")
	if (t = "")
		return

	; 仅识别顶层冒号，避免误用引号内的字符
	dq := Chr(34)
	sq := "'"

	inQ := ""
	colonPos := 0

	Loop Parse t {
		ch := A_LoopField
		pos := A_Index

		if (inQ = "") {
			if (ch = dq || ch = sq) {
				inQ := ch
				continue
			}
			if (ch = ":") {
				colonPos := pos
				break
			}
		} else {
			if (ch = inQ)
				inQ := ""
		}
	}

	if (colonPos = 0)
		return

	k := Trim(SubStr(t, 1, colonPos - 1), "`r`t ")

	; 为保持与 JSON 键规则一致，忽略未使用引号的键
	if (StrLen(k) < 2)
		return

	if ((SubStr(k, 1, 1) = dq && SubStr(k, -1) = dq)
		|| (SubStr(k, 1, 1) = sq && SubStr(k, -1) = sq)) {
		k := SubStr(k, 2, -1)
		if (k != "")
			set[k] := true
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
