#!/usr/bin/env python3
"""Live group/split smoke against a running Jellyfin + Sonos plugin.

Not CI. Speakers will play audio.

Required:
  JF_URL      Jellyfin origin, e.g. http://192.168.1.10:8096
  JF_TOKEN    Jellyfin API key or session token
  PLAYER_A    Coordinator player RINCON id
  PLAYER_B    Second player RINCON id (joined, then removed)

Optional:
  ITEM_A..D   Audio item GUIDs (fetched at random when unset)
  WAIT_SECS   Pause after each Play (default 3)
  SSL_NO_VERIFY  Set to 1 to skip TLS certificate verification

Example:
  JF_URL=http://192.168.1.10:8096 \\
  JF_TOKEN=… \\
  PLAYER_A=RINCON_… \\
  PLAYER_B=RINCON_… \\
  ./scripts/live-group-smoke.py
"""

from __future__ import annotations

import json
import os
import ssl
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("JF_URL", "").rstrip("/")
TOKEN = os.environ.get("JF_TOKEN")
PLAYER_A = os.environ.get("PLAYER_A")
PLAYER_B = os.environ.get("PLAYER_B")
WAIT = float(os.environ.get("WAIT_SECS", "3"))

if not BASE:
    print("Set JF_URL to your Jellyfin origin", file=sys.stderr)
    raise SystemExit(2)
if not TOKEN:
    print("Set JF_TOKEN", file=sys.stderr)
    raise SystemExit(2)
if not PLAYER_A or not PLAYER_B:
    print("Set PLAYER_A and PLAYER_B to two Sonos player RINCON ids", file=sys.stderr)
    raise SystemExit(2)

SSL_CTX = None
if BASE.startswith("https://"):
    if os.environ.get("SSL_NO_VERIFY", "0") == "1":
        SSL_CTX = ssl._create_unverified_context()
    else:
        SSL_CTX = ssl.create_default_context()


def pick(obj: dict, *keys, default=None):
    for k in keys:
        if k in obj:
            return obj[k]
        for ak, av in obj.items():
            if str(ak).lower() == k.lower():
                return av
    return default


