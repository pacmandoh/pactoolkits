; 从目标窗口网格的剪贴板文本解析表头和首条有效药品数据

Parse_TargetInfo(colSpecs, ipt, intCols := 0, text := "", win := "A", parseGridClassNN := "") {
    if !IsObject(intCols)
        intCols := []

    win := Util_NormalizeWin(win)

    IsOpt(spec) => (SubStr(spec, 1, 1) = "?")
    Norm(spec)  => IsOpt(spec) ? Trim(SubStr(spec, 2)) : spec

    MatchAlias(text, specExpr) {
        for _, a in StrSplit(specExpr, "||") {
            if (Trim(text) = Trim(a))
                return true
        }
        return false
    }

    IsIntSpec(specExpr) {
        for _, s in intCols {
            if (Norm(s) = specExpr)
                return true
        }
        return false
    }

    ; 优先使用调用方提供的文本，避免重复复制操作覆盖用户剪贴板
    copied := false
	if (Trim(text) = "") {
		; 住院网格支持自动聚焦选中，不要求用户预先双击
		if (WinGetClass(win) = ipt) {
            if (Trim(parseGridClassNN) != "")
			    UI_FocusGridClassNN(parseGridClassNN, win)
		}
		
        WinActivate(win)
        WinWaitActive(win, , 1)

		oldClip := ClipboardAll()
		try {
			A_Clipboard := ""
			SendInput "^c"
			if !ClipWait(1) {
				return Map(
					"ok", false, "level", "WARN", "type", "[解析错误]",
					"why", "等待超时：`n - 请确认是否选中列表中相应药品",
					"reason", "ClipWait timeout"
				)
			}
			text := A_Clipboard
		} finally {
			try A_Clipboard := oldClip
		}
		copied := true
    }

    txt := Trim(text)
    if (txt = "") {
        return Map(
			"ok", false, "level", "WARN", "type", "[解析错误]", "why", "选中内容为空", 
			"reason", "Text is empty", "raw", text, "copied", copied
		)
    }

    lines := StrSplit(txt, "`n")

    ; 命中结果保留列索引和实际表头，供后续错误信息定位
    hit := Map()
    hdrIdx := 0

    for i, line in lines {
        line := Trim(line, "`r")
        if (line = "")
            continue

        cols := StrSplit(line, "`t")
        if (cols.Length < 2)
            continue

        hit.Clear()

        for j, c in cols {
            c := Trim(c)
            for _, rawSpec in colSpecs {
                spec := Norm(rawSpec)
                if hit.Has(spec)
                    continue
                if MatchAlias(c, spec)
                    hit[spec] := Map("idx", j, "hdr", c)
            }
        }

        ; 未使用问号前缀的列为必填列，缺少任一列时继续检查下一候选表头
        allFound := true
        for _, rawSpec in colSpecs {
            if IsOpt(rawSpec)
                continue
            spec := Norm(rawSpec)
            if !hit.Has(spec) {
                allFound := false
                break
            }
        }

        if allFound {
            hdrIdx := i
            break
        }
    }

    if (!hdrIdx) {
        return Map(
			"ok", false, "level", "WARN", "type", "[解析错误]", "why", "未找到相对应表头", 
			"reason", "Header not found", "raw", txt, "copied", copied
		)
    }

    data  := Map()  ; key=实际表头名
    bySpec := Map() ; key=去掉 ? 后的规范 spec（可含 || 别名）
    k := hdrIdx + 1 ; 表头后第一行起找首条有效数据

    while (k <= lines.Length) {
        line := Trim(lines[k], "`r")
        if (line = "") {
            k++
            continue
        }

        cols := StrSplit(line, "`t")

        maxCol := 0
        for _, m in hit
            if (m["idx"] > maxCol)
                maxCol := m["idx"]

        if (cols.Length < maxCol) {
            k++
            continue
        }

        data.Clear(), bySpec.Clear()

        for spec, m in hit {
            hdr := m["hdr"]
            v := Trim(cols[m["idx"]])

            if IsIntSpec(spec) {
                if RegExMatch(v, "^\d+$")
                    v := Util_ToInt(v)
                else
                    v := 0
            }

            data[hdr] := v
            bySpec[spec] := v
        }

        break
    }

    if (data.Count = 0) {
        return Map(
			"ok", false, "level", "WARN", "type", "[解析错误]", "why", "未找到数据行", 
			"reason", "Row not found", "raw", txt, "copied", copied
		)
    }

    msg := ""
    for hdr, v in data
        msg .= "`n" hdr "=" v

    return Map(
        "ok", true,
		"type", "[解析成功]",
		"why", msg,
        "data", data,
        "bySpec", bySpec,
        "raw", txt,
        "hdrIdx", hdrIdx,
        "copied", copied
    )
}
