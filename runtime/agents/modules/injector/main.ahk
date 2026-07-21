#Requires AutoHotkey v2.0
#SingleInstance Force
#NoTrayIcon
;@Ahk2Exe-SetCompanyName PacDocs
;@Ahk2Exe-SetCopyright PacDocs 2026. All rights reserved.
;@Ahk2Exe-Set Author, PacmanDoh
;@Ahk2Exe-SetProductName PacToolkits Injector
;@Ahk2Exe-SetDescription PacToolkits injector automation module
;@Ahk2Exe-SetInternalName Injector
;@Ahk2Exe-SetOrigFilename Injector.exe
#Include "%A_ScriptDir%\src\parse_clipboard.ahk"
#Include "%A_ScriptDir%\src\db_txn.ahk"
#Include "%A_ScriptDir%\src\pg_exec.ahk"
#Include "%A_ScriptDir%\src\ui_txn.ahk"
#Include "%A_ScriptDir%\src\json.ahk"
#Include "%A_ScriptDir%\src\utils.ahk"
#Include "%A_ScriptDir%\src\main_semi_auto.ahk"
#Include "%A_ScriptDir%\src\msfx_task.ahk"

; Injector 追溯码录入主入口（v0.2.1beta）

; ODBC/Postgres 与打包基底要求 64 位；32 位进程无法正确连接
if (A_PtrSize = 4) {
    if (A_IsCompiled) {
        UI_Err("当前为 32 位打包程序，无法运行`n请使用 64 位 AutoHotkey 基底重新打包后再启动", "追溯码自动化 - 启动自检")
        ExitApp
    }
    Run('"C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe" "' A_ScriptFullPath '"')
    ExitApp
}

