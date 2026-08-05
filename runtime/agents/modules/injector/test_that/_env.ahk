; test_that 共用：本地 PG 连接只认模块目录下这一份文件
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
