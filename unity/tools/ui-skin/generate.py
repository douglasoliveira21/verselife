"""Draws the few textures of the Unity interface that a style sheet cannot draw: the soft shadow of the windows, the check boxes, the arrow of a list and the resize corner.
The rest of the look (the Varanda design) is flat and lives in Theme.uss.

    python unity/tools/ui-skin/generate.py

The textures are drawn at twice the size they show. The files go to UnityClient/Assets/LifeVerse/UI/Resources/LifeVerse/UI/Skin and are committed; run this again only to change
them. No library is needed.
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
        c = rgba("#146B64") if on else rgba("#FFFCF5")
        if -d < 2:
            c = rgba("#0F534D") if on else rgba("#9C6B45")
        if on:
            tick = min(segment(x, y, 9, 19, 15.5, 25.5), segment(x, y, 15.5, 25.5, 27, 11.5))
            c = over((1, 1, 1, coverage(tick - 2.6)), c)
        return c[:3] + (c[3] * inside,)

    write(name, 36, 36, pixel)



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
        return rgba("#9C6B45")[:3] + (a * 0.9,)

    write(name, 28, 28, pixel)


def main():
    # the soft shadow around the floating windows
    shadow("shadow", strength=0.42)
    # the marks a style sheet cannot draw: the check boxes, the arrow of a list and the corner that resizes a window
    checkbox("check", False)
    checkbox("check-on", True)
    triangle("arrow-down", [(4, 8), (20, 8), (12, 18)], colour="#9C6B45")
    grip("grip")
    print("ok", len([f for f in os.listdir(OUT) if f.endswith(".png")]), "textures in", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
