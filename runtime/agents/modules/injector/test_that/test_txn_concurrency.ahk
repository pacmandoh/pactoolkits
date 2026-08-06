#Requires AutoHotkey v2.0
#SingleInstance Force
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\src\pg_exec.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"
#Include "%A_ScriptDir%\..\src\db_txn.ahk"

global Cfg := IsSet(Cfg) && IsObject(Cfg) ? Cfg : Map()
Cfg := Util_LoadDotEnv(A_ScriptDir "\..\.env.local")

global TEST_DRUG := "盐酸氨基葡萄糖胶囊"
global TEST_SPEC := "0.75g*60粒"
global TEST_CLIENT := A_ComputerName "\" A_UserName

global WORKERS := 16
global LOOPS := 80
global MAX_NEED := 5
global JITTER_MS := 40
global MODE := "COMMIT"   ; COMMIT / ROLLBACK
global START_BARRIER_MS := 300   ; 启动栅栏：让子进程尽量同时开跑

Main()

Main() {
	if (A_Args.Length >= 1 && A_Args[1] = "--worker") {
		WorkerMain()
		ExitApp 0
	}

	ping := Ping_DB()
	if !ping["ok"] {
		MsgBox "[连接错误] " ping["err"]
		ExitApp 1
	}

	base := PoolSnapRaw()
	if !base["ok"] {
		MsgBox "[SQL 错误] " base["err"]
		ExitApp 1
	}

	outDir := A_Temp "\codepool_concurrency_hard_" FormatTime(, "yyyyMMdd_HHmmss")
	DirCreate(outDir)

	StartWorkers(outDir)

	summary := CollectWorkerResults(outDir)

	after := PoolSnapRaw()

	txt := ""
	txt .= "[信息] 并发参数`n"
	txt .= "workers=" WORKERS "`n"
	txt .= "loops/worker=" LOOPS "`n"
	txt .= "max_need=" MAX_NEED "`n"
	txt .= "jitter_ms=" JITTER_MS "`n"
	txt .= "mode=" MODE "`n`n"

	txt .= "[信息] 执行汇总`n"
	txt .= "ok=" summary["ok"] "`n"
	txt .= "fail=" summary["fail"] "`n"
	txt .= "committed=" summary["committed"] "`n"
	txt .= "rolled_back=" summary["rolled_back"] "`n"
	txt .= "total_take=" summary["total_take"] "`n`n"

	txt .= "[信息] 失败原因统计`n"
	for k, v in summary["fail_reason"]
		txt .= k "=" v "`n"
	txt .= "`n"

	if (base["ok"] && after["ok"]) {
		txt .= "[信息] 库存快照`n"
		txt .= "sum_all(before)=" base["sum_all"] "`n"
		txt .= "sum_all(after)=" after["sum_all"] "`n"
		txt .= "sum_avail(before)=" base["sum_avail"] "`n"
		txt .= "sum_avail(after)=" after["sum_avail"] "`n"
		txt .= "rows_avail(before)=" base["rows_avail"] "`n"
		txt .= "rows_avail(after)=" after["rows_avail"] "`n`n"

		realDelta := (base["sum_all"] + 0) - (after["sum_all"] + 0)
		if (MODE = "COMMIT") {
			txt .= "[信息] 扣减校验`n"
			txt .= "expected_delta=" (summary["total_take"] + 0) "`n"
			txt .= "real_delta=" realDelta "`n"
		} else {
			txt .= "[信息] 扣减校验`n"
			txt .= "expected_delta=0`n"
			txt .= "real_delta=" realDelta "`n"
		}
	}

	MsgBox txt
	ExitApp 0
}

StartWorkers(outDir) {
	ahk := A_AhkPath
	script := A_ScriptFullPath
	startAt := A_TickCount + START_BARRIER_MS

	Loop WORKERS {
		wid := A_Index
		log := outDir "\w" wid ".log"
		args := Format(
			'"{}" --worker {} "{}" {} {} {} {} "{}" "{}" "{}" {}',
			script,
			wid,
			log,
			LOOPS,
			MAX_NEED,
			JITTER_MS,
			MODE,
			TEST_DRUG,
			TEST_SPEC,
			TEST_CLIENT,
			startAt
		)
		Run(ahk " " args, , "Hide")
	}
}

