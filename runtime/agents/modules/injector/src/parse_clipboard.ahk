; 从目标窗口网格的剪贴板文本解析表头和首条有效药品数据

Parse_TargetInfo(colSpecs, ipt, intCols := 0, text := "", win := "A", parseGridClassNN := "", quiet := false) {
	if !IsObject(intCols)
		intCols := []

	win := Util_NormalizeWin(win)
	t0 := A_TickCount

	IsOpt(spec) => (SubStr(spec, 1, 1) = "?")
	Norm(spec) => IsOpt(spec) ? Trim(SubStr(spec, 2)) : spec

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
		; 解析网格须 FocusGrid（类序 HWND），勿走精确 ClassNN ControlFocus
		winCls := ""
		try winCls := WinGetClass(win)
		if (Trim(parseGridClassNN) != "") {
			if !quiet
				Log_Debug("parse.focus_grid", "解析前聚焦网格", Map(
					"parseNn", parseGridClassNN, "cls", winCls, "ipt", winCls = ipt
				))
			hwndGrid := UI_FocusGridClassNN(parseGridClassNN, win)
			if !hwndGrid && !quiet
				Log_Debug("parse.focus_grid_fail", "解析前网格聚焦失败，仍尝试 ^c", Map(
					"parseNn", parseGridClassNN, "cls", winCls
				))
		}

		WinActivate(win)
		WinWaitActive(win, , 1)

		oldClip := ClipboardAll()
		try {
			A_Clipboard := ""
			SendInput "^c"
			if !ClipWait(1) {
				Log_Debug("parse.clip_timeout", "等待剪贴板超时", Map(
					"winClass", winCls,
					"parseNn", parseGridClassNN,
					"elapsedMs", A_TickCount - t0
				))
				return Map(
					"ok", false, "level", "Warn", "message", "[解析错误] 等待超时：请确认是否选中列表中相应药品",
					"reason", "ClipWait timeout"
				)
			}
			text := A_Clipboard
		} finally {
			try A_Clipboard := oldClip
		}
		copied := true
		if !quiet
			Log_Debug("parse.clip_ok", "剪贴板已取", Map(
				"rawLen", StrLen(text), "parseNn", parseGridClassNN, "elapsedMs", A_TickCount - t0
			))
	}

	txt := Trim(text)
	if !quiet
		Log_Debug("parse.begin", "开始解析网格文本", Map(
			"copied", copied,
			"rawLen", StrLen(txt),
			"lines", StrSplit(txt, "`n").Length,
			"winClass", WinGetClass(win),
			"parseNn", parseGridClassNN
		))
	if (txt = "") {
		if !quiet
			Log_Debug("parse.empty", "选中内容为空", Map("copied", copied, "elapsedMs", A_TickCount - t0))
		return Map(
			"ok", false, "level", "Warn", "message", "[解析错误] 选中内容为空",
			"reason", "Text is empty", "raw", text, "copied", copied
		)
	}

	lines := StrSplit(txt, "`n")

	; 命中结果保留列索引和实际表头，供后续错误信息定位
	hit := Map()
	hdrIdx := 0
	scannedHdr := 0

	for i, line in lines {
		line := Trim(line, "`r")
		if (line = "")
			continue

		cols := StrSplit(line, "`t")
		if (cols.Length < 2)
			continue

		scannedHdr++
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
		if !quiet
			Log_Debug("parse.header_miss", "未找到表头", Map(
				"rawLen", StrLen(txt),
				"scannedHdr", scannedHdr,
				"elapsedMs", A_TickCount - t0
			))
		return Map(
			"ok", false, "level", "Warn", "message", "[解析错误] 未找到相对应表头",
			"reason", "Header not found", "raw", txt, "copied", copied
		)
	}

	hitCols := []
	for spec, m in hit
		hitCols.Push(spec "@" m["idx"] "=" m["hdr"])
	if !quiet
		Log_Debug("parse.header_ok", "表头命中", Map(
			"hdrIdx", hdrIdx, "hit", hitCols.Length, "cols", hitCols, "scannedHdr", scannedHdr
		))

	data := Map()  ; key=实际表头名
	bySpec := Map() ; key=去掉 ? 后的规范 spec（可含 || 别名）
	k := hdrIdx + 1 ; 表头后第一行起找首条有效数据
	skippedShort := 0

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
			skippedShort++
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
		if !quiet
			Log_Debug("parse.row_miss", "未找到数据行", Map(
				"hdrIdx", hdrIdx, "rawLen", StrLen(txt),
				"skippedShort", skippedShort, "elapsedMs", A_TickCount - t0
			))
		return Map(
			"ok", false, "level", "Warn", "message", "[解析错误] 未找到数据行",
			"reason", "Row not found", "raw", txt, "copied", copied
		)
	}

	msg := ""
	for hdr, v in data
		msg .= "`n" hdr "=" v

	if !quiet
		Log_Debug("parse.ok", "解析完成", Map(
			"hdrIdx", hdrIdx, "fields", data.Count, "copied", copied,
			"skippedShort", skippedShort, "elapsedMs", A_TickCount - t0
		))
	return Map(
		"ok", true,
		"level", "Info",
		"message", "[解析成功]" msg,
		"data", data,
		"bySpec", bySpec,
		"raw", txt,
		"hdrIdx", hdrIdx,
		"copied", copied
	)
}
