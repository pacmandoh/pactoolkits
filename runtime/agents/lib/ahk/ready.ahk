; 模块就绪文件 module.ready：自检通过后创建，退出时删除

Ready_Path() {
	return A_ScriptDir "\module.ready"
}

Ready_Mark() {
	path := Ready_Path()
	try FileDelete(path)
	try FileAppend("ok`n", path, "UTF-8")
}

; OnExit 回调签名需要接受可变参数
Ready_Clear(*) {
	try FileDelete(Ready_Path())
}

; 入口自检前挂接：清残留 ready，并在退出时删除
Ready_Install() {
	Ready_Clear()
	OnExit(Ready_Clear)
}
