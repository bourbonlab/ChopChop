#!/usr/bin/env bash
#
# Downloads the third-party tooling deploy.sh installs into the game:
#   - the BepInEx 5 loader, which is also what the plugins build against
#   - ConfigurationManager, the in-game settings editor (see README "Configuring")
# tools/ is gitignored, so run this after a fresh clone.
#
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="5.4.23.3"
URL="https://github.com/BepInEx/BepInEx/releases/download/v${VERSION}/BepInEx_win_x64_${VERSION}.zip"
DEST="$REPO_DIR/tools/BepInEx_win_x64"

# The BepInEx5 asset, not the IL2CPP one - Chop Chop Inc. is a Mono build.
CM_VERSION="19.0"
CM_URL="https://github.com/BepInEx/BepInEx.ConfigurationManager/releases/download/v${CM_VERSION}/BepInEx.ConfigurationManager_BepInEx5_v${CM_VERSION}.zip"
CM_DEST="$REPO_DIR/tools/ConfigurationManager"

mkdir -p "$DEST"
echo "==> Downloading BepInEx $VERSION"
curl -sSL -o /tmp/bepinex.zip "$URL"
unzip -o -q /tmp/bepinex.zip -d "$DEST"
rm -f /tmp/bepinex.zip
echo "==> Extracted to $DEST"

# Ships as a ready-made BepInEx/plugins/ConfigurationManager/ tree, so it extracts
# with the same shape deploy.sh copies into the game directory.
mkdir -p "$CM_DEST"
echo "==> Downloading ConfigurationManager $CM_VERSION"
curl -sSL -o /tmp/configmanager.zip "$CM_URL"
unzip -o -q /tmp/configmanager.zip -d "$CM_DEST"
rm -f /tmp/configmanager.zip
echo "==> Extracted to $CM_DEST"
