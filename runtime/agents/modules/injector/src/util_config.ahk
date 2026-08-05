; 模块配置加载与字段校验（Desktop --config / --module-settings）
; 依赖 main 已 #Include 的 lib（args / path / startup）与 util_misc

Util_GetConfigArg() {
	path := Args_GetValue("--config")
	return path = "" ? "" : Util_PathFull(path)
}

Util_LoadUnifiedConfig(configPath) {
	path := Trim(configPath)
	if (path = "")
		return Util_CfgFail("配置路径为空", "EMPTY_PATH")
	if !FileExist(path)
		return Util_CfgFail("配置文件不存在：`n" path, "FILE_NOT_FOUND")

	try {
		root := JSON.parse(Util_ReadUtf8(path))
	} catch as e {
		return Util_CfgFail("配置 JSON 解析失败：`n" e.Message, "JSON_PARSE")
	}

	if (Type(root) != "Map")
		return Util_CfgFail("配置文件根节点必须是 JSON 对象", "ROOT_NOT_OBJECT")

	schema := Util_CfgGetInt(root, "SchemaVersion", &ok, &err)
	if !ok
		return Util_CfgFail("缺少或非法 SchemaVersion：`n" err, "INVALID_SCHEMA")
	if (schema != 2)
		return Util_CfgFail("SchemaVersion 不受支持：`n" schema "`n仅支持 SchemaVersion=2", "UNSUPPORTED_SCHEMA")

	pg := Util_CfgGetMap(root, "Postgres", &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_POSTGRES")
	; 模块配置必须由 Host 显式传入，禁止回退到安装目录默认文件而绕过用户配置
	moduleSettingsPath := Args_GetValue("--module-settings")
	if (moduleSettingsPath = "")
		return Util_CfgFail("缺少 --module-settings（模块业务配置路径）", "MISSING_MODULE_SETTINGS")
	moduleSettingsPath := Util_PathFull(moduleSettingsPath)
	agent := Util_LoadModuleSettingsMap(moduleSettingsPath, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	; 业务字段校验失败前先应用日志门控
	Log_ApplySettings(agent)

	cfg := Map()

	cfg["PG_HOST"] := Util_CfgGetString(pg, "Host", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_POSTGRES")
	cfg["PG_PORT"] := Util_CfgGetRangeInt(pg, "Port", 1, 65535, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_POSTGRES")
	cfg["PG_DB"] := Util_CfgGetString(pg, "Database", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_POSTGRES")
	cfg["PG_USER"] := Util_CfgGetString(pg, "Username", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_POSTGRES")
	cfg["PG_PASS"] := Util_CfgGetString(pg, "Password", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_POSTGRES")

	cfg["PG_DRIVER"] := Util_CfgGetString(agent, "PgDriver", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["PG_SSL"] := Util_CfgGetOneOf(agent, "PgSsl", ["disable", "allow", "prefer", "require", "verify-ca", "verify-full"], &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["OPT_WINDOW_CLASS"] := Util_CfgGetString(agent, "OptWindowClass", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["IPT_WINDOW_CLASS"] := Util_CfgGetString(agent, "IptWindowClass", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	if (cfg["OPT_WINDOW_CLASS"] = cfg["IPT_WINDOW_CLASS"])
		return Util_CfgFail("OptWindowClass 与 IptWindowClass 不能相同（会导致门诊/住院场景串线）", "INVALID_MODULE_SETTINGS")
	cfg["OPT_PARSE_GRID_CLASSNN"] := Util_CfgGetString(agent, "OptParseGridClassNN", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["IPT_PARSE_GRID_CLASSNN"] := Util_CfgGetString(agent, "IptParseGridClassNN", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["IPT_VERIFY_GRID_CLASSNN"] := Util_CfgGetString(agent, "IptVerifyGridClassNN", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["OPT_INPUT_CLASSNN"] := Util_CfgGetString(agent, "OptInputClassNN", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["IPT_INPUT_CLASSNN"] := Util_CfgGetString(agent, "IptInputClassNN", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["CONFIRM_TIMEOUT_MS"] := Util_CfgGetRangeInt(agent, "ConfirmTimeoutMs", 100, 10000, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["APP_WIN"] := Util_CfgGetAppWin(agent, "AppWin", &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["COL_SPECS"] := Util_CfgGetStringArray(agent, "ColSpecs", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["INT_COLS"] := Util_CfgGetStringArray(agent, "IntCols", false, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")

	cfg["WAREHOUSE_ENABLED"] := Util_CfgGetBool(agent, "WarehouseEnabled", &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["WAREHOUSE_ANCHORS"] := Util_CfgGetStringArray(agent, "WarehouseAnchorTexts", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["CODE_PICK_POLICY"] := Util_CfgGetOneOf(agent, "CodePickPolicy", ["max_level", "min_level"], &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
	cfg["CODE_PICK_POLICY"] := StrUpper(cfg["CODE_PICK_POLICY"])
	cfg["WAREHOUSE_TASK_IDENTIFIER"] := Util_CfgGetString(agent, "WarehouseTaskIdentifier", true, &ok, &err)
	if !ok
		return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")

	return Map("ok", true, "cfg", cfg)
}

Util_LoadModuleSettingsMap(path, &ok, &err) {
	path := Trim("" path)
	if (path = "") {
		ok := false, err := "module-settings 路径为空"
		return ""
	}
	if !FileExist(path) {
		ok := false, err := "module-settings 不存在：`n" path
		return ""
	}

	try {
		root := JSON.parse(Util_ReadUtf8(path))
	} catch as e {
		ok := false, err := "module-settings JSON 解析失败：`n" e.Message
		return ""
	}
	if (Type(root) != "Map") {
		ok := false, err := "module-settings 根节点必须是 JSON 对象"
		return ""
	}

	ok := true, err := ""
	return root
}

Util_CfgFail(why, reason := "CONFIG_INVALID") {
	msg := Trim("" why)
	return Map("ok", false, "level", "Error", "message", "[配置错误]`n" msg, "reason", reason, "err", msg)
}

Util_CfgGetMap(obj, key, &ok, &err) {
	if !obj.Has(key) {
		ok := false, err := "缺少配置项：" key
		return ""
	}
	v := obj[key]
	if (Type(v) != "Map") {
		ok := false, err := "配置项类型错误：" key "（应为对象）"
		return ""
	}
	ok := true, err := ""
	return v
}

Util_CfgGetString(obj, key, nonEmpty, &ok, &err) {
	if !obj.Has(key) {
		ok := false, err := "缺少配置项：" key
		return ""
	}
	v := obj[key]
	if (Type(v) != "String") {
		ok := false, err := "配置项类型错误：" key "（应为字符串）"
		return ""
	}
	t := Trim(v)
	if (nonEmpty && t = "") {
		ok := false, err := "配置项不能为空：" key
		return ""
	}
	ok := true, err := ""
	return t
}

Util_CfgGetInt(obj, key, &ok, &err) {
	if !obj.Has(key) {
		ok := false, err := "缺少配置项：" key
		return 0
	}
	v := obj[key]
	t := Type(v)
	if !(t = "Integer" || t = "Float" || t = "String") {
		ok := false, err := "配置项类型错误：" key "（应为数字）"
		return 0
	}
	s := Trim("" v)
	if !RegExMatch(s, "^-?\d+$") {
		ok := false, err := "配置项格式错误：" key "（应为整数）"
		return 0
	}
	ok := true, err := ""
	return s + 0
}

Util_CfgGetRangeInt(obj, key, min, max, &ok, &err) {
	n := Util_CfgGetInt(obj, key, &ok, &err)
	if !ok
		return 0
	if (n < min || n > max) {
		ok := false, err := "配置项超出范围：" key "（允许范围 " min "-" max "）"
		return 0
	}
	ok := true, err := ""
	return n
}

Util_CfgGetOneOf(obj, key, allows, &ok, &err) {
	v := StrLower(Util_CfgGetString(obj, key, true, &ok, &err))
	if !ok
		return ""
	for _, a in allows {
		if (v = a) {
			ok := true, err := ""
			return v
		}
	}
	ok := false, err := "配置项取值非法：" key "（当前值：" v "）"
	return ""
}

Util_CfgGetAppWin(obj, key, &ok, &err) {
	raw := Util_CfgGetMap(obj, key, &ok, &err)
	if !ok
		return ""
	set := Map()
	for exe, enabled in raw {
		name := Trim("" exe)
		if (name = "")
			continue
		if Util_ToBool(enabled)
			set[name] := true
	}
	if (set.Count = 0) {
		ok := false, err := "配置项不能为空：" key
		return ""
	}
	ok := true, err := ""
	return set
}

Util_CfgGetStringArray(obj, key, nonEmpty, &ok, &err) {
	if !obj.Has(key) {
		ok := false, err := "缺少配置项：" key
		return ""
	}
	raw := obj[key]
	if (Type(raw) != "Array") {
		ok := false, err := "配置项类型错误：" key "（应为字符串数组）"
		return ""
	}
	arr := []
	for _, it in raw {
		if (Type(it) != "String") {
			ok := false, err := "配置项类型错误：" key "（数组元素应为字符串）"
			return ""
		}
		t := Trim(it)
		if (t != "")
			arr.Push(t)
	}
	if (nonEmpty && arr.Length = 0) {
		ok := false, err := "配置项不能为空：" key
		return ""
	}
	ok := true, err := ""
	return arr
}

Util_CfgGetBool(obj, key, &ok, &err) {
	if !obj.Has(key) {
		ok := false, err := "缺少配置项：" key
		return false
	}
	v := obj[key]
	t := Type(v)
	if (t = "Integer" || t = "Float" || t = "String") {
		ok := true, err := ""
		return Util_ToBool(v)
	}
	ok := false, err := "配置项类型错误：" key "（应为布尔/数字/字符串）"
	return false
}

Util_ToBool(v) {
	t := Type(v)
	if (t = "Integer" || t = "Float")
		return v != 0
	if (t = "String") {
		s := StrLower(Trim(v))
		return (s = "1" || s = "true" || s = "yes" || s = "on")
	}
	return false
}
