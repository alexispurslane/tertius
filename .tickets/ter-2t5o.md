---
id: ter-2t5o
status: open
deps: []
links: []
created: 2026-10-11T20:49:05Z
type: feature
priority: 3
assignee: Alexis Purslane
tags: [rendering]
---
# Offer John Hable's Uncharted 2 filmic tone curve beside ACES

The user likes the look of John Hable's filmic tone curve from Uncharted 2 (Naughty Dog, GDC 2010, 'Uncharted 2: HDR Lighting'). Today ToneMapPass and tonemap.frag apply only the ACES fitted curve (Stephen Hill's RRT + ODT fit) after the exposure.

Hable's curve, with his published constants:

    f(x) = ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F
    color = f(ExposureBias * x) / f(W)

where A = 0.15 (shoulder strength), B = 0.50 (linear strength), C = 0.10 (linear angle), D = 0.20 (toe strength), E = 0.02 (toe numerator), F = 0.30 (toe denominator), W = 11.2 (the linear white point, which maps to 1), and ExposureBias = 2.0 in his example. It works per channel, on linear HDR color before the sRGB encoding.

Design to decide (two designs, then the user chooses): a RendererOptions choice of curve (ACES or Hable) versus Hable as the only curve; whether the constants are settings; how it interacts with ter-zzyr (automatic exposure) and ter-63wr (bloom), which work on the same HDR stage. The snapshot tests' reference images use ACES today, so a new default re-approves them, each one reviewed by eye.

## Acceptance Criteria

A RendererOptions setting selects Hable's curve. A CPU test checks the curve against the formula at a few points, including f(W) / f(W) = 1 at the white point. Captures of the orbit scene and the test scene with each curve are reviewed by eye, and the user picks the default.

