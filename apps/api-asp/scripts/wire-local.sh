#!/usr/bin/env bash
# 本机启动 api-asp：读 .env.asp，后台常驻，并校验 health、换票与 ping
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ENV_FILE="${ROOT_DIR}/apps/api-asp/.env.asp"
GEN_SECRETS="${ROOT_DIR}/apps/api-asp/scripts/gen-dev-secrets.sh"
APP_PROJ="${ROOT_DIR}/apps/api-asp/src/PacToolkits.Api.csproj"
BASE_URL="${API_BASE_URL:-http://127.0.0.1:5080}"
LOG_DIR="${ROOT_DIR}/apps/api-asp/.run"
LOG_FILE="${LOG_DIR}/api.log"
PID_FILE="${LOG_DIR}/api.pid"
NO_PROBE=false

log() { printf '[wire-local] %s\n' "$*"; }
die() { printf '[wire-local][error] %s\n' "$*" >&2; exit 1; }

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"
}

usage() {
  cat <<'USAGE'
Usage:
  wire-local.sh [--no-probe] [--base-url URL]

  1) Ensure apps/api-asp/.env.asp has Auth and Postgres (see gen-dev-secrets;
     hand-edited Enabled/Scopes are preserved)
  2) Start api-asp in the background (nohup; survives terminal close)
  3) Probe health, token exchange, and /v1/ping
     (--no-probe skips; use when the client is disabled and probe would fail)
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-probe)
      NO_PROBE=true
      shift
      ;;
    --base-url)
      BASE_URL="${2:?}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      die "unknown option: $1"
      ;;
  esac
done

require_cmd jq
require_cmd curl

[[ -f "${APP_PROJ}" ]] || die "project not found: ${APP_PROJ}"
[[ -x "${GEN_SECRETS}" ]] || chmod +x "${GEN_SECRETS}"

mkdir -p "${LOG_DIR}"

"${GEN_SECRETS}" >/dev/null
# 清掉父 shell 遗留的 Auth scopes / SchemaBounds / Changes，避免盖掉 .env.asp
for i in 0 1 2 3 4 5 6 7 8 9; do
  unset "Auth__Clients__dev__Scopes__${i}" 2>/dev/null || true
done
unset SchemaBounds__MinDbSchema SchemaBounds__MaxDbSchema Changes__ListenEnabled 2>/dev/null || true
# shellcheck disable=SC1090
set -a
# shellcheck source=/dev/null
# 含 SchemaBounds / Changes，避免本机校验时门禁与 Listen 开关未注入进程
source <(grep -E '^(PAC_API_KEY|Auth__|Postgres__|ASPNETCORE_|SchemaBounds__|Changes__)' "${ENV_FILE}" | sed 's/\r$//')
set +a
[[ -n "${PAC_API_KEY:-}" && -n "${Auth__Clients__dev__ApiKeyHash:-}" && -n "${Auth__Jwt__SigningKey:-}" ]] \
  || die "Auth secrets not ready: ${ENV_FILE}"
[[ -n "${Postgres__Host:-}" && -n "${Postgres__Database:-}" && -n "${Postgres__Username:-}" ]] \
  || die "Postgres connection not ready: ${ENV_FILE}"
if [[ -z "${Postgres__Password:-}" ]]; then
  log "warn: Postgres__Password is empty; /health will likely be 503 — set it in ${ENV_FILE} and rerun"
fi

export PAC_API_KEY
export Auth__Clients__dev__ApiKeyHash Auth__Clients__dev__Enabled
# 只 export 文件里出现的 Scopes 行，避免旧进程环境里残留 Scopes__1/2
while IFS= read -r line; do
  key="${line%%=*}"
  export "${key?}"
done < <(grep -E '^Auth__Clients__dev__Scopes__[0-9]+=' "${ENV_FILE}" | sed 's/\r$//' || true)
export Auth__Jwt__SigningKey
export Postgres__Host Postgres__Port Postgres__Database Postgres__Username Postgres__Password
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-dev}"
export ASPNETCORE_URLS="${BASE_URL}"
# SchemaBounds / Changes 若在 .env.asp 中则导出
while IFS= read -r line; do
  key="${line%%=*}"
  export "${key?}"
