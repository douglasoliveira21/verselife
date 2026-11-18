"""
Dresses the man of LifeVerse with a real garment from a model library (glTF modelled on a posed mannequin, not rigged).
  blender -b --python tools/blender/fit_garment.py -- <base.fbx> <garment.gltf> <out.glb> <key> <slot> [--materials A,B,C] [--skip-materials X]

 1. the body is bent (arms, forearms) until it fits inside the garment, and the garment is scaled and placed on it (search over the pose and the placement),
 2. the garment is bent back to the T-pose of the body with the same bending (the arm weights come from the body vertices that are nearest),
 3. the pieces that sink into the body are pushed out, the skin weights are taken from the body and collapsed to the bones the game animates,
 4. the file is written as a skinned GLB on the body's skeleton, with the garment's own materials.
"""
import bpy, bmesh, sys, os, json, math, struct, random
import numpy as np
from mathutils import Vector, kdtree
from mathutils.bvhtree import BVHTree

A = sys.argv[sys.argv.index("--") + 1:]
FBX, GARMENT, OUT, KEY, SLOT = A[:5]
def opt(name, default=''): return A[A.index(name) + 1] if name in A else default
USE = [m for m in opt('--materials').split(',') if m]
SKIP = [m for m in opt('--skip-materials').split(',') if m]
LAYER = opt('--layer', 'top'); TINT = opt('--tint', 'item')

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); body = bpy.data.objects['CC_Game_Body']
bpy.context.view_layer.update()
bw = body.matrix_world
bverts = np.array([(bw @ v.co)[:] for v in body.data.vertices]); bpoly = [tuple(p.vertices) for p in body.data.polygons]
gname = {g.index: g.name for g in body.vertex_groups}
bweights = [[(gname[g.group], g.weight) for g in v.groups if g.weight > 1e-4] for v in body.data.vertices]
ANIMATED = {'pelvis', 'spine_01', 'spine_02', 'neck_01', 'head'} | {f'{b}_{s}' for b in ('upperarm', 'lowerarm', 'hand', 'thigh', 'calf', 'foot') for s in 'lr'}
def canonical(name):
    bone = arm.data.bones.get(name)
    while bone is not None and bone.name not in ANIMATED: bone = bone.parent
    return bone.name if bone is not None else 'pelvis'
cw = [{} for _ in bweights]
for i, ws in enumerate(bweights):
    for n, w in ws: c = canonical(n); cw[i][c] = cw[i].get(c, 0) + w
def bone_head(name): return np.array((arm.matrix_world @ arm.data.bones[name].head_local)[:])
def chain_w(names): return np.array([sum(cw[i].get(n, 0) for n in names) for i in range(len(cw))])

# ---- the garment (centimetres, Y up in the file; Blender turns it Z up) -----------------------------------------------------------------------
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=GARMENT)
new = [o for o in bpy.data.objects if o not in before]
root = next(o for o in new if o.parent is None); root.scale = (.01, .01, .01); bpy.context.view_layer.update()
pieces = []; seen_cotton = False
for o in new:
    if o.type != 'MESH': continue
    mats = [m.name for m in o.data.materials]
    if USE and not any(m in USE for m in mats): continue
    if any(m in SKIP for m in mats) or o.name in opt('--skip-names').split(','): continue
    if any((len(o.data.vertices), tuple(mats)) == (len(q.data.vertices), tuple(m.name for m in q.data.materials)) for q in pieces): continue   # the library repeats some layers
    pieces.append(o)
print("PIECES", [(o.name, [m.name for m in o.data.materials], len(o.data.vertices)) for o in pieces])
gv = []; owner = []
for k, o in enumerate(pieces):
    for v in o.data.vertices: gv.append((o.matrix_world @ v.co)[:]); owner.append(k)
gv = np.array(gv); owner = np.array(owner)

# ---- arms of the body: bending about the shoulder and the elbow --------------------------------------------------------------------------------
def rot_between(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b); v = np.cross(a, b); c = float(np.dot(a, b))
    if c < -0.9999: 
        ax = np.cross(a, [1, 0, 0]); ax /= np.linalg.norm(ax); return 2 * np.outer(ax, ax) - np.eye(3)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]]); return np.eye(3) + k + k @ k / (1 + c)
