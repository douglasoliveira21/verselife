#!/usr/bin/env bash
# Runs every Godot smoke scene (client and island server) headless and prints one line per scene: PASS, FAIL or NOEVENT.
# Usage: scripts/run-all-smokes.sh <path-to-godot-mono-console-exe> [project-filter]
set -u
godot="${1:?path to the Godot Mono console executable}"
only="${2:-}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
passed=0; failed=0
for project in client island-server; do
  [ -n "$only" ] && [ "$only" != "$project" ] && continue
  for scene in "$root/$project"/Tests/*.tscn; do
    [ -f "$scene" ] || continue
    name="$(basename "$scene" .tscn)"
    output=$(timeout 300 "$godot" --headless --path "$root/$project" "res://Tests/$name.tscn" --quit-after 150000 2>&1)
    if grep -q "result=passed" <<< "$output"; then echo "PASS   $project/$name"; passed=$((passed+1))
    elif grep -q "result=failed" <<< "$output"; then echo "FAIL   $project/$name :: $(grep -m1 'result=failed' <<< "$output" | cut -c1-300)"; failed=$((failed+1))
    else echo "NOEVENT $project/$name :: $(grep -m1 -E 'ERROR|SCRIPT ERROR' <<< "$output" | cut -c1-200)"; failed=$((failed+1)); fi
  done
done
echo "TOTAL passed=$passed failed=$failed"
