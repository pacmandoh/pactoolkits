#!/usr/bin/env bash
# 生成本地 apps/api-asp/.env.asp（Auth 与 Postgres）；Auth 密钥可复用，手改 Enabled/Scopes/额外键保留
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ENV_FILE="${ROOT_DIR}/apps/api-asp/.env.asp"
PRINT_EXPORT_ONLY=false
FORCE_NEW=false
CLIENT_ID="dev"

# 与 appsettings.json Postgres 节对齐的本机默认
DEFAULT_PG_HOST=localhost
DEFAULT_PG_PORT=5432
DEFAULT_PG_DATABASE=postgres
DEFAULT_PG_USERNAME=postgres

usage() {
  cat <<'USAGE'
Usage:
  gen-dev-secrets.sh [--export] [--force]

  写入（或复用）apps/api-asp/.env.asp：
    PAC_API_KEY              明文（仅本地 curl；API 进程不读）
    Auth__Clients__dev__*    Key 散列；Enabled / Scopes 若已有则保留
    Auth__Jwt__SigningKey
    Postgres__Host/Port/Database/Username/Password
    其它已有键（SchemaBounds__* / Changes__* 等）原样带回
  --export  仅打印可 eval 的 export 行
  --force   强制重新生成 Auth 密钥（Postgres / Enabled / Scopes / 其它手改仍保留）
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -e|--export)
      PRINT_EXPORT_ONLY=true
      shift
      ;;
    -f|--force)
      FORCE_NEW=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

rand_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 48 | tr -d '=\n' | tr '+/' '-_'
  else
    python3 -c 'import secrets; print(secrets.token_urlsafe(48))'
  fi
}

sha256_hex() {
  local value="$1"
  if command -v openssl >/dev/null 2>&1; then
    printf '%s' "${value}" | openssl dgst -sha256 -hex | awk '{print tolower($NF)}'
  else
    python3 -c 'import hashlib,sys; print(hashlib.sha256(sys.argv[1].encode()).hexdigest())' "${value}"
  fi
}

# 从 .env.asp 读 KEY=value（value 可含 =）；文件/键不存在则空
env_file_get() {
  local key="$1"
  local line
  [[ -f "${ENV_FILE}" ]] || return 0
  line="$(grep -E "^${key}=" "${ENV_FILE}" | sed 's/\r$//' | head -n 1 || true)"
  [[ -n "${line}" ]] || return 0
  printf '%s' "${line#*=}"
}

# 优先：文件旧值 > 已有环境变量 > 默认（避免父 shell 导出的旧 Auth/Pg 盖掉 .env.asp 本机配置）
resolve_pg() {
  local host port database username password
  host="$(env_file_get Postgres__Host)"
  port="$(env_file_get Postgres__Port)"
  database="$(env_file_get Postgres__Database)"
  username="$(env_file_get Postgres__Username)"
  password="$(env_file_get Postgres__Password)"
  [[ -n "${host}" ]] || host="${Postgres__Host:-}"
  [[ -n "${port}" ]] || port="${Postgres__Port:-}"
  [[ -n "${database}" ]] || database="${Postgres__Database:-}"
  [[ -n "${username}" ]] || username="${Postgres__Username:-}"
  [[ -n "${password}" ]] || password="${Postgres__Password:-}"

  Postgres__Host="${host:-${DEFAULT_PG_HOST}}"
  Postgres__Port="${port:-${DEFAULT_PG_PORT}}"
  Postgres__Database="${database:-${DEFAULT_PG_DATABASE}}"
  Postgres__Username="${username:-${DEFAULT_PG_USERNAME}}"
  Postgres__Password="${password:-}"
}

