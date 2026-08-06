#Requires AutoHotkey v2.0
#SingleInstance Force
#Include "%A_ScriptDir%\..\..\..\lib\ahk\JSON.ahk"
#Include "%A_ScriptDir%\..\..\..\lib\ahk\path.ahk"
#Include "%A_ScriptDir%\..\src\util_misc.ahk"

Test_LoadDotEnv_Array()

Test_LoadDotEnv_Array() {
	cfg := Util_LoadDotEnv(A_ScriptDir "\..\.env.local")

	; 验证必需配置项存在
	if !cfg.Has("APP_WIN") {
		MsgBox "[读取错误] 未找到配置项 APP_WIN"
		ExitApp 1
	}

	val := cfg["APP_WIN"]

	; 验证对象与键值集合类型
	if !IsObject(val) {
		MsgBox "[类型错误] APP_WIN 不是对象类型"
		ExitApp 1
	}

	if !(val is Map) {
		MsgBox "[类型错误] APP_WIN 不是键值对类型"
		ExitApp 1
	}

	; 验证 Has 与成员排除行为
	if !val.Has("互慧软件.exe") {
		MsgBox "[匹配错误] APP_WIN.Has('互慧软件.exe') 返回 false"
		ExitApp 1
	}

	if val.Has("NotExists.exe") {
		MsgBox "[匹配错误] APP_WIN.Has('NotExists.exe') 返回 true（不应命中）"
		ExitApp 1
	}

	MsgBox "[信息] DotEnv Map 解析测试通过`nAPP_WIN 已正确解析为键值对 Map"
}
