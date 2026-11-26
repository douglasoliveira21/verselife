extends Node3D
# Reproduz os parametros de island-server/Scripts/ServerPhysics.cs (RigidBody3D, ContinuousCd, CanSleep, damp .05/.6, material) e mede os mesmos cenarios do relatorio do Unity
# (Server/PhysicsComparisonReport.cs). Cada cenario imprime uma linha "RESULT nome chave=valor ..."; as chaves sao iguais nos dois lados. Os cinco primeiros cenarios sao os
# originais (e imprimem como antes). O chao dos originais e uma caixa grande; os de terreno usam um HeightMapShape3D igual ao do servidor.

const DT := 1.0 / 60.0
const CELLS := 128
const CELL := 2.0
const FRICTIONS := {"vidro": 0.2, "metal": 0.3, "plastico": 0.4, "carne": 0.5, "pedra": 0.6, "borracha": 0.9}
const BOUNCES := {"pedra": 0.1, "metal": 0.3, "madeira": 0.4, "plastico": 0.5, "vidro": 0.6, "borracha": 0.9}
const SLOPES := [10, 25, 35]

var scenarios: Array = []
var index := -1
var frame := 0
var bodies: Array = []
var fixed: Array = []
var ground: StaticBody3D
var state := {}

func _ready() -> void:
	Engine.physics_ticks_per_second = 60
	print("ENGINE physics=%s" % str(ProjectSettings.get_setting("physics/3d/physics_engine", "DEFAULT")))
	scenarios = ["drop", "bounce", "on_box", "kick", "box_drop", "stack", "tilted", "spin", "tunnel_wall", "tunnel_ground", "collide", "collide_heavy", "ball_on_box_bounce"]
	for m in FRICTIONS: scenarios.append("slide_" + m)
	for m in BOUNCES: scenarios.append("material_bounce_" + m)
	for s in SLOPES:
		scenarios.append("slope_box_%d" % s)
		scenarios.append("slope_ball_%d" % s)
	scenarios.append("crowd")
	_next()

# ---- o mundo ----

func _flat_ground() -> void:
	ground = StaticBody3D.new(); ground.collision_layer = 1; ground.collision_mask = 1
	var cs := CollisionShape3D.new(); var shape := BoxShape3D.new(); shape.size = Vector3(1000, 2, 1000); cs.shape = shape; cs.position = Vector3(0, -1, 0)
	ground.add_child(cs); add_child(ground)

# a rampa: sobe com x entre -40 e 40, como a do relatorio do Unity
static func slope_height(x: float, degrees: float) -> float:
	return clamp(x + 40.0, 0.0, 80.0) * tan(deg_to_rad(degrees))

func _slope_ground(degrees: float) -> void:
	var size := CELLS + 1; var data := PackedFloat32Array(); data.resize(size * size)
	var half := CELLS * CELL / 2.0
	for z in size:
		for x in size:
			var wx := x * CELL - half; var wz := z * CELL - half
			var h := 0.0
			if abs(wz) <= 40.0 and wx <= 40.0: h = slope_height(wx, degrees)
			data[z * size + x] = min(h, 48.0)
	var shape := HeightMapShape3D.new(); shape.map_width = size; shape.map_depth = size; shape.map_data = data
	ground = StaticBody3D.new(); ground.collision_layer = 1; ground.collision_mask = 1
	var cs := CollisionShape3D.new(); cs.shape = shape; cs.scale = Vector3(CELL, 1, CELL)
	ground.add_child(cs); add_child(ground)

func _body(shape_kind: String, pos: Vector3, size: Vector3 = Vector3.ONE, friction: float = 0.5, bounce: float = 0.0, mass: float = 1.0, euler: Vector3 = Vector3.ZERO) -> RigidBody3D:
	var b := RigidBody3D.new(); b.collision_layer = 1; b.collision_mask = 1; b.continuous_cd = true; b.can_sleep = true; b.contact_monitor = true; b.max_contacts_reported = 6
	var c := CollisionShape3D.new()
	if shape_kind == "sphere":
		var s := SphereShape3D.new(); s.radius = 0.5 * size.x; c.shape = s
	else:
		var box := BoxShape3D.new(); box.size = size; c.shape = box
	b.add_child(c)
	var m := PhysicsMaterial.new(); m.friction = friction; m.bounce = bounce; m.rough = false; m.absorbent = false
	b.physics_material_override = m
	b.mass = mass; b.angular_damp = 0.6; b.linear_damp = 0.05
	add_child(b)
	b.global_transform = Transform3D(Basis.from_euler(Vector3(deg_to_rad(euler.x), deg_to_rad(euler.y), deg_to_rad(euler.z)), EULER_ORDER_YXZ), pos)
	bodies.append(b)
	return b

func _static_box(pos: Vector3, size: Vector3) -> void:
	var s := StaticBody3D.new(); s.collision_layer = 1; s.collision_mask = 1
	var cs := CollisionShape3D.new(); var bs := BoxShape3D.new(); bs.size = size; cs.shape = bs; s.add_child(cs); s.position = pos; add_child(s); fixed.append(s)

