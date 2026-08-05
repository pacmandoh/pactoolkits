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
;@Ahk2Exe-SetMainIcon assets\agents-injector.ico
#Include "%A_ScriptDir%\..\..\lib\ahk\args.ahk"
#Include "%A_ScriptDir%\..\..\lib\ahk\ready.ahk"
#Include "%A_ScriptDir%\..\..\lib\ahk\log.ahk"
#Include "%A_ScriptDir%\..\..\lib\ahk\ui.ahk"
#Include "%A_ScriptDir%\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\..\lib\ahk\startup.ahk"
#Include "%A_ScriptDir%\src\util_misc.ahk"
#Include "%A_ScriptDir%\src\util_config.ahk"
#Include "%A_ScriptDir%\src\util_scene.ahk"
#Include "%A_ScriptDir%\src\ui_focus.ahk"
#Include "%A_ScriptDir%\src\ui_paste.ahk"
#Include "%A_ScriptDir%\src\ui_confirm.ahk"
#Include "%A_ScriptDir%\src\parse_clipboard.ahk"
#Include "%A_ScriptDir%\src\db_txn.ahk"
#Include "%A_ScriptDir%\src\pg_exec.ahk"
#Include "%A_ScriptDir%\src\main_semi_auto.ahk"
#Include "%A_ScriptDir%\src\msfx_code.ahk"
#Include "%A_ScriptDir%\src\msfx_sql.ahk"
#Include "%A_ScriptDir%\src\msfx_task.ahk"

; Injector 追溯码录入模块入口

global Cfg := IsSet(Cfg) ? Cfg : Map()
global VersionInfo := Module_ReadVersion()
; 尽早对齐日志根、版本与模块门控，使后续 UI_Fail / Log_Error 也带 version
Log_Startup(Module_LogId(), VersionInfo["moduleVersion"])
Log_TryApplyModuleSettingsArg()
Arch_Require64(Module_UiTitle("启动自检"))

Ready_Install()
cfgPath := Util_GetConfigArg()
if (cfgPath = "") {
	UI_Fail(
		"startup.config_arg_missing",
		"启动参数缺失：`n请使用 --config " Chr(34) "<配置文件绝对路径>" Chr(34) " 启动",
		Module_UiTitle("启动自检")
	)
	ExitApp
}

cfgLoad := Util_LoadUnifiedConfig(cfgPath)
if !(cfgLoad.Has("ok") && cfgLoad["ok"]) {
	msg := cfgLoad.Has("message") ? cfgLoad["message"] : (cfgLoad.Has("err") ? cfgLoad["err"] : "未知错误")
	UI_Fail("startup.config_load_fail", msg, Module_UiTitle("启动自检"))
	ExitApp
}
global Cfg := cfgLoad["cfg"]
global RuntimeInfo := Util_InitRuntimeInfo(VersionInfo)
UI_Tip(Module_UiTitle() " v" VersionInfo["moduleVersion"], 1600)

Cfg_RequireKeys(Cfg, [
	"PG_HOST", "PG_PORT", "PG_DB", "PG_USER", "PG_PASS", "PG_DRIVER", "PG_SSL",
	"OPT_WINDOW_CLASS", "IPT_WINDOW_CLASS", "OPT_PARSE_GRID_CLASSNN",
	"IPT_PARSE_GRID_CLASSNN", "IPT_VERIFY_GRID_CLASSNN", "OPT_INPUT_CLASSNN", "IPT_INPUT_CLASSNN",
	"COL_SPECS", "INT_COLS", "CONFIRM_TIMEOUT_MS", "APP_WIN"
], Module_UiTitle("启动自检"))

Ready_Mark()
Log_Info("startup.ready", Module_UiTitle() " 自检通过")
apps := []
for exe, _ in Cfg["APP_WIN"]
	apps.Push(exe)
Log_Debug("startup.cfg", "目标门控配置", Map(
	"appWin", apps,
	"optClass", Cfg["OPT_WINDOW_CLASS"],
	"iptClass", Cfg["IPT_WINDOW_CLASS"],
	"optParseNn", Cfg["OPT_PARSE_GRID_CLASSNN"],
	"optInputNn", Cfg["OPT_INPUT_CLASSNN"],
	"iptParseNn", Cfg["IPT_PARSE_GRID_CLASSNN"],
	"iptVerifyNn", Cfg["IPT_VERIFY_GRID_CLASSNN"],
	"iptInputNn", Cfg["IPT_INPUT_CLASSNN"],
	"warehouse", !!Cfg["WAREHOUSE_ENABLED"],
	"confirmMs", Cfg["CONFIRM_TIMEOUT_MS"],
	"codePick", Cfg.Has("CODE_PICK_POLICY") ? Cfg["CODE_PICK_POLICY"] : "",
	"logLevel", Cfg.Has("LogMinimumLevel") ? Cfg["LogMinimumLevel"] : "",
	"logOn", Cfg.Has("LogEnabled") ? !!Cfg["LogEnabled"] : true
))

