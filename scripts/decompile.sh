#!/usr/bin/env bash
#
# Decompiles the game assemblies into decompiled/ for reading.
# Output is gitignored - it is derived from the game's copyrighted assemblies.
#
# Requires: dotnet tool install -g ilspycmd --version 9.1.0.7988
#
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME_DIR="${CHOPCHOP_DIR:-/mnt/e/Program Files/Steam/steamapps/common/ChopChopInc}"
MANAGED="$GAME_DIR/ChopChopInc_Data/Managed"
OUT="$REPO_DIR/decompiled"

export PATH="$PATH:$HOME/.dotnet/tools"
command -v ilspycmd >/dev/null || {
  echo "error: ilspycmd not found. Install with:" >&2
  echo "  dotnet tool install -g ilspycmd --version 9.1.0.7988" >&2
  exit 1
}

mkdir -p "$OUT"
for asm in Assembly-CSharp GameFramework2 Assembly-CSharp-firstpass GameUI; do
  echo "==> $asm"
  ilspycmd -p -o "$OUT/$asm" "$MANAGED/$asm.dll" >/dev/null
done

echo "==> Done. Start with:"
echo "    $OUT/Assembly-CSharp/WorldObjects/SpawnOnDestroy.cs"
