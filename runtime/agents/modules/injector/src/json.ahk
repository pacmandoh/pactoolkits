; 最小 JSON 解析器；失败时返回空 Map，避免异常中断注入主流程

Json_Parse(jsonText) {
    pos := 1
    value := Json__ParseValue(jsonText, &pos)
    if !value["ok"]
        return value

    ws := Json__SkipWs(jsonText, pos)
    pos := ws["pos"]
    if (pos <= StrLen(jsonText))
        return Json__Fail("JSON 解析失败：尾部存在多余字符（位置 " pos "）")

    return Json__Ok(value["val"])
}

Json_ReadFile(path) {
    txt := ""
    try txt := FileRead(path, "UTF-8")
    catch as e
        return Json__Fail("读取 JSON 文件失败：`n" e.Message)

    if (SubStr(txt, 1, 1) = Chr(0xFEFF))
        txt := SubStr(txt, 2)

    return Json_Parse(txt)
}

Json__ParseValue(s, &pos) {
    ws := Json__SkipWs(s, pos)
    pos := ws["pos"]
    if (pos > StrLen(s))
        return Json__Fail("JSON 解析失败：意外结束")

    ch := SubStr(s, pos, 1)
    if (ch = "{")
        return Json__ParseObject(s, &pos)
    if (ch = "[")
        return Json__ParseArray(s, &pos)
    if (ch = '"')
        return Json__ParseString(s, &pos)
    if (ch = "-" || RegExMatch(ch, "^\d$"))
        return Json__ParseNumber(s, &pos)
    if (SubStr(s, pos, 4) = "true") {
        pos += 4
        return Json__Ok(true)
    }
    if (SubStr(s, pos, 5) = "false") {
        pos += 5
        return Json__Ok(false)
    }
    if (SubStr(s, pos, 4) = "null") {
        pos += 4
        return Json__Ok("")
    }
    return Json__Fail("JSON 解析失败：非法值（位置 " pos "）")
}

Json__ParseObject(s, &pos) {
    obj := Map()
    pos++ ; 消费 '{'

    ws := Json__SkipWs(s, pos)
    pos := ws["pos"]
    if (SubStr(s, pos, 1) = "}") {
        pos++
        return Json__Ok(obj)
    }

    loop {
        ws2 := Json__SkipWs(s, pos)
        pos := ws2["pos"]
        if (SubStr(s, pos, 1) != '"')
            return Json__Fail("JSON 解析失败：对象键必须是字符串（位置 " pos "）")

        keyRes := Json__ParseString(s, &pos)
        if !keyRes["ok"]
            return keyRes
        key := keyRes["val"]

        ws3 := Json__SkipWs(s, pos)
        pos := ws3["pos"]
        if (SubStr(s, pos, 1) != ":")
            return Json__Fail("JSON 解析失败：缺少 ':'（位置 " pos "）")
        pos++

        valRes := Json__ParseValue(s, &pos)
        if !valRes["ok"]
            return valRes
        obj[key] := valRes["val"]

        ws4 := Json__SkipWs(s, pos)
        pos := ws4["pos"]
        ch := SubStr(s, pos, 1)
        if (ch = "}") {
            pos++
            break
        }
        if (ch != ",")
            return Json__Fail("JSON 解析失败：对象缺少 ',' 或 '}'（位置 " pos "）")
        pos++
    }

    return Json__Ok(obj)
}

Json__ParseArray(s, &pos) {
    arr := []
    pos++ ; 消费 '['

    ws := Json__SkipWs(s, pos)
    pos := ws["pos"]
    if (SubStr(s, pos, 1) = "]") {
        pos++
        return Json__Ok(arr)
    }

    loop {
        valRes := Json__ParseValue(s, &pos)
        if !valRes["ok"]
            return valRes
        arr.Push(valRes["val"])

        ws2 := Json__SkipWs(s, pos)
        pos := ws2["pos"]
        ch := SubStr(s, pos, 1)
        if (ch = "]") {
            pos++
            break
        }
        if (ch != ",")
            return Json__Fail("JSON 解析失败：数组缺少 ',' 或 ']'（位置 " pos "）")
        pos++
    }

    return Json__Ok(arr)
}

Json__ParseString(s, &pos) {
    if (SubStr(s, pos, 1) != '"')
        return Json__Fail("JSON 解析失败：字符串必须以双引号开始（位置 " pos "）")
    pos++
    out := ""

    loop {
        if (pos > StrLen(s))
            return Json__Fail("JSON 解析失败：字符串未闭合")
        ch := SubStr(s, pos, 1)
        pos++

        if (ch = '"')
            break

        if (ch = "\") {
            if (pos > StrLen(s))
                return Json__Fail("JSON 解析失败：无效转义")
            esc := SubStr(s, pos, 1)
            pos++
            switch esc {
                case '"', "\", "/":
                    out .= esc
                case "b":
                    out .= Chr(8)
                case "f":
                    out .= Chr(12)
                case "n":
                    out .= "`n"
                case "r":
                    out .= "`r"
                case "t":
                    out .= "`t"
                case "u":
                    hex := SubStr(s, pos, 4)
                    if !RegExMatch(hex, "^[0-9A-Fa-f]{4}$")
                        return Json__Fail("JSON 解析失败：无效 \\u 转义（位置 " pos "）")
                    out .= Chr(Integer("0x" hex))
                    pos += 4
                default:
                    return Json__Fail("JSON 解析失败：未知转义字符 '\\" esc "'")
            }
            continue
        }

        out .= ch
    }

    return Json__Ok(out)
}

Json__ParseNumber(s, &pos) {
    text := SubStr(s, pos)
    if !RegExMatch(text, "^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?", &m)
        return Json__Fail("JSON 解析失败：无效数字（位置 " pos "）")

    lit := m[0]
    pos += StrLen(lit)
    return Json__Ok(lit + 0)
}

Json__SkipWs(s, pos) {
    len := StrLen(s)
    while (pos <= len) {
        ch := SubStr(s, pos, 1)
        if (ch = " " || ch = "`t" || ch = "`n" || ch = "`r")
            pos++
        else
            break
    }
    return Map("pos", pos)
}

Json__Ok(val) {
    return Map("ok", true, "val", val)
}

Json__Fail(err) {
    return Map("ok", false, "err", Trim("" err))
}
