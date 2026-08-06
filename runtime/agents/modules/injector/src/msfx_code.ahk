; 追溯码策略：取码、归一、分组
Msfx_ArrayAppend(dst, src) {
	if !IsObject(dst) || !IsObject(src)
		return
	for _, v in src
		dst.Push(v)
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
