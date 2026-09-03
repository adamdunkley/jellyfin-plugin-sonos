#!/usr/bin/env bash
set -euo pipefail

# Publish Jellyfin.Plugin.Sonos and write a catalog zip:
#   artifacts/sonos_<version>.zip
#   artifacts/sonos_<version>.zip.md5
#
# The zip contains Jellyfin.Plugin.Sonos.dll and meta.json.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

VERSION="$(grep -m1 '<Version>' "${PLUGIN_ROOT}/Directory.Build.props" | sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/')"
PLUGIN_NAME="Sonos"
SLUG="sonos"
PUBLISH_DIR="${PLUGIN_ROOT}/artifacts/publish"
STAGING_DIR="${PLUGIN_ROOT}/artifacts/staging"
ZIP_NAME="${SLUG}_${VERSION}.zip"
ZIP_PATH="${PLUGIN_ROOT}/artifacts/${ZIP_NAME}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required. Install .NET 9: https://dotnet.microsoft.com/download/dotnet/9.0" >&2
  exit 1
fi

echo "Building ${PLUGIN_NAME} ${VERSION}..."
dotnet publish "${PLUGIN_ROOT}/src/Jellyfin.Plugin.Sonos/Jellyfin.Plugin.Sonos.csproj" \
  --configuration Release \
  --output "${PUBLISH_DIR}" \
  --nologo \
  -p:UseAppHost=false

rm -rf "${STAGING_DIR}"
mkdir -p "${STAGING_DIR}"
cp "${PUBLISH_DIR}/Jellyfin.Plugin.Sonos.dll" "${STAGING_DIR}/"

cat > "${STAGING_DIR}/meta.json" <<EOF
{
  "category": "Music",
  "changelog": "Web UI: speaker button, Cast Play To sessions, grouping panel",
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

rm -f "${ZIP_PATH}" "${ZIP_PATH}.md5"
python3 - "${STAGING_DIR}" "${ZIP_PATH}" <<'PY'
import sys
import zipfile
from pathlib import Path

staging = Path(sys.argv[1])
zip_path = Path(sys.argv[2])
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
    for path in sorted(staging.iterdir()):
        zf.write(path, path.name)
PY

if command -v md5sum >/dev/null 2>&1; then
  (cd "${PLUGIN_ROOT}/artifacts" && md5sum "${ZIP_NAME}" > "${ZIP_NAME}.md5")
else
  md5 -r "${ZIP_PATH}" > "${ZIP_PATH}.md5"
fi

echo "Packaged ${ZIP_PATH}"
echo "Checksum ${ZIP_PATH}.md5"
