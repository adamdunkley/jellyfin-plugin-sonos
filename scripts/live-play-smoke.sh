#!/usr/bin/env bash
# Temporary live-speaker smoke test against a running Jellyfin + Sonos plugin.
# Not part of CI. Speakers will play audio.
#
# Required:
#   JF_URL     Jellyfin origin, e.g. http://192.168.1.10:8096
#   JF_TOKEN   Jellyfin API key or session token
#   ITEM_A     Jellyfin audio item GUID
#   ITEM_B     Second audio GUID (used to verify Queue/Play overwrites)
#
# Optional:
#   TARGET_ID  Player or group id (RINCON_…). If unset, lists players and exits.
#   WAIT_SECS  Seconds to wait after each Play before polling (default 3)
#   STOP       Set to 1 to Stop the speaker at the end
#
# Example:
#   JF_URL=http://192.168.1.10:8096 \
#   JF_TOKEN=… \
#   TARGET_ID=RINCON_… \
#   ITEM_A=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee \
#   ITEM_B=bbbbbbbb-cccc-dddd-eeee-ffffffffffff \
#   ./scripts/live-play-smoke.sh

set -euo pipefail

JF_URL="${JF_URL:?set JF_URL to your Jellyfin origin}"
JF_TOKEN="${JF_TOKEN:?set JF_TOKEN to a Jellyfin API key or session token}"
WAIT_SECS="${WAIT_SECS:-3}"
STOP="${STOP:-0}"

AUTH_HEADER="Authorization: MediaBrowser Token=${JF_TOKEN}"
BASE="${JF_URL%/}"

red() { printf '\033[31m%s\033[0m\n' "$*" >&2; }
green() { printf '\033[32m%s\033[0m\n' "$*"; }
info() { printf '%s\n' "$*"; }

# Returns body on stdout; dies with status + body on non-2xx.
api() {
  local method=$1 path=$2
  shift 2
  local tmp status
  local curl_opts=(-sS)
  if [[ "${SSL_NO_VERIFY:-0}" == "1" ]]; then
    curl_opts+=(-k)
  fi
  tmp="$(mktemp)"
  status="$(curl "${curl_opts[@]}" -o "$tmp" -w '%{http_code}' \
    -X "$method" \
    -H "$AUTH_HEADER" \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json' \
    "$@" \
    "${BASE}${path}")" || true
  if [[ "$status" != 2* ]]; then
    red "HTTP $status ${method} ${path}"
    cat "$tmp" >&2 || true
    echo >&2
    rm -f "$tmp"
    exit 1
  fi
  cat "$tmp"
  rm -f "$tmp"
}

json_field() {
  local json=$1 field=$2
  python3 -c '
import json, sys
data = json.loads(sys.argv[1])
field = sys.argv[2]

def get(obj, key):
    if isinstance(obj, list):
        return obj[int(key)]
    if not isinstance(obj, dict):
        return None
    if key in obj:
        return obj[key]
    # Jellyfin may return PascalCase
    pascal = key[:1].upper() + key[1:] if key else key
    if pascal in obj:
        return obj[pascal]
    lower = {str(k).lower(): v for k, v in obj.items()}
    return lower.get(key.lower())

cur = data
for part in field.split("."):
    cur = get(cur, part)
    if cur is None:
        break
print("" if cur is None else cur)
' "$json" "$field"
}

json_pretty() {
  python3 -m json.tool
}

info "== GET /Sonos/Players =="
players_json="$(api GET /Sonos/Players)"
echo "$players_json" | json_pretty

if [[ -z "${TARGET_ID:-}" ]]; then
  info
  info "Set TARGET_ID to a player id (or group id) from the list above, plus ITEM_A and ITEM_B."
  exit 0
fi

ITEM_A="${ITEM_A:?set ITEM_A to an audio item GUID}"
ITEM_B="${ITEM_B:?set ITEM_B to a different audio item GUID}"

play() {
  local item=$1 label=$2
  info >&2
  info "== POST /Sonos/Queue/Play ($label) ==" >&2
  local body
  body="$(python3 -c 'import json,sys; print(json.dumps({
    "targetId": sys.argv[1],
    "itemIds": [sys.argv[2]],
    "startIndex": 0,
    "startPositionTicks": 0
  }))' "$TARGET_ID" "$item")"
  api POST /Sonos/Queue/Play -d "$body"
}

poll() {
  info >&2
  info "== GET /Sonos/Queue ==" >&2
  local q
  q="$(api GET "/Sonos/Queue?targetId=${TARGET_ID}")"
  echo "$q" | json_pretty >&2
  printf '%s' "$q"
}

assert_playing() {
  local q=$1 expected_item=$2 label=$3
  local state owned current version
  state="$(json_field "$q" state)"
  owned="$(json_field "$q" pluginOwned)"
  current="$(json_field "$q" items.0.itemId)"
  version="$(json_field "$q" queueVersion)"

  info
  info "-- check ($label): state=$state pluginOwned=$owned itemId=$current queueVersion=$version"

  local ok=1
  case "$(printf '%s' "$state" | tr '[:upper:]' '[:lower:]')" in
    playing|transitioning|paused) ;;
    *) red "expected Playing/Transitioning/Paused, got state=$state"; ok=0 ;;
  esac
  if [[ "$owned" != "True" && "$owned" != "true" ]]; then
    red "expected pluginOwned=true, got $owned"
    ok=0
  fi
  # Compare GUIDs case-insensitively, with or without dashes.
  local want have
  want="$(printf '%s' "$expected_item" | tr -d '-' | tr '[:upper:]' '[:lower:]')"
  have="$(printf '%s' "$current" | tr -d '-' | tr '[:upper:]' '[:lower:]')"
  if [[ "$want" != "$have" ]]; then
    red "expected current itemId=$expected_item, got $current"
    ok=0
  fi
  if [[ "$ok" -ne 1 ]]; then
    exit 1
  fi
  green "ok ($label)"
}

q1="$(play "$ITEM_A" "track A")"
echo "$q1" | json_pretty
sleep "$WAIT_SECS"
q1="$(poll)"
assert_playing "$q1" "$ITEM_A" "after first Play"
v1="$(json_field "$q1" queueVersion)"

q2="$(play "$ITEM_B" "track B overwrite")"
echo "$q2" | json_pretty
sleep "$WAIT_SECS"
q2="$(poll)"
assert_playing "$q2" "$ITEM_B" "after overwrite Play"
v2="$(json_field "$q2" queueVersion)"

if [[ "$v1" == "$v2" ]]; then
  red "queueVersion did not change after overwrite ($v1)"
  exit 1
fi
green "queueVersion bumped: $v1 -> $v2"

if [[ "$STOP" == "1" ]]; then
  info
  info "== POST /Sonos/Playstate Stop =="
  body="$(python3 -c 'import json,sys; print(json.dumps({
    "targetId": sys.argv[1],
    "command": "Stop"
  }))' "$TARGET_ID")"
  api POST /Sonos/Playstate -d "$body" | json_pretty
fi

info
green "live-play-smoke: overwrite path looks healthy"
