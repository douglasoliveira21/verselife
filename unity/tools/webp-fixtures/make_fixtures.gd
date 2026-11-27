extends SceneTree
## Writes the WebP pictures the decoder of the Unity client is checked against, each with the pixels Godot (libwebp) reads from it:
##   Godot_console.exe --headless --script unity/tools/webp-fixtures/make_fixtures.gd -- <output folder>
## <name>.webp is the picture; <name>.rgba holds width and height (two 32-bit integers) and the RGBA bytes, rows from the top.

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		push_error("say the output folder after --")
		quit(1)
		return
	var folder: String = args[0]
	DirAccess.make_dir_recursive_absolute(folder)
	var random := RandomNumberGenerator.new()
	random.seed = 20261127
	var cases := [
		["one", 1, 1, "noise"], ["two", 2, 2, "noise"], ["thin", 1, 37, "photo"], ["flat", 33, 1, "photo"], ["odd", 17, 23, "noise"],
		["photo", 160, 128, "photo"], ["photo-odd", 145, 99, "photo"], ["noise", 64, 48, "noise"], ["stripes", 96, 80, "stripes"], ["palette", 90, 70, "palette"],
		["two-colours", 75, 40, "two"], ["alpha", 80, 60, "alpha"], ["alpha-noise", 41, 29, "alpha-noise"], ["wide", 600, 40, "photo"],
	]
	for item in cases:
		var image := paint(item[1], item[2], item[3], random)
		for mode in [["lossless", false, 1.0], ["q75", true, 0.75], ["q20", true, 0.2], ["q100", true, 1.0]]:
			var name := "%s-%s" % [item[0], mode[0]]
			var bytes := image.save_webp_to_buffer(mode[1], mode[2])
			var back := Image.new()
			if back.load_webp_from_buffer(bytes) != OK:
				push_error("Godot could not read back " + name)
				quit(1)
				return
			back.convert(Image.FORMAT_RGBA8)
			FileAccess.open(folder.path_join(name + ".webp"), FileAccess.WRITE).store_buffer(bytes)
			var raw := FileAccess.open(folder.path_join(name + ".rgba"), FileAccess.WRITE)
			raw.store_32(back.get_width())
			raw.store_32(back.get_height())
			raw.store_buffer(back.get_data())
	print("webp fixtures: ", cases.size() * 4, " in ", folder)
	quit(0)


func paint(width: int, height: int, kind: String, random: RandomNumberGenerator) -> Image:
	var image := Image.create(width, height, false, Image.FORMAT_RGBA8)
	var colours := []
	for i in 12:
		colours.append(Color8(random.randi_range(0, 255), random.randi_range(0, 255), random.randi_range(0, 255)))
	for y in height:
		for x in width:
			var colour: Color
			match kind:
				"noise":
					colour = Color8(random.randi_range(0, 255), random.randi_range(0, 255), random.randi_range(0, 255))
				"photo":
					# smooth gradients, edges and some grain: what makes the encoder use every prediction
					var circle := 1.0 if Vector2(x - width * 0.4, y - height * 0.5).length() < min(width, height) * 0.3 else 0.0
					colour = Color(float(x) / width * 0.8 + 0.2 * circle, 0.5 + 0.4 * sin(x * 0.21 + y * 0.13), float(y) / height * (1.0 - circle) + 0.1)
					if (x / 24 + y / 16) % 3 == 0:
						colour = colour.lerp(Color(random.randf(), random.randf(), random.randf()), 0.25)
				"stripes":
					colour = colours[(x / 3) % 5] if y < height / 2 else colours[((x + y) / 5) % 7]
				"palette":
					colour = colours[random.randi_range(0, 11) if (x / 8 + y / 8) % 2 == 0 else (x / 10) % 12]
				"two":
					colour = colours[0] if (x * 7 + y * 3) % 11 < 5 else colours[1]
				"alpha":
					colour = Color(float(x) / width, float(y) / height, 0.6, clamp(1.0 - Vector2(x - width / 2.0, y - height / 2.0).length() / (width * 0.45), 0.0, 1.0))
				"alpha-noise":
					colour = Color8(random.randi_range(0, 255), (x * 5) % 256, (y * 9) % 256, random.randi_range(0, 255))
			image.set_pixel(x, y, colour)
	return image
