#!/usr/bin/env bash
set -euo pipefail

# Build Jellyfin.Plugin.Sonos for one ABI and copy it into:
# ${JELLYFIN_ROOT}/data/plugins/Sonos_<version>/
#
# JELLYFIN_ROOT is the Jellyfin config/data directory (the folder that contains data/plugins).
# JELLYFIN_ABI selects the build: 12.0 (default, net10) or 10.11 (net9).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

if [ -z "${JELLYFIN_ROOT:-}" ]; then
  echo "Set JELLYFIN_ROOT to the Jellyfin config directory (the folder that contains data/plugins)." >&2
  echo "Example: JELLYFIN_ROOT=/var/lib/jellyfin $0" >&2
  exit 1
fi

ABI="${JELLYFIN_ABI:-12.0}"
case "${ABI}" in
  12.0|12)
    TFM="net10.0"
    TARGET_ABI="12.0.0.0"
    ABI="12.0"
    ;;
  10.11|10)
    TFM="net9.0"
    TARGET_ABI="10.11.0.0"
    ABI="10.11"
    ;;
  *)
    echo "JELLYFIN_ABI must be 12.0 or 10.11 (got: ${ABI})" >&2
    exit 1
    ;;
esac

VERSION="$(grep -m1 '<Version>' "${PLUGIN_ROOT}/Directory.Build.props" | sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/')"
PLUGIN_NAME="Sonos"
DEST="${JELLYFIN_ROOT}/data/plugins/${PLUGIN_NAME}_${VERSION}"
PUBLISH_DIR="${PLUGIN_ROOT}/artifacts/publish/${TFM}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required. Install .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
fi

echo "Building ${PLUGIN_NAME} ${VERSION} for Jellyfin ABI ${ABI} (${TFM})..."
RESTORE_ARGS=(--configuration Release --framework "${TFM}" --output "${PUBLISH_DIR}" --nologo -p:UseAppHost=false)
if [ "${SKIP_RESTORE:-0}" = "1" ]; then
  RESTORE_ARGS+=(--no-restore -p:RunAnalyzers=false -p:EnableNETAnalyzers=false)
fi
dotnet publish "${PLUGIN_ROOT}/src/Jellyfin.Plugin.Sonos/Jellyfin.Plugin.Sonos.csproj" \
  "${RESTORE_ARGS[@]}"

mkdir -p "${DEST}"
cp "${PUBLISH_DIR}/Jellyfin.Plugin.Sonos.dll" "${DEST}/"

cat > "${DEST}/meta.json" <<EOF
{
  "category": "Music",
  "changelog": "Dual-support Jellyfin 10.11 and 12.x; fix ReportCapabilities for JF12",
  "description": "Play Jellyfin music to Sonos S2 speakers using native queueing.",
  "guid": "cef190c1-177d-4018-8271-7a3aa6033a3f",
  "name": "${PLUGIN_NAME}",
  "overview": "Play Jellyfin music to Sonos S2 speakers",
  "owner": "adamdunkley",
  "targetAbi": "${TARGET_ABI}",
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%S.0000000Z")",
  "version": "${VERSION}",
  "status": "Active",
  "autoUpdate": false,
  "assemblies": []
}
EOF

echo "Sideloaded to ${DEST} (targetAbi ${TARGET_ABI})"
echo "Restart Jellyfin for the plugin to load."
