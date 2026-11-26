"""Draws the textures of the Unity interface (the "skin"), so that panels, buttons and fields have bevels, gradients and shadows that a style sheet alone cannot draw.

    python unity/tools/ui-skin/generate.py

Every texture is a 9-slice drawn at twice the size it shows (Theme.uss uses "-unity-slice-scale: 0.5px"), so it stays sharp on large screens. The files go to
UnityClient/Assets/LifeVerse/UI/Resources/LifeVerse/UI/Skin and are committed; run this again only to change the look. No library is needed.
"""
import math
import os
import struct
import zlib

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "UnityClient", "Assets", "LifeVerse", "UI", "Resources", "LifeVerse", "UI", "Skin")


def rgba(value, alpha=1.0):
    value = value.lstrip("#")
    return (int(value[0:2], 16) / 255.0, int(value[2:4], 16) / 255.0, int(value[4:6], 16) / 255.0, alpha)


def over(top, bottom):
    """Alpha compositing of straight (not premultiplied) colours."""
    a = top[3] + bottom[3] * (1 - top[3])
    if a <= 0:
        return (0, 0, 0, 0)
    return tuple((top[i] * top[3] + bottom[i] * bottom[3] * (1 - top[3])) / a for i in range(3)) + (a,)


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(4))


def gradient(stops, t):
    """stops: [(position 0..1, colour)], in order."""
    if t <= stops[0][0]:
        return stops[0][1]
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        if t <= p1:
            return mix(c0, c1, (t - p0) / max(1e-6, p1 - p0))
    return stops[-1][1]


def distance(x, y, w, h, radii):
    """Signed distance to a rectangle with its own radius at each corner (top-left, top-right, bottom-right, bottom-left): negative inside."""
    left, top = x < w / 2.0, y < h / 2.0
    r = radii[0] if left and top else radii[1] if top else radii[3] if left else radii[2]
    qx = abs(x - w / 2.0) - (w / 2.0 - r)
    qy = abs(y - h / 2.0) - (h / 2.0 - r)
    return math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - r


def coverage(d):
    return max(0.0, min(1.0, 0.5 - d))


def write(name, w, h, pixel):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        for x in range(w):
            c = pixel(x + 0.5, y + 0.5)
            raw.extend(int(max(0.0, min(1.0, v)) * 255 + 0.5) for v in c)

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, name + ".png"), "wb") as f:
        f.write(png)


def box(name, w, h, fill, radius=10, border=None, border_width=2, highlight=None, shade=None, bottom_line=None, left_bar=None, glow=None, margin=0):
    """A bevelled rectangle: a vertical gradient (fill), an outline, a light line under the top edge (highlight), a dark one above the bottom (shade), an accent line at the
    bottom (bottom_line = (colour, thickness)) or at the left (left_bar), and a glow of a colour inside the outline."""
    radii = radius if isinstance(radius, (tuple, list)) else (radius,) * 4

    def pixel(x, y):
        bx, by, bw, bh = x - margin, y - margin, w - 2 * margin, h - 2 * margin
        d = distance(bx, by, bw, bh, radii)
        inside = coverage(d)
        if inside <= 0:
            return (0, 0, 0, 0)
        c = gradient(fill, by / bh)
        depth = -d
        if glow and depth < glow[1]:
            k = (1 - depth / glow[1]) ** 2
            c = over(glow[0][:3] + (glow[0][3] * k,), c)
        edge = border_width if border else 0
        if highlight and edge <= by < edge + 2 and depth >= edge:
            c = over(highlight, c)
        if shade and bh - edge - 2 <= by < bh - edge and depth >= edge:
            c = over(shade, c)
        if bottom_line and by >= bh - bottom_line[1]:
            c = over(bottom_line[0], c)
        if left_bar and bx < left_bar[1]:
            c = over(left_bar[0], c)
        if border and depth < border_width:
            k = max(0.0, min(1.0, border_width - depth + 0.5))
            c = mix(c, over(border, c) if border[3] < 1 else border, k)
        return c[:3] + (c[3] * inside,)

    write(name, w, h, pixel)