def direction(theta, phi): return np.array([math.sin(theta) * math.cos(phi), math.sin(theta) * math.sin(phi), math.cos(theta)])
SIDES = {}
for s in 'lr':
    sh, el, wr = bone_head(f'upperarm_{s}'), bone_head(f'lowerarm_{s}'), bone_head(f'hand_{s}')
    SIDES[s] = dict(S=sh, E=el, W=wr, A=chain_w([f'upperarm_{s}', f'lowerarm_{s}', f'hand_{s}']), F=chain_w([f'lowerarm_{s}', f'hand_{s}']),
                    d1=(el - sh) / np.linalg.norm(el - sh), d2=(wr - el) / np.linalg.norm(wr - el))
def pose_points(points, Aw, Fw, side, p, inverse=False):
    S, E = side['S'], side['E']; R1 = rot_between(side['d1'], direction(p[0], p[1])); R2 = rot_between(R1 @ side['d2'], direction(p[2], p[3])) @ R1 if False else None
    d1p = direction(p[0], p[1]); R1 = rot_between(side['d1'], d1p)
    d2p = direction(p[2], p[3]); R2 = rot_between(side['d2'], R1.T @ d2p)                       # the forearm gets its own absolute direction
    Ep = R1 @ (E - S) + S                                                                  # where the elbow goes with the upper arm
    if not inverse:
        q = points + (((points - E) @ R2.T + E) - points) * Fw[:, None]                    # forearm about the elbow
        q = q + (((q - S) @ R1.T + S) - q) * Aw[:, None]                                   # whole arm about the shoulder
        return q
    q = points + (((points - S) @ R1 + S) - points) * Aw[:, None]
    return q + (((q - E) @ R2 + E) - q) * Fw[:, None]
# the forearm direction of the pose is given in the frame of the rest body, as the upper arm's: both are absolute directions

def posed_body(params):
    v = bverts.copy()
    for s, side in SIDES.items(): v = pose_points(v, side['A'], side['F'], side, params[s])
    return v

def tree(points):
    kd = kdtree.KDTree(len(points))
    for i, p in enumerate(points): kd.insert(Vector(p), i)
    kd.balance(); return kd

