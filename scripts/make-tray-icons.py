# Draws the tray icons: the full traffic light with only the current lamp lit.
# Usage: python scripts/make-tray-icons.py   (needs Pillow)
from pathlib import Path
from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parent.parent / "src" / "PatchLight.App" / "Assets"
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SCALE = 8  # draw big, then shrink, for smooth edges

HOUSING = (32, 42, 57, 255)  # same housing and lamps as app.ico
LAMPS = [(239, 68, 68), (250, 204, 21), (34, 197, 94)]  # red, yellow, green (top to bottom)
OFF_MIX = 0.22  # how much of its own color an unlit lamp keeps

# app.ico geometry on a 256 grid: housing box, corner radius, lamp centers and radius
BOX = (69, 9, 187, 246)
CORNER = 34
LAMP_Y = (49.5, 128, 206.5)
LAMP_R = 35.5


def mix(color, amount):
    return tuple(round(h + (c - h) * amount) for c, h in zip(color, HOUSING[:3])) + (255,)


def draw(size, lit):
    s = size * SCALE
    k = s / 256
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([v * k for v in BOX], radius=CORNER * k, fill=HOUSING)

    cx, r = 128 * k, LAMP_R * k
    for i, color in enumerate(LAMPS):
        cy = LAMP_Y[i] * k
        fill = color + (255,) if i == lit else mix(color, OFF_MIX)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)
    return img.resize((size, size), Image.LANCZOS)


def main():
    for name, lit in [("red", 0), ("yellow", 1), ("green", 2), ("gray", None)]:
        frames = [draw(size, lit) for size in SIZES]
        frames[-1].save(ASSETS / f"tray-{name}.ico", sizes=[(n, n) for n in SIZES],
                        append_images=frames[:-1])


if __name__ == "__main__":
    main()
