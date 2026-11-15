# filmsheet.py <out.png> <case> <zoom> <label=variant[:still]>...: a sheet of a film's sampled frames, one row for the
# middle frame and one for the last frame, one column per argument. "variant" shows the variant's film frame
# (s/<variant>/images/motion/<case>-<middle|end>.png), and "variant:still" the still of the same state that the survey
# set the frame against (<case>-<middle|end>-still.png). Labels on top of each tile.
import sys
from PIL import Image, ImageDraw
out, case, zoom = sys.argv[1], sys.argv[2], float(sys.argv[3])
rows = []
for sample in ("middle", "end"):
    tiles = []
    for arg in sys.argv[4:]:
        label, v = arg.split("=", 1)
        name = f"{case}-{sample}-still.png" if v.endswith(":still") else f"{case}-{sample}.png"
        im = Image.open(f"s/{v.split(':')[0]}/images/motion/{name}").convert("RGB")
        im = im.resize((int(im.width * zoom), int(im.height * zoom)), Image.LANCZOS)
        tile = Image.new("RGB", (im.width, im.height + 22), (0, 0, 0)); tile.paste(im, (0, 22))
        ImageDraw.Draw(tile).text((4, 5), f"{label}, {sample} frame", fill=(255, 255, 255)); tiles.append(tile)
    rows.append(tiles)
w = sum(t.width for t in rows[0]) + 6 * (len(rows[0]) - 1); h = sum(r[0].height for r in rows) + 6
sheet = Image.new("RGB", (w, h), (60, 60, 60)); y = 0
for r in rows:
    x = 0
    for t in r: sheet.paste(t, (x, y)); x += t.width + 6
    y += r[0].height + 6
sheet.save(out); print(out, sheet.size)