def shadow(name, size=96, blur=28, radius=16, strength=0.55):
    def pixel(x, y):
        d = distance(x - blur, y - blur, size - 2 * blur, size - 2 * blur, (radius,) * 4)
        if d <= -1:
            return (0, 0, 0, 0)          # nothing under the window itself: a translucent window must not look darker
        t = max(0.0, min(1.0, 1 - d / blur))
        return (0, 0, 0, strength * t * t)

    write(name, size, size, pixel)


def segment(x, y, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / max(1e-6, dx * dx + dy * dy)))
    return math.hypot(x - ax - t * dx, y - ay - t * dy)


def checkbox(name, on):
    def pixel(x, y):
        d = distance(x, y, 36, 36, (8,) * 4)
        inside = coverage(d)
        if inside <= 0:
            return (0, 0, 0, 0)
        c = gradient([(0, rgba("#1FB5A8")), (1, rgba("#0E7E78"))], y / 36) if on else gradient([(0, rgba("#0C0F14")), (1, rgba("#1A2029"))], y / 36)
        if -d < 2:
            c = rgba("#5CF0E2") if on else rgba("#6B778C")
        if on:
            tick = min(segment(x, y, 9, 19, 15.5, 25.5), segment(x, y, 15.5, 25.5, 27, 11.5))
            c = over((1, 1, 1, coverage(tick - 2.6)), c)
        return c[:3] + (c[3] * inside,)

    write(name, 36, 36, pixel)


def knob(name, top, bottom, ring, size=36):
    def pixel(x, y):
        d = math.hypot(x - size / 2, y - size / 2) - (size / 2 - 1)
        inside = coverage(d)
        if inside <= 0:
            return (0, 0, 0, 0)
        c = gradient([(0, top), (1, bottom)], y / size)
        if -d < 3:
            c = ring
        return c[:3] + (c[3] * inside,)

    write(name, size, size, pixel)


def triangle(name, points, size=24, colour="#C9D3E3"):
    def side(x, y, a, b):
        return (x - b[0]) * (a[1] - b[1]) - (a[0] - b[0]) * (y - b[1])

    def pixel(x, y):
        hit = 0
        for sx in (-0.25, 0.25):
            for sy in (-0.25, 0.25):
                s = [side(x + sx, y + sy, points[i], points[(i + 1) % 3]) for i in range(3)]
                if all(v >= 0 for v in s) or all(v <= 0 for v in s):
                    hit += 1
        return rgba(colour)[:3] + (hit / 4.0,)

    write(name, size, size, pixel)


def grip(name):
    def pixel(x, y):
        a = 0.0
        for offset in (10, 18, 26):
            a = max(a, coverage(abs((x + y) - (56 - offset)) - 1.2) if x > 4 and y > 4 else 0)
        return rgba("#8C99AE")[:3] + (a * 0.9,)

    write(name, 28, 28, pixel)


