# Builds unity/UnityClient/Assets/LifeVerse/Avatar/Resources/Animations/typing_animation.bytes (a GLB with one clip, "Typing") from an Unreal-skeleton mocap FBX (Mocap Pack 03: Standing Conversation).
# Usage: blender -b --python tools/blender/build_typing_animation.py -- <input.fbx> <output.glb>
# The mocap is 45 s long and does not loop. This finds, by itself, the stretch of 5 to 10 seconds whose last pose is the closest to its first one
# (with real movement in it: hands and head, not a person standing still), and exports only that, at 30 frames per second, in meters, in place.
import sys, os, bpy
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = ARGS[0]; OUT = ARGS[1] if len(ARGS) > 1 else "typing_animation.glb"
KEEP = ["pelvis", "spine_01", "spine_02", "spine_03", "spine_04", "spine_05", "neck_01", "neck_02", "head", "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l", "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r",
        "thigh_l", "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r"]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
act = max(bpy.data.actions, key=lambda a: a.frame_range[1] - a.frame_range[0])
arm.animation_data_create(); arm.animation_data.action = act
sc = bpy.context.scene
first, last = int(act.frame_range[0]), int(act.frame_range[1])
step = max(1, int(round(sc.render.fps / 30)))          # 30 samples per second
frames = list(range(first, last + 1, step)); n = len(frames)
quats = []
for f in frames:
    sc.frame_set(f); bpy.context.view_layer.update()
    quats.append([arm.pose.bones[b].matrix.to_quaternion() for b in KEEP])
def dist(a, b): return sum(x.rotation_difference(y).angle for x, y in zip(a, b)) / len(a)
motion = [dist(quats[i], quats[i + 1]) for i in range(n - 1)]
best = None
for L in range(150, 301, 10):
    for s in range(0, n - L - 1, 3):
        energy = sum(motion[s:s + L]) / L
        if energy < 0.012: continue                                       # standing still is not what we want
        d = dist(quats[s], quats[s + L])
        score = d - 0.2 * energy                                          # close ends first, a lively stretch as a tie breaker
        if best is None or score < best[0]: best = (score, s, L, d, energy)
print("BEST", best)
_, s, L, d, energy = best
a, b = frames[s], frames[s + L]
print("SEGMENT", a, b, "seconds", (b - a) / sc.render.fps, "end-to-start gap (rad)", d)
# only what the game uses: delete the meshes and the animation of every other bone
for o in list(bpy.data.objects):
    if o.type != "ARMATURE": bpy.data.objects.remove(o, do_unlink=True)
def fcurves_of(action):
    try: return list(action.fcurves)
    except Exception:
        out = []
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags: out.extend(bag.fcurves)
        return out
for fc in fcurves_of(act):
    bone = fc.data_path.split('"')[1] if 'pose.bones["' in fc.data_path else None
    if bone is not None and bone not in KEEP: fc.mute = True
# meters: the armature is 0.01 (centimeters)
bpy.context.view_layer.objects.active = arm
for o in bpy.context.selected_objects: o.select_set(False)
arm.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
for fc in fcurves_of(act):
    if fc.data_path.endswith(".location"):
        for kp in fc.keyframe_points: kp.co.y *= 0.01; kp.handle_left.y *= 0.01; kp.handle_right.y *= 0.01
        fc.update()
act.name = "TypingSource"; act.use_fake_user = True
# the stretch goes on its own track, starting at the first frame, so the clip starts at time 0 and is called "Typing"
ad = arm.animation_data; ad.action = None
track = ad.nla_tracks.new(); track.name = "Typing"
strip = track.strips.new("Typing", 1, act); strip.use_sync_length = False
strip.action_frame_start = a; strip.action_frame_end = b; strip.frame_start = 1; strip.frame_end = 1 + (b - a)
sc.frame_start, sc.frame_end = 1, 1 + (b - a)
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_animations=True, export_animation_mode="NLA_TRACKS", export_frame_range=False, export_frame_step=step,
    export_force_sampling=True, export_skins=True, export_yup=True, export_cameras=False, export_lights=False, export_def_bones=False, export_optimize_animation_size=True)
print("OUTSIZE", os.path.getsize(OUT))
