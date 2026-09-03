"""Regenerates the CAI mark: logo (256), favicon (64, bold cut) and share card assets.

The mark is drawn geometrically (supersampled 8x, Lanczos downscale) rather than kept as a
drawing-tool file, so a colour or proportion change is a diff, not a re-export.
Run: python3 generate.py  (writes into this directory; the share card needs the HTML step below)
"""
from PIL import Image, ImageDraw

SS = 8  # supersample factor

TEAL = (42, 169, 139, 255)
TEAL_DARK = (23, 122, 99, 255)
BANDS = [(201, 79, 67, 255), (217, 127, 62, 255), (201, 161, 59, 255),
         (85, 160, 108, 255), (46, 143, 117, 255)]


def draw_mark(px, bold=False):
    """The lens over the pin. bold=True is the favicon cut: thicker ring, taller
    bands, larger pin - tuned so the mark survives 16 px in a browser tab."""
    W = px * SS
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = W / 64.0
    cx, cy, r = 27 * u, 27 * u, 16.5 * u
    ring = (5.6 if bold else 4.6) * u
    hw = (8.4 if bold else 7.2) * u

    d.line([(38.5 * u, 38.5 * u), (53 * u, 53 * u)], fill=TEAL_DARK, width=int(hw))
    for hx, hy in [(38.5, 38.5), (53, 53)]:
        rr = hw / 2
        d.ellipse([hx * u - rr, hy * u - rr, hx * u + rr, hy * u + rr], fill=TEAL_DARK)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=TEAL, width=int(ring))

    seg = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    sd = ImageDraw.Draw(seg)
    y0, y1 = (23.6 * u, 30.8 * u) if bold else (24.8 * u, 29.6 * u)
    for x, c in zip([13, 18.8, 24.6, 30.4, 36.2], BANDS):
        w = 6.6 if c == BANDS[-1] else 5.0
        sd.rounded_rectangle([x * u, y0, (x + w) * u, y1], radius=1.5 * u, fill=c)
    mask = Image.new("L", (W, W), 0)
    ri = r - ring * 0.65
    ImageDraw.Draw(mask).ellipse([cx - ri, cy - ri, cx + ri, cy + ri], fill=255)
    img.paste(seg, (0, 0), Image.composite(seg.split()[3], Image.new("L", (W, W), 0), mask))

    pcx, pcy = 30.2 * u, 27.2 * u
    h = (6.6 if bold else 5.2) * u
    pts = [(pcx, pcy - h), (pcx + h, pcy), (pcx, pcy + h), (pcx - h, pcy)]
    d.polygon(pts, fill=(255, 255, 255, 255))
    ow = (2.6 if bold else 2.0) * u
    for i in range(4):
        d.line([pts[i], pts[(i + 1) % 4]], fill=TEAL_DARK, width=int(ow))
        rr = ow / 2
        p = pts[i]
        d.ellipse([p[0] - rr, p[1] - rr, p[0] + rr, p[1] + rr], fill=TEAL_DARK)
    return img.resize((px, px), Image.LANCZOS)


if __name__ == "__main__":
    draw_mark(256).save("cai-logo-256.png")
    draw_mark(64, bold=True).save("cai-favicon-64.png")
    print("wrote cai-logo-256.png, cai-favicon-64.png")
    print("share card: render docs/brand og-card HTML at 1200x630 with the 256 mark (see README)")