# 手改 Enabled / Scopes：文件已有则保留（优先于父 shell），缺省才给开发全权限
resolve_client_prefs() {
  local en
  en="$(env_file_get "Auth__Clients__${CLIENT_ID}__Enabled")"
  if [[ -z "${en}" ]]; then
    local en_var="Auth__Clients__${CLIENT_ID}__Enabled"
    en="${!en_var:-}"
  fi
  Auth_Client_Enabled="${en:-true}"

  Auth_Client_Scopes_Lines=()
  if [[ -f "${ENV_FILE}" ]]; then
    while IFS= read -r line; do
      Auth_Client_Scopes_Lines+=("${line}")
    done < <(grep -E "^Auth__Clients__${CLIENT_ID}__Scopes__[0-9]+=" "${ENV_FILE}" | sed 's/\r$//' || true)
  fi
  if [[ ${#Auth_Client_Scopes_Lines[@]} -eq 0 ]]; then
    Auth_Client_Scopes_Lines=(
      "Auth__Clients__${CLIENT_ID}__Scopes__0=read"
      "Auth__Clients__${CLIENT_ID}__Scopes__1=write"
      "Auth__Clients__${CLIENT_ID}__Scopes__2=system.status"
    )
  fi
}

# 写回托管块时，把未托管的键（SchemaBounds / Changes 等）原样带上，避免 wire-local 冲掉本机配置
collect_passthrough_lines() {
  Passthrough_Lines=()
  [[ -f "${ENV_FILE}" ]] || return 0
  local prefix="Auth__Clients__${CLIENT_ID}__"
  while IFS= read -r line || [[ -n "${line}" ]]; do
    line="${line%$'\r'}"
    [[ -z "${line}" || "${line}" =~ ^[[:space:]]*# ]] && continue
    [[ "${line}" != *=* ]] && continue
    local key="${line%%=*}"
    case "${key}" in
      PAC_API_KEY|Auth__Jwt__SigningKey|ASPNETCORE_ENVIRONMENT|ASPNETCORE_URLS) continue ;;
      Postgres__Host|Postgres__Port|Postgres__Database|Postgres__Username|Postgres__Password) continue ;;
      "${prefix}ApiKeyHash"|"${prefix}Enabled") continue ;;
      "${prefix}Scopes__"*) continue ;;
    esac
    Passthrough_Lines+=("${line}")
  done <"${ENV_FILE}"
}

write_env_file() {
  local scopes_block passthrough_block
  scopes_block="$(printf '%s\n' "${Auth_Client_Scopes_Lines[@]}")"
  passthrough_block=""
  if [[ ${#Passthrough_Lines[@]} -gt 0 ]]; then
    passthrough_block="$(printf '%s\n' "${Passthrough_Lines[@]}")"
  fi

  umask 077
  cat >"${ENV_FILE}" <<EOF
# local only — do not commit (gitignored)
# PAC_API_KEY is plaintext for local curl; API reads only ApiKeyHash
# Enabled / Scopes / ApiKeyHash 手改、SchemaBounds、Changes 在下次 gen/wire 时保留（--force 只换 PAC_API_KEY 与 SigningKey）
PAC_API_KEY=${PAC_API_KEY}
Auth__Clients__${CLIENT_ID}__ApiKeyHash=${Auth_Client_ApiKeyHash}
Auth__Clients__${CLIENT_ID}__Enabled=${Auth_Client_Enabled}
${scopes_block}
Auth__Jwt__SigningKey=${Auth__Jwt__SigningKey}
ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-dev}
ASPNETCORE_URLS=${ASPNETCORE_URLS:-http://127.0.0.1:5080}
Postgres__Host=${Postgres__Host}
Postgres__Port=${Postgres__Port}
Postgres__Database=${Postgres__Database}
Postgres__Username=${Postgres__Username}
Postgres__Password=${Postgres__Password}
${passthrough_block}
EOF
}

resolve_pg
resolve_client_prefs
collect_passthrough_lines

# 环境变量可覆盖文件中的 ASPNETCORE_*
ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-$(env_file_get ASPNETCORE_ENVIRONMENT)}"
ASPNETCORE_URLS="${ASPNETCORE_URLS:-$(env_file_get ASPNETCORE_URLS)}"
ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-dev}"
ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5080}"

if [[ "${FORCE_NEW}" != "true" && -f "${ENV_FILE}" ]] \
  && grep -q '^PAC_API_KEY=.\+' "${ENV_FILE}" \
  && grep -q "^Auth__Clients__${CLIENT_ID}__ApiKeyHash=.\+" "${ENV_FILE}" \
  && grep -q '^Auth__Jwt__SigningKey=.\+' "${ENV_FILE}"; then
  PAC_API_KEY="$(env_file_get PAC_API_KEY)"
  # 与明文不同步的手改散列也会保留（便于 T5 错散列回归）
  Auth_Client_ApiKeyHash="$(env_file_get "Auth__Clients__${CLIENT_ID}__ApiKeyHash")"
  Auth__Jwt__SigningKey="$(env_file_get Auth__Jwt__SigningKey)"
else
  PAC_API_KEY="$(rand_secret)"
  Auth__Jwt__SigningKey="$(rand_secret)"
  if [[ ${#Auth__Jwt__SigningKey} -lt 32 ]]; then
    echo "error: JWT SigningKey shorter than 32 characters" >&2
    exit 1
  fi
  Auth_Client_ApiKeyHash="$(sha256_hex "${PAC_API_KEY}")"
fi

# 缺散列时与明文对齐（首次或缺字段）
if [[ -z "${Auth_Client_ApiKeyHash:-}" ]]; then
  Auth_Client_ApiKeyHash="$(sha256_hex "${PAC_API_KEY}")"
fi

# 每次写回托管键；复用时不重置 Enabled/Scopes/ApiKeyHash；Postgres 与 passthrough 手改保留
write_env_file

export_block() {
  cat <<EOF
export PAC_API_KEY='${PAC_API_KEY}'
export Auth__Clients__${CLIENT_ID}__ApiKeyHash='${Auth_Client_ApiKeyHash}'
export Auth__Clients__${CLIENT_ID}__Enabled='${Auth_Client_Enabled}'
EOF
  local line k
  for line in "${Auth_Client_Scopes_Lines[@]}"; do
    k="${line%%=*}"
    printf "export %s='%s'\n" "${k}" "${line#*=}"
  done
  cat <<EOF
export Auth__Jwt__SigningKey='${Auth__Jwt__SigningKey}'
export Postgres__Host='${Postgres__Host}'
export Postgres__Port='${Postgres__Port}'
export Postgres__Database='${Postgres__Database}'
export Postgres__Username='${Postgres__Username}'
export Postgres__Password='${Postgres__Password}'
EOF
}

if [[ "${PRINT_EXPORT_ONLY}" == "true" ]]; then
  export_block
  exit 0
fi

cat <<EOF
# 写入 .env.asp（${ENV_FILE}）
# 客户端 X-Api-Key: ${PAC_API_KEY}
# Jwt SigningKey 仅服务端；API 只读 ApiKeyHash
# Client Enabled=${Auth_Client_Enabled}（手改 .env.asp 后重跑 wire-local 会保留）
# Postgres: ${Postgres__Username}@${Postgres__Host}:${Postgres__Port}/${Postgres__Database}
# （Password 空则 /health 会 503；在 .env.asp 填 Postgres__Password 后重跑 wire-local）

# source 进 shell:
set -a; source '${ENV_FILE}'; set +a

# 或:
$(export_block)

# 启动:
#   ./apps/api-asp/scripts/wire-local.sh
EOF
