"""Create the macOS icon (1024 px PNG) with the same geometry as the Windows icon,
inside Apple's icon grid (824 px body with transparent margin)."""
from PIL import Image, ImageDraw
from pathlib import Path
root = Path(__file__).resolve().parents[1]
S = 1024
scale, offset = 824 / 248, 100
def p(x, y): return (offset + (x - 4) * scale, offset + (y - 4) * scale)
img = Image.new('RGBA', (S * 2, S * 2), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
def q(x, y): a, b = p(x, y); return (a * 2, b * 2)
d.rounded_rectangle((*q(4, 4), *q(252, 252)), radius=185 * 2, fill='#181d24')
d.polygon([q(86, 57), q(86, 199), q(202, 128)], fill='#72e1b7')
d.polygon([q(86, 57), q(86, 199), q(113, 182), q(113, 74)], fill='#aff4d8')
img = img.resize((S, S), Image.LANCZOS)
img.save(root / 'mac' / 'AppIcon.png', optimize=True)
