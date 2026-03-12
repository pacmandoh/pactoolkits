; ================== 码上放心仓库任务执行模块 ==================
global __MSFX_COL := Map()

Msfx_RunWarehouseTaskFlow(timeoutMs, classNN, win := "A") {
    win := Util_NormalizeWin(win)
    pre := Util_WarehouseSoftCheck(win)
    if !pre["ok"]
        return pre
    headerLine := pre.Has("header_line") ? Trim(pre["header_line"]) : ""

    ip := Util_GetPrimaryIPv4()
    osName := Util_GetOSName()
    clientId := A_ComputerName "|" A_UserName "|ip=" ip "|os=" osName "|ver=" Util_GetAgentVersionTag()

    claim := Msfx_ClaimInjectTasks(clientId, 1)
    if !claim["ok"]
        return claim

    tasks := claim["tasks"]
    if (tasks.Length = 0)
        return Map("ok", true, "skip", true, "type", "[仓库任务]", "why", "当前无待执行任务")
    UI_Tip("[仓库模式] 已领取任务 " tasks.Length " 条，开始注入…", 1000)

    policy := Cfg.Has("CODE_PICK_POLICY") ? Cfg["CODE_PICK_POLICY"] : "MAX_LEVEL"
    lastErr := ""
    hasErr := false
    idx := 0
    for _, task in tasks {
        idx++
        taskId := task["task_id"]
        r := Msfx_RunOneWarehouseTask(taskId, policy, timeoutMs, classNN, win, headerLine)
        if !r["ok"] {
            lastErr := r["why"]
            if (r.Has("level") && r["level"] = "ERR")
                hasErr := true
        }
    }

    if (lastErr != "")
        return Map("ok", false, "level", hasErr ? "ERR" : "WARN", "type", "[仓库任务]", "why", lastErr)

    return Map(
        "ok", true, "type", "[仓库任务完成]",
        "why", "已处理任务数=" tasks.Length
    )
}

