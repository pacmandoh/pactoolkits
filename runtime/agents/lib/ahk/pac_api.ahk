; PacAPI：环境变量优先；缺省再读 --config 的 PacApi（BaseUrl / AgentsApiKey）
; 环境：PAC_API_BASE_URL、PAC_API_KEY；可选 PAC_API_HEADER（默认 X-Api-Key）
; 独立启动不读 Desktop ApiKey

global __PAC := Map(
	"base", "",
	"key", "",
	"header", "X-Api-Key",
	"token", "",
	"tokenExp", 0
)

PacApi_InitFromEnv() {
	global __PAC
	base := Trim(EnvGet("PAC_API_BASE_URL"))
	key := Trim(EnvGet("PAC_API_KEY"))
	header := Trim(EnvGet("PAC_API_HEADER"))
	if (header = "")
		header := "X-Api-Key"
	__PAC["base"] := RTrim(base, "/")
	__PAC["key"] := key
	__PAC["header"] := header
	__PAC["token"] := ""
	__PAC["tokenExp"] := 0
	if (__PAC["base"] = "" || __PAC["key"] = "")
		return Map("ok", false, "level", "Error", "message", "[PacAPI] 缺少 PAC_API_BASE_URL 或 PAC_API_KEY")
	return Map("ok", true)
}

PacApi_InitFromConfig(root) {
	global __PAC
	if (Type(root) != "Map" || !root.Has("PacApi"))
		return Map("ok", false, "level", "Error", "message", "[PacAPI] 配置缺少 PacApi")
	pac := root["PacApi"]
	if (Type(pac) != "Map")
		return Map("ok", false, "level", "Error", "message", "[PacAPI] PacApi 必须是对象")

	base := Trim(pac.Has("BaseUrl") ? pac["BaseUrl"] : "")
	key := Trim(pac.Has("AgentsApiKey") ? pac["AgentsApiKey"] : "")
	header := Trim(pac.Has("HeaderName") ? pac["HeaderName"] : "")
	if (header = "")
		header := "X-Api-Key"
	__PAC["base"] := RTrim(base, "/")
	__PAC["key"] := key
	__PAC["header"] := header
	__PAC["token"] := ""
	__PAC["tokenExp"] := 0
	if (__PAC["base"] = "" || __PAC["key"] = "")
		return Map("ok", false, "level", "Error", "message", "[PacAPI] 配置缺少 PacApi.BaseUrl 或 PacApi.AgentsApiKey")
	return Map("ok", true)
}

; 环境变量优先；没有再读 --config 的 PacApi（独立启动）
PacApi_Init(root := "") {
	api := PacApi_InitFromEnv()
	if api["ok"]
		return api
	if (Type(root) = "Map")
		return PacApi_InitFromConfig(root)
	return api
}

PacApi_NewCommandId() {
	return Trim(ComObject("Scriptlet.TypeLib").GUID, "{}")
}

PacApi_UrlEncode(v) {
	s := ""
	buf := Buffer(StrPut(v, "UTF-8"))
	StrPut(v, buf, "UTF-8")
	Loop buf.Size - 1 {
		b := NumGet(buf, A_Index - 1, "UChar")
		if ((b >= 0x30 && b <= 0x39) || (b >= 0x41 && b <= 0x5A) || (b >= 0x61 && b <= 0x7A)
			|| b = 0x2D || b = 0x2E || b = 0x5F || b = 0x7E)
			s .= Chr(b)
		else
			s .= Format("%{:02X}", b)
	}
	return s
}

PacApi_EnsureToken() {
	global __PAC
	now := A_TickCount
	if (__PAC["token"] != "" && now + 5000 < __PAC["tokenExp"])
		return Map("ok", true)

	r := PacApi_Http("POST", "/v1/auth/token", "", false)
	if !r["ok"]
		return r
	body := r["body"]
	if !IsObject(body) || !body.Has("accessToken") || body["accessToken"] = ""
		return Map("ok", false, "level", "Error", "message", "[PacAPI] 换票响应无效")
	__PAC["token"] := body["accessToken"]
	expires := 60
	if body.Has("expiresIn")
		expires := Max(10, Util_ToInt(body["expiresIn"], 60))
	__PAC["tokenExp"] := now + (expires * 1000) - 10000
	return Map("ok", true)
}

PacApi_Get(path) {
	t := PacApi_EnsureToken()
	if !t["ok"]
		return t
	return PacApi_Http("GET", path, "", true)
}

PacApi_Post(path, payload) {
	t := PacApi_EnsureToken()
	if !t["ok"]
		return t
	body := IsObject(payload) ? JSON.stringify(payload, 0) : (payload = "" ? "{}" : payload)
	return PacApi_Http("POST", path, body, true)
}

PacApi_Http(method, path, body, withJwt) {
	global __PAC
	if (__PAC["base"] = "")
		return Map("ok", false, "level", "Error", "message", "[PacAPI] 未初始化")

	url := __PAC["base"] path
	try {
		http := ComObject("WinHttp.WinHttpRequest.5.1")
		http.Open(method, url, false)
		http.SetTimeouts(3000, 3000, 30000, 60000)
		if withJwt
			http.SetRequestHeader("Authorization", "Bearer " __PAC["token"])
		else
			http.SetRequestHeader(__PAC["header"], __PAC["key"])
		if (method = "POST") {
			http.SetRequestHeader("Content-Type", "application/json; charset=utf-8")
			http.SetRequestHeader("X-Command-Id", PacApi_NewCommandId())
		}
		http.Send(body)
		status := http.Status
		text := http.ResponseText
	} catch as e {
		return Map("ok", false, "level", "Error", "message", "[PacAPI] 请求失败：`n" e.Message, "err", e.Message)
	}

	parsed := ""
	if (Trim(text) != "") {
		try parsed := JSON.parse(text)
		catch as _
			parsed := ""
	}

	if (status = 401 && withJwt) {
		__PAC["token"] := ""
		__PAC["tokenExp"] := 0
		return Map("ok", false, "level", "Error", "message", "[PacAPI] 未授权", "status", status)
	}
	if (status < 200 || status >= 300) {
		detail := IsObject(parsed) && parsed.Has("detail") ? parsed["detail"] : text
		return Map("ok", false, "level", "Error", "message", "[PacAPI] HTTP " status "`n" detail, "status", status, "err", detail)
	}
	return Map("ok", true, "status", status, "body", parsed)
}
