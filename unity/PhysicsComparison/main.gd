extends Node3D
# Reproduz os parametros de island-server/Scripts/ServerPhysics.cs (RigidBody3D, ContinuousCd, CanSleep, damp .05/.6, material) e mede os mesmos cenarios do relatorio do Unity.

const DT := 1.0 / 60.0
var scenarios := ["drop", "bounce", "on_box", "kick", "box_drop"]
var index := -1
var frame := 0
var body: RigidBody3D
var box_fixed: StaticBody3D
var ground: StaticBody3D
var r := {}
var state := {}

func _ready() -> void:
	Engine.physics_ticks_per_second = 60
	ground = StaticBody3D.new(); ground.collision_layer = 1; ground.collision_mask = 1
	var cs := CollisionShape3D.new(); var shape := BoxShape3D.new(); shape.size = Vector3(1000, 2, 1000); cs.shape = shape; cs.position = Vector3(0, -1, 0)
	ground.add_child(cs); add_child(ground)
	_next()

func _make_body(shape_kind: String, pos: Vector3, bounce: float, mass: float = 1.0) -> void:
	body = RigidBody3D.new(); body.collision_layer = 1; body.collision_mask = 1; body.continuous_cd = true; body.can_sleep = true; body.contact_monitor = true; body.max_contacts_reported = 6
	var c := CollisionShape3D.new()
	if shape_kind == "sphere":
		var s := SphereShape3D.new(); s.radius = 0.5; c.shape = s
	else:
		var b := BoxShape3D.new(); b.size = Vector3(1, 1, 1); c.shape = b
	body.add_child(c)
	var m := PhysicsMaterial.new(); m.friction = 0.5; m.bounce = bounce; m.rough = false; m.absorbent = false
	body.physics_material_override = m
	body.mass = mass; body.angular_damp = 0.6; body.linear_damp = 0.05
	add_child(body); body.global_position = pos
	body.body_entered.connect(func(_o): if not state.has("contact_t"): state["contact_t"] = frame * DT)

func _next() -> void:
	if body: body.queue_free(); body = null
	if box_fixed: box_fixed.queue_free(); box_fixed = null
	index += 1; frame = 0; state = {"peak": 0.0, "landed": false, "vmax": 0.0}
	if index >= scenarios.size():
		print("GODOT_DONE"); get_tree().quit(); return
	match scenarios[index]:
		"drop": _make_body("sphere", Vector3(5, 4.5, 5), 0.0)
		"bounce": _make_body("sphere", Vector3(5, 5.5, 5), 1.0)
		"on_box":
			box_fixed = StaticBody3D.new(); box_fixed.collision_layer = 1; box_fixed.collision_mask = 1
			var bc := CollisionShape3D.new(); var bs := BoxShape3D.new(); bs.size = Vector3(1, 1, 1); bc.shape = bs; box_fixed.add_child(bc); box_fixed.position = Vector3(12, 0.5, 12); add_child(box_fixed)
			_make_body("sphere", Vector3(12, 5.5, 12), 0.0)
		"kick":
			_make_body("sphere", Vector3(5, 0.5, 5), 0.0)
			state["kicked"] = false
		"box_drop": _make_body("box", Vector3(5, 3.5, 5), 0.0)

func _physics_process(_delta: float) -> void:
	if index >= scenarios.size() or body == null: return
	frame += 1
	var t := frame * DT
	var y := body.global_position.y
	var name: String = scenarios[index]
	var sleeping := body.sleeping
	if name == "kick":
		if not state["kicked"]:
			if frame == 120:  # one second to settle, then one kick: impulse 3.6 N.s (speed 4 * mass 1 * 0.9) along +X
				body.sleeping = false; body.apply_central_impulse(Vector3(3.6, 0, 0)); state["kicked"] = true; state["x0"] = body.global_position.x; state["kick_t"] = t
		else:
			if sleeping or t > 14.0:
				print("RESULT kick roll_distance=%.3f time_to_sleep=%.2f slept=%s" % [body.global_position.x - state["x0"], t - state["kick_t"], str(sleeping)]); _next()
		return
	if not state["landed"]:
		state["vmax"] = max(state["vmax"], -body.linear_velocity.y)
		var half := 0.5
		var base := 0.0
		if name == "on_box": base = 1.0
		if y - half <= base + 0.02:
			state["landed"] = true; state["land_t"] = t
	if state["landed"]:
		state["peak"] = max(state["peak"], y)
	if sleeping or t > 14.0:
		print("RESULT %s land_t=%.2f impact_speed=%.2f rest_y=%.3f peak_after_landing=%.3f time_to_sleep=%.2f slept=%s" % [name, state.get("land_t", -1.0), state["vmax"], y, state["peak"], t, str(sleeping)])
		_next()
