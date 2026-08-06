; 路径工具

Util_PathFull(p) {
	; 统一为绝对路径，避免工作目录变化影响文件访问
	buf := Buffer(32768 * 2, 0)
	len := DllCall("Kernel32\GetFullPathNameW", "str", p, "uint", 32768, "ptr", buf, "ptr", 0, "uint")
	return len ? StrGet(buf, len, "UTF-16") : p
}
