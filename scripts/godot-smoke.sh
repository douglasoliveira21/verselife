#!/usr/bin/env bash
# Runs the Godot smoke scenes headless and fails unless each one prints its "result=passed" event.
# Usage: scripts/godot-smoke.sh [path-to-godot]   (default: godot on PATH)
set -u
godot="${1:-godot}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
failed=0

run() {
  local project="$1" scene="$2" marker="$3" output
  output=$(timeout 300 "$godot" --headless --path "$root/$project" "$scene" --quit-after 150000 2>&1)
  if grep -q "event=${marker} result=passed" <<< "$output"; then
    echo "PASS  $marker"
  else
    echo "FAIL  $marker"; tail -25 <<< "$output"; failed=1
  fi
}

run client res://Tests/TerrainSmoke.tscn terrain_smoke
run client res://Tests/BuildSmoke.tscn build_smoke
run client res://Tests/AvatarGroundingSmoke.tscn avatar_grounding
run client res://Tests/InventorySmoke.tscn inventory_smoke
run client res://Tests/PeopleSmoke.tscn people_smoke
run client res://Tests/VehicleSmoke.tscn vehicle_smoke
run island-server res://Tests/ScriptingSmoke.tscn scripting_smoke
exit $failed