done < <(grep -E '^(SchemaBounds__|Changes__)' "${ENV_FILE}" | sed 's/\r$//' || true)

# 本机 wire 只需 host:port；从 BASE_URL 取端口
LISTEN_HOSTPORT="${BASE_URL#*://}"
LISTEN_HOSTPORT="${LISTEN_HOSTPORT%%/*}"
LISTEN_PORT="${LISTEN_HOSTPORT##*:}"
[[ "${LISTEN_PORT}" =~ ^[0-9]+$ ]] || die "BASE_URL must include an explicit port (got ${BASE_URL})"

if command -v lsof >/dev/null 2>&1; then
  for pid in $(lsof -nP -tiTCP:"${LISTEN_PORT}" -sTCP:LISTEN 2>/dev/null || true); do
    log "stop :${LISTEN_PORT} pid=${pid}"
    kill "${pid}" 2>/dev/null || true
  done
  sleep 0.4
fi
if [[ -f "${PID_FILE}" ]]; then
  old="$(cat "${PID_FILE}" 2>/dev/null || true)"
  if [[ -n "${old}" ]] && kill -0 "${old}" 2>/dev/null; then
    kill "${old}" 2>/dev/null || true
  fi
  rm -f "${PID_FILE}"
fi

log "start API (nohup) log=${LOG_FILE}"
(
  cd "${ROOT_DIR}"
  exec nohup dotnet run --project "${APP_PROJ}" -c Debug --no-launch-profile
) >>"${LOG_FILE}" 2>&1 &
API_PID=$!
echo "${API_PID}" >"${PID_FILE}"
disown "${API_PID}" 2>/dev/null || true

for i in $(seq 1 60); do
  if ! kill -0 "${API_PID}" 2>/dev/null; then
    tail -n 40 "${LOG_FILE}" >&2 || true
    die "API exited early (see ${LOG_FILE})"
  fi
  # 等进程处理 /health（200 就绪；503 也说明 HTTP 已起，校验阶段再判是否健康）
  code="$(curl -sS -o /dev/null -w '%{http_code}' "${BASE_URL}/health" 2>/dev/null || true)"
  if [[ "${code}" == "200" || "${code}" == "503" ]]; then
    log "http up (${i}s) health=${code} pid=${API_PID}"
    break
  fi
  if [[ "${i}" -eq 60 ]]; then
    tail -n 40 "${LOG_FILE}" >&2 || true
    die "timeout waiting for ${BASE_URL}/health"
  fi
  sleep 1
done

if [[ "${NO_PROBE}" != "true" ]]; then
  log "probe health, token exchange, and ping"
  HEALTH_CODE="$(curl -sS -o /tmp/pac-api-wire-health.json -w '%{http_code}' "${BASE_URL}/health" || true)"
  if [[ "${HEALTH_CODE}" != "200" ]]; then
    cat /tmp/pac-api-wire-health.json 2>/dev/null || true
    if [[ -z "${Postgres__Password:-}" ]]; then
      die "health HTTP ${HEALTH_CODE} (empty Postgres__Password often yields 503; set it in ${ENV_FILE} and rerun)"
    fi
    die "health HTTP ${HEALTH_CODE} (want 200: status=ok; process and DB gates must pass)"
  fi
  TOKEN="$(
    curl -fsS -X POST "${BASE_URL}/v1/auth/token" \
      -H "X-Api-Key: ${PAC_API_KEY}" \
      | jq -r '.accessToken // empty'
  )"
  [[ -n "${TOKEN}" ]] || die "empty accessToken"
  curl -fsS "${BASE_URL}/v1/ping" \
    -H "Authorization: Bearer ${TOKEN}" >/dev/null
  log "probe ok"
fi

log "API running pid=${API_PID}  stop: kill \$(cat ${PID_FILE})"