; 启动时恢复超时的 PENDING 事务，避免异常退出后库存长期占用
global _CLEANUP_BUSY := false
try {
	rr := Txn_CleanupPending(10, 200)
	if (IsObject(rr) && rr.Has("ok") && rr["ok"] && rr.Has("cleaned") && rr["cleaned"] > 0) {
		Log_Info("startup.cleanup_pending", "已回滚超时 PENDING", Map("cleaned", rr["cleaned"]))
		UI_Tip("已自动回滚超时预留事务：" rr["cleaned"] " 条", 1500)
	}
}

; 以低频周期检查 PENDING 事务，降低恢复任务对注入主流程的性能影响
Cleanup_PendingTimer(*) {
	global _CLEANUP_BUSY
	if (_CLEANUP_BUSY)
		return
	_CLEANUP_BUSY := true
	try Txn_CleanupPending(10, 200)
	finally _CLEANUP_BUSY := false
}
SetTimer(Cleanup_PendingTimer, 300000)

global _BUSY := false
global _LAST_RUN := 0

#HotIf Util_HotIf_TargetApp()
~RButton::
{
	t0 := A_TickCount
	ctx := Util_CaptureWin("A")
	MouseGetPos(, , , &ctrlHwnd, 2)
	ptrNn := ""
	try ptrNn := ctrlHwnd ? ControlGetClassNN(ctrlHwnd) : ""
	parseGridClassNN := (ctx["cls"] = Cfg["IPT_WINDOW_CLASS"]) ? Cfg["IPT_PARSE_GRID_CLASSNN"] : Cfg["OPT_PARSE_GRID_CLASSNN"]
	Log_Debug("hot.rbutton", "右键入口", Map(
		"cls", ctx["cls"], "ttl", ctx["ttl"],
		"needNn", parseGridClassNN, "ptrNn", ptrNn
	))
	if !UI_MouseOnClassNN(parseGridClassNN) {
		Log_Debug("hot.rbutton.miss_grid", "右键未落在解析网格", Map(
			"needNn", parseGridClassNN, "ptrNn", ptrNn, "elapsedMs", A_TickCount - t0
		))
		return
	}

	if (Cfg.Has("WAREHOUSE_ENABLED") && Cfg["WAREHOUSE_ENABLED"]) {
		ck := Util_WarehouseSoftCheck(ctx["win"])
		Log_Debug("hot.rbutton.warehouse_check", ck["ok"] ? "仓库特征通过" : "仓库特征失败", Map(
			"ok", ck["ok"], "message", ck.Has("message") ? ck["message"] : ""
		))
		if !ck["ok"] {
			UI_Fail("warehouse.check_fail", ck["message"], Module_UiTitle())
			return
		}
		UI_Tip("[仓库模式] 列特征校验通过")
		return
	}

	p := Parse_TargetInfo(Cfg["COL_SPECS"], Cfg["IPT_WINDOW_CLASS"], Cfg["INT_COLS"], "", ctx["win"], parseGridClassNN)
	Log_Debug("hot.rbutton.parse", p["ok"] ? "解析成功" : "解析失败", Map(
		"ok", p["ok"],
		"level", p.Has("level") ? p["level"] : "",
		"reason", p.Has("reason") ? p["reason"] : "",
		"elapsedMs", A_TickCount - t0,
		"rawLen", p.Has("raw") ? StrLen(p["raw"]) : 0
	))
	if (!p["ok"]) {
		UI_Fail("parse.fail", p["message"], Module_UiTitle())
		return
	}
	UI_Tip(p["message"])
}

