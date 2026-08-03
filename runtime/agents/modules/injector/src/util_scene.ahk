; 窗口场景：HotIf、场景识别、仓库软校验、表头抓取
Util_NormalizeWin(win := "A") {
	; 立即捕获活动窗口句柄，避免弹窗或窗口切换改变后续操作目标
	if (win = "A") {
		try hwnd := WinGetID("A")
		catch
			return "A"
		return "ahk_id " hwnd
	}
	return win
}

Util_CaptureWin(win := "A") {
	win := Util_NormalizeWin(win)
	hwnd := 0
	try hwnd := WinGetID(win)
	catch
		hwnd := 0

	if (!hwnd)
		return Map("hwnd", 0, "win", win, "cls", "", "ttl", "")

	winId := "ahk_id " hwnd
	cls := ""
	ttl := ""
	try cls := WinGetClass(winId)
	catch
		cls := ""
	try ttl := WinGetTitle(winId)
	catch
		ttl := ""

	return Map("hwnd", hwnd, "win", winId, "cls", cls, "ttl", ttl)
}

Util_HotIf_TargetApp() {
	global Cfg

	; 配置完成加载前禁止响应自动化热键
	if !IsSet(Cfg) || (Type(Cfg) != "Map")
		return false
	if !Cfg.Has("OPT_WINDOW_CLASS") || !Cfg.Has("IPT_WINDOW_CLASS") || !Cfg.Has("APP_WIN")
		return false

	; 使用固定窗口句柄完成整次判断，避免中途切换活动窗口
	ctx := Util_CaptureWin("A")
	if (!ctx["hwnd"])
		return false

	; 进程白名单是窗口识别的第一层安全边界
	try exe := WinGetProcessName(ctx["win"])
	catch
		return false

	exeName := Cfg["APP_WIN"]
	if !exeName.Has(exe) {
		; 仅键按下时记 Debug，避免 HotIf 轮询刷屏
		if (GetKeyState("RButton", "P") || GetKeyState("LButton", "P")) {
			MouseGetPos(, , , &ctrlHwnd, 2)
			ptrNn := ""
			try ptrNn := ctrlHwnd ? ControlGetClassNN(ctrlHwnd) : ""
			Log_Debug("hotif.deny", "AppWin 未命中", Map(
				"exe", exe, "cls", ctx["cls"], "ttl", ctx["ttl"], "ptrNn", ptrNn
			))
		}
		return false
	}

	; 窗口类是第二层安全边界，仓库模式仅适用于住院或仓库窗口
	cls := ctx["cls"]
	if (Cfg.Has("WAREHOUSE_ENABLED") && Cfg["WAREHOUSE_ENABLED"]) {
		if (cls = Cfg["IPT_WINDOW_CLASS"])
			return true
		if (GetKeyState("RButton", "P") || GetKeyState("LButton", "P")) {
			MouseGetPos(, , , &ctrlHwnd, 2)
			ptrNn := ""
			try ptrNn := ctrlHwnd ? ControlGetClassNN(ctrlHwnd) : ""
			Log_Debug("hotif.deny", "窗口类不匹配(仓库)", Map(
				"exe", exe, "cls", cls, "ttl", ctx["ttl"],
				"need", Cfg["IPT_WINDOW_CLASS"], "ptrNn", ptrNn
			))
		}
		return false
	}
	if (cls = Cfg["OPT_WINDOW_CLASS"] || cls = Cfg["IPT_WINDOW_CLASS"])
		return true
	if (GetKeyState("RButton", "P") || GetKeyState("LButton", "P")) {
		MouseGetPos(, , , &ctrlHwnd, 2)
		ptrNn := ""
		try ptrNn := ctrlHwnd ? ControlGetClassNN(ctrlHwnd) : ""
		Log_Debug("hotif.deny", "窗口类不匹配", Map(
			"exe", exe, "cls", cls, "ttl", ctx["ttl"], "ptrNn", ptrNn,
			"needOpt", Cfg["OPT_WINDOW_CLASS"], "needIpt", Cfg["IPT_WINDOW_CLASS"]
		))
	}
	return false
}

