; Injector 通用工具：配置加载、窗口场景、剪贴板与日志

UI_Tip(msg, ms := 1200) {
    ToolTip(msg)
    SetTimer(() => ToolTip(), -ms)
}

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

; 截断长 SQL，避免错误弹窗/日志刷屏
Util_ShortSQL(sql, maxLen := 1200) {
    if (StrLen(sql) <= maxLen)
        return sql
    return SubStr(sql, 1, maxLen) "`n... (truncated, len=" StrLen(sql) ")"
}

Util_PathFull(p) {
    ; 相对路径展开为绝对路径，供后续文件读写
    buf := Buffer(32768 * 2, 0)
    len := DllCall("Kernel32\GetFullPathNameW", "str", p, "uint", 32768, "ptr", buf, "ptr", 0, "uint")
    return len ? StrGet(buf, len, "UTF-16") : p
}

Util_ReadVersionFile() {
    global VersionInfo
    if (IsSet(VersionInfo) && Type(VersionInfo) = "Map" && VersionInfo.Has("moduleVersion"))
        return VersionInfo

    info := Map(
        "moduleVersion", "unknown"
    )

    ; Tip/版本身份只来自本模块 module.json，避免误读宿主版本
    moduleMetaPath := Util_PathFull(A_ScriptDir "\module.json")
    if FileExist(moduleMetaPath) {
        meta := Json_ReadFile(moduleMetaPath)
        if (IsObject(meta) && meta.Has("ok") && meta["ok"] && meta.Has("val")
            && IsObject(meta["val"]) && meta["val"].Has("version")) {
            info["moduleVersion"] := meta["val"]["version"]
        }
    }

    return info
}

Util_ClearModuleReady() {
    path := Util_PathFull(A_ScriptDir "\module.ready")
    try FileDelete(path)
}

Util_MarkModuleReady() {
    path := Util_PathFull(A_ScriptDir "\module.ready")
    try FileDelete(path)
    try FileAppend("ok`n", path, "UTF-8")
}

Util_InitRuntimeInfo(versionInfo := "") {
    v := (IsObject(versionInfo) && versionInfo.Has("moduleVersion")) ? versionInfo["moduleVersion"] : Util_ReadVersionFile()["moduleVersion"]
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
    v := Util_ReadVersionFile()
    return "agents-" v["moduleVersion"] "+ahk-" A_AhkVersion
}

