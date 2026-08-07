#!/usr/bin/env bash
# 生成本地 apps/api-asp/.env.asp（Auth 密钥）；已存在则复用，不重新随机
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ENV_FILE="${ROOT_DIR}/apps/api-asp/.env.asp"
PRINT_EXPORT_ONLY=false
FORCE_NEW=false

usage() {
  cat <<'USAGE'
Usage:
  gen-dev-secrets.sh [--export] [--force]

  写入（或复用）apps/api-asp/.env.asp 中的 Auth__* 密钥；不写仓库 appsettings。
  --export  仅打印可 eval 的 export 行
  --force   强制重新生成并覆盖密钥
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

load_existing() {
  # shellcheck disable=SC1090
  set -a
  # shellcheck source=/dev/null
  source <(grep -E '^(Auth__ApiKeys__0|Auth__Jwt__SigningKey)=' "${ENV_FILE}" | sed 's/\r$//')
  set +a
}

write_env_file() {
  umask 077
  cat >"${ENV_FILE}" <<EOF
# local only — do not commit (gitignored)
Auth__ApiKeys__0=${Auth__ApiKeys__0}
Auth__Jwt__SigningKey=${Auth__Jwt__SigningKey}
ASPNETCORE_ENVIRONMENT=dev
ASPNETCORE_URLS=http://127.0.0.1:5080
EOF
}

if [[ "${FORCE_NEW}" != "true" && -f "${ENV_FILE}" ]] \
  && grep -q '^Auth__ApiKeys__0=.\+' "${ENV_FILE}" \
  && grep -q '^Auth__Jwt__SigningKey=.\+' "${ENV_FILE}"; then
  load_existing
else
  Auth__ApiKeys__0="$(rand_secret)"
  Auth__Jwt__SigningKey="$(rand_secret)"
  if [[ ${#Auth__Jwt__SigningKey} -lt 32 ]]; then
    echo "error: JWT SigningKey shorter than 32 characters" >&2
    exit 1
  fi
  write_env_file
fi

export_block() {
  cat <<EOF
export Auth__ApiKeys__0='${Auth__ApiKeys__0}'
export Auth__Jwt__SigningKey='${Auth__Jwt__SigningKey}'
EOF
}

if [[ "${PRINT_EXPORT_ONLY}" == "true" ]]; then
  export_block
  exit 0
fi

cat <<EOF
# .env.asp → ${ENV_FILE}
# 客户端 X-Api-Key: ${Auth__ApiKeys__0}
# Jwt SigningKey 仅服务端

# source 进 shell:
set -a; source '${ENV_FILE}'; set +a

# 或:
$(export_block)

# 启动:
#   ./apps/api-asp/scripts/wire-local.sh
EOF
