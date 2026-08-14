; test_that 共用：本地 PacAPI 环境只认模块目录下这一份文件
; 绝对位置：runtime/agents/modules/injector/.env.local
; （主脚本在 test_that/ 时：A_ScriptDir\..\.env.local）
Test_EnvLocalPath() {
	return A_ScriptDir "\..\.env.local"
}

Test_LoadEnvLocal() {
	path := Test_EnvLocalPath()
	if !FileExist(path)
		return Map("ok", false, "path", path, "err", "missing", "cfg", Map())
	cfg := Util_LoadDotEnv(path)
	return Map("ok", true, "path", path, "cfg", cfg)
}

; PacApi_InitFromEnv 只读进程环境，不读 dotenv Map
Test_InitPacApi() {
	loaded := Test_LoadEnvLocal()
	if !loaded["ok"] {
		MsgBox "[读取错误] 未找到 env 文件`n期望位置：`n" loaded["path"] "`n`n可复制：modules/injector/.env.example 为 .env.local"
		ExitApp 1
	}

	cfg := loaded["cfg"]
	need := ["PAC_API_BASE_URL", "PAC_API_KEY"]
	miss := []
	for _, k in need {
		if !(cfg.Has(k) && Trim("" cfg[k]) != "")
			miss.Push(k)
	}
	if (miss.Length > 0) {
		txt := ""
		for i, v in miss
			txt .= (i > 1 ? ", " : "") v
		MsgBox "[配置错误] " loaded["path"] "`n缺少键：`n" txt
		ExitApp 1
	}

	Test_ApplyEnv(cfg)

	api := PacApi_InitFromEnv()
	if !api["ok"] {
		MsgBox api.Has("message") ? api["message"] : "[PacAPI] 初始化失败"
		ExitApp 1
	}

	t := PacApi_EnsureToken()
	if !t["ok"] {
		MsgBox t.Has("message") ? t["message"] : "[PacAPI] 换票失败"
		ExitApp 1
	}

	return cfg
}

Test_ApplyEnv(cfg) {
	if !IsObject(cfg)
		return
	for k, v in cfg
		EnvSet(k, "" v)
}

Test_DrugId(cfg) {
	v := Trim(cfg.Has("TEST_DRUG_ID") ? cfg["TEST_DRUG_ID"] : "")
	return v != "" ? v : "盐酸氨基葡萄糖胶囊"
}

Test_Spec(cfg) {
	v := Trim(cfg.Has("TEST_SPEC") ? cfg["TEST_SPEC"] : "")
	return v != "" ? v : "0.75g*60粒"
}

Test_ClientId(cfg) {
	v := Trim(cfg.Has("TEST_CLIENT_ID") ? cfg["TEST_CLIENT_ID"] : "")
	if (v != "")
		return v
	r := PacApi_Get("/v1/catalog/client-ids")
	if !r["ok"] {
		MsgBox r.Has("message") ? r["message"] : "[PacAPI] 读取 client-ids 失败"
		ExitApp 1
	}
	body := r["body"]
	items := IsObject(body) && body.Has("items") && IsObject(body["items"]) ? body["items"] : []
	if (items.Length < 1) {
		MsgBox "[配置错误] 目录无 client-ids；可在 .env.local 设 TEST_CLIENT_ID"
		ExitApp 1
	}
	return Trim("" items[1])
}

Test_GetQuantity(drugId, spec) {
	path := "/v1/catalog/drugs/" PacApi_UrlEncode(drugId) "/" PacApi_UrlEncode(spec) "/quantity"
	r := PacApi_Get(path)
	if !r["ok"]
		return r
	body := r["body"]
	if !(IsObject(body) && body.Has("quantity"))
		return Map("ok", false, "level", "Error", "message", "[目录] 未返回 quantity")
	qty := body["quantity"]
	if (qty = "" || qty = "null")
		return Map("ok", false, "level", "Error", "message", "[目录] 无该药品库存数量`n" drugId "`n" spec)
	return Map("ok", true, "qty", Util_ToInt(qty, 0))
}

Test_Fail(msg) {
	MsgBox msg
	ExitApp 1
}

Test_Ok(msg) {
	MsgBox msg
	ExitApp 0
}
