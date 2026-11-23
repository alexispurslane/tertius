# Tool code of ter-8pco: draws each still's census regions (regions.json, output pixels) on its last capture, with
# their numbers, for the record's region pictures. Usage: python3 overlay.py <regions.json> <takes dir> <case> <take>
import json, sys
from pathlib import Path
from PIL import Image, ImageDraw
regions = json.loads(Path(sys.argv[1]).read_text())[sys.argv[3]]
take = Path(sys.argv[2]) / sys.argv[4]
image = Image.open(take / 'c-s19.png').convert('RGB')
draw = ImageDraw.Draw(image)
for i, region in enumerate(regions, 1):
    x0, y0, x1, y1 = region['rect']
    for d in range(3):
        draw.rectangle([x0 - d, y0 - d, x1 + d, y1 + d], outline=(255, 60, 40))
    draw.text((x0 + 6, y0 + 4), str(i), fill=(255, 60, 40))
image.save(take / 'regions.png')