Util_DetectScene(win := "A") {
	global Cfg
	ctx := Util_CaptureWin(win)
	cls := ctx["cls"]
	ttl := ctx["ttl"]
	winId := ctx["win"]

	if (cls = Cfg["OPT_WINDOW_CLASS"])
		return "OPT"

	; 住院与仓库界面共用窗口类，需要通过表头特征进一步区分
	if (cls = Cfg["IPT_WINDOW_CLASS"]) {
		if Util_IsWarehouseWindow(winId)
			return "WAREHOUSE"
		if InStr(ttl, "追溯码录入")
			return "IPT"
		return "IPT"
	}

	return "UNKNOWN"
}

Util_IsWarehouseWindow(win := "A") {
	global Cfg
	if !(Cfg.Has("WAREHOUSE_ANCHORS") && IsObject(Cfg["WAREHOUSE_ANCHORS"]))
		return false
	anchors := Cfg["WAREHOUSE_ANCHORS"]
	if (anchors.Length = 0)
		return false

	; 仓库入库网格通常不包含“患者姓名”和“应扫次数”列
	hdrLine := Util_TryGetGridHeaderLine(win)
	if (hdrLine = "")
		return false

	; 任一住院特征列存在时均按住院界面处理
	for _, a in anchors {
		t := Trim(a)
		if (t != "" && InStr(hdrLine, t))
			return false
	}
	return true
}

Util_WarehouseSoftCheck(win := "A") {
	global Cfg
	if !(Cfg.Has("WAREHOUSE_ANCHORS") && IsObject(Cfg["WAREHOUSE_ANCHORS"])) {
		Log_Debug("wh.soft.cfg_miss", "缺 WarehouseAnchorTexts")
		return Map("ok", false, "level", "Error", "message", "[仓库模式校验]`n缺少仓库列特征配置 WarehouseAnchorTexts")
	}

	anchors := Cfg["WAREHOUSE_ANCHORS"]
	if (anchors.Length = 0) {
		Log_Debug("wh.soft.cfg_empty", "仓库列特征为空")
		return Map("ok", false, "level", "Warn", "message", "[仓库模式校验]`n仓库列特征不能为空")
	}

	hdrLine := Util_TryGetGridHeaderLine(win)
	if (hdrLine = "") {
		Log_Debug("wh.soft.hdr_empty", "无法抓取表头")
		return Map("ok", false, "level", "Warn", "message", "[仓库模式校验]`n无法抓取表头，请检查当前选中行或剪贴板权限")
	}

	for _, a in anchors {
		t := Trim(a)
		if (t != "" && InStr(hdrLine, t)) {
			Log_Debug("wh.soft.ipt_hit", "表头命中住院特征", Map(
				"anchor", t, "hdrLen", StrLen(hdrLine)
			))
			return Map(
				"ok", false,
				"level", "Error",
				"message", "[仓库模式校验]`n当前表头命中住院列特征：" t "，请关闭仓库模式后再操作",
				"header_line", hdrLine
			)
		}
	}
	Log_Debug("wh.soft.ok", "仓库软校验通过", Map("hdrLen", StrLen(hdrLine)))
	return Map("ok", true, "header_line", hdrLine)
}

Util_TryGetGridHeaderLine(win := "A") {
	global Cfg
	win := Util_NormalizeWin(win)
	if !WinExist(win)
		return ""

	old := ClipboardAll()
	txt := ""
	try UI_FocusGridClassNN(Cfg["IPT_PARSE_GRID_CLASSNN"], win)
	catch
		return ""
	if !WinExist(win)
		return ""

	try {
		A_Clipboard := ""
		SendInput "^c"
		if !ClipWait(0.5)
			return ""
		txt := A_Clipboard
	} finally {
		try A_Clipboard := old
	}

	if (Trim(txt) = "")
		return ""

	firstTabLine := ""
	for _, line in StrSplit(txt, "`n") {
		line := Trim(line, "`r`t ")
		if (line = "")
			continue
		if !InStr(line, "`t")
			continue

		if (InStr(line, "追溯码") || InStr(line, "患者姓名") || InStr(line, "应扫次数") || InStr(line, "药品名称") || InStr(line, "物资名称")) {
			return line
		}

		if (firstTabLine = "")
			firstTabLine := line
	}
	return firstTabLine
}
