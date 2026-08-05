; 从目标窗口网格的剪贴板文本解析表头和首条有效药品数据
; colFields：Array of Map(id, headers[], required, asInt)；bySpec 键为 id

Parse_TargetInfo(colFields, ipt, text := "", win := "A", parseGridClassNN := "", quiet := false) {
	if !IsObject(colFields)
		colFields := []

	win := Util_NormalizeWin(win)
	t0 := A_TickCount

	; 优先使用调用方提供的文本，避免重复复制操作覆盖用户剪贴板
	copied := false
	if (Trim(text) = "") {
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

	fields := Parse_NormalizeColFields(colFields)
	if (fields.Length = 0) {
		return Map(
			"ok", false, "level", "Warn", "message", "[解析错误] 列映射为空",
			"reason", "ColFields empty", "raw", txt, "copied", copied
		)
	}

	lines := StrSplit(txt, "`n")
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
			for _, field in fields {
				id := field["id"]
				if hit.Has(id)
					continue
				if Parse_MatchHeader(c, field["headers"])
					hit[id] := Map("idx", j, "hdr", c, "asInt", field["asInt"])
			}
		}

		allFound := true
		for _, field in fields {
			if !field["required"]
				continue
			if !hit.Has(field["id"]) {
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
	for id, m in hit
		hitCols.Push(id "@" m["idx"] "=" m["hdr"])
	if !quiet
		Log_Debug("parse.header_ok", "表头命中", Map(
			"hdrIdx", hdrIdx, "hit", hitCols.Length, "cols", hitCols, "scannedHdr", scannedHdr
		))

	data := Map()
	bySpec := Map()
	k := hdrIdx + 1
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

		for id, m in hit {
			hdr := m["hdr"]
			v := Trim(cols[m["idx"]])

			if m["asInt"] {
				if RegExMatch(v, "^\d+$")
					v := Util_ToInt(v)
				else
					v := 0
			}

			data[hdr] := v
			bySpec[id] := v
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

; 将配置中的列对象规范为 Array of Map(id, headers, required, asInt)
Parse_NormalizeColFields(raw) {
	out := []
	if !(IsObject(raw) && Type(raw) = "Array")
		return out
	for _, item in raw {
		f := Parse_NormalizeOneColField(item)
		if IsObject(f)
			out.Push(f)
	}
	return out
}

Parse_NormalizeOneColField(item) {
	if !IsObject(item)
		return 0
	if (Type(item) != "Map")
		return 0

	id := item.Has("id") ? Trim("" item["id"]) : ""
	if (id = "")
		return 0

	headers := []
	if item.Has("headers") && Type(item["headers"]) = "Array" {
		for _, h in item["headers"] {
			t := Trim("" h)
			if (t != "")
				headers.Push(t)
		}
	}
	if (headers.Length = 0)
		return 0

	required := true
	if item.Has("required")
		required := !!Util_ToBool(item["required"])

	asInt := false
	if item.Has("asInt")
		asInt := !!Util_ToBool(item["asInt"])

	return Map("id", id, "headers", headers, "required", required, "asInt", asInt)
}

Parse_MatchHeader(text, headers) {
	t := Trim("" text)
	for _, h in headers {
		if (t = Trim("" h))
			return true
	}
	return false
}

By_Get(by, id, default := "") {
	if !IsObject(by)
		return default
	id := Trim("" id)
	if (id = "" || !by.Has(id))
		return default
	return by[id]
}
