#!/usr/bin/env bash
# 写入 .env.asp：Desktop 与 Agents 两套换票密钥（明文仅给客户端；API 只读散列）
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ENV_FILE="${ROOT_DIR}/apps/api-asp/.env.asp"
PRINT_EXPORT_ONLY=false
FORCE_NEW=false
DESKTOP_ID="dev"
AGENTS_ID="agents"

# 与 appsettings.json Postgres 节对齐的本机默认
DEFAULT_PG_HOST=localhost
DEFAULT_PG_PORT=5432
DEFAULT_PG_DATABASE=postgres
DEFAULT_PG_USERNAME=postgres

usage() {
  cat <<'USAGE'
Usage:
  gen-dev-secrets.sh [--export] [--force]

  Write (or reuse) apps/api-asp/.env.asp:
    PAC_API_KEY                 plaintext (Desktop / local curl; the API process does not read this)
    PAC_AGENTS_API_KEY          plaintext (Desktop Agents key; the API process does not read this)
    PAC_UNLOCK_PASSWORD         unlock password plaintext (local only; the API process does not read this)
    Auth__Clients__dev__*       Desktop key hash; keep existing Enabled / Scopes
    Auth__Clients__agents__*    Agents key hash; keep existing Enabled / Scopes
    Auth__UnlockPasswordHash    unlock password hash
    Auth__Jwt__SigningKey
    Postgres__Host/Port/Database/Username/Password
    other existing keys (SchemaBounds__* / Changes__* / …) are copied through
  --export  print eval-able export lines only
  --force   regenerate Auth secrets (Postgres / Enabled / Scopes / other edits are kept)
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

# 优先：文件旧值 > 已有环境变量 > 默认（避免父 shell 导出的旧 Auth/Pg 盖掉本机配置）
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

resolve_client_enabled() {
  local id="$1"
  local en
  en="$(env_file_get "Auth__Clients__${id}__Enabled")"
  if [[ -z "${en}" ]]; then
    local en_var="Auth__Clients__${id}__Enabled"
    en="${!en_var:-}"
  fi
  printf '%s' "${en:-true}"
}

# 手改 Scopes：文件已有则保留；缺省开发全权限
read_client_scopes() {
  local id="$1"
  local -a lines=()
  if [[ -f "${ENV_FILE}" ]]; then
    while IFS= read -r line; do
      lines+=("${line}")
    done < <(grep -E "^Auth__Clients__${id}__Scopes__[0-9]+=" "${ENV_FILE}" | sed 's/\r$//' || true)
  fi
  if [[ ${#lines[@]} -eq 0 ]]; then
    lines=(
      "Auth__Clients__${id}__Scopes__0=read"
      "Auth__Clients__${id}__Scopes__1=write"
      "Auth__Clients__${id}__Scopes__2=system.status"
    )
  fi
  printf '%s\n' "${lines[@]}"
}

# 明文与散列成对复用，不核对是否匹配；缺散列则从明文补
resolve_client_secrets() {
  local id="$1"
  local plain_key="$2"
  local existing_plain existing_hash new_plain new_hash
  existing_plain="$(env_file_get "${plain_key}")"
  existing_hash="$(env_file_get "Auth__Clients__${id}__ApiKeyHash")"

  if [[ "${FORCE_NEW}" != "true" && -n "${existing_plain}" && -n "${existing_hash}" ]]; then
    new_plain="${existing_plain}"
    new_hash="${existing_hash}"
  elif [[ "${FORCE_NEW}" != "true" && -n "${existing_plain}" ]]; then
    new_plain="${existing_plain}"
    new_hash="$(sha256_hex "${existing_plain}")"
  else
    new_plain="$(rand_secret)"
    new_hash="$(sha256_hex "${new_plain}")"
  fi

  if [[ "${plain_key}" == PAC_API_KEY ]]; then
    PAC_API_KEY="${new_plain}"
    Desktop_ApiKeyHash="${new_hash}"
  else
    PAC_AGENTS_API_KEY="${new_plain}"
    Agents_ApiKeyHash="${new_hash}"
  fi
}

# 写回托管块时，把未托管的键（SchemaBounds / Changes / 其它 Clients）原样带上
collect_passthrough_lines() {
  Passthrough_Lines=()
  [[ -f "${ENV_FILE}" ]] || return 0
  local desktop_prefix="Auth__Clients__${DESKTOP_ID}__"
  local agents_prefix="Auth__Clients__${AGENTS_ID}__"
  while IFS= read -r line || [[ -n "${line}" ]]; do
    line="${line%$'\r'}"
    [[ -z "${line}" || "${line}" =~ ^[[:space:]]*# ]] && continue
    [[ "${line}" != *=* ]] && continue
    local key="${line%%=*}"
    case "${key}" in
      PAC_API_KEY|PAC_AGENTS_API_KEY|PAC_UNLOCK_PASSWORD|Auth__UnlockPasswordHash|Auth__Jwt__SigningKey|ASPNETCORE_ENVIRONMENT|ASPNETCORE_URLS) continue ;;
      Postgres__Host|Postgres__Port|Postgres__Database|Postgres__Username|Postgres__Password) continue ;;
      "${desktop_prefix}ApiKeyHash"|"${desktop_prefix}Enabled"|"${agents_prefix}ApiKeyHash"|"${agents_prefix}Enabled") continue ;;
      "${desktop_prefix}Scopes__"*|"${agents_prefix}Scopes__"*) continue ;;
    esac
    Passthrough_Lines+=("${line}")
  done <"${ENV_FILE}"
}

write_env_file() {
  local desktop_scopes_block agents_scopes_block passthrough_block
  desktop_scopes_block="$(printf '%s\n' "${Desktop_Scopes_Lines[@]}")"
  agents_scopes_block="$(printf '%s\n' "${Agents_Scopes_Lines[@]}")"
  passthrough_block=""
  if [[ ${#Passthrough_Lines[@]} -gt 0 ]]; then
    passthrough_block="$(printf '%s\n' "${Passthrough_Lines[@]}")"
  fi

  umask 077
  cat >"${ENV_FILE}" <<EOF
# local only — do not commit (gitignored)
# PAC_API_KEY / PAC_AGENTS_API_KEY / PAC_UNLOCK_PASSWORD 是明文，仅给客户端或本机 curl；API 只读散列
# Enabled / Scopes / ApiKeyHash、SchemaBounds、Changes 手改保留；--force 只换明文 Key 与 SigningKey
PAC_API_KEY=${PAC_API_KEY}
PAC_AGENTS_API_KEY=${PAC_AGENTS_API_KEY}
PAC_UNLOCK_PASSWORD=${PAC_UNLOCK_PASSWORD}
Auth__UnlockPasswordHash=${Auth__UnlockPasswordHash}
Auth__Clients__${DESKTOP_ID}__ApiKeyHash=${Desktop_ApiKeyHash}
Auth__Clients__${DESKTOP_ID}__Enabled=${Desktop_Enabled}
${desktop_scopes_block}
Auth__Clients__${AGENTS_ID}__ApiKeyHash=${Agents_ApiKeyHash}
Auth__Clients__${AGENTS_ID}__Enabled=${Agents_Enabled}
${agents_scopes_block}
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
Desktop_Enabled="$(resolve_client_enabled "${DESKTOP_ID}")"
Agents_Enabled="$(resolve_client_enabled "${AGENTS_ID}")"
Desktop_Scopes_Lines=()
Agents_Scopes_Lines=()
while IFS= read -r line; do
  Desktop_Scopes_Lines+=("${line}")
done < <(read_client_scopes "${DESKTOP_ID}")
while IFS= read -r line; do
  Agents_Scopes_Lines+=("${line}")
done < <(read_client_scopes "${AGENTS_ID}")
collect_passthrough_lines

# 环境变量可覆盖文件中的 ASPNETCORE_*
ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-$(env_file_get ASPNETCORE_ENVIRONMENT)}"
ASPNETCORE_URLS="${ASPNETCORE_URLS:-$(env_file_get ASPNETCORE_URLS)}"
ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-dev}"
ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5080}"

resolve_client_secrets "${DESKTOP_ID}" PAC_API_KEY
resolve_client_secrets "${AGENTS_ID}" PAC_AGENTS_API_KEY

existing_jwt="$(env_file_get Auth__Jwt__SigningKey)"
if [[ "${FORCE_NEW}" == "true" || -z "${existing_jwt}" ]]; then
  Auth__Jwt__SigningKey="$(rand_secret)"
  if [[ ${#Auth__Jwt__SigningKey} -lt 32 ]]; then
    echo "error: JWT SigningKey shorter than 32 characters" >&2
    exit 1
  fi
else
  Auth__Jwt__SigningKey="${existing_jwt}"
fi

PAC_UNLOCK_PASSWORD="$(env_file_get PAC_UNLOCK_PASSWORD)"
Auth__UnlockPasswordHash="$(env_file_get Auth__UnlockPasswordHash)"
if [[ -z "${Auth__UnlockPasswordHash}" ]]; then
  if [[ -z "${PAC_UNLOCK_PASSWORD}" ]]; then
    PAC_UNLOCK_PASSWORD="$(rand_secret)"
  fi
  Auth__UnlockPasswordHash="$(sha256_hex "${PAC_UNLOCK_PASSWORD}")"
fi

# 每次写回托管键；复用时不重置 Enabled/Scopes/ApiKeyHash；Postgres 与 passthrough 手改保留
write_env_file

export_client_scopes() {
  local line k
  for line in "$@"; do
    k="${line%%=*}"
    printf "export %s='%s'\n" "${k}" "${line#*=}"
  done
}

export_block() {
  cat <<EOF
export PAC_API_KEY='${PAC_API_KEY}'
export PAC_AGENTS_API_KEY='${PAC_AGENTS_API_KEY}'
export PAC_UNLOCK_PASSWORD='${PAC_UNLOCK_PASSWORD}'
export Auth__UnlockPasswordHash='${Auth__UnlockPasswordHash}'
export Auth__Clients__${DESKTOP_ID}__ApiKeyHash='${Desktop_ApiKeyHash}'
export Auth__Clients__${DESKTOP_ID}__Enabled='${Desktop_Enabled}'
EOF
  export_client_scopes "${Desktop_Scopes_Lines[@]}"
  cat <<EOF
export Auth__Clients__${AGENTS_ID}__ApiKeyHash='${Agents_ApiKeyHash}'
export Auth__Clients__${AGENTS_ID}__Enabled='${Agents_Enabled}'
EOF
  export_client_scopes "${Agents_Scopes_Lines[@]}"
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
# wrote ${ENV_FILE}
# PAC_API_KEY: ${PAC_API_KEY}
# PAC_AGENTS_API_KEY: ${PAC_AGENTS_API_KEY}
# PAC_UNLOCK_PASSWORD: ${PAC_UNLOCK_PASSWORD}
# Jwt SigningKey is server-side; API reads only ApiKeyHash / UnlockPasswordHash
# Desktop client=${DESKTOP_ID} Enabled=${Desktop_Enabled}; Agents client=${AGENTS_ID} Enabled=${Agents_Enabled}
# Postgres: ${Postgres__Username}@${Postgres__Host}:${Postgres__Port}/${Postgres__Database}
# empty Postgres__Password => /health 503

# source:
set -a; source '${ENV_FILE}'; set +a

# or:
$(export_block)

# start:
#   ./apps/api-asp/scripts/wire-local.sh
EOF
