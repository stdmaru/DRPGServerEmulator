#!/usr/bin/env bash
# Builds (or reuses) a Ghidra project for the original client binary and exports a
# grep-friendly dump of it. The dump is derived from a copyrighted binary: it stays
# outside the repository (default ../../re/<label>) and is never committed.
#
# usage: tools/re/dump.sh <binary> <label> [output-root]
# example: tools/re/dump.sh ../client/DigimonRPG.bin.orig v322
set -euo pipefail

binary=$(readlink -f "$1")
label=$2
script_dir=$(dirname "$(readlink -f "$0")")
root=$(readlink -m "${3:-$script_dir/../../../re}")
ghidra_home=${GHIDRA_HOME:-/opt/ghidra}
project_dir="$root/$label/ghidra"
dump_dir="$root/$label/dump"
program=$(basename "$binary")

mkdir -p "$project_dir" "$dump_dir"
export MAXMEM=${MAXMEM:-12G}

if [ -d "$project_dir/drpg.rep" ]; then
  "$ghidra_home/support/analyzeHeadless" "$project_dir" drpg -process "$program" -noanalysis \
    -scriptPath "$script_dir" -postScript ExportDump.java "$dump_dir"
else
  "$ghidra_home/support/analyzeHeadless" "$project_dir" drpg -import "$binary" \
    -scriptPath "$script_dir" -postScript ExportDump.java "$dump_dir"
fi

sha256sum "$binary" > "$dump_dir/SOURCE.sha256"
echo "dump: $dump_dir"
