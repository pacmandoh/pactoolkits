#Requires AutoHotkey v2.0
#SingleInstance Force
#Include "%A_ScriptDir%\src\parse_clipboard.ahk"
#Include "%A_ScriptDir%\src\db_txn.ahk"
#Include "%A_ScriptDir%\src\pg_exec.ahk"
#Include "%A_ScriptDir%\src\ui_txn.ahk"
#Include "%A_ScriptDir%\src\json.ahk"
#Include "%A_ScriptDir%\src\utils.ahk"
#Include "%A_ScriptDir%\src\main_semi_auto.ahk"

; 录入药物追溯码程序主入口 v0.2.1beta

; ! 强制 64-bit !
if (A_PtrSize = 4) {
    if (A_IsCompiled) {
        UI_Err("当前为 32 位打包程序，无法运行。`n请使用 64 位 AutoHotkey 基底重新打包后再启动。", "追溯码自动化 - 启动自检")
        ExitApp
    }
    Run('"C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe" "' A_ScriptFullPath '"')
    ExitApp
}

global Cfg := IsSet(Cfg) ? Cfg : Map()
cfgPath := Util_GetConfigArg()
if (cfgPath = "") {
    UI_Err("启动参数缺失：`n请使用 --config " Chr(34) "<配置文件绝对路径>" Chr(34) " 启动。", "追溯码自动化 - 启动自检")
    ExitApp
}

cfgLoad := Util_LoadUnifiedConfig(cfgPath)
if !(cfgLoad.Has("ok") && cfgLoad["ok"]) {
    t := cfgLoad.Has("type") ? cfgLoad["type"] : "[配置错误]"
    w := cfgLoad.Has("why") ? cfgLoad["why"] : (cfgLoad.Has("err") ? cfgLoad["err"] : "未知错误")
    UI_Err(t " " w, "追溯码自动化 - 启动自检")
    ExitApp
}
global Cfg := cfgLoad["cfg"]
global VersionInfo := Util_ReadVersionFile()
versionTag := VersionInfo["agentVersion"] "|" VersionInfo["uiVersion"] "|" VersionInfo["dbSchemaVersion"]
if Util_ShouldShowVersionTip(versionTag)
    UI_Tip("Agent v" VersionInfo["agentVersion"] " | UI v" VersionInfo["uiVersion"] " | DB Schema v" VersionInfo["dbSchemaVersion"], 1600)

; ===== 启动自检：关键配置缺失直接报错退出 =====
_missing := []
for _, k in ["PG_HOST","PG_PORT","PG_DB","PG_USER","PG_PASS","PG_DRIVER","PG_SSL","OPT_CLS","IPT_CLS","COL_SPECS","INT_COLS","CLASSNN","CONFIRM_TIMEOUT_MS","APP_WIN"] {
    if !Cfg.Has(k) {
        _missing.Push(k)
        continue
    }
    v := Cfg[k]
    if (!IsObject(v) && Trim("" v) = "")
        _missing.Push(k)
}
if (_missing.Length > 0) {
    join := ""
    for i, kk in _missing
        join .= (i=1 ? kk : "`n - " kk)
    UI_Err("配置缺失：`n - " join "`n`n请检查 --config 指向的统一配置文件。", "追溯码自动化 - 启动自检")
    ExitApp
}

; ===== 启动自愈：清理超时 PENDING（避免异常退出导致库存被“扣住”）=====
global _CLEANUP_BUSY := false
try {
    rr := Txn_CleanupPending(10, 200)
    if (IsObject(rr) && rr.Has("ok") && rr["ok"] && rr.Has("cleaned") && rr["cleaned"] > 0)
        UI_Tip("已自动回滚超时预留事务：" rr["cleaned"] " 条", 1500)
}

; 每 5 分钟扫一次（低频、低性能占用）
Cleanup_PendingTimer(*) {
    global _CLEANUP_BUSY
    if (_CLEANUP_BUSY)
        return
    _CLEANUP_BUSY := true
    try Txn_CleanupPending(10, 200)
    catch
    _CLEANUP_BUSY := false
}
SetTimer(Cleanup_PendingTimer, 300000)

global _BUSY := false
global _LAST_RUN := 0

; 热键
#HotIf Util_HotIf_TargetApp()
~RButton::
{
    if !UI_MouseOnClassNN(Cfg["CLASSNN"] "2") {
        return
    }
	
    ctx := Util_CaptureWin("A")
	p := Parse_TargetInfo(Cfg["COL_SPECS"], Cfg["IPT_CLS"], Cfg["INT_COLS"], "", ctx["win"])
    if (!p["ok"]) {
        UI_Err(p["type"] " " p["why"])
        return
    }
    UI_Tip("[解析成功]" p["why"])
}

~LButton:: {
    global _BUSY, _LAST_RUN, Cfg

	if !UI_MouseOnClassNN(Cfg["CLASSNN"] "2") {
		return
    }
	
    Critical
    KeyWait("LButton")

    if (_BUSY)
        return UI_Tip("忙碌中…已忽略重复触发", 800)

    now := A_TickCount
    if (now - _LAST_RUN < 400)
        return UI_Tip("触发过快，已忽略", 600)

    _LAST_RUN := now

    _BUSY := true
	
    ctx := Util_CaptureWin("A")
	cls := ctx["cls"]
	
    try {	
		msa := Semi_Auto_Fill(
			Cfg["OPT_CLS"], Cfg["IPT_CLS"], 
			Cfg["COL_SPECS"], Cfg["INT_COLS"],
			Cfg["CONFIRM_TIMEOUT_MS"], Cfg["CLASSNN"], ctx["win"]
		)
		
		if (msa.Has("skip") && msa["skip"]) {
			UI_Tip(msa["type"] " " msa["why"])
			UI_FocusTarget(msa["focusNN"], msa["focusN"], ctx["win"])
			return true
		}

		if (!msa["ok"]) {
		
			if (msa["level"] = "WARN") {
				UI_Tip(msa["type"] " " msa["why"])
			}
			if (msa["level"] = "ERR") {
				Util_LogLine("ERR | " msa["type"] " | " StrReplace(msa["why"], "`n", " | ") " | cls=" cls " | ttl=" ctx["ttl"]) 
				UI_Err(msa["type"] " " msa["why"])
			}
			
			if (cls = Cfg["IPT_CLS"]) {
				UI_FocusTarget("TEdit", 1, ctx["win"])
			}
			if (cls = Cfg["OPT_CLS"]) {
				UI_FocusTarget("TMemo", 2, ctx["win"])
			}
			return false
		}
		
		if (cls = Cfg["IPT_CLS"]) {
			UI_FocusTarget("TEdit", 1, ctx["win"])
		}
		if (cls = Cfg["OPT_CLS"]) {
			UI_FocusTarget("TMemo", 2, ctx["win"])
		}
	}
	
    finally _BUSY := false
}
#HotIf