Msfx_RunOneWarehouseTask(taskId, policy, timeoutMs, classNN, win, headerLine := "") {
    Msfx_InsertEvent(taskId, "PARSE", "INFO", "仓库任务开始执行")
    if (Trim(headerLine) != "") {
        line := headerLine
        if (StrLen(line) > 240)
            line := SubStr(line, 1, 240) "..."
        Msfx_InsertEvent(taskId, "PARSE", "INFO", "仓库软校验表头: " line)
    }

    rowsRes := Msfx_GetPendingTaskCodes(taskId)
    if !rowsRes["ok"] {
        Msfx_FinalizeInjectTask(taskId, "读取任务明细失败")
        return rowsRes
    }

    codeRows := rowsRes["rows"]
    if (codeRows.Length = 0) {
        Msfx_InsertEvent(taskId, "PARSE", "WARN", "任务无待处理明细")
        Msfx_FinalizeInjectTask(taskId, "任务无待处理明细")
        return Map("ok", false, "level", "WARN", "type", "[仓库任务错误]", "why", "任务无待注入明细")
    }

    succ := 0
    fail := 0
    injectRuns := 0
    firstVerified := false
    isMinPolicy := (StrUpper(Trim(policy)) = "MIN_LEVEL")
    groups := []
    groupPos := Map()
    noCodeLeafs := []
    noCodeStaging := []
    ; MIN_LEVEL 极速路径：成功结果先累计，循环结束后批量回写，减少数据库往返。
    minFirstLeafs := []
    minFirstStaging := []
    minSoftLeafs := []
    minSoftStaging := []

    ; 先按取码策略计算目标码，再按目标码分组，避免同码重复注入。
    for _, item in codeRows {
        leafCode := item["leaf_code"]
        stagingId := item["staging_id"]
        injectCode := Msfx_NormalizeCode(Msfx_SelectInjectCode(item, policy))

        if (Trim(injectCode) = "") {
            fail++
            noCodeLeafs.Push(leafCode)
            if (stagingId > 0)
                noCodeStaging.Push(stagingId)
            continue
        }

        if !groupPos.Has(injectCode) {
            idx := groups.Length + 1
            groupPos[injectCode] := idx
            groups.Push(Map("inject_code", injectCode, "items", []))
        }
        groups[groupPos[injectCode]]["items"].Push(item)
    }

    if (noCodeLeafs.Length > 0) {
        u1 := Msfx_BatchUpdateTaskCodes(taskId, noCodeLeafs, "FAILED", "", "取码策略结果为空")
        if !u1["ok"] {
            Msfx_FinalizeInjectTask(taskId, "更新失败明细状态失败")
            return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u1["err"])
        }
        u2 := Msfx_BatchUpdateStagingCodes(noCodeStaging, "FAILED", "取码策略结果为空")
        if !u2["ok"] {
            Msfx_FinalizeInjectTask(taskId, "更新 staging 失败状态失败")
            return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u2["err"])
        }
        Msfx_InsertEvent(taskId, "PARSE", "ERR", "取码策略结果为空，明细数=" noCodeLeafs.Length)
    }

    totalGroups := groups.Length
    prep := UI_PrepareWarehouseFastTarget("TEdit1", win)
    if !prep["ok"] {
        why := prep.Has("why") ? prep["why"] : "仓库窗口准备失败"
        Msfx_FinalizeInjectTask(taskId, "仓库窗口准备失败")
        return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", why)
    }

    for gIdx, grp in groups {
        injectCode := grp["inject_code"]
        items := grp["items"]
        itemCount := items.Length
        leafCodes := Msfx_GroupLeafCodes(items)
        stagingIds := Msfx_GroupStagingIds(items)
        injectRuns++

        ; 稳启动：仅首条走稳注入并强验证，后续全部走极速注入。
        ; 目标是确保注入链对齐，同时把吞吐压到高位。
        useStableInject := !firstVerified
        if useStableInject
            pr := UI_Paste_Impl(win, "TEdit1", injectCode, false)
        else
            pr := UI_Paste_Warehouse(injectCode, "TEdit1", win)
        if !pr["ok"] {
            why := pr.Has("why") ? pr["why"] : "仓库窗口注入失败"
            fail += itemCount
            u1 := Msfx_BatchUpdateTaskCodes(taskId, leafCodes, "FAILED", "", why)
            if !u1["ok"] {
                Msfx_FinalizeInjectTask(taskId, "更新注入失败状态失败")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u1["err"])
            }
            u2 := Msfx_BatchUpdateStagingCodes(stagingIds, "FAILED", why)
            if !u2["ok"] {
                Msfx_FinalizeInjectTask(taskId, "更新 staging 注入失败状态失败")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u2["err"])
            }
            Msfx_InsertEvent(taskId, "INJECT", "ERR", "注入失败，码=" injectCode "，影响明细=" itemCount "，原因=" why)
            Msfx_ApplyWarehouseBurstPacing(gIdx, totalGroups)
            continue
        }

        verifyResult := "SOFT_OK"
        verifyOk := true
        if !firstVerified {
            ; 仓库验证窗口结构与住院一致，沿用 TcxGridSite 第 1 个网格验证首条。
            firstTimeout := (timeoutMs < 3500) ? 3500 : timeoutMs
            wc := UI_WaitConfirm_Warehouse([injectCode], firstTimeout, classNN, 1, win)
            verifyOk := wc["ok"]
            verifyResult := verifyOk ? "FIRST_OK" : "FIRST_FAIL"
            if verifyOk {
                firstVerified := true
                ; 首条验证会把焦点切到验证区，进入极速循环前强制回到输入框。
                rePrep := UI_PrepareWarehouseFastTarget("TEdit1", win)
                if !rePrep["ok"] {
                    why := rePrep.Has("why") ? rePrep["why"] : "仓库窗口准备失败"
                    Msfx_FinalizeInjectTask(taskId, "仓库窗口准备失败")
                    return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", why)
                }
            }
        }

        if !verifyOk {
            why := wc.Has("why") ? wc["why"] : "仓库窗口验证失败"
            fail += itemCount
            u1 := Msfx_BatchUpdateTaskCodes(taskId, leafCodes, "FAILED", verifyResult, why)
            if !u1["ok"] {
                Msfx_FinalizeInjectTask(taskId, "更新验证失败状态失败")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u1["err"])
            }
            u2 := Msfx_BatchUpdateStagingCodes(stagingIds, "FAILED", why)
            if !u2["ok"] {
                Msfx_FinalizeInjectTask(taskId, "更新 staging 验证失败状态失败")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u2["err"])
            }
            Msfx_InsertEvent(taskId, "VERIFY", "ERR", "验证失败，码=" injectCode "，影响明细=" itemCount "，原因=" why)

            ; 首条失败会导致后续注入发生错位链，直接终止本任务保证准确性。
            if !firstVerified {
                Msfx_FinalizeInjectTask(taskId, "首条验证失败，任务已终止以避免错位注入")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", "首条注入验证失败，已终止任务，避免后续错位")
            }

            Msfx_ApplyWarehouseBurstPacing(gIdx, totalGroups)
            continue
        }

        succ += itemCount
        if isMinPolicy {
            if (verifyResult = "FIRST_OK") {
                Msfx_ArrayAppend(minFirstLeafs, leafCodes)
                Msfx_ArrayAppend(minFirstStaging, stagingIds)
            } else {
                Msfx_ArrayAppend(minSoftLeafs, leafCodes)
                Msfx_ArrayAppend(minSoftStaging, stagingIds)
            }
            Msfx_ApplyWarehouseBurstPacing(gIdx, totalGroups)
            continue
        }

        u1 := Msfx_BatchUpdateTaskCodes(taskId, leafCodes, "INJECTED", verifyResult, "")
        if !u1["ok"] {
            Msfx_FinalizeInjectTask(taskId, "更新注入成功状态失败")
            return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u1["err"])
        }
        u2 := Msfx_BatchUpdateStagingCodes(stagingIds, "VERIFIED", "")
        if !u2["ok"] {
            Msfx_FinalizeInjectTask(taskId, "更新 staging 成功状态失败")
            return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u2["err"])
        }
        msg := (itemCount > 1) ? "注入并验证成功（同码合并）" : "注入并验证成功"
        Msfx_InsertEvent(taskId, "INJECT", "INFO", msg "，码=" injectCode "，影响明细=" itemCount)
        Msfx_ApplyWarehouseBurstPacing(gIdx, totalGroups)
    }

    if isMinPolicy {
        if (minFirstLeafs.Length > 0) {
            u1 := Msfx_BatchUpdateTaskCodes(taskId, minFirstLeafs, "INJECTED", "FIRST_OK", "")
            if !u1["ok"] {
                Msfx_FinalizeInjectTask(taskId, "MIN_LEVEL 批量更新首组成功状态失败")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u1["err"])
            }
            u2 := Msfx_BatchUpdateStagingCodes(minFirstStaging, "VERIFIED", "")
            if !u2["ok"] {
                Msfx_FinalizeInjectTask(taskId, "MIN_LEVEL 批量更新首组 staging 成功状态失败")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u2["err"])
            }
        }
        if (minSoftLeafs.Length > 0) {
            u1 := Msfx_BatchUpdateTaskCodes(taskId, minSoftLeafs, "INJECTED", "SOFT_OK", "")
            if !u1["ok"] {
                Msfx_FinalizeInjectTask(taskId, "MIN_LEVEL 批量更新后续成功状态失败")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u1["err"])
            }
            u2 := Msfx_BatchUpdateStagingCodes(minSoftStaging, "VERIFIED", "")
            if !u2["ok"] {
                Msfx_FinalizeInjectTask(taskId, "MIN_LEVEL 批量更新后续 staging 成功状态失败")
                return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", u2["err"])
            }
        }
        Msfx_InsertEvent(taskId, "INJECT", "INFO", "MIN_LEVEL 极速注入完成，注入次数=" injectRuns "，成功明细=" succ "，失败明细=" fail)
    }

    finalErr := ""
    if (fail > 0) {
        if (succ = 0)
            finalErr := "全部失败: " fail "/" codeRows.Length
        else
            finalErr := "部分失败: " fail "/" codeRows.Length
    }
    fr := Msfx_FinalizeInjectTask(taskId, finalErr)
    if !fr["ok"]
        return Map("ok", false, "level", "ERR", "type", "[仓库任务错误]", "why", "任务结算失败：`n" fr["why"])

    return Map("ok", true, "type", "[仓库任务完成]", "why", "task_id=" taskId " 注入次数=" injectRuns " 成功=" succ " 失败=" fail)
}

