"""
Builds the default man of LifeVerse from the Character Creator base (LifeVerse_Avatar_Base.fbx), exactly as it is (no clothes and no hair are added):
  blender -b --python tools/blender/build_default_man.py -- <base.fbx> <out.glb>
(Blender adds .glb to the name: rename the result to client/Assets/avatars/lifeverse_man.bin.)

What is kept: every mesh, the skeleton, the weights and every texture at its full size and in the format the file had (normal maps included).
What is dropped: the morph targets (expressions and body morphs: the game does not use them, and each avatar would have to load them: 161 of them on the body alone).
What is put right: the importer turns the skin into a glossy, blended surface (roughness 0, alpha blend), which looks like wet plastic and sorts badly; the skin, the teeth, the tongue and the nails become
solid surfaces with a natural roughness; the lashes get their opacity map back (the importer leaves it unconnected, and without it they are solid bands around the eyes) and, like the brows, are cut-outs
(alpha mask) instead of blended.
"""
import bpy, sys, json, struct

src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
for o in bpy.data.objects:
    if o.type == 'MESH' and o.data.shape_keys:
        bpy.context.view_layer.objects.active = o
        o.shape_key_clear()

# the lashes come with their own opacity map, which the importer leaves unconnected: without it the lashes are solid bands around the eyes
for material in bpy.data.materials:
    opacity = bpy.data.images.get(material.name + '_Opacity')
    if material.use_nodes and opacity is not None and material.name == 'Ga_Eyelash':
        node = material.node_tree.nodes.new('ShaderNodeTexImage'); node.image = opacity; opacity.colorspace_settings.name = 'Non-Color'
        principled = [n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'][0]
        for link in list(principled.inputs['Alpha'].links): material.node_tree.links.remove(link)
        material.node_tree.links.new(node.outputs['Color'], principled.inputs['Alpha'])

# otherwise, the importer reads the alpha of a picture from a second copy of it: make the alpha come from the same picture node as the colour, which is what the exporter understands
for material in bpy.data.materials:
    if not material.use_nodes: continue
    for node in material.node_tree.nodes:
        if node.type != 'BSDF_PRINCIPLED' or not node.inputs['Alpha'].links or not node.inputs['Base Color'].links: continue
        alpha = node.inputs['Alpha'].links[0]; colour = node.inputs['Base Color'].links[0].from_node
        if material.name != 'Ga_Eyelash' and alpha.from_socket.name == 'Alpha' and alpha.from_node.type == 'TEX_IMAGE' and colour.type == 'TEX_IMAGE' and alpha.from_node != colour:
            material.node_tree.links.new(colour.outputs['Alpha'], node.inputs['Alpha'])

bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_image_format='AUTO', export_morph=False, export_animations=False, export_skins=True, export_yup=True)

# ---- the materials, as the game should draw them -----------------------------------------------------------------------------------------------
path = out if out.lower().endswith('.glb') else out + '.glb'
data = open(path, 'rb').read()
magic, version, length = struct.unpack('<4sII', data[:12])
json_length, json_type = struct.unpack('<II', data[12:20])
doc = json.loads(data[20:20 + json_length].decode('utf-8'))
rest = data[20 + json_length:]
SOLID = {'Ga_Skin_Head': .55, 'Ga_Skin_Body': .55, 'Ga_Skin_Arm': .55, 'Ga_Skin_Leg': .55, 'Ga_Nails': .35, 'Ga_Tongue': .5, 'Std_Upper_Teeth': .5, 'Std_Lower_Teeth': .5}
CUTOUT = {'Ga_Eyelash': .35, 'Male_Brow_Transparency': .35, 'Male_Brow_Base_Transparency': .35}
for material in doc.get('materials', []):
    pbr = material.setdefault('pbrMetallicRoughness', {})
    if material['name'] in SOLID:
        material.pop('alphaMode', None); material.pop('alphaCutoff', None); pbr['roughnessFactor'] = SOLID[material['name']]; pbr['metallicFactor'] = 0.0
        if 'baseColorFactor' in pbr: pbr['baseColorFactor'][3] = 1.0
    elif material['name'] in CUTOUT:
        material['alphaMode'] = 'MASK'; material['alphaCutoff'] = CUTOUT[material['name']]; material['doubleSided'] = True
body = json.dumps(doc, separators=(',', ':')).encode('utf-8'); body += b' ' * ((4 - len(body) % 4) % 4)
blob = struct.pack('<II', len(body), json_type) + body + rest
open(path, 'wb').write(struct.pack('<4sII', magic, version, 12 + len(blob)) + blob)
print("DONE", path)
