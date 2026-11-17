"""
Builds the default man of LifeVerse from the Character Creator base (LifeVerse_Avatar_Base.fbx):
  blender -b --python tools/blender/build_default_man.py -- <base.fbx> <out.glb>   (Blender adds .glb to the name: rename client/Assets/avatars/lifeverse_man.bin.glb to lifeverse_man.bin)
The base is a bare body, so a first set of clothes is made out of the body itself (shells of the faces of the
torso, legs, feet and scalp, pushed out a few millimetres, skinned to the same bones, in plain colours that the
game tints with the person's choices: materials LV_Top, LV_Bottom, LV_Shoes and LV_Hair). The morph targets are
dropped (the game does not use them and they weigh most of the file) and the textures are brought to 1024 pixels.
"""
import bpy, bmesh, sys
from mathutils import Vector

src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = bpy.data.objects['CC_Game_Body']
for o in bpy.data.objects:
    if o.type == 'MESH' and o.data.shape_keys:
        bpy.context.view_layer.objects.active = o
        o.shape_key_clear()
for img in bpy.data.images:
    if img.size[0] > 1024 or img.size[1] > 1024: img.scale(1024, 1024)

bpy.context.view_layer.update()
def bone_pos(name):
    b = arm.data.bones[name]; return arm.matrix_world @ b.head_local
P = {n: bone_pos(n) for n in ['pelvis', 'spine_03', 'neck_01', 'head', 'foot_l', 'ball_l', 'upperarm_l', 'lowerarm_l', 'upperarm_r', 'lowerarm_r', 'thigh_l', 'calf_l', 'CC_Base_L_Eye']}
if "--debug" in sys.argv:
    for k, v in P.items(): print("BONE", k, tuple(round(x, 3) for x in v))

ARM_GROUPS = {'upperarm_l', 'upperarm_twist_01_l', 'lowerarm_l', 'lowerarm_twist_01_l', 'hand_l', 'upperarm_r', 'upperarm_twist_01_r', 'lowerarm_r', 'lowerarm_twist_01_r', 'hand_r'}
for side in 'lr':
    for f in ('middle', 'index', 'ring', 'pinky', 'thumb'):
        for i in '123': ARM_GROUPS.add(f'{f}_0{i}_{side}')
LEG_GROUPS = {'thigh_l', 'thigh_twist_01_l', 'thigh_r', 'thigh_twist_01_r'}
HEAD_GROUPS = {'head', 'neck_01', 'CC_Base_FacialBone', 'CC_Base_JawRoot', 'CC_Base_UpperJaw'}
group_names = {g.index: g.name for g in body.vertex_groups}

def face_info(face, bm, deform):
    """Centre in the world, and the share of the weight that goes to the arms and to the head."""
    centre = body.matrix_world @ face.calc_center_median()
    arm = head = leg = total = 0.0
    for v in face.verts:
        for gi, w in deform[v.index].items():
            name = group_names[gi]; total += w
            if name in ARM_GROUPS: arm += w
            elif name in HEAD_GROUPS: head += w
            elif name in LEG_GROUPS: leg += w
    total = total or 1.0
    return centre, arm / total, head / total, leg / total

ankle = P['foot_l'].z + .035; hem = P['pelvis'].z - .07; waist = P['pelvis'].z + .08; collar = P['neck_01'].z - .035
eye = P['CC_Base_L_Eye'].z; head_x = P['head'].x; head_y = P['head'].y

def shoulder_t(centre, side):
    a = P[f'upperarm_{side}']; b = P[f'lowerarm_{side}']; ab = b - a
    return (centre - a).dot(ab) / ab.length_squared

def near_arm(centre, side, reach):
    """The point is around the upper arm, from just behind the shoulder to the given fraction of its length."""
    a = P[f'upperarm_{side}']; b = P[f'lowerarm_{side}']; ab = b - a
    t = (centre - a).dot(ab) / ab.length_squared
    across = (centre - (a + ab * t)).length
    return -.45 < t < reach and across < .105

def pick(kind, centre, arm, head, leg):
    side = 'l' if centre.x > 0 else 'r'                      # the character looks toward -Y; its left is +X in Blender
    if kind == 'top':
        sleeve = near_arm(centre, side, .5)
        trunk = hem < centre.z < collar and abs(centre.x) < .205 and arm < .6
        return sleeve and centre.z > P['pelvis'].z or trunk
    if kind == 'bottom':
        return arm < .5 and head < .5 and ankle < centre.z < waist and not near_arm(centre, side, 1.2)
    if kind == 'shoes':
        return arm < .5 and centre.z < ankle + .01
    if kind == 'hair':
        if head < .5: return False
        behind = centre.y > head_y + .015                     # the back of the head (the face looks toward -Y)
        return centre.z > eye + .065 or (behind and centre.z > eye - .045)
    return False

def shell(kind, name, colour, push):
    bpy.ops.object.select_all(action='DESELECT')
    copy = body.copy(); copy.data = body.data.copy(); copy.name = name; copy.data.name = name
    bpy.context.collection.objects.link(copy)
    copy.parent = body.parent; copy.matrix_parent_inverse = body.matrix_parent_inverse.copy()
    bm = bmesh.new(); bm.from_mesh(copy.data); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    deform_layer = bm.verts.layers.deform.verify()
    deform = {v.index: dict(v[deform_layer]) for v in bm.verts}
    doomed = []
    for f in bm.faces:
        centre, arm, head, leg = face_info(f, bm, deform)
        if not pick(kind, centre, arm, head, leg): doomed.append(f)
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.normal_update()
    for v in bm.verts: v.co += v.normal * (push / copy.matrix_world.to_scale().x if copy.matrix_world.to_scale().x else push)
    bm.to_mesh(copy.data); bm.free()
    copy.data.materials.clear()
    mat = bpy.data.materials.new(name=colour[0]); mat.use_nodes = True
    bsdf = mat.node_tree.nodes['Principled BSDF']; bsdf.inputs['Base Color'].default_value = colour[1]; bsdf.inputs['Roughness'].default_value = .85
    copy.data.materials.append(mat)
    print("SHELL", name, len(copy.data.polygons), "faces")
    return copy

def srgb(h): return tuple(((int(h[i:i + 2], 16) / 255) ** 2.2) for i in (1, 3, 5)) + (1.0,)
shell('top', 'LV_Outfit_Top', ('LV_Top', srgb('#3AAFC1')), .011)
shell('bottom', 'LV_Outfit_Bottom', ('LV_Bottom', srgb('#263B56')), .010)
shell('shoes', 'LV_Outfit_Shoes', ('LV_Shoes', srgb('#E7E9EE')), .016)
shell('hair', 'LV_Outfit_Hair', ('LV_Hair', srgb('#3A2A20')), .012)

bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_image_format='JPEG', export_jpeg_quality=85, export_morph=False, export_animations=False, export_skins=True, export_yup=True)
print("DONE")
