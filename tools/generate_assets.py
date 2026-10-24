from PIL import Image, ImageDraw, ImageFilter
import os

SIZE = 1024
SCALE = 2
S = SIZE * SCALE
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "AudioOrb", "Assets")
os.makedirs(OUT, exist_ok=True)

# Layered Windows 11 / Fluent app icon: a colored squircle, a dimensional
# glyph, a soft shadow, and a restrained highlight.
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
pad = 62 * SCALE
radius = 220 * SCALE
tile = (pad, pad, S - pad, S - pad)

mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle(tile, radius=radius, fill=255)

gradient = Image.new("RGBA", (S, S), (0, 0, 0, 0))
g = ImageDraw.Draw(gradient)
top = (43, 136, 216)
bottom = (8, 78, 145)
for i in range(S * 2):
    t = i / (S * 2 - 1)
    g.line(
        [(0, i), (i, 0)],
        fill=(
            int(top[0] * (1 - t) + bottom[0] * t),
            int(top[1] * (1 - t) + bottom[1] * t),
            int(top[2] * (1 - t) + bottom[2] * t),
            255,
        ),
    )
img.paste(gradient, (0, 0), mask)

# Cool light source in the upper-left, like the Edge / Mail tiles.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse((-220 * SCALE, -260 * SCALE, 760 * SCALE, 660 * SCALE), fill=(150, 220, 255, 80))
glow = glow.filter(ImageFilter.GaussianBlur(120 * SCALE))
img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

# Inner bottom shade: gives the tile the soft volume of Mail's tile.
shade = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(shade)
sd.ellipse((60 * SCALE, 650 * SCALE, 980 * SCALE, 1180 * SCALE), fill=(0, 35, 80, 90))
shade = shade.filter(ImageFilter.GaussianBlur(110 * SCALE))
img = Image.alpha_composite(img, Image.composite(shade, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

# Glyph mask: headphone + equalizer.
glyph = Image.new("L", (S, S), 0)
d = ImageDraw.Draw(glyph)
d.arc((246 * SCALE, 230 * SCALE, 778 * SCALE, 714 * SCALE), start=182, end=358, fill=255, width=64 * SCALE)
for x in (246, 622):
    d.rounded_rectangle(
        (x * SCALE, 500 * SCALE, (x + 156) * SCALE, 738 * SCALE),
        radius=54 * SCALE,
        fill=255,
    )
for x, top_y in [(426, 592), (487, 500), (548, 554)]:
    d.rounded_rectangle(
        (x * SCALE, top_y * SCALE, (x + 34) * SCALE, 648 * SCALE),
        radius=17 * SCALE,
        fill=255,
    )

# Soft glyph shadow, kept inside the tile.
shadow = glyph.filter(ImageFilter.GaussianBlur(22 * SCALE))
shadow = shadow.point(lambda p: int(p * 0.55))
shadow_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
shadow_layer.putalpha(shadow)
shadow_layer = Image.composite(
    shadow_layer,
    Image.new("RGBA", (S, S), (0, 0, 0, 0)),
    mask,
)
shadow_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
shadow_layer.putalpha(shadow)
img = Image.alpha_composite(img, Image.composite(shadow_layer, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

# Glyph gradient: white at the top, icy blue at the bottom.
glyph_gradient = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gg = ImageDraw.Draw(glyph_gradient)
gg.rectangle((0, 0, S, S), fill=(255, 255, 255, 255))
for y in range(420 * SCALE, 760 * SCALE):
    t = (y - 420 * SCALE) / (340 * SCALE)
    t = min(1.0, max(0.0, t))
    gg.line(
        [(0, y), (S, y)],
        fill=(
            int(255 * (1 - t) + 205 * t),
            int(255 * (1 - t) + 231 * t),
            int(255 * (1 - t) + 255 * t),
            255,
        ),
    )
glyph_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
glyph_layer.paste(glyph_gradient, (0, 0), glyph)
img = Image.alpha_composite(img, glyph_layer)

# Thin inner rim.
rim = Image.new("RGBA", (S, S), (0, 0, 0, 0))
rd = ImageDraw.Draw(rim)
rd.rounded_rectangle(
    (pad + 2 * SCALE, pad + 2 * SCALE, S - pad - 2 * SCALE, S - pad - 2 * SCALE),
    radius=radius - 2 * SCALE,
    outline=(255, 255, 255, 38),
    width=2 * SCALE,
)
img = Image.alpha_composite(img, rim)

img = img.resize((SIZE, SIZE), Image.LANCZOS)

img.save(os.path.join(OUT, "AppIcon.png"), "PNG")
img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "Square256Logo.png"), "PNG")
img.resize((150, 150), Image.LANCZOS).save(os.path.join(OUT, "Square150x150Logo.png"), "PNG")
img.resize((48, 48), Image.LANCZOS).save(os.path.join(OUT, "Square48Logo.png"), "PNG")

sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
img.save(os.path.join(OUT, "App.ico"), format="ICO", sizes=[(x, x) for x in sizes])
print("Generated", OUT)
