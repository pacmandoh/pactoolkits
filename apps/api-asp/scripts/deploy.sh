#!/usr/bin/env bash
# PacAPI 进程切换：从 Feed 快照解包并改 current；不发快照、不迁库、不写密钥
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_TEMPLATE="$SCRIPT_DIR/../deploy/pactoolkits-api.service"

SEMVER_RE='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-beta\.(0|[1-9][0-9]*))?$'

DEFAULT_ROOT="/opt/pactoolkits/api"
DEFAULT_UNIT="pactoolkits-api"
DEFAULT_ENV_FILE="/etc/pactoolkits/.env.asp"
DEFAULT_HEALTH_URL="http://127.0.0.1:5080/health"
DEFAULT_DOTNET="/usr/bin/dotnet"
HEALTH_ATTEMPTS=20

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

usage() {
  cat << 'USAGE'
Usage:
  deploy.sh <command> [options]

Commands:
  doctor         Check tools, install root, env file, and unit
  status         Show current/previous pointers, unit, and /health
  apply          Verify a Feed snapshot, extract, switch current, restart, probe /health
  rollback       Switch current back to previous, restart, probe /health
  install-unit   Install the systemd unit (does not start the process)

Options:
  --snapshot DIR       Feed snapshot directory (tar.gz + SHA256SUMS + release.json)
  --feed ROOT          Feed component root (.../pactoolkits or .../pactoolkits-test)
  --channel current|beta   With --feed, follow api/current or api/beta
  --root DIR           Install root (default /opt/pactoolkits/api)
  --unit NAME          systemd unit name without .service (default pactoolkits-api)
  --env-file PATH      systemd EnvironmentFile (default /etc/pactoolkits/.env.asp)
  --health-url URL     Probe URL (default http://127.0.0.1:5080/health)
  --skip-service       Extract and switch pointers only; do not touch systemd
  --dry-run            Verify and print the plan; do not write
  -h, --help

apply does not publish Feed snapshots, migrate PostgreSQL, or write secrets.
USAGE
}

log() {
  printf '%s\n' "$*"
}

require_cmd() {
  command -v "$1" > /dev/null 2>&1 || die "required command not found: $1"
}

absolute_dir() {
  local path="$1"
  local label="$2"
  [[ "$path" == /* ]] || die "$label must be an absolute path: $path"
  [[ "$path" != *'/..'* && "$path" != '..'* ]] || die "$label must not contain ..: $path"
}

verify_sums() {
  local dir="$1"
  [[ -f "$dir/SHA256SUMS" ]] || die "SHA256SUMS missing: $dir/SHA256SUMS"
  if command -v sha256sum > /dev/null 2>&1; then
    (cd "$dir" && sha256sum -c SHA256SUMS)
  else
    (cd "$dir" && shasum -a 256 -c SHA256SUMS)
  fi
}

atomic_link() {
  local target="$1"
  local link="$2"
  local tmp="${link}.new.$$"
  local old="${link}.old.$$"
  ln -s "$target" "$tmp"
  # 已有 symlink 若指向目录，mv 会跟进目录；先挪走旧 symlink
  if [[ -L "$link" || -e "$link" ]]; then
    mv -f "$link" "$old"
    if ! mv -f "$tmp" "$link"; then
      mv -f "$old" "$link"
      rm -f "$tmp"
      die "failed to update symlink $link"
    fi
    rm -f "$old"
  else
    mv -f "$tmp" "$link"
  fi
}

current_target() {
  local root="$1"
  if [[ -L "$root/current" ]]; then
    readlink "$root/current"
  else
    printf ''
  fi
}

previous_target() {
  local root="$1"
  if [[ -L "$root/previous" ]]; then
    readlink "$root/previous"
  else
    printf ''
  fi
}

resolve_snapshot() {
  if [[ -n "$SNAPSHOT" && -n "$FEED_ROOT" ]]; then
    die "use either --snapshot or --feed, not both"
  fi
  if [[ -n "$SNAPSHOT" ]]; then
    absolute_dir "$SNAPSHOT" "--snapshot"
    [[ -d "$SNAPSHOT" ]] || die "snapshot directory not found: $SNAPSHOT"
    printf '%s\n' "$SNAPSHOT"
    return
  fi
  if [[ -n "$FEED_ROOT" ]]; then
    [[ -n "$CHANNEL" ]] || die "--feed requires --channel current|beta"
    absolute_dir "$FEED_ROOT" "--feed"
    local pointer="$FEED_ROOT/api/$CHANNEL"
    [[ -L "$pointer" ]] || die "Feed pointer is not a symlink: $pointer"
    local rel
    rel="$(readlink "$pointer")"
    [[ "$rel" == releases/* ]] || die "Feed pointer must be relative releases/<version>: $pointer -> $rel"
    local resolved="$FEED_ROOT/api/$rel"
    [[ -d "$resolved" ]] || die "Feed snapshot missing: $resolved"
    printf '%s\n' "$resolved"
    return
  fi
  die "apply requires --snapshot DIR or --feed ROOT --channel current|beta"
}

read_snapshot_version() {
  local snapshot="$1"
  [[ -f "$snapshot/release.json" ]] || die "release.json missing: $snapshot/release.json"
  local version
  version="$(jq -r '.version // empty' "$snapshot/release.json")"
  [[ "$version" =~ $SEMVER_RE ]] || die "invalid release.json version: $version"
  printf '%s\n' "$version"
}

snapshot_tarball() {
  local snapshot="$1"
  local version="$2"
  local tar_path="$snapshot/PacToolkits-Api-${version}.tar.gz"
  [[ -f "$tar_path" ]] || die "API tarball missing: $tar_path"
  printf '%s\n' "$tar_path"
}

restart_unit() {
  systemctl restart "$UNIT"
}

wait_health() {
  local i http
  for ((i = 1; i <= HEALTH_ATTEMPTS; i++)); do
    http="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 2 "$HEALTH_URL" || true)"
    if [[ "$http" == "200" ]]; then
      return 0
    fi
    sleep 1
  done
  return 1
}

maybe_restart_and_probe() {
  if [[ "$SKIP_SERVICE" == "true" ]]; then
    return 0
  fi
  restart_unit
  wait_health || die "/health did not return 200 at $HEALTH_URL"
}

restore_current() {
  local root="$1"
  local previous="$2"
  [[ -n "$previous" ]] || return 0
  atomic_link "$previous" "$root/current"
}

COMMAND=""
SNAPSHOT=""
FEED_ROOT=""
CHANNEL=""
INSTALL_ROOT="$DEFAULT_ROOT"
UNIT="$DEFAULT_UNIT"
ENV_FILE="$DEFAULT_ENV_FILE"
HEALTH_URL="$DEFAULT_HEALTH_URL"
SKIP_SERVICE="false"
DRY_RUN="false"

parse_args() {
  if [[ $# -lt 1 ]]; then
    usage
    exit 1
  fi
  case "$1" in
    -h | --help)
      usage
      exit 0
      ;;
    doctor | status | apply | rollback | install-unit)
      COMMAND="$1"
      shift
      ;;
    *)
      die "unknown command: $1"
      ;;
  esac

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --snapshot)
        SNAPSHOT="${2:-}"
        shift 2
        ;;
      --feed)
        FEED_ROOT="${2:-}"
        shift 2
        ;;
      --channel)
        CHANNEL="${2:-}"
        shift 2
        ;;
      --root)
        INSTALL_ROOT="${2:-}"
        shift 2
        ;;
      --unit)
        UNIT="${2:-}"
        shift 2
        ;;
      --env-file)
        ENV_FILE="${2:-}"
        shift 2
        ;;
      --health-url)
        HEALTH_URL="${2:-}"
        shift 2
        ;;
      --skip-service)
        SKIP_SERVICE="true"
        shift
        ;;
      --dry-run)
        DRY_RUN="true"
        shift
        ;;
      -h | --help)
        usage
        exit 0
        ;;
      *)
        die "unknown argument: $1"
        ;;
    esac
  done

  case "$CHANNEL" in
    "" | current | beta) ;;
    *)
      die "--channel must be current or beta"
      ;;
  esac
  [[ -n "$UNIT" && "$UNIT" != *'/'* && "$UNIT" != *.service ]] \
    || die "invalid --unit: $UNIT"
  absolute_dir "$INSTALL_ROOT" "--root"
  absolute_dir "$(dirname "$ENV_FILE")" "--env-file directory"
}

doctor() {
  require_cmd jq
  require_cmd tar
  if [[ "$SKIP_SERVICE" != "true" ]]; then
    require_cmd curl
    require_cmd systemctl
    [[ -x "$DEFAULT_DOTNET" ]] \
      || die "dotnet runtime missing: $DEFAULT_DOTNET"
    [[ -f "$ENV_FILE" ]] || die "env file missing: $ENV_FILE"
    [[ -f "/etc/systemd/system/${UNIT}.service" || -f "/lib/systemd/system/${UNIT}.service" ]] \
      || die "systemd unit missing: ${UNIT}.service (run install-unit)"
    if ! id pactoolkits > /dev/null 2>&1; then
      die "system user pactoolkits does not exist"
    fi
  fi
  [[ -d "$INSTALL_ROOT" || -d "$(dirname "$INSTALL_ROOT")" ]] \
    || die "install root parent missing: $(dirname "$INSTALL_ROOT")"
  log "doctor OK"
}

status() {
  local current previous
  current="$(current_target "$INSTALL_ROOT")"
  previous="$(previous_target "$INSTALL_ROOT")"
  log "root: $INSTALL_ROOT"
  if [[ -n "$current" ]]; then
    log "current: $current"
  else
    log "current: (none)"
  fi
  if [[ -n "$previous" ]]; then
    log "previous: $previous"
  else
    log "previous: (none)"
  fi
  if [[ "$SKIP_SERVICE" == "true" ]]; then
    return 0
  fi
  if systemctl is-active --quiet "$UNIT"; then
    log "unit: $UNIT active"
  else
    log "unit: $UNIT inactive"
  fi
  local http
  http="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 2 "$HEALTH_URL" || true)"
  log "health: HTTP ${http:-unreachable}"
}

extract_release() {
  local snapshot="$1"
  local version="$2"
  local dest="$INSTALL_ROOT/releases/$version"
  local tar_path
  tar_path="$(snapshot_tarball "$snapshot" "$version")"

  if [[ -d "$dest" ]]; then
    [[ -f "$dest/PacToolkits.Api.dll" ]] \
      || die "existing release is incomplete: $dest"
    return 0
  fi
  [[ ! -e "$dest" ]] || die "release path exists and is not a directory: $dest"

  mkdir -p "$INSTALL_ROOT/releases"
  local incoming
  incoming="$(mktemp -d "$INSTALL_ROOT/releases/.incoming.XXXXXX")"
  if ! tar -xzf "$tar_path" -C "$incoming"; then
    rm -rf "$incoming"
    die "failed to extract $tar_path"
  fi
  if [[ ! -f "$incoming/PacToolkits.Api.dll" ]]; then
    rm -rf "$incoming"
    die "tarball missing PacToolkits.Api.dll: $tar_path"
  fi
  cp "$snapshot/release.json" "$incoming/release.json"
  mv "$incoming" "$dest"
}

apply() {
  local snapshot version dest rel old
  snapshot="$(resolve_snapshot)"
  verify_sums "$snapshot" > /dev/null
  version="$(read_snapshot_version "$snapshot")"
  snapshot_tarball "$snapshot" "$version" > /dev/null
  grep -Fq "PacToolkits-Api-${version}.tar.gz" "$snapshot/SHA256SUMS" \
    || die "SHA256SUMS missing PacToolkits-Api-${version}.tar.gz"
  dest="$INSTALL_ROOT/releases/$version"
  rel="releases/$version"
  old="$(current_target "$INSTALL_ROOT")"

  log "snapshot: $snapshot"
  log "version: $version"
  log "dest: $dest"
  if [[ -n "$old" ]]; then
    log "current: $old"
  else
    log "current: (none)"
  fi

  if [[ "$DRY_RUN" == "true" ]]; then
    log "dry-run: no changes"
    return 0
  fi

  extract_release "$snapshot" "$version"
  atomic_link "$rel" "$INSTALL_ROOT/current"
  if [[ -n "$old" && "$old" != "$rel" ]]; then
    atomic_link "$old" "$INSTALL_ROOT/previous"
  fi

  if [[ "$SKIP_SERVICE" == "true" ]]; then
    log "applied $version (service skipped)"
    return 0
  fi

  if restart_unit && wait_health; then
    log "applied $version"
    return 0
  fi

  if [[ -n "$old" ]]; then
    restore_current "$INSTALL_ROOT" "$old"
    restart_unit || true
    die "apply failed; restored $old"
  fi
  rm -f "$INSTALL_ROOT/current"
  die "apply failed; removed current"
}

rollback() {
  local current previous
  current="$(current_target "$INSTALL_ROOT")"
  previous="$(previous_target "$INSTALL_ROOT")"
  [[ -n "$previous" ]] || die "no previous release to roll back to"
  [[ "$previous" != "$current" ]] || die "previous already matches current: $current"

  log "current: ${current:-none}"
  log "previous: $previous"
  if [[ "$DRY_RUN" == "true" ]]; then
    log "dry-run: no changes"
    return 0
  fi

  atomic_link "$previous" "$INSTALL_ROOT/current"
  if [[ -n "$current" ]]; then
    atomic_link "$current" "$INSTALL_ROOT/previous"
  fi
  maybe_restart_and_probe
  log "rolled back to $previous"
}

install_unit() {
  [[ "$SKIP_SERVICE" == "true" ]] && die "install-unit cannot use --skip-service"
  [[ -f "$UNIT_TEMPLATE" ]] || die "unit template missing: $UNIT_TEMPLATE"
  local unit_text
  unit_text="$(
    sed \
      -e "s|^WorkingDirectory=.*|WorkingDirectory=${INSTALL_ROOT}/current|" \
      -e "s|^ExecStart=.*|ExecStart=${DEFAULT_DOTNET} ${INSTALL_ROOT}/current/PacToolkits.Api.dll|" \
      -e "s|^EnvironmentFile=.*|EnvironmentFile=${ENV_FILE}|" \
      "$UNIT_TEMPLATE"
  )"
  if [[ "$DRY_RUN" == "true" ]]; then
    log "dry-run: would install /etc/systemd/system/${UNIT}.service"
    printf '%s\n' "$unit_text"
    return 0
  fi
  local tmp
  tmp="$(mktemp)"
  printf '%s\n' "$unit_text" > "$tmp"
  install -m 644 "$tmp" "/etc/systemd/system/${UNIT}.service"
  rm -f "$tmp"
  systemctl daemon-reload
  systemctl enable "$UNIT"
  log "installed ${UNIT}.service"
}

parse_args "$@"
case "$COMMAND" in
  doctor) doctor ;;
  status) status ;;
  apply) apply ;;
  rollback) rollback ;;
  install-unit) install_unit ;;
esac
