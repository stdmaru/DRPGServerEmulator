#!/usr/bin/env bash
# Publishes the game client (NativeAOT, needs clang) as DigimonRPG.bin and, when the folder exists, copies it to the
# test install so it can be tried at once. Run on the host, not in the devcontainer (it has no clang).
#   tools/publish-client.sh [runtime-id] [install-folder]
# Defaults: linux-x64 and /media/games/DigimonRPG/work/client_test.
set -euo pipefail
cd "$(dirname "$0")/.."
runtime="${1:-linux-x64}"
install="${2:-/media/games/DigimonRPG/work/client_test}"
out="${DRPG_PUBLISH_DIR:-/tmp/drpg-client-publish}/$runtime"
rm -rf "$out"
dotnet publish src/DRPG.Client/DRPG.Client.csproj -c Release -r "$runtime" -o "$out" --artifacts-path "${DRPG_ARTIFACTS:-/tmp/dotnet-drpg-client}"
ls -l "$out/DigimonRPG.bin"
# The test install is shared with the side-by-side runs of the original under Wine, which swap files in it. So the copy
# only replaces a binary that is a Linux build of this client (ELF) or no binary at all, never the original (a Windows
# PE), and it replaces by rename, so a process that is running the old file is left alone.
if [ -d "$install" ]; then
  target="$install/DigimonRPG.bin"
  if [ -e "$install/DigimonRPG.bin__" ] || { [ -e "$target" ] && [ "$(head -c4 "$target" | od -An -tx1 | tr -d ' ')" != "7f454c46" ]; }; then
    echo "NOT copied: $install holds the original (a Wine run is swapping files there). Copy $out/DigimonRPG.bin by hand."
  else
    cp "$out/DigimonRPG.bin" "$target.new" && mv -f "$target.new" "$target"
    echo "copied to $target"
  fi
fi
