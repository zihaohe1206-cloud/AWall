"""Render AWall.png / AWall.ico (reference renderer; Draw-Icon.ps1 draws the same mark on Windows).

Mark: a rounded tile with an indigo -> cyan gradient, a white "A" peak and a wave
running through it as the crossbar (a live landscape), plus a small moon.
Small frames (<=32px) use a simplified, thicker mark so the tray icon stays crisp.
"""
import io, math, struct, sys
import numpy as np
from PIL import Image, ImageDraw

TOP, BOTTOM = (124, 97, 255), (31, 182, 232)


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def tile(size, inset, radius):
    yy, xx = np.mgrid[0:size, 0:size]
    t = np.clip((xx * 0.45 + yy * 0.9) / (size * 1.35), 0, 1)[..., None]
    rgb = np.array(TOP) + (np.array(BOTTOM) - np.array(TOP)) * t
    grad = Image.fromarray(np.dstack([rgb, np.full((size, size, 1), 255)]).astype("uint8"), "RGBA")
    # soft highlight on the upper half
    hl = Image.new("L", (size, size), 0)
    hd = ImageDraw.Draw(hl)
    for y in range(size // 2):
        hd.line([(0, y), (size, y)], fill=round(34 * (1 - y / (size / 2))))
    white = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    grad = Image.composite(white, grad, hl)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([inset, inset, size - inset, size - inset], radius, fill=255)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out


def stroke(draw, pts, width, fill):
    """Round-capped, round-joined stroke: stamp discs densely along the path."""
    r = width / 2
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        steps = max(1, int(math.hypot(x1 - x0, y1 - y0) / 1.5))
        for i in range(steps + 1):
            x, y = x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps
            draw.ellipse([x - r, y - r, x + r, y + r], fill=fill)


def wave(x0, x1, y, amp, n=64):
    return [(x0 + (x1 - x0) * i / n, y - amp * math.sin(2 * math.pi * i / n)) for i in range(n + 1)]


def render(size, simple=False):
    big = 1024  # always draw on a large grid, then downsample
    img = tile(big, 64 if not simple else 40, 232 if not simple else 250)
    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    if simple:
        stroke(d, [(270, 790), (512, 250), (754, 790)], 150, white)
        stroke(d, wave(300, 724, 610, 36), 104, white)
    else:
        # A peak
        stroke(d, [(292, 768), (512, 290), (732, 768)], 104, white)
        # crossbar wave, slightly translucent where it sits on the gradient
        stroke(d, wave(232, 792, 600, 44), 70, white)
        # moon
        d.ellipse([706, 246, 810, 350], fill=(255, 255, 255, 235))
    return img.resize((size, size), Image.LANCZOS)


def dib(img):
    """32bpp BGRA DIB + AND mask, the most compatible ICO frame format."""
    w, h = img.size
    rows = []
    px = img.load()
    for y in range(h - 1, -1, -1):
        rows.append(b"".join(struct.pack("BBBB", px[x, y][2], px[x, y][1], px[x, y][0], px[x, y][3]) for x in range(w)))
    mask_row = ((w + 31) // 32) * 4
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    return header + b"".join(rows) + b"\0" * (mask_row * h)


def main(out_dir):
    master = render(1024)
    master.save(f"{out_dir}/AWall.png")
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    frames = []
    for sz in sizes:
        im = render(sz, simple=sz <= 32)
        if sz == 256:
            b = io.BytesIO(); im.save(b, "PNG"); frames.append(b.getvalue())
        else:
            frames.append(dib(im))
    with open(f"{out_dir}/AWall.ico", "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(sizes)))
        off = 6 + 16 * len(sizes)
        for sz, data in zip(sizes, frames):
            dim = 0 if sz >= 256 else sz
            f.write(struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), off))
            off += len(data)
        for data in frames:
            f.write(data)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