def main():
    black = rgba("#05070A")
    line = rgba("#FFFFFF", 0.10)
    # floating windows: the body, the title bar and the shadow around them
    box("window", 64, 64, [(0, rgba("#1D232C")), (1, rgba("#1A1F27"))], radius=16, border=rgba("#586478"), highlight=line)
    box("titlebar", 64, 68, [(0, rgba("#465167")), (0.5, rgba("#343D4E")), (1, rgba("#272E3A"))], radius=(16, 16, 0, 0), border=rgba("#586478"), highlight=rgba("#FFFFFF", 0.18), bottom_line=(black, 2))
    box("titlebar-front", 64, 68, [(0, rgba("#4E6178")), (0.5, rgba("#37495C")), (1, rgba("#283441"))], radius=(16, 16, 0, 0), border=rgba("#6C8199"), highlight=rgba("#FFFFFF", 0.2), bottom_line=(rgba("#2ED3C6"), 3))
    shadow("shadow")
    # panels of the HUD, menus and tooltips
    box("panel", 48, 48, [(0, rgba("#232A35", 0.975)), (1, rgba("#171C24", 0.975))], radius=14, border=rgba("#4C586C"), highlight=line)
    box("panel-solid", 48, 48, [(0, rgba("#2A313D")), (1, rgba("#1B2029"))], radius=12, border=rgba("#63708A"), highlight=line)
    box("tooltip", 32, 32, [(0, rgba("#FFF3C9")), (1, rgba("#F4DF9B"))], radius=8, border=rgba("#6B5519"))
    box("inset", 32, 32, [(0, rgba("#0D1116", 0.62)), (1, rgba("#12171E", 0.55))], radius=10, border=rgba("#000000", 0.55), shade=rgba("#FFFFFF", 0.07))
    # the bars of the HUD (stretched sideways)
    box("menubar", 16, 60, [(0, rgba("#333B49", 0.995)), (1, rgba("#1F252E", 0.995))], radius=0, highlight=rgba("#FFFFFF", 0.09), bottom_line=(black, 2))
    box("navbar", 16, 76, [(0, rgba("#283039", 0.985)), (1, rgba("#1A1F28", 0.985))], radius=0, highlight=rgba("#FFFFFF", 0.06), bottom_line=(black, 2))
    box("toolbar", 16, 124, [(0, rgba("#343C4B", 0.96)), (0.12, rgba("#252C37", 0.96)), (1, rgba("#12161D", 0.98))], radius=0, highlight=rgba("#FFFFFF", 0.14))
    # buttons
    plain = dict(radius=10, border=black, highlight=rgba("#FFFFFF", 0.17), shade=rgba("#000000", 0.30))
    box("btn", 40, 56, [(0, rgba("#4A5366")), (0.5, rgba("#3A4252")), (1, rgba("#2B313E"))], **plain)
    box("btn-hover", 40, 56, [(0, rgba("#5D6A82")), (0.5, rgba("#4A556A")), (1, rgba("#373F50"))], radius=10, border=rgba("#2ED3C6"), highlight=rgba("#FFFFFF", 0.24))
    box("btn-down", 40, 56, [(0, rgba("#1F242E")), (1, rgba("#333B4A"))], radius=10, border=rgba("#2ED3C6"), shade=rgba("#FFFFFF", 0.08))
    box("btn-disabled", 40, 56, [(0, rgba("#2E333D", 0.85)), (1, rgba("#262B34", 0.85))], radius=10, border=rgba("#05070A", 0.8))
    box("btn-primary", 40, 56, [(0, rgba("#25C2B4")), (0.5, rgba("#14968D")), (1, rgba("#0B6B66"))], radius=10, border=rgba("#03211F"), highlight=rgba("#FFFFFF", 0.35), shade=rgba("#000000", 0.28))
    box("btn-primary-hover", 40, 56, [(0, rgba("#3FE3D3")), (0.5, rgba("#1DB5A9")), (1, rgba("#10857D"))], radius=10, border=rgba("#B9FFF7"), highlight=rgba("#FFFFFF", 0.4))
    box("btn-primary-down", 40, 56, [(0, rgba("#08504C")), (1, rgba("#13867E"))], radius=10, border=rgba("#B9FFF7"))
    box("btn-gold", 40, 56, [(0, rgba("#E7A93C")), (0.5, rgba("#BA7D1F")), (1, rgba("#865512"))], radius=10, border=rgba("#2A1900"), highlight=rgba("#FFFFFF", 0.35), shade=rgba("#000000", 0.28))
    box("btn-gold-hover", 40, 56, [(0, rgba("#FFC759")), (0.5, rgba("#D99528")), (1, rgba("#A16818"))], radius=10, border=rgba("#FFE7B0"), highlight=rgba("#FFFFFF", 0.4))
    box("btn-selected", 40, 56, [(0, rgba("#0C5753")), (1, rgba("#127A73"))], radius=10, border=rgba("#3FE3D3"), shade=rgba("#FFFFFF", 0.1))
    box("btn-flat-hover", 32, 32, [(0, rgba("#FFFFFF", 0.13)), (1, rgba("#FFFFFF", 0.08))], radius=8, border=rgba("#FFFFFF", 0.16))
    # the buttons of the bottom toolbar
    box("tool", 40, 56, [(0, rgba("#FFFFFF", 0.07)), (1, rgba("#FFFFFF", 0.015))], radius=12, border=rgba("#FFFFFF", 0.08))
    box("tool-hover", 40, 56, [(0, rgba("#7F93B3", 0.45)), (1, rgba("#4D5C75", 0.30))], radius=12, border=rgba("#9FB4D6", 0.75), highlight=rgba("#FFFFFF", 0.2))
    box("tool-open", 40, 56, [(0, rgba("#0F4A49", 0.95)), (1, rgba("#14807A", 0.95))], radius=12, border=rgba("#3FE3D3"), bottom_line=(rgba("#7FFFF1"), 6), glow=(rgba("#3FE3D3", 0.35), 12))
    box("tool-open-orange", 40, 56, [(0, rgba("#5A3A10", 0.95)), (1, rgba("#9A6418", 0.95))], radius=12, border=rgba("#FFC45C"), bottom_line=(rgba("#FFE0A3"), 6), glow=(rgba("#FFC45C", 0.35), 12))
    # fields and lists
    box("field", 32, 40, [(0, rgba("#0B0E13")), (0.35, rgba("#141920")), (1, rgba("#1A2029"))], radius=9, border=rgba("#4A566B"), shade=rgba("#FFFFFF", 0.05))
    box("field-focus", 32, 40, [(0, rgba("#0B1214")), (1, rgba("#18252A"))], radius=9, border=rgba("#3FE3D3"), glow=(rgba("#3FE3D3", 0.28), 8))
    box("row", 32, 32, [(0, rgba("#FFFFFF", 0.065)), (1, rgba("#FFFFFF", 0.035))], radius=10, border=rgba("#000000", 0.35))
    box("row-hover", 32, 32, [(0, rgba("#7F93B3", 0.30)), (1, rgba("#7F93B3", 0.18))], radius=10, border=rgba("#9FB4D6", 0.5))
    box("tab", 32, 40, [(0, rgba("#FFFFFF", 0.10)), (1, rgba("#FFFFFF", 0.06))], radius=9, border=rgba("#FFFFFF", 0.10))
    box("tab-selected", 32, 40, [(0, rgba("#15938A")), (1, rgba("#0C5E59"))], radius=9, border=rgba("#042B28"), highlight=rgba("#FFFFFF", 0.25), left_bar=(rgba("#8CFFF2"), 6))
    box("plate", 48, 48, [(0, rgba("#10161E", 0.82)), (1, rgba("#080C12", 0.82))], radius=20, border=rgba("#FFFFFF", 0.22))
    # small controls
    checkbox("check", False)
    checkbox("check-on", True)
    box("track", 24, 16, [(0, rgba("#06080B")), (1, rgba("#1D232D"))], radius=7, border=rgba("#4A566B"))
    box("fill", 24, 16, [(0, rgba("#5CF0E2")), (1, rgba("#14968D"))], radius=7, border=rgba("#042B28"))
    knob("knob", rgba("#E9F0FA"), rgba("#93A1B8"), rgba("#0B0E13"))
    box("thumb", 16, 32, [(0, rgba("#8592A9")), (1, rgba("#8592A9"))], radius=7, border=rgba("#0B0E13", 0.8))
    triangle("arrow-down", [(4, 8), (20, 8), (12, 18)])
    grip("grip")
    print("ok", len([f for f in os.listdir(OUT) if f.endswith(".png")]), "textures in", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
