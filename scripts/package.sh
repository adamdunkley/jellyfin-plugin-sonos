#!/usr/bin/env bash
set -euo pipefail

# Publish Jellyfin.Plugin.Sonos for both ABIs:
#   artifacts/sonos_<version>_10.11.zip  (net9.0, targetAbi 10.11.0.0)
#   artifacts/sonos_<version>_12.0.zip   (net10.0, targetAbi 12.0.0.0)
#   plus .md5 checksums
#
# Each zip contains Jellyfin.Plugin.Sonos.dll and meta.json.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

VERSION="$(grep -m1 '<Version>' "${PLUGIN_ROOT}/Directory.Build.props" | sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/')"
PLUGIN_NAME="Sonos"
SLUG="sonos"
PUBLISH_ROOT="${PLUGIN_ROOT}/artifacts/publish"
STAGING_ROOT="${PLUGIN_ROOT}/artifacts/staging"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required. Install .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
fi

package_one() {
  local tfm="$1"
  local abi="$2"
  local abi_label="$3"
  local framework="$tfm"
  local publish_dir="${PUBLISH_ROOT}/${tfm}"
  local staging_dir="${STAGING_ROOT}/${abi_label}"
  local zip_name="${SLUG}_${VERSION}_${abi_label}.zip"
  local zip_path="${PLUGIN_ROOT}/artifacts/${zip_name}"

  echo "Building ${PLUGIN_NAME} ${VERSION} (${tfm}, targetAbi ${abi})..."
  # Force nuget.org so a stale/local nuget.config (e.g. on an old release tag) cannot break CI.
  # Skip analyzers on publish: SDK 10 surfaces CA1873/CA2025 as errors under TreatWarningsAsErrors.
  dotnet publish "${PLUGIN_ROOT}/src/Jellyfin.Plugin.Sonos/Jellyfin.Plugin.Sonos.csproj" \
    --configuration Release \
    --framework "${tfm}" \
    --output "${publish_dir}" \
    --nologo \
    --source "https://api.nuget.org/v3/index.json" \
    -p:UseAppHost=false \
    -p:RunAnalyzers=false \
    -p:EnableNETAnalyzers=false

  rm -rf "${staging_dir}"
  mkdir -p "${staging_dir}"
  cp "${publish_dir}/Jellyfin.Plugin.Sonos.dll" "${staging_dir}/"

  cat > "${staging_dir}/meta.json" <<EOF
{
  "category": "Music",
  "changelog": "Adds POST /Sonos/Queue/Clear for cast-back to local: stops the speaker, clears the Cloud Queue session (and residual Sonos-app source/artwork), and wipes the plugin queue so rooms show nothing queued.",
  "description": "Play Jellyfin music to Sonos S2 speakers using native queueing.",
  "guid": "cef190c1-177d-4018-8271-7a3aa6033a3f",
  "name": "${PLUGIN_NAME}",
  "overview": "Play Jellyfin music to Sonos S2 speakers",
  "owner": "adamdunkley",
  "targetAbi": "${abi}",
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%S.0000000Z")",
  "version": "${VERSION}",
  "status": "Active",
  "autoUpdate": false,
  "assemblies": []
}
EOF

  rm -f "${zip_path}" "${zip_path}.md5"
  python3 - "${staging_dir}" "${zip_path}" <<'PY'
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
    (cd "${PLUGIN_ROOT}/artifacts" && md5sum "${zip_name}" > "${zip_name}.md5")
  else
    md5 -r "${zip_path}" > "${zip_path}.md5"
  fi

  echo "Packaged ${zip_path}"
  echo "Checksum ${zip_path}.md5"
}

package_one "net9.0" "10.11.0.0" "10.11"
package_one "net10.0" "12.0.0.0" "12.0"
