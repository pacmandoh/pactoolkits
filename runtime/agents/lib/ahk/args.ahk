; 从 A_Args 读取命名参数（支持 `--name value` 与 `--name=value`）

Args_GetValue(name) {
	for index, arg in A_Args {
		if (arg = name && index < A_Args.Length)
			return Trim(A_Args[index + 1])

		prefix := name "="
		if (InStr(arg, prefix) = 1)
			return Trim(SubStr(arg, StrLen(prefix) + 1))
	}

	return ""
}