func _clear() -> void:
	for b in bodies: b.free()
	for s in fixed: s.free()
	bodies = []; fixed = []
	if ground: ground.free(); ground = null

# ---- os cenarios ----

func _next() -> void:
	_clear()
	index += 1; frame = 0; state = {"peak": 0.0, "landed": false, "vmax": 0.0}
	if index >= scenarios.size():
		print("GODOT_DONE"); get_tree().quit(); return
	var name: String = scenarios[index]
	if name.begins_with("slope_"):
		var degrees := float(name.get_slice("_", 2))
		_slope_ground(degrees)
		var x := 20.0; var h := slope_height(x, degrees)
		if name.begins_with("slope_box"): _body("box", Vector3(x, h + 1.6, 0), Vector3(4, 1, 4), 0.5, 0.0, 20.0, Vector3(0, 0, degrees))
		else: _body("sphere", Vector3(x, h + 1.0, 0))
		state["x0"] = x
		return
	_flat_ground()
	if name == "drop": _body("sphere", Vector3(5, 4.5, 5))
	elif name == "bounce": _body("sphere", Vector3(5, 5.5, 5), Vector3.ONE, 0.5, 1.0)
	elif name == "on_box":
		_static_box(Vector3(12, 0.5, 12), Vector3.ONE); _body("sphere", Vector3(12, 5.5, 12))
	elif name == "kick": _body("sphere", Vector3(5, 0.5, 5)); state["kicked"] = false
	elif name == "box_drop": _body("box", Vector3(5, 3.5, 5))
	elif name == "stack":
		for i in 5: _body("box", Vector3(5, 0.5 + i * 1.05, 5))
	elif name == "tilted": _body("box", Vector3(5, 4.0, 5), Vector3.ONE, 0.5, 0.0, 1.0, Vector3(35, 20, 15))
	elif name == "spin":
		var b := _body("box", Vector3(5, 0.5, 5)); b.angular_velocity = Vector3(0, 10, 0); state["angle"] = 0.0
	elif name == "tunnel_wall":
		_static_box(Vector3(10, 2, 0), Vector3(0.05, 4, 4)); var b := _body("sphere", Vector3(0, 2, 0), Vector3(0.2, 0.2, 0.2)); b.linear_velocity = Vector3(80, 0, 0)
	elif name == "tunnel_ground":
		var b := _body("sphere", Vector3(5, 20, 5), Vector3(0.2, 0.2, 0.2)); b.linear_velocity = Vector3(0, -80, 0); state["miny"] = 100.0
	elif name == "collide":
		_body("sphere", Vector3(0, 0.5, 0)); var b := _body("sphere", Vector3(-3, 0.5, 0)); b.linear_velocity = Vector3(6, 0, 0)
	elif name == "collide_heavy":
		_body("box", Vector3(0, 0.5, 0), Vector3.ONE, 0.5, 0.0, 10.0); var b := _body("sphere", Vector3(-3, 0.5, 0)); b.linear_velocity = Vector3(10, 0, 0)
	elif name == "ball_on_box_bounce":
		_body("box", Vector3(5, 0.5, 5), Vector3.ONE, 0.5, 0.4, 5.0); _body("sphere", Vector3(5, 4.5, 5), Vector3.ONE, 0.5, 0.4)
	elif name.begins_with("slide_"):
		var b := _body("box", Vector3(0, 0.5, 0), Vector3.ONE, FRICTIONS[name.trim_prefix("slide_")]); b.linear_velocity = Vector3(6, 0, 0)
	elif name.begins_with("material_bounce_"):
		_body("sphere", Vector3(5, 3.5, 5), Vector3.ONE, 0.5, BOUNCES[name.trim_prefix("material_bounce_")])
	elif name == "crowd":
		for i in 200:
			var col := i % 10; var row := (i / 10) % 10; var layer := i / 100
			_body("sphere" if i % 2 == 1 else "box", Vector3(col * 1.6 + 0.2 * layer, 1.0 + layer * 1.8, row * 1.6 + 0.2 * layer))
		state["step_sum"] = 0.0; state["steps"] = 0; state["step_max"] = 0.0

func _all_asleep() -> bool:
	for b in bodies:
		if not b.sleeping: return false
	return true