~LButton:: {
	global _BUSY, _LAST_RUN, Cfg
	hookT0 := A_TickCount

	activeCls := ""
	try activeCls := WinGetClass("A")
	catch
		activeCls := ""
	parseGridClassNN := (activeCls = Cfg["IPT_WINDOW_CLASS"]) ? Cfg["IPT_PARSE_GRID_CLASSNN"] : Cfg["OPT_PARSE_GRID_CLASSNN"]
	MouseGetPos(, , , &ctrlHwnd, 2)
	ptrNn := ""
	try ptrNn := ctrlHwnd ? ControlGetClassNN(ctrlHwnd) : ""

	Log_Debug("hot.lbutton", "左键入口", Map(
		"activeCls", activeCls, "needNn", parseGridClassNN, "ptrNn", ptrNn
	))
	if !UI_MouseOnClassNN(parseGridClassNN) {
		Log_Debug("hot.lbutton.miss_grid", "左键未落在解析网格", Map(
			"needNn", parseGridClassNN, "ptrNn", ptrNn, "elapsedMs", A_TickCount - hookT0
		))
		return
	}

	clickAnchor := ""
	warehouseMode := (Cfg.Has("WAREHOUSE_ENABLED") && Cfg["WAREHOUSE_ENABLED"])
	; 仅仓库防重 / 门诊点回需要锚点；住院半自动不采集
	needAnchor := warehouseMode || (activeCls = Cfg["OPT_WINDOW_CLASS"])
	if needAnchor {
		clickAnchor := UI_CaptureGridClickAnchor(parseGridClassNN, ctrlHwnd)
		Log_Debug("hot.lbutton.anchor", "点击锚点已采集", Map(
			"ok", IsObject(clickAnchor) && clickAnchor.Has("ok") && clickAnchor["ok"],
			"restoreOk", IsObject(clickAnchor) && clickAnchor.Has("restoreOk") && clickAnchor["restoreOk"],
			"cx", IsObject(clickAnchor) && clickAnchor.Has("clientX") ? clickAnchor["clientX"] : "",
			"cy", IsObject(clickAnchor) && clickAnchor.Has("clientY") ? clickAnchor["clientY"] : "",
			"sx", IsObject(clickAnchor) && clickAnchor.Has("screenX") ? clickAnchor["screenX"] : "",
			"sy", IsObject(clickAnchor) && clickAnchor.Has("screenY") ? clickAnchor["screenY"] : "",
			"rowSlot", IsObject(clickAnchor) && clickAnchor.Has("rowSlot") ? clickAnchor["rowSlot"] : "",
			"warehouse", warehouseMode
		))
	}

	Critical
	KeyWait("LButton")

	if (_BUSY) {
		Log_Debug("hot.lbutton.busy", "忙碌中忽略", Map("elapsedMs", A_TickCount - hookT0))
		return UI_Tip("忙碌中…已忽略重复触发", 800)
	}

	now := A_TickCount
	if (now - _LAST_RUN < 400) {
		Log_Debug("hot.lbutton.throttle", "触发过快忽略", Map("deltaMs", now - _LAST_RUN))
		return UI_Tip("触发过快，已忽略", 600)
	}

	_LAST_RUN := now

	_BUSY := true

	ctx := Util_CaptureWin("A")
	Log_Debug("hot.lbutton.run", "开始半自动/仓库流程", Map(
		"warehouse", warehouseMode,
		"cls", ctx["cls"], "ttl", ctx["ttl"],
		"elapsedMs", A_TickCount - hookT0
	))
	if !warehouseMode {
		try {
			if (ctx["cls"] = Cfg["OPT_WINDOW_CLASS"]) {
				Log_Info("opt_hook", "门诊热键触发", Map(
					"elapsedMs", A_TickCount - hookT0,
					"activeCls", activeCls,
					"cls", ctx["cls"],
					"ttl", ctx["ttl"]
				))
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
				Cfg["OPT_PARSE_GRID_CLASSNN"],
				Cfg["IPT_PARSE_GRID_CLASSNN"], Cfg["IPT_VERIFY_GRID_CLASSNN"],
				Cfg["OPT_INPUT_CLASSNN"], Cfg["IPT_INPUT_CLASSNN"],
				ctx["win"],
				clickAnchor
			)
		}

		Log_Debug("hot.lbutton.result", msa.Has("ok") && msa["ok"] ? "流程结束-成功" : "流程结束-失败/跳过", Map(
			"ok", msa.Has("ok") ? msa["ok"] : false,
			"skip", msa.Has("skip") ? msa["skip"] : false,
			"level", msa.Has("level") ? msa["level"] : "",
			"message", msa.Has("message") ? msa["message"] : "",
			"elapsedMs", A_TickCount - hookT0
		))

		if (msa.Has("skip") && msa["skip"]) {
			UI_Tip(msa["message"])
			if (msa.Has("focusClassNN"))
				UI_FocusClassNN(msa["focusClassNN"], ctx["win"])
			return true
		}

		if (!msa["ok"]) {

			if (msa["level"] = "Warn") {
				Log_Warn("semi_auto.warn", msa["message"], Map("cls", cls))
				UI_Tip(msa["message"])
			}
			if (msa["level"] = "Error") {
				UI_Fail(
					"semi_auto.fail",
					msa["message"],
					Module_UiTitle(),
					Map("cls", cls, "ttl", ctx["ttl"])
				)
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
			UI_Tip(msa["message"], 1500)

		; 成功：优先用返回的 focusClassNN，否则按场景回输入框
		if (msa.Has("focusClassNN") && Trim(msa["focusClassNN"]) != "") {
			UI_FocusClassNN(msa["focusClassNN"], ctx["win"])
		} else if (cls = Cfg["IPT_WINDOW_CLASS"]) {
			UI_FocusClassNN(Cfg["IPT_INPUT_CLASSNN"], ctx["win"])
		} else if (cls = Cfg["OPT_WINDOW_CLASS"]) {
			UI_FocusClassNN(Cfg["OPT_INPUT_CLASSNN"], ctx["win"])
		}
	} finally _BUSY := false
}
#HotIf