def api(method: str, path: str, body: dict | None = None) -> dict:
    url = BASE + path
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(
        url,
        data=data,
        method=method,
        headers={
            "Authorization": f"MediaBrowser Token={TOKEN}",
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=90, context=SSL_CTX) as resp:
            raw = resp.read().decode()
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")
        print(f"FAIL HTTP {e.code} {method} {path}\n{detail}", file=sys.stderr)
        raise SystemExit(1) from e


def q_summary(q: dict, label: str = "") -> None:
    items = pick(q, "Items", "items", default=[]) or []
    name = pick(items[0], "Name", "name", default="-") if items else "-"
    print(
        f"  {label}state={pick(q, 'State', 'state')} "
        f"pluginOwned={pick(q, 'PluginOwned', 'pluginOwned')} "
        f"track={name!r} "
        f"coord={pick(q, 'CoordinatorId', 'coordinatorId')}"
    )


def groups_summary(payload: dict) -> None:
    groups = pick(payload, "Groups", "groups", default=[]) or []
    print("  groups:")
    for g in groups:
        print(
            f"    {pick(g, 'Name', 'name')}: "
            f"id={pick(g, 'Id', 'id')} "
            f"coordinator={pick(g, 'CoordinatorId', 'coordinatorId')} "
            f"members={pick(g, 'MemberIds', 'memberIds')}"
        )


def assert_playing(q: dict, label: str) -> None:
    state = str(pick(q, "State", "state") or "").lower()
    owned = pick(q, "PluginOwned", "pluginOwned")
    if state not in ("playing", "transitioning", "paused"):
        print(f"FAIL {label}: expected playing/paused, got state={state}", file=sys.stderr)
        raise SystemExit(1)
    if owned not in (True, "true", "True"):
        print(f"FAIL {label}: expected pluginOwned=true, got {owned!r}", file=sys.stderr)
        raise SystemExit(1)
    print(f"  ok ({label})")


def assert_epoch_group_id(group_id: str | None, label: str) -> None:
    if not group_id or ":" not in str(group_id):
        print(
            f"FAIL {label}: expected live RINCON_:epoch group id, got {group_id!r}",
            file=sys.stderr,
        )
        raise SystemExit(1)
    print(f"  ok ({label}): group id has epoch ({group_id})")


def play(target: str, item: str, label: str) -> dict:
    print(f"\n== PLAY {label} → {target} ==")
    out = api(
        "POST",
        "/Sonos/Queue/Play",
        {
            "targetId": target,
            "itemIds": [item],
            "startIndex": 0,
            "startPositionTicks": 0,
        },
    )
    q_summary(out)
    time.sleep(WAIT)
    polled = api("GET", "/Sonos/Queue?" + urllib.parse.urlencode({"targetId": target}))
    q_summary(polled, label="(poll) ")
    assert_playing(polled, label)
    return polled


def stop(target: str) -> None:
    print(f"\n== STOP {target} ==")
    out = api("POST", "/Sonos/Playstate", {"targetId": target, "command": "Stop"})
    q_summary(out)


def find_group_id(payload: dict, a: str, b: str) -> str | None:
    for g in pick(payload, "Groups", "groups", default=[]) or []:
        members = [str(m) for m in (pick(g, "MemberIds", "memberIds", default=[]) or [])]
        if a in members and b in members:
            return pick(g, "Id", "id")
    return None


def player_b_group_id(payload: dict) -> str | None:
    for g in pick(payload, "Groups", "groups", default=[]) or []:
        members = [str(m) for m in (pick(g, "MemberIds", "memberIds", default=[]) or [])]
        coord = pick(g, "CoordinatorId", "coordinatorId")
        if PLAYER_B in members or coord == PLAYER_B:
            return pick(g, "Id", "id")
    for p in pick(payload, "Players", "players", default=[]) or []:
        if pick(p, "Id", "id") == PLAYER_B:
            return pick(p, "GroupId", "groupId")
    return None


def resolve_items() -> tuple[str, str, str, str]:
    if all(os.environ.get(k) for k in ("ITEM_A", "ITEM_B", "ITEM_C", "ITEM_D")):
        return os.environ["ITEM_A"], os.environ["ITEM_B"], os.environ["ITEM_C"], os.environ["ITEM_D"]

    users = api("GET", "/Users")
    if not isinstance(users, list) or not users:
        print("FAIL: no Jellyfin users", file=sys.stderr)
        raise SystemExit(1)
    uid = pick(users[0], "Id", "id")
    items = api(
        "GET",
        "/Users/"
        + uid
        + "/Items?"
        + urllib.parse.urlencode(
            {
                "IncludeItemTypes": "Audio",
                "Recursive": "true",
                "Limit": "5",
                "SortBy": "Random",
            }
        ),
    )
    rows = pick(items, "Items", "items", default=[]) or []
    ids = [pick(i, "Id", "id") for i in rows]
    if len(ids) < 4:
        print(f"FAIL: need 4 audio items, got {len(ids)}", file=sys.stderr)
        raise SystemExit(1)
    print("Tracks:", [(pick(i, "Name", "name"), pick(i, "Id", "id")) for i in rows[:4]])
    return ids[0], ids[1], ids[2], ids[3]


def main() -> None:
    print(f"JF_URL={BASE}")
    print(f"PLAYER_A={PLAYER_A}")
    print(f"PLAYER_B={PLAYER_B}")
    item_a, item_b, item_c, item_d = resolve_items()

    print("======== PRE: individual play ========")
    play(PLAYER_A, item_a, "PLAYER_A (pre)")
    play(PLAYER_B, item_b, "PLAYER_B (pre)")
    stop(PLAYER_A)
    stop(PLAYER_B)
    time.sleep(2)

    print("\n======== CREATE group PLAYER_A + PLAYER_B ========")
    created = api(
        "POST",
        "/Sonos/Groups",
        {"coordinatorId": PLAYER_A, "playerIds": [PLAYER_A, PLAYER_B]},
    )
    groups_summary(created)
    time.sleep(2)
    groups_summary(api("GET", "/Sonos/Groups"))

    print("\n======== PLAY while grouped ========")
    play(PLAYER_A, item_c, "grouped (PLAYER_A target)")
    print("  PLAYER_B queue poll (should resolve to same coordinator):")
    q_summary(api("GET", "/Sonos/Queue?" + urllib.parse.urlencode({"targetId": PLAYER_B})))

    groups_now = api("GET", "/Sonos/Groups")
    group_id = find_group_id(groups_now, PLAYER_A, PLAYER_B) or PLAYER_A
    print(f"GROUP_ID={group_id}")

    print("\n======== SPLIT: remove PLAYER_B ========")
    split = api(
        "POST",
        f"/Sonos/Groups/{urllib.parse.quote(str(group_id))}/Members",
        {"playerIdsToAdd": [], "playerIdsToRemove": [PLAYER_B]},
    )
    groups_summary(split)
    time.sleep(2)
    after = api("GET", "/Sonos/Players")
    groups_summary(after)
    # After split, PLAYER_B may briefly have a cleared GroupId until Play refreshes;
    # post Play must succeed. If a group id is present it must include :epoch.
    bg = player_b_group_id(after)
    if bg:
        assert_epoch_group_id(bg, "PLAYER_B group id after split")
    else:
        print("  note: PLAYER_B GroupId empty after split (will refresh on Play)")

    print("\n======== POST: individual play after split ========")
    play(PLAYER_A, item_a, "PLAYER_A (post)")
    play(PLAYER_B, item_d, "PLAYER_B (post)")
    stop(PLAYER_A)
    stop(PLAYER_B)

    final = api("GET", "/Sonos/Players")
    bg2 = player_b_group_id(final)
    assert_epoch_group_id(bg2, "PLAYER_B group id after post Play")

    print("\nDONE — group/split overwrite path completed")


if __name__ == "__main__":
    main()
