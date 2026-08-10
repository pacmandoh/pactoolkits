#!/usr/bin/env bash
# 连本机 PostgreSQL 做 API 手动回归：鉴权与变更流；有 psql 时再校验 NOTIFY
# 有 psql 且已配密码时默认跑 NOTIFY，否则跳过
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ENV_FILE="${ROOT_DIR}/apps/api-asp/.env.asp"
WIRE="${ROOT_DIR}/apps/api-asp/scripts/wire-local.sh"
BASE_URL="${API_BASE_URL:-http://127.0.0.1:5080}"
SKIP_WIRE=false
SKIP_NOTIFY=false
FAILS=0

log() { printf '[api-manual-regression] %s\n' "$*"; }
ok() { log "OK  $*"; }
fail() { log "FAIL $*"; FAILS=$((FAILS + 1)); }
die() { log "error: $*"; exit 1; }

usage() {
  cat <<'USAGE'
Usage:
  run-api-manual-regression.sh [--skip-wire] [--skip-notify] [--base-url URL]

  默认：wire-local（/health 须 200 / status=ok）后校验换票、ping、system/info、system/status、watermarks、SSE ready；
        条件允许时再校验 app_touch_watermark 与 SSE change。
  --skip-wire    假定 API 已在 BASE 上跑着（.env.asp 仍须有 PAC_API_KEY）
  --skip-notify  跳过 psql 触发与 SSE change
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-wire) SKIP_WIRE=true; shift ;;
    --skip-notify) SKIP_NOTIFY=true; shift ;;
    --base-url) BASE_URL="${2:?}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

[[ -f "${ENV_FILE}" ]] || die "missing ${ENV_FILE}（先 gen-dev-secrets / wire-local）"
[[ -x "${WIRE}" ]] || chmod +x "${WIRE}"

if [[ "${SKIP_WIRE}" != "true" ]]; then
  log "wire-local"
  "${WIRE}" --base-url "${BASE_URL}"
fi

set -a
# shellcheck disable=SC1090
source <(grep -E '^(PAC_API_KEY|Postgres__)' "${ENV_FILE}" | sed 's/\r$//')
set +a
KEY="${PAC_API_KEY:?PAC_API_KEY missing}"
PGHOST="${Postgres__Host:-localhost}"
PGPORT="${Postgres__Port:-5432}"
PGUSER="${Postgres__Username:-postgres}"
PGDATABASE="${Postgres__Database:-postgres}"
export PGPASSWORD="${Postgres__Password:-${PGPASSWORD:-}}"

need_jq() {
  command -v jq >/dev/null 2>&1 || die "jq required"
}
need_jq

http_code() {
  local method="$1" url="$2"
  shift 2
  curl -sS -o /tmp/pac-api-reg-body.json -w '%{http_code}' -X "${method}" "${url}" "$@"
}

log "=== 1 health ==="
code="$(http_code GET "${BASE_URL}/health")"
if [[ "${code}" == "200" ]] && jq -e '.status=="ok"' /tmp/pac-api-reg-body.json >/dev/null 2>&1; then
  ok "health 200 ok"
else
  fail "health code=${code} body=$(cat /tmp/pac-api-reg-body.json 2>/dev/null || true)"
fi

log "=== 2 token ==="
code="$(http_code POST "${BASE_URL}/v1/auth/token" -H "X-Api-Key: ${KEY}")"
TOKEN="$(jq -r '.accessToken // empty' /tmp/pac-api-reg-body.json 2>/dev/null || true)"
CLIENT_ID="$(jq -r '.clientId // empty' /tmp/pac-api-reg-body.json 2>/dev/null || true)"
if [[ "${code}" == "200" && -n "${TOKEN}" && -n "${CLIENT_ID}" ]]; then
  ok "token clientId=${CLIENT_ID}"
else
  fail "token code=${code}"
fi
AUTH=( -H "Authorization: Bearer ${TOKEN}" )

log "=== 3 token denied (no key) ==="
code="$(http_code POST "${BASE_URL}/v1/auth/token")"
if [[ "${code}" == "401" ]]; then
  ok "token 401 without key"
else
  fail "token without key expected 401 got ${code}"
fi

log "=== 4 ping ==="
code="$(http_code GET "${BASE_URL}/v1/ping" "${AUTH[@]}")"
if [[ "${code}" == "200" ]]; then
  ok "ping"
else
  fail "ping code=${code}"
fi

