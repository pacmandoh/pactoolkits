; 模块通用提示 / 失败弹窗（失败必先 Log_Error）
; 依赖：先 #Include log.ahk

UI_Tip(msg, ms := 1200) {
	ToolTip(msg)
	SetTimer(() => ToolTip(), -ms)
}

; title 由调用方传入，避免公共库写死业务文案
UI_Fail(event, text, title, context := unset) {
	if IsSet(context)
		Log_Error(event, text, context)
	else
		Log_Error(event, text)
	; 置顶 + 系统模态，避免被目标 HIS 窗口挡住
	return MsgBox(text, title, 0x40000 | 0x1000)
}
