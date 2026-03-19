; 录入拆零药物追溯码 v0.1.0beta
; 	Semi_Auto_Fill() - 逐条手动点击后半自动录入
; 		半自动：手动点选目标信息后按热键执行
; 		流程：复制目标信息 -> 解析 -> 预留/计算减扣 -> 粘贴追溯码 -> 验证 -> Commit/Rollback

Semi_Auto_Fill(opt, ipt, colSpecs, intCols, timeoutMs, optParseGridClassNN, optVerifyGridClassNN, iptParseGridClassNN, iptVerifyGridClassNN, optInputClassNN, iptInputClassNN, win := "A") {
    ; 0) 场景识别
    win := Util_NormalizeWin(win)
    cls := WinGetClass(win)

    ip := Util_GetPrimaryIPv4()
    osName := Util_GetOSName()
    clientId := A_ComputerName "|" A_UserName "|ip=" ip "|os=" osName "|ver=" Util_GetAgentVersionTag()
	
    mode := ""
    if (cls = ipt) {
        mode := "住院"
    } else if (cls = opt) {
        mode := "门诊"
    } else {
        return Map("ok", false, "level", "WARN", "type", "[界面错误]", "why", "当前窗口不在允许场景内`nclass=" cls)
    }

    ; 1) 复制目标信息并解析
    parseGridClassNN := (cls = ipt) ? iptParseGridClassNN : optParseGridClassNN
    verifyGridClassNN := (cls = ipt) ? iptVerifyGridClassNN : optVerifyGridClassNN
    inputClassNN := (cls = ipt) ? iptInputClassNN : optInputClassNN

    p := Parse_TargetInfo(colSpecs, ipt, intCols, "", win, parseGridClassNN)

    if !p["ok"] {
        return p
    }

    by := p["bySpec"]
    drugId := by.Has("物资名称||药品名称") ? Trim(by["物资名称||药品名称"]) : ""
    spec   := by.Has("规格||药品规格") ? Trim(by["规格||药品规格"]) : ""
    if (drugId = "" || spec = "") {
        return Map("ok", false, "level", "WARN", "type", "[解析错误]", "why", "解析结果缺少关键字段`n药品名称=" drugId " 规格=" spec)
    }

    ; 2) 事务预留/计算减扣
    txnId := Util_TxnId()
	
    r := Txn_ReservePick(txnId, clientId, drugId, spec, 0, opt, ipt, by, cls)

    if !r["ok"] {
        return r
    }
	if (r["skip"]) {
		; skip 提示与聚焦由 main 统一处理
		return Map("ok", true, "skip", true, "type", r["type"], "why", r["why"], "focusClassNN", inputClassNN)
	}

    codes := r.Has("codes") ? r["codes"] : []
    if (codes.Length = 0) {
        Txn_Rollback(txnId)
        return Map("ok", false, "level", "ERR", "type", "[预留错误]", "why", "预留成功但追溯码异常并且为空")
    }

    debugOptMulti := (cls = opt && codes.Length > 1)
    tInjectStart := A_TickCount
    optBaseScanned := -1
    if (debugOptMulti) {
        optBaseScanned := UI_GetOptScannedCount(optVerifyGridClassNN, win)
        Util_LogLine(
            "OPT_MULTI | start"
            . " | txn=" txnId
            . " | drug=" drugId
            . " | spec=" spec
            . " | codes=" codes.Length
            . " | input=" optInputClassNN
            . " | base_scanned=" optBaseScanned
        )
    }

    ; 3) UI 注入追溯码（逐条粘贴）
    for i, code in codes {
        if (debugOptMulti) {
            Util_LogLine(
                "OPT_MULTI | paste_begin"
                . " | txn=" txnId
                . " | idx=" i "/" codes.Length
                . " | t=" (A_TickCount - tInjectStart) "ms"
                . " | len=" StrLen(code)
            )
        }
        pr := UI_Paste_ByPolicy(code, opt, ipt, optInputClassNN, iptInputClassNN, win)
        if (!pr["ok"]) {
            ; 已预留扣库 -> 业务回滚
            if (debugOptMulti) {
                Util_LogLine(
                    "OPT_MULTI | paste_fail"
                    . " | txn=" txnId
                    . " | idx=" i "/" codes.Length
                    . " | t=" (A_TickCount - tInjectStart) "ms"
                    . " | why=" StrReplace(pr["why"], "`n", " | ")
                )
            }
            Txn_Rollback(txnId)
            return Map("ok", false, "level", "ERR", "type", pr["type"], "why", "注入失败（第" i "条）：`n" pr["why"])
        }

        if (debugOptMulti) {
            Util_LogLine(
                "OPT_MULTI | paste_ok"
                . " | txn=" txnId
                . " | idx=" i "/" codes.Length
                . " | t=" (A_TickCount - tInjectStart) "ms"
            )
        }

        if (debugOptMulti) {
            targetScanned := (optBaseScanned >= 0) ? (optBaseScanned + i) : i
            wr := UI_WaitOptScannedCount(targetScanned, optVerifyGridClassNN, 450, win)
            if (debugOptMulti) {
                Util_LogLine(
                    "OPT_MULTI | scan_wait"
                    . " | txn=" txnId
                    . " | idx=" i "/" codes.Length
                    . " | t=" (A_TickCount - tInjectStart) "ms"
                    . " | target=" targetScanned
                    . " | ok=" (wr["ok"] ? "1" : "0")
                    . " | seen=" wr["count"]
                    . " | elapsed=" wr["elapsed"] "ms"
                )
            }
            if !wr["ok"] {
                Txn_Rollback(txnId)
                return Map(
                    "ok", false, "level", "ERR", "type", "[录入验证错误]",
                    "why", "门诊窗口未观察到“已扫码数”按条递增，已停止后续注入，避免多条码在同一轮内合并提交"
                )
            }
        }
    }

    ; 4) 验证
    if (debugOptMulti) {
        wc := Map("ok", true)
        Util_LogLine(
            "OPT_MULTI | final_confirm"
            . " | txn=" txnId
            . " | t=" (A_TickCount - tInjectStart) "ms"
            . " | ok=1"
            . " | mode=incremental_scan_wait"
        )
    } else {
	    wc := UI_WaitConfirm(codes, timeoutMs, opt, ipt, optVerifyGridClassNN, iptVerifyGridClassNN, iptParseGridClassNN, win)
    }
	
    if !wc["ok"] {
        Txn_Rollback(txnId)
        return wc
    }

    ; 5) 回写
    rc := Txn_Commit(txnId)
    if !(rc is Map) {
        return Map("ok", false, "level", "ERR", "type", "[提交错误]", "why", "未知执行错误")
    }
	
	if (!rc["ok"]) {
		return rc
	}

    UI_Tip("[半自动注入完成] " drugId " / " spec "（" mode "）", 1500)
    return Map("ok", true)
}