Util_LoadDotEnv(path) {
    env := Map()

    full := Util_PathFull(path)
    if !FileExist(full)
        return env

    txt := FileRead(full, "UTF-8")

    ; 去掉 UTF-8 BOM，避免首 key 解析异常
    if (SubStr(txt, 1, 1) = Chr(0xFEFF))
        txt := SubStr(txt, 2)

    dq := Chr(34)  ; "
    sq := "'"      ; '

    for _, line in StrSplit(txt, "`n") {
        line := Trim(line, "`r`t ")

        if (line = "" || SubStr(line, 1, 1) = "#")
            continue

        ; 兼容 shell 风格 export KEY=VAL
        if (SubStr(line, 1, 7) = "export ")
            line := Trim(SubStr(line, 8))

        ; KEY=VAL 分割允许 VAL 为空
        if !RegExMatch(line, "^\s*([^=]+?)\s*=\s*(.*)\s*$", &m)
            continue

        key := Trim(m[1])
        val := Trim(m[2])

        ; 行尾 # 注释仅在引号外剥离
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
    ; 成功返回 Array；失败返回 "" 表示交由后续标量/集合解析
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

; 解析类 JSON 对象为 Map-as-Set（APP_WIN 等）
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

    ; 按顶层逗号切 token，忽略引号内逗号
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

; 从 "key":value token 提取 key 写入 set
; token 形如 "xxx":1 或 'xxx':true
Util_SetConsumeToken(set, token) {
    t := Trim(token, "`r`t ")
    if (t = "")
        return

    ; 找顶层冒号（忽略引号内）
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

    ; key 必须带引号，否则丢弃该 token
    if (StrLen(k) < 2)
        return

    if ((SubStr(k, 1, 1) = dq && SubStr(k, -1) = dq)
     || (SubStr(k, 1, 1) = sq && SubStr(k, -1) = sq)) {
        k := SubStr(k, 2, -1)
        if (k != "")
            set[k] := true
    }
}



Util_NormalizeWin(win := "A") {
    ; 尽早把 "A" 冻成 ahk_id HWND，避免 MsgBox/切窗后 "A" 漂移
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

    ; Cfg 未就绪时不启用热键
    if !IsSet(Cfg) || (Type(Cfg) != "Map")
        return false
    if !Cfg.Has("OPT_WINDOW_CLASS") || !Cfg.Has("IPT_WINDOW_CLASS") || !Cfg.Has("APP_WIN")
        return false

    ; 冻结当前活动窗口，避免判定中途 "A" 漂移
    ctx := Util_CaptureWin("A")
    if (!ctx["hwnd"])
        return false

    ; 先按 APP_WIN exe 白名单过滤
    try exe := WinGetProcessName(ctx["win"])
    catch
        return false

    exeName := Cfg["APP_WIN"]
    if !exeName.Has(exe) {
        return false
    }

    ; 再按窗口 class；仓库模式仅允许住院/仓库类
    cls := ctx["cls"]
    if (Cfg.Has("WAREHOUSE_ENABLED") && Cfg["WAREHOUSE_ENABLED"])
        return (cls = Cfg["IPT_WINDOW_CLASS"])
    return (cls = Cfg["OPT_WINDOW_CLASS"] || cls = Cfg["IPT_WINDOW_CLASS"])
}

Util_DetectScene(win := "A") {
    global Cfg
    ctx := Util_CaptureWin(win)
    cls := ctx["cls"]
    ttl := ctx["ttl"]
    winId := ctx["win"]

    if (cls = Cfg["OPT_WINDOW_CLASS"])
        return "OPT"

    ; 住院与仓库共用窗口类，仅在此分支用表头锚点区分
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

    ; 主判定：用当前网格表头特征区分住院/仓库
    ; 仓库入库表头通常不含“患者姓名”“应扫次数”
    hdrLine := Util_TryGetGridHeaderLine(win)
    if (hdrLine = "")
        return false

    ; 命中任一住院锚点列 → 非仓库；否则视为仓库
    for _, a in anchors {
        t := Trim(a)
        if (t != "" && InStr(hdrLine, t))
            return false
    }
    return true
}

Util_WarehouseSoftCheck(win := "A") {
    global Cfg
    if !(Cfg.Has("WAREHOUSE_ANCHORS") && IsObject(Cfg["WAREHOUSE_ANCHORS"]))
        return Map("ok", false, "level", "ERR", "type", "[仓库模式校验]", "why", "缺少仓库列特征配置 WarehouseAnchorTexts")

    anchors := Cfg["WAREHOUSE_ANCHORS"]
    if (anchors.Length = 0)
        return Map("ok", false, "level", "WARN", "type", "[仓库模式校验]", "why", "仓库列特征不能为空")

    hdrLine := Util_TryGetGridHeaderLine(win)
    if (hdrLine = "")
        return Map("ok", false, "level", "WARN", "type", "[仓库模式校验]", "why", "无法抓取表头，请检查当前选中行或剪贴板权限")

    for _, a in anchors {
        t := Trim(a)
        if (t != "" && InStr(hdrLine, t))
            return Map(
                "ok", false,
                "level", "ERR",
                "type", "[仓库模式校验]",
                "why", "当前表头命中住院列特征：" t "，请关闭仓库模式后再操作",
                "header_line", hdrLine
            )
    }
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

UI_FocusClassNN(classNN, win := "A", control := true) {
    nn := Trim("" classNN)
    if (nn = "")
        return ""

    win := Util_NormalizeWin(win)
    hwndCtrl := 0
    try hwndCtrl := ControlGetHwnd(nn, win)
    catch
        return ""
    if !hwndCtrl
        return ""

    if !WinActive(win) {
        try WinActivate(win)
        catch
            return ""
        try WinWaitActive(win, , 0.3)
        catch
            return ""
    }

    if (control) {
        try ControlFocus(nn, win)
        catch
            return ""
    } else {
        try DllCall("SetFocus", "Ptr", hwndCtrl)
        catch
            return ""
        try UI_PostClick(hwndCtrl, 30, 40)
        catch
            return ""
    }
    return hwndCtrl
}

Util_GetCtrlHwndByClassNN(classNN, win := "A") {
    nn := Trim("" classNN)
    if (nn = "")
        return 0

    win := Util_NormalizeWin(win)
    hwndCtrl := 0
    try hwndCtrl := ControlGetHwnd(nn, win)
    catch
        return 0
    return hwndCtrl ? hwndCtrl : 0
}



Util_WithClipboard(tempText, fn) {
    ; 临时覆盖剪贴板执行 fn，finally 无条件恢复用户内容
    old := ClipboardAll()
    try {
        A_Clipboard := tempText
        ClipWait(0.4)
        return fn.Call()
    } finally {
        try A_Clipboard := old
    }
}

Util_LogLine(line, logDir := "") {
    ; ERR 级追加到 logs\YYYYMMDD.log，避免热路径噪音
    if (logDir = "")
        logDir := A_ScriptDir "\logs"
    try DirCreate(logDir)
    stamp := FormatTime(, "yyyy-MM-dd HH:mm:ss")
    file := logDir "\" FormatTime(, "yyyyMMdd") ".log"
    try FileAppend(stamp " " line "`n", file, "UTF-8")
}

Util_GetPrimaryIPv4() {
    ; 取首个可用 IPv4；失败返回空串（写入 clientId）
    try {
        q := "SELECT IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True"
        for nic in ComObjGet("winmgmts:").ExecQuery(q) {
            ips := nic.IPAddress
            if !IsObject(ips)
                continue
            for ip in ips {
                ip := Trim("" ip)
                if RegExMatch(ip, "^\d{1,3}(\.\d{1,3}){3}$")
                    return ip
            }
        }
    }
    return ""
}

Util_GetOSName() {
    ; 读 ProductName；失败回退 A_OSVersion
    try {
        key := "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion"
        name := RegRead(key, "ProductName", "")
        name := Trim("" name)
        if (name != "")
            return name
    }
    return A_OSVersion
}

Util_GetConfigArg() {
    i := 1
    while (i <= A_Args.Length) {
        arg := A_Args[i]
        if (arg = "--config") {
            if (i + 1 <= A_Args.Length)
                return Util_PathFull(A_Args[i + 1])
            return ""
        }
        if (SubStr(arg, 1, 9) = "--config=")
            return Util_PathFull(SubStr(arg, 10))
        i++
    }
    return ""
}

Util_GetArgValue(flagName) {
    i := 1
    while (i <= A_Args.Length) {
        arg := A_Args[i]
        if (arg = flagName) {
            if (i + 1 <= A_Args.Length)
                return Trim(A_Args[i + 1])
            return ""
        }

        prefix := flagName "="
        if (SubStr(arg, 1, StrLen(prefix)) = prefix)
            return Trim(SubStr(arg, StrLen(prefix) + 1))
        i++
    }
    return ""
}

Util_LoadUnifiedConfig(configPath) {
    path := Trim(configPath)
    if (path = "")
        return Util_CfgFail("配置路径为空", "EMPTY_PATH")
    if !FileExist(path)
        return Util_CfgFail("配置文件不存在：`n" path, "FILE_NOT_FOUND")

    parsed := Json_ReadFile(path)
    if !(parsed.Has("ok") && parsed["ok"]) {
        msg := parsed.Has("err") ? parsed["err"] : "未知解析错误"
        return Util_CfgFail("配置 JSON 解析失败：`n" msg, "JSON_PARSE")
    }
    root := parsed["val"]

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
    agents := Util_CfgGetMap(root, "Agents", &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_AGENTS")
    agent := Util_CfgGetMap(agents, "Injector", &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")

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
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["PG_SSL"] := Util_CfgGetOneOf(agent, "PgSsl", ["disable", "allow", "prefer", "require", "verify-ca", "verify-full"], &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["OPT_WINDOW_CLASS"] := Util_CfgGetString(agent, "OptWindowClass", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["IPT_WINDOW_CLASS"] := Util_CfgGetString(agent, "IptWindowClass", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["OPT_PARSE_GRID_CLASSNN"] := Util_CfgGetString(agent, "OptParseGridClassNN", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["OPT_VERIFY_GRID_CLASSNN"] := Util_CfgGetString(agent, "OptVerifyGridClassNN", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["IPT_PARSE_GRID_CLASSNN"] := Util_CfgGetString(agent, "IptParseGridClassNN", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["IPT_VERIFY_GRID_CLASSNN"] := Util_CfgGetString(agent, "IptVerifyGridClassNN", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["OPT_INPUT_CLASSNN"] := Util_CfgGetString(agent, "OptInputClassNN", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["IPT_INPUT_CLASSNN"] := Util_CfgGetString(agent, "IptInputClassNN", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["CONFIRM_TIMEOUT_MS"] := Util_CfgGetRangeInt(agent, "ConfirmTimeoutMs", 100, 10000, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["APP_WIN"] := Util_CfgGetAppWin(agent, "AppWin", &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["COL_SPECS"] := Util_CfgGetStringArray(agent, "ColSpecs", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["INT_COLS"] := Util_CfgGetStringArray(agent, "IntCols", false, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")

    cfg["WAREHOUSE_ENABLED"] := Util_CfgGetBool(agent, "WarehouseEnabled", &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["WAREHOUSE_ANCHORS"] := Util_CfgGetStringArray(agent, "WarehouseAnchorTexts", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["CODE_PICK_POLICY"] := Util_CfgGetOneOf(agent, "CodePickPolicy", ["max_level", "min_level"], &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")
    cfg["CODE_PICK_POLICY"] := StrUpper(cfg["CODE_PICK_POLICY"])
    cfg["WAREHOUSE_TASK_IDENTIFIER"] := Util_CfgGetString(agent, "WarehouseTaskIdentifier", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_INJECTOR")

    return Map("ok", true, "cfg", cfg)
}

Util_CfgFail(why, reason := "CONFIG_INVALID") {
    msg := Trim("" why)
    return Map("ok", false, "level", "ERR", "type", "[配置错误]", "why", msg, "reason", reason, "err", msg)
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