main = int(np.bincount(owner).argmax())
sub = gv[owner == main][::max(1, int((owner == main).sum() // 1400))]                              # the biggest piece stands for the garment
bnorm = np.array([(bw.to_3x3() @ v.normal)[:] for v in body.data.vertices])
def score(params, scale, trans):
    pts = (sub - sub.mean(axis=0)) * scale + sub.mean(axis=0) + trans
    pv = posed_body(params); kd = tree(pv); total = 0.0
    for p in pts:
        _, j, d = kd.find(Vector(p)); sd = float(np.dot(p - pv[j], bnorm[j]))                  # signed distance: negative inside the body
        total += (3 * (.006 - sd) if sd < .002 else abs(sd - .012)) if d < .08 else .08
    return total / len(pts)

# 1. the torso: scale and placement of the garment over the rest body (only the points of the torso count)
body_mid = bverts[(bverts[:, 2] > 1.15) & (bverts[:, 2] < 1.5)].mean(axis=0); sub_mid = sub.mean(axis=0)
torso = sub[(abs(sub[:, 0] - sub_mid[0]) < .14) & (sub[:, 2] > 1.1) & (sub[:, 2] < 1.5)]
bnorm = np.array([(bw.to_3x3() @ v.normal)[:] for v in body.data.vertices]); kd_rest = tree(bverts)
def torso_score(scale, trans):
    pts = (torso - sub_mid) * scale + sub_mid + trans; total = 0.0
    for p in pts:
        _, j, d = kd_rest.find(Vector(p)); sd = float(np.dot(p - bverts[j], bnorm[j]))
        total += (3 * (.006 - sd) if sd < .002 else abs(sd - .012)) if d < .08 else .08
    return total / len(pts)
scale = 1.0; trans = body_mid - sub_mid; trans[1] = 0; trans[2] = 0; random.seed(3)
best = torso_score(scale, trans); step = 1.0
for it in range(300):
    cs = scale; ct = trans.copy(); k = random.randrange(4)
    if k == 0: cs = min(1.15, max(.9, scale * (1 + random.gauss(0, .02 * step))))
    else: ct[k - 1] += random.gauss(0, .01 * step)
    sc = torso_score(cs, ct)
    if sc < best: best, scale, trans = sc, cs, ct
    if it % 100 == 99: step *= .6
print("TORSO score", best, "scale", scale, "trans", trans)
placed = (gv - sub_mid) * scale + sub_mid + trans
# 2. the arms: where the sleeves go (shoulder, elbow, cuff) is read from the garment itself
main_pts = placed[owner == main]
cuff_piece = [k for k, o in enumerate(pieces) if any(m.name.startswith("Collo") for m in o.data.materials)]
cuff_pts = placed[np.isin(owner, cuff_piece)]
params = {}
for s_, side in SIDES.items():
    sg = 1 if side["S"][0] > 0 else -1; S, E0, W0 = side["S"], side["E"], side["W"]
    cuff = cuff_pts[(sg * cuff_pts[:, 0] > .15) & (cuff_pts[:, 2] < 1.35)]
    cuff_c = cuff.mean(axis=0)
    ax = (cuff_c - S) / np.linalg.norm(cuff_c - S); sleeve = main_pts[(sg * main_pts[:, 0] > .17) & (main_pts[:, 2] < 1.5)]
    t = (sleeve - S) @ ax; length = np.linalg.norm(cuff_c - S); line = []
    for lo in np.linspace(0, 1, 12)[:-1]:
        sel = sleeve[(t >= lo * length) & (t < (lo + .1) * length)]
        if len(sel): line.append(sel.mean(axis=0))
    line = np.array([S] + line + [cuff_c]); seg = np.linalg.norm(np.diff(line, axis=0), axis=1); cum = np.concatenate([[0], np.cumsum(seg)])
    frac = np.linalg.norm(E0 - S) / (np.linalg.norm(E0 - S) + np.linalg.norm(W0 - E0))
    target = cum[-1] * frac; idx = int(np.searchsorted(cum, target)); idx = min(max(idx, 1), len(line) - 1)
    f = (target - cum[idx - 1]) / max(cum[idx] - cum[idx - 1], 1e-6); elbow = line[idx - 1] + (line[idx] - line[idx - 1]) * f
    d1 = (elbow - S) / np.linalg.norm(elbow - S); d2 = (cuff_c - elbow) / np.linalg.norm(cuff_c - elbow)
    ang = lambda d: (math.acos(max(-1, min(1, d[2]))), math.atan2(d[1], d[0]))
    params[s_] = [*ang(d1), *ang(d2)]; side['Eg'] = elbow; side['Cg'] = cuff_c
    print("ARM", s_, "elbow", elbow, "cuff", cuff_c, "d1", d1, "d2", d2)
# ---- the garment goes to the body's T-pose -----------------------------------------------------------------------------------------------------
centre = sub_mid
g = placed.copy()
kd_posed = tree(posed_body(params))
def seg_dist(P, a, b):
    ab = b - a; t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1); q = a + t[:, None] * ab; return np.linalg.norm(P - q, axis=1), t * np.linalg.norm(ab)
def smooth(x): x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)
gA = {}; gF = {}
for s_, side in SIDES.items():
    S, E, C = side["S"], side["Eg"], side["Cg"]; sg = 1 if S[0] > 0 else -1
    r1, t1 = seg_dist(g, S, E); r2, t2 = seg_dist(g, E, C); l1 = np.linalg.norm(E - S)
    r = np.minimum(r1, r2); t = np.where(r1 <= r2, t1, l1 + t2)
    inside = smooth((.09 - r) / .03) * smooth((t - .02) / .10) * (sg * (g[:, 0] - S[0]) > -.03 * sg * 0 - .06)       # near the sleeve, not the trunk
    gA[s_] = inside; gF[s_] = inside * smooth((t - l1 + .05) / .10)
rest = g.copy()
for s in 'lr': rest = pose_points(rest, gA[s], gF[s], SIDES[s], params[s], inverse=True)
# the pieces that sink into the body (or touch it) are pushed out of it
bvh = BVHTree.FromPolygons([tuple(v) for v in bverts], bpoly)
pushed = 0
for i, p in enumerate(rest):
    hit = bvh.find_nearest(Vector(p))
    if hit[0] is None: continue
    out = (Vector(p) - hit[0]).dot(hit[1])
    if out < .003 and (Vector(p) - hit[0]).length < .08: rest[i] = np.array(hit[0]) + np.array(hit[1]) * .003; pushed += 1
print("PUSHED", pushed, "of", len(rest))

# ---- the objects, the skin weights, the file ------------------------------------------------------------------------------------------------------
to_local = body.matrix_world.inverted()
names = sorted(ANIMATED); kd_body = tree(bverts)
offset = 0; out_objs = []
for k, o in enumerate(pieces):
    n = len(o.data.vertices); mesh = o.data
    for i, v in enumerate(mesh.vertices):
        v.co = to_local @ Vector(rest[offset + i])
    mesh.update()
    o.parent = None; o.matrix_world = o.matrix_world.Identity(4) if False else o.matrix_world
    # a copy in the body's space: the vertices already are in that space, so the object gets the body's parent frame
    obj = bpy.data.objects.new(f'{KEY}_{k}', mesh); bpy.context.collection.objects.link(obj)
    for name in names: obj.vertex_groups.new(name=name)
    gi = {gr.name: gr.index for gr in obj.vertex_groups}
    for i in range(n):
        p = rest[offset + i]; near = kd_body.find_n(Vector(p), 4); total = {}
        for _, bi, dist in near:
            w = 1.0 / (dist + 1e-4)
            for nm, bwt in cw[bi].items(): total[nm] = total.get(nm, 0.0) + bwt * w
        top = sorted(total.items(), key=lambda kv: -kv[1])[:4]; norm = sum(v for _, v in top) or 1.0
        for nm, v in top: obj.vertex_groups[gi[nm]].add([i], v / norm, 'REPLACE')
    for poly in mesh.polygons: poly.use_smooth = True
    mod = obj.modifiers.new('Armature', 'ARMATURE'); mod.object = arm; obj.parent = arm; obj.matrix_parent_inverse = body.matrix_parent_inverse.copy(); obj.matrix_basis = body.matrix_basis.copy()
    out_objs.append(obj); offset += n
for o in pieces:
    pass
for o in list(bpy.data.objects):
    if o in pieces and o not in out_objs: bpy.data.objects.remove(o, do_unlink=True)
# the garment is a single piece with its materials, and the double-sided ones as the library made them
bpy.ops.object.select_all(action='DESELECT')
for o in out_objs: o.select_set(True)
bpy.context.view_layer.objects.active = out_objs[0]
if len(out_objs) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active; obj.name = KEY
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); obj.select_set(True); bpy.context.view_layer.objects.active = arm
for o in bpy.data.objects:
    if o.type == 'MESH' and o is not obj: o.hide_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_morph=False, export_animations=False, export_skins=True, export_yup=True)
data = open(OUT, 'rb').read(); magic, version, length = struct.unpack('<4sII', data[:12]); jl, jt = struct.unpack('<II', data[12:20])
doc = json.loads(data[20:20 + jl].decode('utf-8')); tail = data[20 + jl:]
doc.setdefault('asset', {}).setdefault('extras', {})['lifeverse'] = {'slot': SLOT, 'layer': LAYER, 'tint': TINT}
for m in doc.get('materials', []): m['doubleSided'] = True
bb = json.dumps(doc, separators=(',', ':')).encode('utf-8'); bb += b' ' * ((4 - len(bb) % 4) % 4)
blob = struct.pack('<II', len(bb), jt) + bb + tail
open(OUT, 'wb').write(struct.pack('<4sII', magic, version, 12 + len(blob)) + blob)
print("WROTE", OUT, os.path.getsize(OUT), "bytes", len(obj.data.polygons), "faces")