Msfx_ArrayAppend(dst, src) {
    if !IsObject(dst) || !IsObject(src)
        return
    for _, v in src
        dst.Push(v)
}

Msfx_ApplyWarehouseBurstPacing(groupIndex, totalGroups) {
    ; 批量微节拍：每 N 组插入极短让步，降低窗口消息堆积，提升长序列稳定吞吐。
    if (groupIndex >= totalGroups)
        return

    ; 固定节拍（硬编码）。
    burstN := 40
    pauseMs := 2

    if (burstN <= 0 || pauseMs <= 0)
        return
    if (Mod(groupIndex, burstN) = 0)
        Sleep(pauseMs)
}

Msfx_ClaimInjectTasks(clientId, limit := 1) {
    escClient := Util_EscapeSQL(clientId)
    lim := Util_ToInt(limit, 1)
    if (lim <= 0)
        lim := 1

    sql := ""
        . "SELECT task_id, bill_id, source_bill_code, mapped_drug_id, mapped_spec, total_codes "
        . "FROM msfx_claim_inject_tasks('" escClient "', " lim ");"
    r := DB_Query(sql)
    if !r["ok"] {
        return Map("ok", false, "level", "ERR", "type", "[SQL 错误]", "why", r["err"])
    }

    tasks := []
    for _, row in r["rows"] {
        tasks.Push(Map(
            "task_id", Util_ToInt(row[1]),
            "bill_id", Util_ToInt(row[2]),
            "source_bill_code", row[3],
            "mapped_drug_id", row[4],
            "mapped_spec", row[5],
            "total_codes", Util_ToInt(row[6])
        ))
    }

    return Map("ok", true, "tasks", tasks)
}

