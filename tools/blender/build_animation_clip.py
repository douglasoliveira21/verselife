# Builds a one-clip animation file (a GLB, kept with the extension .bin) for a custom body from a Mixamo FBX.
# Usage: blender -b --python tools/blender/build_animation_clip.py -- <input.fbx> <output.glb> <ClipName> [--zero-hips]
#   idle_animation.bin   <- "Standing W_Briefcase Idle.fbx"   clip "Idle"   (the avatar standing still on the ground; it loops by itself)
#   jump_animation.bin   <- "Jumping Up.fbx"                  clip "Jump"   --zero-hips (the game's physics lifts the avatar: the clip must not lift the hips a second time)
# Blender adds .glb to the name: rename the result to the .bin. The clip stays in place, is in meters, sampled at 30 frames per second; the fingers are left out (the game curls them).
import sys, os, bpy
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = ARGS[0]; OUT = ARGS[1]; NAME = ARGS[2]; ZERO_HIPS = "--zero-hips" in ARGS
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
act = max(bpy.data.actions, key=lambda a: a.frame_range[1] - a.frame_range[0])
arm.animation_data_create(); arm.animation_data.action = act
sc = bpy.context.scene
first, last = int(act.frame_range[0]), int(act.frame_range[1])
step = max(1, int(round(sc.render.fps / 30)))
def fcurves_of(action):
    try: return list(action.fcurves)
    except Exception:
        out = []
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags: out.extend(bag.fcurves)
        return out
for o in list(bpy.data.objects):
    if o.type != "ARMATURE": bpy.data.objects.remove(o, do_unlink=True)
for fc in fcurves_of(act):
    bone = fc.data_path.split('"')[1] if 'pose.bones["' in fc.data_path else None
    if bone is not None and any(k in bone for k in ("Thumb", "Index", "Middle", "Ring", "Pinky")): fc.mute = True
bpy.context.view_layer.objects.active = arm
for o in bpy.context.selected_objects: o.select_set(False)
arm.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)                 # meters: the armature is 0.01 (centimeters)
for fc in fcurves_of(act):
    if fc.data_path.endswith(".location"):
        for kp in fc.keyframe_points: kp.co.y *= 0.01; kp.handle_left.y *= 0.01; kp.handle_right.y *= 0.01
        fc.update()
if ZERO_HIPS:
    for fc in fcurves_of(act):
        if fc.data_path.endswith(".location") and "Hips" in fc.data_path and fc.keyframe_points:
            for kp in fc.keyframe_points: kp.co.y = 0.0; kp.handle_left.y = 0.0; kp.handle_right.y = 0.0
            fc.update()
act.name = NAME
ad = arm.animation_data; ad.action = None
track = ad.nla_tracks.new(); track.name = NAME
strip = track.strips.new(NAME, 1, act); strip.use_sync_length = False
strip.action_frame_start = first; strip.action_frame_end = last; strip.frame_start = 1; strip.frame_end = 1 + (last - first)
sc.frame_start, sc.frame_end = 1, 1 + (last - first)
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_animations=True, export_animation_mode="NLA_TRACKS", export_frame_range=False, export_frame_step=step,
    export_force_sampling=True, export_skins=True, export_yup=True, export_cameras=False, export_lights=False, export_def_bones=False, export_optimize_animation_size=True)
print("OUTSIZE", os.path.getsize(OUT if OUT.lower().endswith(".glb") else OUT + ".glb"), "seconds", (last - first) / sc.render.fps)