CollectWorkerResults(outDir) {
	ok := 0
	fail := 0
	committed := 0
	rolled_back := 0
	total_take := 0
	reason := Map()

	startTick := A_TickCount
	timeoutMs := 1000 * 60 * 15

	done := Map()
	Loop WORKERS
		done[A_Index] := false

	while true {
		allDone := true

		Loop WORKERS {
			wid := A_Index
			if done[wid]
				continue

			log := outDir "\w" wid ".log"
			if FileExist(log) {
				txt := FileRead(log, "UTF-8")
				if InStr(txt, "`n[END]") {
					done[wid] := true
				} else {
					allDone := false
				}
			} else {
				allDone := false
			}
		}

		if allDone
			break

		if (A_TickCount - startTick > timeoutMs)
			break

		Sleep 200
	}

	Loop WORKERS {
		wid := A_Index
		log := outDir "\w" wid ".log"
		if !FileExist(log) {
			fail += LOOPS
			k := "NO_LOG"
			reason[k] := (reason.Has(k) ? reason[k] : 0) + LOOPS
			continue
		}

		txt := FileRead(log, "UTF-8")
		for _, line in StrSplit(txt, "`n") {
			line := Trim(line, "`r`t ")
			if (line = "" || SubStr(line, 1, 1) = ";")
				continue

			if RegExMatch(line, "^OK\s+take=(\d+)\s+mode=(\w+)\s+need=(\d+)", &m) {
				ok += 1
				total_take += (m[1] + 0)
				if (m[2] = "COMMIT")
					committed += 1
				else
					rolled_back += 1
			} else if RegExMatch(line, "^FAIL\s+message=([A-Z0-9_]+)", &m2) {
				fail += 1
				k := m2[1]
				reason[k] := (reason.Has(k) ? reason[k] : 0) + 1
			}
		}
	}

	return Map(
		"ok", ok,
		"fail", fail,
		"committed", committed,
		"rolled_back", rolled_back,
		"total_take", total_take,
		"fail_reason", reason
	)
}

WorkerMain() {
	wid := A_Args[2] + 0
	logPath := A_Args[3]
	loops := A_Args[4] + 0
	maxNeed := A_Args[5] + 0
	jitter := A_Args[6] + 0
	mode := A_Args[7]
	drug := A_Args[8]
	spec := A_Args[9]
	client := A_Args[10] "_" wid
	startAt := A_Args[11] + 0

	while (A_TickCount < startAt)
		Sleep 1

	fh := FileOpen(logPath, "w", "UTF-8")
	if !IsObject(fh)
		ExitApp 2

	ping := Ping_DB()
	if !ping["ok"] {
		fh.WriteLine("FAIL message=PING_FAIL")
		fh.WriteLine("[END]")
		fh.Close()
		ExitApp 3
	}

	Loop loops {
		if (jitter > 0)
			Sleep Random(0, jitter)

		need := Random(1, maxNeed)
		txnId := Util_TxnId() "_" wid "_" A_Index "_" need

		r := Txn_ReservePick(txnId, client, drug, spec, need, "", "", 0)

		; 聚合端只认稳定 reason code；完整文案不进 FAIL 行（可含换行/空格）
		if !IsObject(r) || !r.Has("ok") || !r["ok"] {
			fh.WriteLine("FAIL message=RESERVE_FAIL")
			continue
		}

		if (r.Has("skip") && r["skip"]) {
			fh.WriteLine("FAIL message=SKIP")
			continue
		}

		takeSum := 0
		if r.Has("items") {
			for _, it in r["items"]
				takeSum += (it["take"] + 0)
		}

		if (mode = "COMMIT") {
			c := Txn_Commit(txnId)
			if !IsObject(c) || !c.Has("ok") || !c["ok"] {
				fh.WriteLine("FAIL message=COMMIT_FAIL")
				continue
			}
			fh.WriteLine("OK take=" takeSum " mode=COMMIT need=" need)
		} else {
			rb := Txn_Rollback(txnId)
			if !IsObject(rb) || !rb.Has("ok") || !rb["ok"] {
				fh.WriteLine("FAIL message=ROLLBACK_FAIL")
				continue
			}
			fh.WriteLine("OK take=" takeSum " mode=ROLLBACK need=" need)
		}
	}

	fh.WriteLine("[END]")
	fh.Close()
}

Ping_DB() {
	rOpen := PG_EnsureOpen()
	if !rOpen["ok"]
		return Map("ok", false, "err", rOpen["err"])
	r := DB_Query("SELECT 1;")
	if !r["ok"]
		return Map("ok", false, "err", r["err"])
	return Map("ok", true, "text", "ok")
}

PoolSnapRaw() {
	sql := ""
		. "SELECT "
		. "  coalesce(sum(remain),0) AS sum_all, "
		. "  coalesce(sum(remain) FILTER (WHERE remain>0),0) AS sum_avail, "
		. "  count(*) AS rows_all, "
		. "  count(*) FILTER (WHERE remain>0) AS rows_avail "
		. "FROM trace_pool "
		. "WHERE drug_id='" Util_EscapeSQL(TEST_DRUG) "' "
		. "  AND spec='" Util_EscapeSQL(TEST_SPEC) "';"

	r := DB_Query(sql)
	if !r["ok"]
		return Map("ok", false, "err", r["err"])

	if (r["rows"].Length = 0)
		return Map("ok", true, "sum_all", 0, "sum_avail", 0, "rows_all", 0, "rows_avail", 0)

	row := r["rows"][1]
	return Map("ok", true
		, "sum_all", row[1] + 0
		, "sum_avail", row[2] + 0
		, "rows_all", row[3] + 0
		, "rows_avail", row[4] + 0
	)
}
