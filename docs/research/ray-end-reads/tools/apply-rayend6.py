# Tool code of ter-pdw8, the sixth probe step (S2'), applied after apply-rayend5.py on the same build copy (never to src).
# S2' (bit 8, 256; run as 272 with the guide's bit 4): the share from the surface test alone, with no state and without
# the history's length: share = 1 where the diffuse history's surface test holds a history at the pixel (holdsHistory),
# else 1 / P with P = min(phases, draws since the reset + 1). The share map of S2 (sharemap.py) showed that the length is
# cut at random pixels of a still that pass the surface test (51,304 pixels below a share of 1 at draw 630, of which
# 0.14 % fail the test), so the length is not a witness of a new surface; the test is.
# An impostor keeps a share of 1 (apply-rayend5.py gives it probeHolds = 1).
# Usage: python3 apply-rayend6.py <build copy root>
import sys
from pathlib import Path

root = Path(sys.argv[1])
P = root / 'src/Tertius.Rendering/Shaders/screen_trace_plan.comp'
text = P.read_text()
old = '''    if ((rayEndVariant & 192u) == 0u || probeCovered <= 0.0)
        return;
    float share = (rayEndVariant & 64u) != 0u ? share1 : share2;'''
new = '''    if ((rayEndVariant & 448u) == 0u || probeCovered <= 0.0)
        return;
    float share3 = probeHolds > 0.5 ? 1.0 : 1.0 / phases; // S2' (bit 8): the surface test alone
    float share = (rayEndVariant & 64u) != 0u ? share1 : (rayEndVariant & 256u) != 0u ? share3 : share2;'''
assert text.count(old) == 1
P.write_text(text.replace(old, new))
print('applied')