func _physics_process(_delta: float) -> void:
	if index >= scenarios.size() or bodies.is_empty(): return
	frame += 1
	var t := frame * DT
	var name: String = scenarios[index]
	var body: RigidBody3D = bodies[0]
	var y := body.global_position.y
	if name == "kick":
		if not state["kicked"]:
			if frame == 120:  # one second to settle, then one kick: impulse 3.6 N.s (speed 4 * mass 1 * 0.9) along +X
				body.sleeping = false; body.apply_central_impulse(Vector3(3.6, 0, 0)); state["kicked"] = true; state["x0"] = body.global_position.x; state["kick_t"] = t
		else:
			if body.sleeping or t > 14.0:
				print("RESULT kick roll_distance=%.3f time_to_sleep=%.2f slept=%s" % [body.global_position.x - state["x0"], t - state["kick_t"], str(body.sleeping)]); _next()
		return
	if name in ["drop", "bounce", "on_box", "box_drop"] or name.begins_with("material_bounce_") or name == "ball_on_box_bounce":
		var ball: RigidBody3D = bodies[bodies.size() - 1] if name == "ball_on_box_bounce" else body
		var by := ball.global_position.y
		if not state["landed"]:
			state["vmax"] = max(state["vmax"], -ball.linear_velocity.y)
			var base := 1.0 if name in ["on_box", "ball_on_box_bounce"] else 0.0
			if by - 0.5 <= base + 0.02:
				state["landed"] = true; state["land_t"] = t
		elif name.begins_with("material_bounce_") or name == "ball_on_box_bounce":
			if ball.linear_velocity.y > 0.05: state["rising"] = true
			if state.get("rising", false): state["peak"] = max(state["peak"], by)
		if state["landed"] and not (name.begins_with("material_bounce_") or name == "ball_on_box_bounce"):
			state["peak"] = max(state["peak"], by)
		if name.begins_with("material_bounce_") or name == "ball_on_box_bounce":
			if t >= 4.0:
				print("RESULT %s peak_after_landing=%.3f" % [name, state["peak"]]); _next()
		elif body.sleeping or t > 14.0:
			print("RESULT %s land_t=%.2f impact_speed=%.2f rest_y=%.3f peak_after_landing=%.3f time_to_sleep=%.2f slept=%s" % [name, state.get("land_t", -1.0), state["vmax"], y, state["peak"], t, str(body.sleeping)])
			_next()
	elif name == "stack":
		if _all_asleep() or t > 14.0:
			var drift := 0.0
			for b in bodies: drift = max(drift, Vector2(b.global_position.x - 5, b.global_position.z - 5).length())
			print("RESULT stack top_y=%.3f max_drift=%.3f time_to_sleep=%.2f slept=%s" % [bodies[4].global_position.y, drift, t, str(_all_asleep())]); _next()
	elif name == "tilted":
		if body.sleeping or t > 14.0:
			var basis := body.global_transform.basis
			var flat: float = max(abs(basis.x.normalized().y), abs(basis.y.normalized().y), abs(basis.z.normalized().y))
			print("RESULT tilted rest_y=%.3f flatness=%.4f travelled=%.3f time_to_sleep=%.2f slept=%s" % [y, flat, Vector2(body.global_position.x - 5, body.global_position.z - 5).length(), t, str(body.sleeping)]); _next()
	elif name == "spin":
		state["angle"] += body.angular_velocity.y * DT
		if body.sleeping or t > 14.0:
			print("RESULT spin turned_rad=%.2f time_to_sleep=%.2f slept=%s" % [state["angle"], t, str(body.sleeping)]); _next()
	elif name == "tunnel_wall":
		if t >= 1.0:
			print("RESULT tunnel_wall passed=%s x=%.2f" % [str(body.global_position.x > 10.2), body.global_position.x]); _next()
	elif name == "tunnel_ground":
		state["miny"] = min(state["miny"], y)
		if t >= 2.0:
			print("RESULT tunnel_ground passed=%s min_y=%.3f" % [str(state["miny"] < -0.5), state["miny"]]); _next()
	elif name.begins_with("collide"):
		var other: RigidBody3D = bodies[1]
		if frame == 60: state["va"] = body.linear_velocity.x; state["vb"] = other.linear_velocity.x
		if t >= 6.0:
			print("RESULT %s target_speed_1s=%.2f striker_speed_1s=%.2f target_x=%.3f striker_x=%.3f" % [name, state["va"], state["vb"], body.global_position.x, other.global_position.x]); _next()
	elif name.begins_with("slide_"):
		if body.sleeping or t > 14.0 or (frame > 5 and body.linear_velocity.length() < 0.02):
			var upright: float = body.global_transform.basis.y.normalized().y
			print("RESULT %s distance=%.3f stop_t=%.2f upright=%.3f" % [name, body.global_position.x, t, upright]); _next()
	elif name.begins_with("slope_"):
		if t >= (5.0 if name.begins_with("slope_box") else 3.0):
			var degrees := float(name.get_slice("_", 2))
			var gap := y - slope_height(body.global_position.x, degrees)
			print("RESULT %s downhill=%.3f speed=%.2f height_over_ground=%.3f" % [name, state["x0"] - body.global_position.x, body.linear_velocity.length(), gap]); _next()
	elif name == "crowd":
		var step := Performance.get_monitor(Performance.TIME_PHYSICS_PROCESS) * 1000.0
		if frame > 5: state["step_sum"] += step; state["steps"] += 1; state["step_max"] = max(state["step_max"], step)
		if t >= 8.0:
			var asleep := 0; var miny := 100.0; var maxy := -100.0
			for b in bodies:
				if b.sleeping: asleep += 1
				miny = min(miny, b.global_position.y); maxy = max(maxy, b.global_position.y)
			print("RESULT crowd asleep=%d min_y=%.3f max_y=%.3f step_ms_avg=%.3f step_ms_max=%.3f" % [asleep, miny, maxy, state["step_sum"] / max(1, state["steps"]), state["step_max"]]); _next()