code="$(http_code GET "${BASE_URL}/v1/ping")"
if [[ "${code}" == "401" ]]; then
  ok "ping anonymous 401"
else
  fail "ping anonymous expected 401 got ${code}"
fi

log "=== 5 system/info ==="
code="$(http_code GET "${BASE_URL}/v1/system/info" "${AUTH[@]}")"
if [[ "${code}" == "200" ]] && jq -e '
  (.contractVersion | type == "string") and
  (.contractVersion | test("^[0-9]+\\.[0-9]+\\.[0-9]+$")) and
  has("apiVersion")
' /tmp/pac-api-reg-body.json >/dev/null 2>&1; then
  ok "system/info contract"
else
  fail "system/info code=${code} body=$(cat /tmp/pac-api-reg-body.json 2>/dev/null || true) (若 403 检查 client scopes 含 system.status)"
fi

log "=== 5b system/status ==="
code="$(http_code GET "${BASE_URL}/v1/system/status" "${AUTH[@]}")"
if [[ "${code}" == "200" ]] && jq -e '.status=="ok" and .database=="ok" and .schema=="ok"' /tmp/pac-api-reg-body.json >/dev/null 2>&1; then
  ok "system/status diagnostics"
else
  fail "system/status code=${code} body=$(cat /tmp/pac-api-reg-body.json 2>/dev/null || true)"
fi

log "=== 6 watermarks ==="
code="$(http_code GET "${BASE_URL}/v1/changes/watermarks" "${AUTH[@]}")"
if [[ "${code}" == "200" ]] && jq -e 'has("items")' /tmp/pac-api-reg-body.json >/dev/null 2>&1; then
  ok "watermarks items"
else
  fail "watermarks code=${code}"
fi

log "=== 7 SSE ready (+ optional heartbeat within 20s) ==="
SSE_FILE=/tmp/pac-api-reg-sse.txt
rm -f "${SSE_FILE}"
# 拉最多 18s；应有 ready；heartbeat 约 15s 后出现
set +e
curl -sS -N --max-time 18 "${BASE_URL}/v1/changes/stream" "${AUTH[@]}" >"${SSE_FILE}" 2>/dev/null
set -e
if grep -q 'event: ready' "${SSE_FILE}"; then
  ok "SSE ready"
else
  fail "SSE missing ready: $(head -c 400 "${SSE_FILE}" | tr '\n' ' ')"
fi
if grep -q 'event: heartbeat' "${SSE_FILE}"; then
  ok "SSE heartbeat"
else
  log "WARN SSE no heartbeat in 18s window（慢机可忽略一次）"
fi

if [[ "${SKIP_NOTIFY}" != "true" ]] && command -v psql >/dev/null 2>&1 && [[ -n "${PGPASSWORD}" ]]; then
  log "=== 8 NOTIFY 与 SSE change ==="
  V0="$(
    curl -sS "${BASE_URL}/v1/changes/watermarks" "${AUTH[@]}" \
      | jq -r '[.items[] | select(.topic=="inventory") | .version] | first // 0'
  )"
  rm -f "${SSE_FILE}"
  (
    sleep 1
    psql -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" -d "${PGDATABASE}" -v ON_ERROR_STOP=1 \
      -c "select app_touch_watermark('inventory');" >/dev/null
  ) &
  TOUCH_PID=$!
  set +e
  curl -sS -N --max-time 12 "${BASE_URL}/v1/changes/stream" "${AUTH[@]}" >"${SSE_FILE}" 2>/dev/null
  set -e
  wait "${TOUCH_PID}" 2>/dev/null || true
  if grep -q 'event: change' "${SSE_FILE}" && grep -q 'inventory' "${SSE_FILE}"; then
    ok "SSE change inventory"
  else
    fail "SSE missed change after app_touch_watermark（查 LISTEN / 触发器 / 同库）"
  fi
  V1="$(
    curl -sS "${BASE_URL}/v1/changes/watermarks" "${AUTH[@]}" \
      | jq -r '[.items[] | select(.topic=="inventory") | .version] | first // 0'
  )"
  if [[ "${V1}" -gt "${V0}" ]]; then
    ok "watermark inventory ${V0} -> ${V1}"
  else
    fail "watermark not bumped V0=${V0} V1=${V1}"
  fi
else
  log "skip NOTIFY（无 psql 或 Postgres__Password 空）"
fi

log "=== result fails=${FAILS} ==="
if [[ "${FAILS}" -gt 0 ]]; then
  exit 1
fi
ok "all scripted checks passed"
exit 0
