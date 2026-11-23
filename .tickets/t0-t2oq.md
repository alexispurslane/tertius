---
id: t0-t2oq
status: open
deps: []
links: [t1-af5w]
created: 2026-11-23T21:39:17Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, shadows]
---
# A lamp's shadow edge of a far caster is 2.6 to 13 times too sharp: the fixed ramp of 4 tile texels, and no form of a wider read fits the direct pass's cost

Left open by the shadow softness ticket t1-af5w on 2026-11-23. A lamp's share at a receiver is the lit share of a square of 4 tile texels, for every gap between caster and receiver. The measure (docs/research/t1-af5w-shadow-softness/README.md) found the 1.2 m panels 6 to 13 times too sharp where the gap is 8 to 11 m, and the 0.15 x 0.5 m panels 2.6 to 4.3 times. The Cycles direct-only reference shows the same shadow bands with much softer edges (pics/part1/ref-deck-far.png, direct-deck-far.png, lean-deck-far.png). Near casters and the contact band at a wall's base are right: in direct light only, the engine draws them black and sharp like the reference.

## Design

What was tried, with the record's numbers. (1) A pyramid of depth moments: lets light through where casters overlap in depth; out. (2) Views across the emitter: draws a double edge beyond about 0.5 m; out. (3) Capped direct taps (a box of 8 texels, 25 gathers, blended with today's box by the footprint's width): clean and close to the reference, but its lean form costs 1.89 ms in the exit cabin's direct passes (key and fill, direct light, light pyramid) against the clean main, and 1.41 ms with its reads replaced by one value. The line's rule was about 0.5 ms. direct_key_fill.frag sits at its register limit r115, and every form measured r116 or more. (4) Coarser depth levels: refused by argument, a thin caster is lost or as dark as a wall. This ticket reopens the question only when one of these changes: the direct pass's register budget, the cost of a second lookup in key and fill, or the user's bar for the lamps' far edges. A design then names one owner of a tile's contents and freshness (the record's section 'The per-tile data that the probe writes and reads'), because the far-field probe of ter-6fka adds a color beside a shadow tile's depth.

## Acceptance Criteria

The far caster's edge in exit-deck is as soft as the Cycles direct-only reference at 1:1, a near caster's edge and the contact band stay sharp, and the direct passes cost at most about 0.5 ms more in exit-cabin against the clean main.

