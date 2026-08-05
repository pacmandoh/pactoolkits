; 半自动取码计划：整盒数 + 拆零粒（rem 只注拆零；full 整盒+拆零）
; injectMode: "rem" | "full"

; 读 drug_index 单盒数量
Txn_FetchDbQty(drugId, spec) {
	q := ""
		. "SELECT qty FROM drug_index "
		. "WHERE drug_id='" Util_EscapeSQL(drugId) "' "
		. "  AND spec='" Util_EscapeSQL(spec) "' "
		. "LIMIT 1;"
	rr := DB_Query(q)
	if !rr["ok"]
		return Map("ok", false, "level", "Error",
			"message", "[查询错误]`n读取药品索引中单盒数量失败：`n" (rr.Has("err") ? rr["err"] : ""),
			"err", rr.Has("err") ? rr["err"] : "")
	if (rr["rows"].Length = 0)
		return Map("ok", false, "level", "Warn",
			"message", "[查询错误]`n药品索引未配置该药品规格（无法计算拆零余数）`n药品=" drugId "`n规格=" spec)
	dbQty := Util_ToInt(rr["rows"][1][1])
	if (dbQty <= 0)
		return Map("ok", false, "level", "Error",
			"message", "[查询错误]`n药品索引中单盒数量非法：" dbQty)
	return Map("ok", true, "dbQty", dbQty)
}

; 由行字段 + 模式算出 wholePick / remNeed
; alreadyScanned：门诊已扫码数；住院传 0
Txn_PlanPick(by, injectMode, isOpt, drugId, spec, alreadyScanned := 0) {
	if !IsObject(by)
		return Map("ok", false, "level", "Warn", "message", "[解析错误] 缺少行字段")

	injectMode := StrLower(Trim("" injectMode))
	if (injectMode != "full")
		injectMode := "rem"

	splitFlag := Trim("" By_Get(by, "splitFlag"))
	qtyVal := By_Get(by, "qty")
	unit := Trim("" By_Get(by, "unit"))
	doseUnit := Trim("" By_Get(by, "doseUnit"))
	qtyN := Util_ToInt(qtyVal, 0)
	scannedN := Util_ToInt(alreadyScanned, 0)

	Log_Debug("txn.plan.begin", "取码计划", Map(
		"injectMode", injectMode, "isOpt", isOpt, "split", splitFlag,
		"qty", qtyVal, "unit", unit, "doseUnit", doseUnit, "alreadyScanned", scannedN
	))

	if (qtyN <= 0)
		return Map("ok", false, "level", "Warn", "message", "[解析错误] 数量无效：" qtyVal)

	; packWhole：数量按整包装计（不做 qty//dbQty）
	packWhole := false
	if isOpt {
		if (unit = "" || doseUnit = "")
			return Map(
				"ok", false, "level", "Warn",
				"message", "[解析错误]`n门诊计算整盒/拆零需要「单位」与「用量单位」`n单位=" unit "`n用量单位=" doseUnit
			)
		if (unit != doseUnit)
			packWhole := true
	} else if (splitFlag = "否") {
		packWhole := true
	}

	planWhole := 0
	planRem := 0
	dbQty := 0

	if packWhole {
		if (injectMode = "rem") {
			msg := isOpt
				? "[跳过取码]`n整包装（发药单位与用量单位不同，按整包装发药）`n单位=" unit "`n用量单位=" doseUnit "`n数量=" qtyN
				: "[跳过取码] 未拆零药物"
			Log_Debug("txn.plan.skip", "rem 整包装跳过", Map("packWhole", true, "qty", qtyN))
			return Map("ok", true, "skip", true, "level", "Info", "message", msg,
				"wholePick", 0, "remNeed", 0, "planWhole", qtyN, "planRem", 0, "dbQty", 0, "packWhole", true)
		}
		planWhole := qtyN
		planRem := 0
	} else {
		db := Txn_FetchDbQty(drugId, spec)
		if !db["ok"]
			return db
		dbQty := db["dbQty"]
		planWhole := qtyN // dbQty
		planRem := Mod(qtyN, dbQty)
		Log_Debug("txn.plan.split", "拆零整除", Map(
			"qty", qtyN, "dbQty", dbQty, "planWhole", planWhole, "planRem", planRem
		))

		if (injectMode = "rem") {
			if (planRem = 0) {
				return Map("ok", true, "skip", true, "level", "Info",
					"message", "[跳过取码] 整包装（数量为整包整数倍，单盒数量=" dbQty "）",
					"wholePick", 0, "remNeed", 0, "planWhole", planWhole, "planRem", 0,
					"dbQty", dbQty, "packWhole", false, "qty", qtyN)
			}
			; rem 只预留余数；门诊整盒手扫优先
			if isOpt {
				splitScanned := Max(0, scannedN - planWhole)
				if (splitScanned >= 1) {
					return Map("ok", true, "skip", true, "level", "Info",
						"message", "[跳过取码] 拆零余数已有已扫记录，无需重复注入",
						"wholePick", 0, "remNeed", 0, "planWhole", planWhole, "planRem", planRem,
						"dbQty", dbQty, "packWhole", false, "already_scanned", scannedN)
				}
				if (planWhole > 0 && scannedN < planWhole) {
					return Map("ok", false, "level", "Warn",
						"message", "[取码提示]`n请先手动扫完整盒追溯码，再注入拆零余数`n已扫=" scannedN "`n整盒=" planWhole,
						"reason", "WHOLE_BOX_PENDING",
						"wholePick", 0, "remNeed", 0, "planWhole", planWhole, "planRem", planRem,
						"dbQty", dbQty, "packWhole", false, "already_scanned", scannedN)
				}
			}
			return Map(
				"ok", true, "skip", false, "level", "Info",
				"wholePick", 0, "remNeed", planRem,
				"planWhole", planWhole, "planRem", planRem, "dbQty", dbQty,
				"packWhole", false, "qty", qtyN
			)
		}
	}

	; full：按已扫减量；已扫超过整盒视作拆零侧已有进度
	wholePick := Max(0, planWhole - Min(scannedN, planWhole))
	remNeed := 0
	if (planRem > 0 && scannedN <= planWhole)
		remNeed := planRem

	if (wholePick = 0 && remNeed = 0) {
		Log_Debug("txn.plan.skip", "full 已扫足跳过", Map(
			"planWhole", planWhole, "planRem", planRem, "alreadyScanned", scannedN
		))
		return Map("ok", true, "skip", true, "level", "Info",
			"message", "[跳过取码] 全量目标码已覆盖，无需重复注入",
			"wholePick", 0, "remNeed", 0, "planWhole", planWhole, "planRem", planRem,
			"dbQty", dbQty, "packWhole", packWhole, "already_scanned", scannedN)
	}

	Log_Debug("txn.plan.ok", "计划完成", Map(
		"injectMode", injectMode, "packWhole", packWhole,
		"planWhole", planWhole, "planRem", planRem,
		"wholePick", wholePick, "remNeed", remNeed, "dbQty", dbQty
	))
	return Map(
		"ok", true, "skip", false, "level", "Info",
		"wholePick", wholePick, "remNeed", remNeed,
		"planWhole", planWhole, "planRem", planRem, "dbQty", dbQty,
		"packWhole", packWhole, "qty", qtyN
	)
}