Msfx_GetPendingTaskCodes(taskId) {
    escTask := Util_ToInt(taskId, 0)
    if (escTask <= 0)
        return Map("ok", false, "level", "ERR", "type", "[SQL 错误]", "why", "task_id 非法")

    codeCol := Msfx_GetTaskCodeColumnName()
    sql := ""
        . "SELECT "
        . "  tc.seq, tc." codeCol ", COALESCE(tc.staging_id, 0), "
        . "  COALESCE(s.source_code_level_1, ''), "
        . "  COALESCE(s.source_code_level_2, ''), "
        . "  COALESCE(s.source_code_level_3, ''), "
        . "  COALESCE(s.source_code_level_4, ''), "
        . "  COALESCE(s.source_code_level_5, '') "
        . "FROM msfx_inject_task_code tc "
        . "LEFT JOIN msfx_code_staging s ON s.id = tc.staging_id "
        . "WHERE tc.task_id = " escTask " "
        . "  AND tc.status = 'PENDING' "
        . "ORDER BY tc.seq, tc." codeCol ";"
    r := DB_Query(sql)
    if !r["ok"] {
        return Map("ok", false, "level", "ERR", "type", "[SQL 错误]", "why", r["err"])
    }

    rows := []
    for _, row in r["rows"] {
        rows.Push(Map(
            "seq", Util_ToInt(row[1]),
            "leaf_code", row[2],
            "staging_id", Util_ToInt(row[3]),
            "l1", row[4],
            "l2", row[5],
            "l3", row[6],
            "l4", row[7],
            "l5", row[8]
        ))
    }
    return Map("ok", true, "rows", rows)
}

Msfx_SelectInjectCode(item, policy := "MAX_LEVEL") {
    pol := StrUpper(Trim(policy))
    if (pol = "MIN_LEVEL") {
        for _, key in ["l1", "l2", "l3", "l4", "l5"] {
            v := item.Has(key) ? Trim(item[key]) : ""
            if (v != "")
                return v
        }
    } else {
        for _, key in ["l5", "l4", "l3", "l2", "l1"] {
            v := item.Has(key) ? Trim(item[key]) : ""
            if (v != "")
                return v
        }
    }
    return item.Has("leaf_code") ? Trim(item["leaf_code"]) : ""
}

