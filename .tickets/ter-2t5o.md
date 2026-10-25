---
id: ter-2t5o
status: closed
deps: [ter-ptft]
links: []
created: 2026-10-11T20:49:05Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, long-term]
---
# Offer John Hable's Uncharted 2 filmic tone curve beside ACES

The user likes the look of John Hable's filmic tone curve from Uncharted 2 (Naughty Dog, GDC 2010, 'Uncharted 2: HDR Lighting'). Today ToneMapPass and tonemap.frag apply only the ACES fitted curve (Stephen Hill's RRT + ODT fit) after the exposure.

Hable's curve, with his published constants:

    f(x) = ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F
    color = f(ExposureBias * x) / f(W)

where A = 0.15 (shoulder strength), B = 0.50 (linear strength), C = 0.10 (linear angle), D = 0.20 (toe strength), E = 0.02 (toe numerator), F = 0.30 (toe denominator), W = 11.2 (the linear white point, which maps to 1), and ExposureBias = 2.0 in his example. It works per channel, on linear HDR color before the sRGB encoding.

Design to decide (two designs, then the user chooses): a RendererOptions choice of curve (ACES or Hable) versus Hable as the only curve; whether the constants are settings; how it interacts with ter-zzyr (automatic exposure) and ter-63wr (bloom), which work on the same HDR stage. The renderer has no snapshot tests yet: ticket ter-rtxu adds them. If they exist by then, a new default re-approves their reference images, each one reviewed by eye.

## Acceptance Criteria

A RendererOptions setting selects Hable's curve. A CPU test checks the curve against the formula at a few points, including f(W) / f(W) = 1 at the white point. Captures of the orbit scene and the test scene with each curve are reviewed by eye, and the user picks the default.


## Design decisions (user, 2026-10-25)

Settled together with automatic exposure (ter-zzyr), which shares the tone map stage:

- The tone curve is a renderer setting with two cases: ACES fitted, as today, and Hable's Uncharted 2 curve.
- Hable's constants are fixed, as published, so the setting has no data per case. His exposure bias of 2 is part of his curve, so the curve looks as he published it.
- The default stays ACES until the user picks one from captures of both curves.
- After ter-zzyr, the tone map pass multiplies by no exposure: the shading passes write pre-exposed light. Whichever of the two tickets merges second resolves the overlap in tonemap.frag.

## The default (user, 2026-10-25)

The user compared captures of both curves side by side (the spheres, the cabin and the orbit) and kept ACES as the default: they prefer its contrast, deep shadows and strong colors. Hable stays as an option.
