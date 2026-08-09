#!/usr/bin/env bash
#
# Downloads the BepInEx 5 loader used for both building against and deploying.
# tools/ is gitignored, so run this after a fresh clone.
#
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="5.4.23.3"
URL="https://github.com/BepInEx/BepInEx/releases/download/v${VERSION}/BepInEx_win_x64_${VERSION}.zip"
DEST="$REPO_DIR/tools/BepInEx_win_x64"

mkdir -p "$DEST"
echo "==> Downloading BepInEx $VERSION"
curl -sSL -o /tmp/bepinex.zip "$URL"
unzip -o -q /tmp/bepinex.zip -d "$DEST"
rm -f /tmp/bepinex.zip
echo "==> Extracted to $DEST"
