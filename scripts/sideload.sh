#!/usr/bin/env bash
set -euo pipefail

# Build Jellyfin.Plugin.Sonos and copy it into:
# ${JELLYFIN_ROOT}/data/plugins/Sonos_<version>/
#
# JELLYFIN_ROOT is the Jellyfin config/data directory (the folder that contains data/plugins).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

if [ -z "${JELLYFIN_ROOT:-}" ]; then
  echo "Set JELLYFIN_ROOT to the Jellyfin config directory (the folder that contains data/plugins)." >&2
  echo "Example: JELLYFIN_ROOT=/var/lib/jellyfin $0" >&2
  exit 1
fi

VERSION="$(grep -m1 '<Version>' "${PLUGIN_ROOT}/Directory.Build.props" | sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/')"
PLUGIN_NAME="Sonos"
DEST="${JELLYFIN_ROOT}/data/plugins/${PLUGIN_NAME}_${VERSION}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required. Install .NET 9: https://dotnet.microsoft.com/download/dotnet/9.0" >&2
  exit 1
fi

echo "Building ${PLUGIN_NAME} ${VERSION}..."
dotnet publish "${PLUGIN_ROOT}/src/Jellyfin.Plugin.Sonos/Jellyfin.Plugin.Sonos.csproj" \
  --configuration Release \
  --output "${PLUGIN_ROOT}/artifacts/publish" \
  --nologo \
  -p:UseAppHost=false

mkdir -p "${DEST}"
cp "${PLUGIN_ROOT}/artifacts/publish/Jellyfin.Plugin.Sonos.dll" "${DEST}/"

cat > "${DEST}/meta.json" <<EOF
{
  "category": "Music",
  "changelog": "Include Play To supportedCommands so the now-playing bar does not crash after reload",
  "description": "Play Jellyfin music to Sonos S2 speakers using native queueing.",
  "guid": "cef190c1-177d-4018-8271-7a3aa6033a3f",
  "name": "${PLUGIN_NAME}",
  "overview": "Play Jellyfin music to Sonos S2 speakers",
  "owner": "adamdunkley",
  "targetAbi": "10.11.0.0",
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%S.0000000Z")",
  "version": "${VERSION}",
  "status": "Active",
  "autoUpdate": false,
  "assemblies": []
}
EOF

echo "Sideloaded to ${DEST}"
echo "Restart Jellyfin for the plugin to load."
