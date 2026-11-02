# Avatar system

## Placeholder and licensing

The Phase 02 avatar is an original procedural placeholder assembled at runtime from Godot `BoxMesh` and `SphereMesh` primitives. No downloaded model, texture, motion-capture clip, or proprietary asset is included. It is clearly a replacement target for a production skinned GLTF.

The standard skeleton declares root, hips, spine, chest, neck, head, paired upper/lower arms and hands, and paired upper/lower legs and feet. The segmented visual hierarchy mirrors those regions. Because pieces are rigid, it avoids skin-weight deformation while preserving stable animation and replacement contracts.

## Customization catalog

- Bases: Classic (`human_a`), Athletic (`human_b`)
- Hair: short, bob, mohawk, bald
- Tops: basic tee, basic jacket
- Bottoms: basic pants, basic shorts
- Shoes: sneakers, boots
- Accessories: none, round glasses, basic hat
- Skin/hair colors and camera sensitivity/invert-Y preferences

Face, height, and proportion settings are reserved in the appearance schema but intentionally not exposed until a production skinned model supports them safely.

## Animation states

`Idle`, `Walk`, `Run`, `Jump`, `Fall`, `Landing`, `FlyIdle`, `FlyMove`, `Sit`, `StandUp`, `Swim`, `Dance`, and `Wave` are real AnimationLibrary clips connected through an AnimationTree state machine with cross-fades. Physics resolves a logical state through the shared state resolver; the visual rig consumes that result.

## Controls and interactions

Movement is camera-relative and uses `CharacterBody3D`, floor snapping, a 50-degree floor angle, collision, gravity, jump, run, flight, landing, water volumes, and surface speed metadata. `SpringArm3D` prevents the third-person camera crossing walls. First-person, cursor release, sensitivity, inversion, object raycast, contextual seating, stand-up, wave, and dance are implemented.