global Cfg := IsSet(Cfg) ? Cfg : Map()
Util_ClearModuleReady()
cfgPath := Util_GetConfigArg()
if (cfgPath = "") {
    UI_Err("启动参数缺失：`n请使用 --config " Chr(34) "<配置文件绝对路径>" Chr(34) " 启动", "追溯码自动化 - 启动自检")
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
global RuntimeInfo := Util_InitRuntimeInfo(VersionInfo)
UI_Tip("Injector v" VersionInfo["moduleVersion"], 1600)

; 关键配置缺失时直接报错退出，避免半残运行
_missing := []
for _, k in ["PG_HOST","PG_PORT","PG_DB","PG_USER","PG_PASS","PG_DRIVER","PG_SSL","OPT_WINDOW_CLASS","IPT_WINDOW_CLASS","OPT_PARSE_GRID_CLASSNN","OPT_VERIFY_GRID_CLASSNN","IPT_PARSE_GRID_CLASSNN","IPT_VERIFY_GRID_CLASSNN","OPT_INPUT_CLASSNN","IPT_INPUT_CLASSNN","COL_SPECS","INT_COLS","CONFIRM_TIMEOUT_MS","APP_WIN"] {
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
    UI_Err("配置缺失：`n - " join "`n`n请检查 --config 指向的统一配置文件", "追溯码自动化 - 启动自检")
    ExitApp
}

Util_MarkModuleReady()

; 启动时清理超时 PENDING，避免异常退出导致库存被扣住
global _CLEANUP_BUSY := false
try {
    rr := Txn_CleanupPending(10, 200)
    if (IsObject(rr) && rr.Has("ok") && rr["ok"] && rr.Has("cleaned") && rr["cleaned"] > 0)
        UI_Tip("已自动回滚超时预留事务：" rr["cleaned"] " 条", 1500)
}

; 低频定时扫 PENDING，降低对注入热路径的性能干扰
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

#HotIf Util_HotIf_TargetApp()
~RButton::
{
    ctx := Util_CaptureWin("A")
    parseGridClassNN := (ctx["cls"] = Cfg["IPT_WINDOW_CLASS"]) ? Cfg["IPT_PARSE_GRID_CLASSNN"] : Cfg["OPT_PARSE_GRID_CLASSNN"]
    if !UI_MouseOnClassNN(parseGridClassNN)
        return

    if (Cfg.Has("WAREHOUSE_ENABLED") && Cfg["WAREHOUSE_ENABLED"]) {
        ck := Util_WarehouseSoftCheck(ctx["win"])
        if !ck["ok"] {
            UI_Err(ck["type"] " " ck["why"])
            return
        }
        UI_Tip("[仓库模式] 列特征校验通过")
        return
    }

	p := Parse_TargetInfo(Cfg["COL_SPECS"], Cfg["IPT_WINDOW_CLASS"], Cfg["INT_COLS"], "", ctx["win"], parseGridClassNN)
    if (!p["ok"]) {
        UI_Err(p["type"] " " p["why"])
        return
    }
    UI_Tip("[解析成功]" p["why"])
}

~LButton:: {
    global _BUSY, _LAST_RUN, Cfg
    hookT0 := A_TickCount

    activeCls := ""
    try activeCls := WinGetClass("A")
    catch
        activeCls := ""
    parseGridClassNN := (activeCls = Cfg["IPT_WINDOW_CLASS"]) ? Cfg["IPT_PARSE_GRID_CLASSNN"] : Cfg["OPT_PARSE_GRID_CLASSNN"]
    if !UI_MouseOnClassNN(parseGridClassNN)
        return
    clickAnchor := ""
    warehouseMode := (Cfg.Has("WAREHOUSE_ENABLED") && Cfg["WAREHOUSE_ENABLED"])
    if warehouseMode {
        MouseGetPos &sx, &sy
        clickAnchor := Map("ok", true, "screenX", sx, "screenY", sy, "targetNN", parseGridClassNN)
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
    if !warehouseMode {
        try {
            if (ctx["cls"] = Cfg["OPT_WINDOW_CLASS"]) {
                Util_LogLine(
                    "OPT_HOOK"
                    . " | t=" (A_TickCount - hookT0) "ms"
                    . " | activeCls=" activeCls
                    . " | ctxCls=" ctx["cls"]
                    . " | ttl=" StrReplace(ctx["ttl"], "`n", " ")
                )
            }
        }
    }
		
	cls := ctx["cls"]
		
    try {
        if warehouseMode {
            UI_Tip("[仓库模式] 开始执行注入流程…", 900)
            msa := Msfx_RunWarehouseTaskFlow(
                Cfg["CONFIRM_TIMEOUT_MS"],
                Cfg["IPT_PARSE_GRID_CLASSNN"],
                Cfg["IPT_VERIFY_GRID_CLASSNN"],
                Cfg["IPT_INPUT_CLASSNN"],
                Cfg["COL_SPECS"],
                Cfg["INT_COLS"],
                Cfg["IPT_WINDOW_CLASS"],
                ctx["win"],
                clickAnchor
            )
        } else {
			msa := Semi_Auto_Fill(
				Cfg["OPT_WINDOW_CLASS"], Cfg["IPT_WINDOW_CLASS"], 
				Cfg["COL_SPECS"], Cfg["INT_COLS"],
				Cfg["CONFIRM_TIMEOUT_MS"],
                Cfg["OPT_PARSE_GRID_CLASSNN"], Cfg["OPT_VERIFY_GRID_CLASSNN"],
                Cfg["IPT_PARSE_GRID_CLASSNN"], Cfg["IPT_VERIFY_GRID_CLASSNN"],
                Cfg["OPT_INPUT_CLASSNN"], Cfg["IPT_INPUT_CLASSNN"],
                ctx["win"]
			)
        }
		
		if (msa.Has("skip") && msa["skip"]) {
			UI_Tip(msa["type"] " " msa["why"])
            if (msa.Has("focusClassNN"))
			    UI_FocusClassNN(msa["focusClassNN"], ctx["win"])
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
			
			if (cls = Cfg["IPT_WINDOW_CLASS"]) {
				UI_FocusClassNN(Cfg["IPT_INPUT_CLASSNN"], ctx["win"])
			}
			if (cls = Cfg["OPT_WINDOW_CLASS"]) {
				UI_FocusClassNN(Cfg["OPT_INPUT_CLASSNN"], ctx["win"])
			}
			return false
		}

        if (Cfg.Has("WAREHOUSE_ENABLED") && Cfg["WAREHOUSE_ENABLED"])
            UI_Tip(msa["type"] " " msa["why"], 1500)
		
		if (cls = Cfg["IPT_WINDOW_CLASS"]) {
			UI_FocusClassNN(Cfg["IPT_INPUT_CLASSNN"], ctx["win"])
		}
		if (cls = Cfg["OPT_WINDOW_CLASS"]) {
			UI_FocusClassNN(Cfg["OPT_INPUT_CLASSNN"], ctx["win"])
		}
	}
	
    finally _BUSY := false
}
#HotIf
