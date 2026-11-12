---
id: ter-hek6
status: open
deps: [ter-hxhl]
links: [ter-gi1v]
created: 2026-11-11T18:40:24Z
type: task
priority: 4
assignee: Alexis Purslane
parent: ter-8hyd
---
# Halve the rough reflection rays of heavy views by interleaving blocks in space, not by skipping whole draws

One candidate for the heavy views (proposal section 2.3, design A) is a reflection ray per block in every second draw on rough surfaces, about 1.3 ms on the deck by estimate. Alexis asked on 2026-11-11 to spread the skip in space instead: in each draw, about half of the blocks trace their ray and the other half do not, and the halves swap in the next draw. Every draw then brings new rays into every neighborhood, so the lobe fit's sharing and the history see fresh samples nearby in each draw. A skip of whole draws instead stalls every pixel's reflection in the same draw, makes the noise pulse at half the frame rate and doubles the delay of every reflection change at once.

## Design

Both forms halve the rays per pixel per second, so at the same history length the rough reflections' noise rises by about the square root of 2 in either form. The interleave only changes where the missing rays fall.

Points to settle when it is built:
- Pick each block's phase (even or odd draws) from a hash of the block, as chosenPixelOf already does for the block's turn, not from a regular checkerboard. A regular two-phase checker can beat against FSR's jitter and show as a fine checker pattern, as the shadows did before ter-yfb5.
- The corner rotation in chosenPixelOf advances by the draw index. If a block traces only on draws of one parity, its turn takes only two of its four corners. Advance the turn by the block's own count of traced draws (draw index divided by 2), so all four corners still take turns.
- The draws after a camera cut (FullRateFrames) keep every block tracing.
- ter-gi1v wants more own rays on glossy floors for less grain. This ticket removes rays. Decide both together with the heavy-view decision (ter-8hyd step 10).

## Acceptance Criteria

On the deck, the reflection rays cost about half of their 2.65 ms. The rough reflection noise on the plate and in reflections-low rises by no more than the square root of 2, with no regular pattern in a still under FSR. Alexis accepts the trade in the heavy-view decision.


## Notes

**2026-11-11T18:45:51Z**

Alexis, 2026-11-11: do not cut reflection rays until the other frame-time cuts land: the frame-time cuts (t6-h5e3), the thinner filter (ter-hxhl) and the light budget per tile (ter-69s8). Reflection rays matter for the look and take only 2.65 of the deck's about 24 ms. Revisit this ticket only if the heavy views still miss after those cuts.
