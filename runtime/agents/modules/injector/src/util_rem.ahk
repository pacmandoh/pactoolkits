; 门诊拆零「已注入余数」闩锁（内存+落盘）
; key = 药|规|量|库存快照：本单库存中途不变，发药后扣减，降低跨方同药同量误锁
; 无可用库存字段时不上闩（返回空 key），避免退回三元组永久误伤
global __OptRemDone := Map()
global __OptRemDoneLoaded := false

Util_OptRemNormStock(stock) {
	s := Trim("" stock)
	if (s = "")
		return ""
	; 去空白，统一小数点，避免「41.4667」与「41.4667 」不一致
	s := RegExReplace(s, "[\s,]", "")
	s := StrReplace(s, "．", ".")
	return s
}

; 从 parse bySpec 取库存快照（ColSpecs 建议含 ?库存数量）
Util_OptRemStockFromBy(bySpec) {
	if !IsObject(bySpec)
		return ""
	for _, spec in ["库存数量", "库存数量||库存", "库存", "可用库存"] {
		if bySpec.Has(spec) {
			s := Util_OptRemNormStock(bySpec[spec])
			if (s != "")
				return s
		}
	}
	return ""
}

; 空库存 → 空 key（Has/Set/Clear 均为 no-op）
Util_OptRemKey(drugId, spec, qtyN, stock := "") {
	st := Util_OptRemNormStock(stock)
	if (st = "")
		return ""
	return Trim("" drugId) "|" Trim("" spec) "|" (Util_ToInt(qtyN, 0) + 0) "|" st
}

Util_OptRemDone_Path() {
	dir := A_AppData "\PacToolkits\state\injector"
	try DirCreate(dir)
	catch {
	}
	return dir "\opt-rem.latch"
}

Util_OptRemDone_EnsureLoaded() {
	global __OptRemDone, __OptRemDoneLoaded
	if __OptRemDoneLoaded
		return
	__OptRemDoneLoaded := true
	path := Util_OptRemDone_Path()
	if !FileExist(path)
		return
	try {
		for line in StrSplit(FileRead(path, "UTF-8"), "`n", "`r") {
			k := Trim(line)
			if (k = "")
				continue
			; 仅加载含库存指纹的 key（段数≥4），丢弃旧三元组以免无库存误锁
			if (StrSplit(k, "|").Length < 4)
				continue
			__OptRemDone[k] := 1
		}
	} catch {
	}
}

Util_OptRemDone_Flush() {
	global __OptRemDone
	path := Util_OptRemDone_Path()
	body := ""
	for k, _ in __OptRemDone
		body .= k "`n"
	try FileDelete(path)
	catch {
	}
	if (body = "")
		return
	try FileAppend(body, path, "UTF-8")
	catch {
	}
}

Util_OptRemDone_Has(key) {
	global __OptRemDone
	if (key = "")
		return false
	Util_OptRemDone_EnsureLoaded()
	return __OptRemDone.Has(key)
}

Util_OptRemDone_Set(key) {
	global __OptRemDone
	if (key = "")
		return
	Util_OptRemDone_EnsureLoaded()
	__OptRemDone[key] := A_TickCount
	Util_OptRemDone_Flush()
}

Util_OptRemDone_Clear(key) {
	global __OptRemDone
	if (key = "")
		return
	Util_OptRemDone_EnsureLoaded()
	if __OptRemDone.Has(key) {
		__OptRemDone.Delete(key)
		Util_OptRemDone_Flush()
	}
}
