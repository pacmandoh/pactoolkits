; DB_* 将事务和查询统一转发至 PG_*，使业务脚本不依赖数据库驱动细节

DB_Exec(sql) {
	return PG_Exec(sql)
}

DB_Query(sql) {
	return PG_Query(sql)
}

PG_Exec(sql) {
	r := PG_EnsureOpen()
	if !r["ok"]
		return r

	conn := __PG["conn"]
	try {
		conn.Execute(sql)
		return Map("ok", true)
	} catch as e {
		return Map("ok", false, "level", "ERR", "type", "[SQL 错误]", "err", e.Message "`nSQL:`n" Util_ShortSQL(sql))
	}
}

PG_Query(sql) {
	r := PG_EnsureOpen()
	if !r["ok"]
		return r

	conn := __PG["conn"]
	rs := 0
	try {
		rs := conn.Execute(sql)
		rows := []

		if !IsObject(rs)
			return Map("ok", true, "rows", rows)

		try {
			if rs.EOF {
				try rs.Close()
				return Map("ok", true, "rows", rows)
			}
		} catch as _e {
			try rs.Close()
			return Map("ok", true, "rows", rows)
		}

		colCount := rs.Fields.Count
		while !rs.EOF {
			row := []
			Loop colCount {
				v := ""
				try v := rs.Fields.Item(A_Index - 1).Value
				catch as _e
					v := ""
				row.Push("" v)
			}
			rows.Push(row)
			rs.MoveNext()
		}
		try rs.Close()
		return Map("ok", true, "rows", rows)

	} catch as e {
		try {
			if IsObject(rs)
				rs.Close()
		}
		return Map("ok", false, "level", "ERR", "type", "[SQL 错误]", "err", e.Message "`nSQL:`n" Util_ShortSQL(sql))
	}
}

PG_EnsureOpen() {
	if (__PG.Has("conn") && __PG["conn"])
		return Map("ok", true)

	try {
		conn := ComObject("ADODB.Connection")
		driver := Cfg["PG_DRIVER"]

		cs := ""
			. "Driver={" driver "};"
			. "Server=" Cfg["PG_HOST"] ";"
			. "Port=" Cfg["PG_PORT"] ";"
			. "Database=" Cfg["PG_DB"] ";"
			. "Uid=" Cfg["PG_USER"] ";"
			. "Pwd=" Cfg["PG_PASS"] ";"
			. "SSLmode=" Cfg["PG_SSL"] ";"

		conn.ConnectionTimeout := 3
		conn.CommandTimeout := 60
		conn.Open(cs)

		__PG["conn"] := conn
		__PG["in_txn"] := false
		return Map("ok", true)

	} catch as e {
		return Map("ok", false, "level", "ERR", "type", "[数据库错误]", "err", "数据库链接失败：`n" e.Message)
	}
}

PG_Close() {
	try {
		if (__PG.Has("conn") && __PG["conn"]) {
			try {
				if (__PG.Has("in_txn") && __PG["in_txn"])
					__PG["conn"].RollbackTrans()
			}
			try __PG["conn"].Close()
			__PG["conn"] := 0
			__PG["in_txn"] := false
		}
	}
}

OnExit(*) => PG_Close()