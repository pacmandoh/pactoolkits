; Injector 的配置加载、目标窗口识别、剪贴板访问和日志支持

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

; 限制 SQL 文本长度，避免诊断信息遮蔽主要错误
Util_ShortSQL(sql, maxLen := 1200) {
    if (StrLen(sql) <= maxLen)
        return sql
    return SubStr(sql, 1, maxLen) "`n... (truncated, len=" StrLen(sql) ")"
}

Util_PathFull(p) {
    ; 统一为绝对路径，避免工作目录变化影响文件访问
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

    ; 版本信息仅来自当前模块描述文件，避免错误显示 Host 版本
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
        return false
    }

    ; 窗口类是第二层安全边界，仓库模式仅适用于住院或仓库窗口
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

Util_LogLine(line, logDir := "") {
    ; 仅持久化错误级别事件，控制高频自动化路径的日志量
    if (logDir = "")
        logDir := A_ScriptDir "\logs"
    try DirCreate(logDir)
    stamp := FormatTime(, "yyyy-MM-dd HH:mm:ss")
    file := logDir "\" FormatTime(, "yyyyMMdd") ".log"
    try FileAppend(stamp " " line "`n", file, "UTF-8")
}

Util_GetPrimaryIPv4() {
    ; 使用首个可用 IPv4 生成客户端标识；无法获取时保留空值
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
    ; 模块配置必须由 Host 显式传入，禁止回退到安装目录默认文件而绕过用户配置
    moduleSettingsPath := Util_GetArgValue("--module-settings")
    if (moduleSettingsPath = "")
        return Util_CfgFail("缺少 --module-settings（模块业务配置路径）", "MISSING_MODULE_SETTINGS")
    moduleSettingsPath := Util_PathFull(moduleSettingsPath)
    agent := Util_LoadModuleSettingsMap(moduleSettingsPath, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")

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
    cfg["OPT_PARSE_GRID_CLASSNN"] := Util_CfgGetString(agent, "OptParseGridClassNN", true, &ok, &err)
    if !ok
        return Util_CfgFail(err, "INVALID_MODULE_SETTINGS")
    cfg["OPT_VERIFY_GRID_CLASSNN"] := Util_CfgGetString(agent, "OptVerifyGridClassNN", true, &ok, &err)
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

    parsed := Json_ReadFile(path)
    if !(parsed.Has("ok") && parsed["ok"]) {
        msg := parsed.Has("err") ? parsed["err"] : "未知解析错误"
        ok := false, err := "module-settings JSON 解析失败：`n" msg
        return ""
    }
    root := parsed["val"]
    if (Type(root) != "Map") {
        ok := false, err := "module-settings 根节点必须是 JSON 对象"
        return ""
    }

    ok := true, err := ""
    return root
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
