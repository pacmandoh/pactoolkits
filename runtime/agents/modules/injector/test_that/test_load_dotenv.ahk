#Requires AutoHotkey v2.0
#SingleInstance Force
; 校验 modules/injector/.env.local：路径约定与 PAC_API_* 键（业务 AppWin 在 settings.json）
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\_env.ahk"

Main()
ExitApp 0

Main() {
	loaded := Test_LoadEnvLocal()
	if !loaded["ok"] {
		MsgBox "[读取错误] 未找到 env 文件`n期望位置：`n" loaded["path"] "`n`n可复制：modules/injector/.env.example 为 .env.local"
		ExitApp 1
	}

	path := loaded["path"]
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
		MsgBox "[配置错误] " path "`n缺少键：`n" txt
		ExitApp 1
	}

	MsgBox "[信息] env 路径与 PAC_API_* 键校验通过`n`n路径=`n" path "`n`n"
		. "PAC_API_BASE_URL=" cfg["PAC_API_BASE_URL"] "`n"
		. "PAC_API_KEY=" cfg["PAC_API_KEY"]
}