Msfx_NormalizeCode(v) {
    s := Trim("" v)
    s := StrReplace(s, "`r", "")
    s := StrReplace(s, "`n", "")
    s := StrReplace(s, " ", "")
    digits := RegExReplace(s, "\D")
    if (digits != "" && StrLen(digits) >= 8)
        s := digits
    return s
}

Msfx_GroupLeafCodes(items) {
    out := []
    seen := Map()
    for _, item in items {
        c := item.Has("leaf_code") ? Msfx_NormalizeCode(item["leaf_code"]) : ""
        if (c = "" || seen.Has(c))
            continue
        seen[c] := true
        out.Push(c)
    }
    return out
}

Msfx_GroupStagingIds(items) {
    out := []
    seen := Map()
    for _, item in items {
        sid := item.Has("staging_id") ? Util_ToInt(item["staging_id"], 0) : 0
        if (sid <= 0 || seen.Has(sid))
            continue
        seen[sid] := true
        out.Push(sid)
    }
    return out
}

Msfx_BatchUpdateTaskCodes(taskId, leafCodes, status, verifyResult := "", errMsg := "") {
    escTask := Util_ToInt(taskId, 0)
    if (escTask <= 0)
        return Map("ok", false, "err", "task_id 非法")
    if !IsObject(leafCodes) || (leafCodes.Length = 0)
        return Map("ok", true)

    codeCol := Msfx_GetTaskCodeColumnName()
    inList := Msfx_SqlListQuoted(leafCodes)
    if (inList = "")
        return Map("ok", true)

    escStatus := Util_EscapeSQL(status)
    escVerify := Util_EscapeSQL(verifyResult)
    escErr := Util_EscapeSQL(errMsg)
    sql := ""
        . "UPDATE msfx_inject_task_code "
        . "SET status='" escStatus "', "
        . "    injected_at = CASE WHEN '" escStatus "' = 'INJECTED' THEN now() ELSE injected_at END, "
        . "    verify_result = NULLIF('" escVerify "', ''), "
        . "    err_msg = NULLIF('" escErr "', '') "
        . "WHERE task_id=" escTask " AND " codeCol " IN (" inList ");"
    return DB_Exec(sql)
}

Msfx_BatchUpdateStagingCodes(stagingIds, codeStatus, errMsg := "") {
    if !IsObject(stagingIds) || (stagingIds.Length = 0)
        return Map("ok", true)

    inList := Msfx_SqlListInts(stagingIds)
    if (inList = "")
        return Map("ok", true)

    escStatus := Util_EscapeSQL(codeStatus)
    escErr := Util_EscapeSQL(errMsg)
    sql := ""
        . "UPDATE msfx_code_staging "
        . "SET code_status='" escStatus "', "
        . "    verified_at = CASE WHEN '" escStatus "' = 'VERIFIED' THEN now() ELSE verified_at END, "
        . "    err_msg = NULLIF('" escErr "', ''), "
        . "    updated_at = now() "
        . "WHERE id IN (" inList ");"
    return DB_Exec(sql)
}

Msfx_SqlListQuoted(items) {
    out := []
    seen := Map()
    for _, x in items {
        v := Msfx_NormalizeCode(x)
        if (v = "" || seen.Has(v))
            continue
        seen[v] := true
        out.Push("'" Util_EscapeSQL(v) "'")
    }
    return out.Length > 0 ? Msfx_Join(out, ",") : ""
}

Msfx_SqlListInts(items) {
    out := []
    seen := Map()
    for _, x in items {
        n := Util_ToInt(x, 0)
        if (n <= 0 || seen.Has(n))
            continue
        seen[n] := true
        out.Push(n)
    }
    return out.Length > 0 ? Msfx_Join(out, ",") : ""
}

Msfx_Join(arr, sep := ",") {
    if !IsObject(arr) || (arr.Length = 0)
        return ""
    s := ""
    for i, v in arr
        s .= (i = 1 ? "" : sep) v
    return s
}

