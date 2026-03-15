; ================== 解析模块 ==================

Parse_TargetInfo(colSpecs, ipt, intCols := 0, text := "", win := "A", parseGridClassNN := "") {
    if !IsObject(intCols)
        intCols := []

    win := Util_NormalizeWin(win)

    ; --- 内联小工具 ---
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

    ; --- 获取待解析文本：优先用传入 text，否则走复制 ---
    copied := false
    if (Trim(text) = "") {
		; FEAT: 住院窗口自动选中，不用双击
		if (WinGetClass(win) = ipt) {
            if (Trim(parseGridClassNN) != "")
			    UI_FocusClassNN(parseGridClassNN, win)
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

    ; hit: specExpr -> Map("idx", colIndex, "hdr", actualHeader)
    hit := Map()
    hdrIdx := 0

    ; 1) 找表头（Tab 分隔）
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

        ; 必选列（没有 ? 前缀）必须都命中
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

    ; 2) 找数据行
    data  := Map()  ; key=实际表头名
    bySpec := Map() ; key=spec表达式（去掉 ? 后的规范形式）
    k := hdrIdx + 1 ; 表头后一行起，找第一条有效

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

    ; 3) 返回解析结果
    msg := ""
    for hdr, v in data
        msg .= "`n" hdr "=" v
    ; UI_Tip(msg, 1500)

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
