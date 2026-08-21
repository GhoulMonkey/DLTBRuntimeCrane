# SPDX-License-Identifier: GPL-3.0-only
"""Generates CraneLoader.ico (and PNG previews) with no third-party deps.

A tower crane, because the mod is called CRANE and a silhouette is the one kind
of artwork that survives being shrunk to 16 pixels in a Vortex tool list, which
is where this icon actually lives.

Palette taken from the Nexus banner rather than from the app's own light theme:
the icon sits beside the banner far more often than beside the window.
"""
import os, struct, zlib

BG = (0x17, 0x0F, 0x09)      # warm near-black, the banner's ground
FG = (0xEE, 0xE7, 0xDD)      # bone white, the wordmark's colour
ACCENT = (0xE2, 0x7A, 0x14)  # sunset amber, the banner's light

# The silhouette, painted in order.
#   ('r', x0, y0, x1, y1, colour)            axis-aligned rectangle
#   ('s', x0, y0, x1, y1, half_width, colour) thick line segment
#
# Two earlier attempts read as a letter T. The fixes, in order of how much they
# helped: make the jib and counter-jib ASYMMETRIC about the mast; add the apex
# and tie-bars, which no letterform has; and shrink the foot, which was reading
# as a serif. Diagonals are what a glyph will not give you.
SHAPES = [
    # Tie-bars from the apex out to each arm.
    ('s', 0.35, 0.10, 0.86, 0.255, 0.022, FG),
    ('s', 0.35, 0.10, 0.13, 0.255, 0.022, FG),
    # Counter-jib and its counterweight: short, opposite the jib.
    ('r', 0.10, 0.255, 0.30, 0.315, FG),
    ('r', 0.11, 0.315, 0.21, 0.415, ACCENT),
    # The jib: long, reaching most of the way across.
    ('r', 0.30, 0.255, 0.94, 0.315, FG),
    # Mast, narrow, left of centre, running from the apex to the ground.
    ('r', 0.305, 0.10, 0.395, 0.86, FG),
    # Hoist line and hook block, well out along the jib.
    ('r', 0.765, 0.315, 0.800, 0.58, FG),
    ('r', 0.715, 0.58, 0.855, 0.675, ACCENT),
    # Foot: small. A wide one reads as a serif and turns the whole thing into a
    # letter again.
    ('r', 0.245, 0.86, 0.455, 0.905, FG),
]


def in_segment(x, y, x0, y0, x1, y1, hw):
    dx = x1 - x0
    dy = y1 - y0
    length2 = dx * dx + dy * dy
    if length2 == 0:
        return (x - x0) ** 2 + (y - y0) ** 2 <= hw * hw
    t = ((x - x0) * dx + (y - y0) * dy) / length2
    if t < 0.0:
        t = 0.0
    elif t > 1.0:
        t = 1.0
    px = x0 + t * dx
    py = y0 + t * dy
    return (x - px) ** 2 + (y - py) ** 2 <= hw * hw


def inside_rounded(x, y, r):
    if x < r and y < r:
        return (x - r) ** 2 + (y - r) ** 2 <= r * r
    if x > 1 - r and y < r:
        return (x - (1 - r)) ** 2 + (y - r) ** 2 <= r * r
    if x < r and y > 1 - r:
        return (x - r) ** 2 + (y - (1 - r)) ** 2 <= r * r
    if x > 1 - r and y > 1 - r:
        return (x - (1 - r)) ** 2 + (y - (1 - r)) ** 2 <= r * r
    return True


def render(size):
    # Pure-Python rasterising, so the supersample factor drops at large sizes:
    # 8x at 256 would be a 2048-square loop for no visible gain.
    ss = 8 if size <= 64 else 3
    big = size * ss
    radius = 0.16
    rows = []
    for py in range(big):
        y = (py + 0.5) / big
        row = []
        for px in range(big):
            x = (px + 0.5) / big
            if not inside_rounded(x, y, radius):
                row.append((0, 0, 0, 0))
                continue
            colour = BG
            for shape in SHAPES:
                if shape[0] == 'r':
                    _, x0, y0, x1, y1, c = shape
                    if x0 <= x <= x1 and y0 <= y <= y1:
                        colour = c
                else:
                    _, x0, y0, x1, y1, hw, c = shape
                    if in_segment(x, y, x0, y0, x1, y1, hw):
                        colour = c
            row.append((colour[0], colour[1], colour[2], 255))
        rows.append(row)

    out = []
    n = ss * ss
    for oy in range(size):
        line = []
        for ox in range(size):
            r = g = b = a = 0
            for sy in range(ss):
                src = rows[oy * ss + sy]
                for sx in range(ss):
                    pr, pg, pb, pa = src[ox * ss + sx]
                    if pa:
                        r += pr
                        g += pg
                        b += pb
                        a += pa
            if a == 0:
                line.append((0, 0, 0, 0))
            else:
                covered = a // 255
                line.append((r // covered, g // covered, b // covered, a // n))
        out.append(line)
    return out


def png_bytes(img):
    size = len(img)
    raw = b''
    for row in img:
        raw += b'\x00' + b''.join(struct.pack('BBBB', *p) for p in row)

    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data +
                struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9))
            + chunk(b'IEND', b''))


def dib_bytes(img):
    """32-bit BGRA DIB plus an empty AND mask, bottom-up, as an ICO wants."""
    size = len(img)
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b''
    for row in reversed(img):
        pixels += b''.join(struct.pack('BBBB', p[2], p[1], p[0], p[3]) for p in row)
    mask_row = ((size + 31) // 32) * 4
    return header + pixels + b'\x00' * (mask_row * size)


sizes = [16, 24, 32, 48, 64, 256]
images = {}
for s in sizes:
    images[s] = render(s)

entries = []
blobs = []
offset = 6 + 16 * len(sizes)
for s in sizes:
    # 256 goes in as PNG, which is what the format expects at that size and
    # keeps the file from ballooning.
    blob = png_bytes(images[s]) if s == 256 else dib_bytes(images[s])
    dim = 0 if s == 256 else s
    entries.append(struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(blob), offset))
    blobs.append(blob)
    offset += len(blob)

ico = struct.pack('<HHH', 0, 1, len(sizes)) + b''.join(entries) + b''.join(blobs)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), os.pardir)
open(os.path.join(out, 'CraneLoader.ico'), 'wb').write(ico)

open(os.path.join(out, 'preview256.png'), 'wb').write(png_bytes(images[256]))

# The 16px tile blown up with nearest-neighbour, so it can be judged at the size
# it is actually used at.
zoom = []
for row in images[16]:
    wide = []
    for p in row:
        wide.extend([p] * 10)
    for _ in range(10):
        zoom.append(wide)
open(os.path.join(out, 'preview16.png'), 'wb').write(png_bytes(zoom))
print('ico bytes:', len(ico))