Msfx_UpdateTaskCode(taskId, leafCode, status, verifyResult := "", errMsg := "") {
    escTask := Util_ToInt(taskId, 0)
    escLeaf := Util_EscapeSQL(leafCode)
    escStatus := Util_EscapeSQL(status)
    escVerify := Util_EscapeSQL(verifyResult)
    escErr := Util_EscapeSQL(errMsg)

    codeCol := Msfx_GetTaskCodeColumnName()
    sql := ""
        . "UPDATE msfx_inject_task_code "
        . "SET status='" escStatus "', "
        . "    injected_at = CASE WHEN '" escStatus "' = 'INJECTED' THEN now() ELSE injected_at END, "
        . "    verify_result = NULLIF('" escVerify "', ''), "
        . "    err_msg = NULLIF('" escErr "', '') "
        . "WHERE task_id=" escTask " AND " codeCol "='" escLeaf "';"
    return DB_Exec(sql)
}

Msfx_UpdateStagingCodeStatus(stagingId, codeStatus, errMsg := "") {
    sid := Util_ToInt(stagingId, 0)
    if (sid <= 0)
        return Map("ok", true)

    escStatus := Util_EscapeSQL(codeStatus)
    escErr := Util_EscapeSQL(errMsg)
    sql := ""
        . "UPDATE msfx_code_staging "
        . "SET code_status='" escStatus "', "
        . "    verified_at = CASE WHEN '" escStatus "' = 'VERIFIED' THEN now() ELSE verified_at END, "
        . "    err_msg = NULLIF('" escErr "', ''), "
        . "    updated_at = now() "
        . "WHERE id=" sid ";"
    return DB_Exec(sql)
}

Msfx_InsertEvent(taskId, stage, level, msg, leafCode := "") {
    tid := Util_ToInt(taskId, 0)
    if (tid <= 0)
        return Map("ok", false, "level", "ERR", "type", "[SQL 错误]", "why", "task_id 非法")

    escStage := Util_EscapeSQL(stage)
    escLevel := Util_EscapeSQL(level)
    escMsg := Util_EscapeSQL(msg)
    escLeaf := Util_EscapeSQL(leafCode)

    codeCol := Msfx_GetEventCodeColumnName()
    sql := ""
        . "INSERT INTO msfx_inject_event(task_id, " codeCol ", stage, level, message) "
        . "VALUES ("
        . tid ", "
        . "NULLIF('" escLeaf "', ''), "
        . "'" escStage "', "
        . "'" escLevel "', "
        . "'" escMsg "'"
        . ");"
    return DB_Exec(sql)
}

Msfx_FinalizeInjectTask(taskId, errMsg := "") {
    tid := Util_ToInt(taskId, 0)
    if (tid <= 0)
        return Map("ok", false, "level", "ERR", "type", "[SQL 错误]", "why", "task_id 非法")

    escErr := Util_EscapeSQL(errMsg)
    sql := ""
        . "SELECT task_id, task_status, success_codes, failed_codes, total_codes "
        . "FROM msfx_finalize_inject_task(" tid ", NULLIF('" escErr "', ''));"
    r := DB_Query(sql)
    if !r["ok"]
        return Map("ok", false, "level", "ERR", "type", "[SQL 错误]", "why", r["err"])
    return Map("ok", true, "rows", r["rows"])
}

Msfx_GetTaskCodeColumnName() {
    global __MSFX_COL
    if (__MSFX_COL.Has("task_code_col"))
        return __MSFX_COL["task_code_col"]

    col := Msfx_DetectColumn("msfx_inject_task_code", "leaf_code") ? "leaf_code" : "trace_code"
    __MSFX_COL["task_code_col"] := col
    return col
}

Msfx_GetEventCodeColumnName() {
    global __MSFX_COL
    if (__MSFX_COL.Has("event_code_col"))
        return __MSFX_COL["event_code_col"]

    col := Msfx_DetectColumn("msfx_inject_event", "leaf_code") ? "leaf_code" : "trace_code"
    __MSFX_COL["event_code_col"] := col
    return col
}

Msfx_DetectColumn(tableName, colName) {
    escTable := Util_EscapeSQL(tableName)
    escCol := Util_EscapeSQL(colName)
    sql := ""
        . "SELECT 1 "
        . "FROM information_schema.columns "
        . "WHERE table_schema='public' "
        . "  AND table_name='" escTable "' "
        . "  AND column_name='" escCol "' "
        . "LIMIT 1;"
    r := DB_Query(sql)
    return (r["ok"] && r["rows"].Length > 0)
}
