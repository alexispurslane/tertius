#!/usr/bin/env python3
# Tool code of t1-3er4. Decodes one take's lossless film, keeps the crops that the analysis reads, and the SHA-256 of
# every decoded frame, then the caller deletes the film. Frames 0..59 film the aft view, frame 60 the far view, frame 61
# is d0, the first draw after the real cut, and frame 125 is d64.
# Usage: python3 -I extract.py <take dir>
import hashlib, json, subprocess, sys
from pathlib import Path
import numpy as np
from PIL import Image

W, H, CUT = 1920, 1080, 61
take = Path(sys.argv[1])
dec = subprocess.Popen(["ffmpeg", "-v", "error", "-i", str(take / "film.mkv"), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
                       stdout=subprocess.PIPE)
frames, hashes = [], []
while len(buf := dec.stdout.read(W * H * 3)) == W * H * 3:
    hashes.append(hashlib.sha256(buf).hexdigest())
    frames.append(np.frombuffer(buf, dtype=np.uint8).reshape(H, W, 3))
assert dec.wait() == 0
post = frames[CUT:]
np.savez_compressed(take / "crops.npz",
                    cone=np.stack([f[0:360, 640:1280] for f in post]),      # CONE_BOX of ter-fe63's analyze.py
                    floor=np.stack([f[620:1000, 600:1320] for f in post]),  # FLOOR_BOX
                    nose=np.stack([f[0:330, 750:1170] for f in post]))      # NOSE_BOX
for d in (0, 1, 2, 29, 64):
    Image.fromarray(post[d]).save(take / f"full-d{d}.png")
(take / "hashes.json").write_text(json.dumps({"frames": len(frames), "sha256": hashes}, indent=0))
print(take.name, len(frames), "frames; d0", hashes[CUT][:16], "d29", hashes[CUT + 29][:16])
