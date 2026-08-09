#!/usr/bin/env bash
#
# Builds the mod and installs it into the game directory.
# On first run it also installs the BepInEx loader itself.
#
# Usage:
#   scripts/deploy.sh                 # build + install plugin (and BepInEx if missing)
#   scripts/deploy.sh --with-bepinex  # force-reinstall the BepInEx runtime too
#
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME_DIR="${CHOPCHOP_DIR:-/mnt/e/Program Files/Steam/steamapps/common/ChopChopInc}"
BEPINEX_SRC="$REPO_DIR/tools/BepInEx_win_x64"
CM_SRC="$REPO_DIR/tools/ConfigurationManager"
CM_CFG="$GAME_DIR/BepInEx/config/com.bepis.bepinex.configurationmanager.cfg"

FORCE_BEPINEX=0
[[ "${1:-}" == "--with-bepinex" ]] && FORCE_BEPINEX=1

if [[ ! -f "$GAME_DIR/ChopChopInc.exe" ]]; then
  echo "error: ChopChopInc.exe not found in '$GAME_DIR'" >&2
  echo "       set CHOPCHOP_DIR to your install path and retry." >&2
  exit 1
fi

# --- BepInEx runtime -------------------------------------------------------
if [[ $FORCE_BEPINEX -eq 1 || ! -f "$GAME_DIR/winhttp.dll" ]]; then
  if [[ ! -d "$BEPINEX_SRC" ]]; then
    echo "error: BepInEx not found at '$BEPINEX_SRC'. Run scripts/fetch-bepinex.sh first." >&2
    exit 1
  fi
  echo "==> Installing BepInEx runtime into the game directory"
  cp -r "$BEPINEX_SRC/BepInEx" "$GAME_DIR/"
  cp "$BEPINEX_SRC/winhttp.dll" "$BEPINEX_SRC/doorstop_config.ini" "$BEPINEX_SRC/.doorstop_version" "$GAME_DIR/"
else
  echo "==> BepInEx already installed (winhttp.dll present), skipping runtime"
fi

# --- ConfigurationManager (in-game settings editor) ------------------------
if [[ -d "$CM_SRC" ]]; then
  echo "==> Installing ConfigurationManager"
  cp -r "$CM_SRC/BepInEx" "$GAME_DIR/"

  # Seed the hotkey as HOME before first launch. Only when the file is absent, so a
  # rebind made in-game (which BepInEx writes straight back here) is never clobbered.
  # BepInEx fills in the remaining defaults on the next run.
  if [[ ! -f "$CM_CFG" ]]; then
    mkdir -p "$(dirname "$CM_CFG")"
    cat > "$CM_CFG" <<'EOF'
[Keyboard shortcuts]

## The shortcut used to toggle the config manager window on and off.
# Setting type: KeyboardShortcut
# Default value: F1
Show config manager = Home
EOF
    echo "    hotkey set to HOME"
  else
    echo "    keeping existing config (hotkey unchanged)"
  fi
else
  echo "==> ConfigurationManager not in tools/, skipping - run scripts/fetch-bepinex.sh for it"
fi

# --- Plugin ----------------------------------------------------------------
echo "==> Building"
dotnet build "$REPO_DIR/ChopChopMods.sln" -c Release -p:GameDir="$GAME_DIR" --nologo -v quiet

for plugin in MoreWood ChopChopTweaks; do
  PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/$plugin"
  mkdir -p "$PLUGIN_DIR"

  # Windows keeps a loaded assembly open, so cp fails with a bare "Invalid argument" while the
  # game is running. Say what is actually wrong instead.
  if ! cp "$REPO_DIR/src/$plugin/bin/Release/$plugin.dll" "$PLUGIN_DIR/" 2>/dev/null; then
    echo >&2
    echo "error: could not overwrite $PLUGIN_DIR/$plugin.dll" >&2
    echo "       Chop Chop Inc. is almost certainly still running and has the DLL loaded." >&2
    echo "       Quit the game, then re-run this script." >&2
    exit 1
  fi

  echo "==> Installed $plugin"
done

echo
echo "Next: launch the game and press HOME to edit settings in-game."
echo "Or edit the files directly (requires a restart to take effect):"
echo "  $GAME_DIR/BepInEx/config/chopchopmods.morewood.cfg"
echo "  $GAME_DIR/BepInEx/config/chopchopmods.tweaks.cfg"
echo "Logs: $GAME_DIR/BepInEx/LogOutput.log"
