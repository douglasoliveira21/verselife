# Builds client/Assets/animations/default_avatar_animations.bin (a GLB) from Mixamo FBX clips.
# Usage: blender -b --python tools/blender/build_default_avatar_animations.py -- <folder with the FBX files> <output .glb>
# Clips (Mixamo .fbx in the folder): Idle, Walking (1), Running, Floating, Swimming, Treading Water, Jump, Jumping Down, Falling, Flying, Walking Backwards, Falling Flat Impact, Male Dynamic Pose. Meters, in place, the jump without hip lift.
import sys, os
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
D = ARGS[0] if len(ARGS) > 0 else r"C:\Users\Douglas\Downloads"
import bpy, os, math
OUT = ARGS[1] if len(ARGS) > 1 else "default_avatar_animations.glb"
# file -> animation name (game states: Idle, Walk, Run, Jump, Fall, FlyMove, FlyIdle; extras keep a descriptive name)
files = [("Idle","Idle"),("Walking (1)","Walk"),("Running","Run"),("Jump","Jump"),("Falling","Fall"),("Flying","FlyMove"),
         ("Walking Backwards","WalkBackwards"),("Falling Flat Impact","FallFlat"),("Male Dynamic Pose","DynamicPose"),("Jumping Down","JumpDown"),("Floating","FlyIdle"),("Swimming","Swim"),("Treading Water","SwimIdle")]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=rf"{D}\Idle.fbx")
base = next(o for o in bpy.data.objects if o.type=="ARMATURE")
actions = {}
def grab(name, label):
    act = bpy.data.actions[-1]
    act.name = label; act.use_fake_user = True
    return act
# the first action
actions["Idle"] = grab("Idle","Idle")
for fname, label in files[1:]:
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=rf"{D}\{fname}.fbx")
    new = [o for o in bpy.data.objects if o.name not in before]
    act = [a for a in bpy.data.actions if not a.name in actions and a.name not in [x.name for x in actions.values()] and a.users >= 0][-1]
    act.name = label; act.use_fake_user = True; actions[label] = act
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
print("ACTIONS", list(actions))
# a clip of one frame (a pose) needs some length to be an animation: hold it for half a second
dyn = actions["DynamicPose"]
for fc in dyn.fcurves if hasattr(dyn, "fcurves") else []:
    pass
# meters: apply the 0.01 scale of the armature and scale the translation curves the same way
bpy.context.view_layer.objects.active = base
for o in bpy.context.selected_objects: o.select_set(False)
base.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
def fcurves_of(act):
    try: return list(act.fcurves)
    except Exception:
        out = []
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags: out.extend(bag.fcurves)
        return out
for label, act in actions.items():
    for fc in fcurves_of(act):
        if fc.data_path.endswith(".location"):
            for kp in fc.keyframe_points:
                kp.co.y *= 0.01; kp.handle_left.y *= 0.01; kp.handle_right.y *= 0.01
            fc.update()
# the jump is done by the game'"'"'s physics: its clip must not lift the hips a second time
for fc in fcurves_of(actions["Jump"]):
    if fc.data_path.endswith(".location") and "Hips" in fc.data_path and fc.keyframe_points:
        first = 0.0
        for kp in fc.keyframe_points: kp.co.y = first; kp.handle_left.y = first; kp.handle_right.y = first
        fc.update()
# hold the one-frame pose for 15 frames
for fc in fcurves_of(dyn):
    if fc.keyframe_points:
        last = fc.keyframe_points[-1]; v = last.co.y
        fc.keyframe_points.insert(last.co.x + 15, v)
# put each action on its own NLA track so all of them are exported
ad = base.animation_data_create(); ad.action = None
for label, act in actions.items():
    try:
        track = ad.nla_tracks.new(); track.name = label
        strip = track.strips.new(label, 1, act); strip.name = label
    except Exception as e:
        print("NLA ERR", label, e)
print("NLA", [t.name for t in ad.nla_tracks])
base.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_animations=True, export_animation_mode="NLA_TRACKS",
    export_force_sampling=True, export_skins=True, export_yup=True, export_cameras=False, export_lights=False, export_def_bones=False)
print("OUTSIZE", os.path.getsize(OUT))
