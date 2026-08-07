#!/usr/bin/env bash
# 本机启动 api-asp：复用 .env.asp 密钥 → 后台常驻 API → health + token + ping 探活
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

usage() {
  cat <<'USAGE'
Usage:
  wire-local.sh [--no-probe] [--base-url URL]

  1) 确保 apps/api-asp/.env.asp 有 Auth 密钥（可复用）
  2) 后台启动 api-asp（nohup，关掉终端也活着）
  3) health + 换票 + /v1/ping 探活（--no-probe 跳过）
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

[[ -f "${APP_PROJ}" ]] || die "project not found: ${APP_PROJ}"
[[ -x "${GEN_SECRETS}" ]] || chmod +x "${GEN_SECRETS}"

mkdir -p "${LOG_DIR}"

"${GEN_SECRETS}" >/dev/null
# shellcheck disable=SC1090
set -a
# shellcheck source=/dev/null
source <(grep -E '^Auth__' "${ENV_FILE}" | sed 's/\r$//')
set +a
[[ -n "${Auth__ApiKeys__0:-}" && -n "${Auth__Jwt__SigningKey:-}" ]] || die "Auth 密钥未就绪: ${ENV_FILE}"

export Auth__ApiKeys__0 Auth__Jwt__SigningKey
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-dev}"
# 监听与探活共用 BASE_URL，避免双源漂移
export ASPNETCORE_URLS="${BASE_URL}"

LISTEN_PORT="$(
  python3 - "${BASE_URL}" <<'PY'
import sys
from urllib.parse import urlparse
raw = sys.argv[1].strip()
u = urlparse(raw if "://" in raw else f"http://{raw}")
print(u.port or 80)
PY
)"

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
  if curl -fsS "${BASE_URL}/health" >/dev/null 2>&1; then
    log "health ok (${i}s) pid=${API_PID}"
    break
  fi
  if [[ "${i}" -eq 60 ]]; then
    tail -n 40 "${LOG_FILE}" >&2 || true
    die "timeout waiting for ${BASE_URL}/health"
  fi
  sleep 1
done

if [[ "${NO_PROBE}" != "true" ]]; then
  log "probe token + ping"
  TOKEN="$(
    curl -fsS -X POST "${BASE_URL}/v1/auth/token" \
      -H "X-Api-Key: ${Auth__ApiKeys__0}" \
      | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])"
  )"
  [[ -n "${TOKEN}" ]] || die "empty accessToken"
  curl -fsS "${BASE_URL}/v1/ping" \
    -H "Authorization: Bearer ${TOKEN}" >/dev/null
  log "probe ok"
fi

log "API running pid=${API_PID}  stop: kill \$(cat ${PID_FILE})"
