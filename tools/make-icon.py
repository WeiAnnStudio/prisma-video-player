"""Create the application icon from simple vector geometry."""
from PIL import Image, ImageDraw
from pathlib import Path
root = Path(__file__).resolve().parents[1]
img = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((4, 4, 252, 252), radius=54, fill='#181d24')
d.polygon([(86, 57), (86, 199), (202, 128)], fill='#72e1b7')
d.polygon([(86, 57), (86, 199), (113, 182), (113, 74)], fill='#aff4d8')
img.save(root / 'Prisma' / 'Prisma.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
