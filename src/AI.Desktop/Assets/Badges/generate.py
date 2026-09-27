"""Regenerate the Windows taskbar overlay icons (requires Pillow at design time)."""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


root = Path(__file__).parent
font_path = "arialbd.ttf"

for label in [*(str(value) for value in range(1, 10)), "9+"]:
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse((2, 2, 61, 61), fill="#345f91")
    font = ImageFont.truetype(font_path, 42 if len(label) == 1 else 31)
    bounds = draw.textbbox((0, 0), label, font=font)
    x = (64 - (bounds[2] - bounds[0])) / 2 - bounds[0]
    y = (64 - (bounds[3] - bounds[1])) / 2 - bounds[1] - 1
    draw.text((x, y), label, font=font, fill="white")
    image.save(root / f"{label.replace('+', 'plus')}.ico", sizes=[(16, 16), (32, 32)])
