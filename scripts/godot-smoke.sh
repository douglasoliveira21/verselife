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
run client res://Tests/MovementSmoke.tscn movement_smoke
run client res://Tests/RadialSmoke.tscn radial_smoke
run client res://Tests/LoadingSmoke.tscn loading_smoke
run client res://Tests/TeleportSmoke.tscn teleport_smoke
run client res://Tests/AssetSmoke.tscn asset_smoke
run client res://Tests/ScenarioSmoke.tscn scenario_smoke
run client res://Tests/ShaderSmoke.tscn shader_smoke
run client res://Tests/PhysicsSyncSmoke.tscn physics_sync_smoke
run client res://Tests/ObjectEffectsSmoke.tscn object_effects_smoke
run client res://Tests/LandSmoke.tscn land_smoke
run client res://Tests/LandPresenceSmoke.tscn land_presence_smoke
run client res://Tests/TerrainEditorSmoke.tscn terrain_editor_smoke
run client res://Tests/BuildEditorV2Smoke.tscn build_editor_v2_smoke
run client res://Tests/BuildTransformSmoke.tscn build_transform_smoke
run client res://Tests/BuildLinkFaceSmoke.tscn build_link_face_smoke
run client res://Tests/BuildGeneralSmoke.tscn build_general_smoke
run client res://Tests/BuildObjectTabSmoke.tscn build_object_tab_smoke
run client res://Tests/BuildResourcesSmoke.tscn build_resources_smoke
run client res://Tests/BuildTextureSmoke.tscn build_texture_smoke
run client res://Tests/BuildContentSmoke.tscn build_content_smoke
run client res://Tests/BuildCollabSmoke.tscn build_collab_smoke
run client res://Tests/BuildPackageSmoke.tscn build_package_smoke
run client res://Tests/ProfileSmoke.tscn profile_smoke
run client res://Tests/IslandManagementSmoke.tscn island_management_smoke
run client res://Tests/ImportFormatsSmoke.tscn import_formats_smoke
run client res://Tests/BuildClickActionSmoke.tscn build_click_action_smoke
run client res://Tests/BuildLoadSmoke.tscn build_load_smoke
run client res://Tests/AvatarGroundingSmoke.tscn avatar_grounding
run client res://Tests/Avatar2Smoke.tscn avatar2_smoke
run client res://Tests/AvatarCustomSmoke.tscn avatar_custom_smoke
run client res://Tests/AvatarWearablesSmoke.tscn avatar_wearables_smoke
run client res://Tests/AvatarAnimationsSmoke.tscn avatar_animations_smoke
run client res://Tests/AvatarDetailSmoke.tscn avatar_detail_smoke
run client res://Tests/InventorySmoke.tscn inventory_smoke
run client res://Tests/PeopleSmoke.tscn people_smoke
run client res://Tests/GroupsSmoke.tscn groups_smoke
run client res://Tests/MirrorSmoke.tscn mirror_smoke
run client res://Tests/ModelLodSmoke.tscn model_lod_smoke
run client res://Tests/StartPlaceSmoke.tscn start_place_smoke
run client res://Tests/SearchSmoke.tscn search_smoke
run client res://Tests/ItemRightsSmoke.tscn item_rights_smoke
run client res://Tests/VehicleSmoke.tscn vehicle_smoke
run client res://Tests/UpdateSmoke.tscn update_smoke
run client res://Tests/VoiceSmoke.tscn voice_smoke
run client res://Tests/VoiceUiSmoke.tscn voice_ui_smoke
run island-server res://Tests/ScriptingSmoke.tscn scripting_smoke
run island-server res://Tests/PhysicsSmoke.tscn physics_smoke
exit $failed
