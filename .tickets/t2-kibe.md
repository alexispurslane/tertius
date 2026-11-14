---
id: t2-kibe
status: open
deps: []
links: []
created: 2026-11-14T15:44:35Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, lights]
---
# Key and fill: a cheap specular fill, so that the fill lamps keep their soft specular light (the hall is 2.5 % darker without it)

Found in t2-vsqh (ARCHITECTURE.md, 'Key and fill', 'The known bias'). The fill response of a lamp is its diffuse light only. So a pixel loses the specular light of each lamp by the lamp's fill share. A sharp highlight is not lost, because a lamp with one is a key lamp there. What is lost is the sum of the soft specular light of the many lamps that are fill.

The cause is measured (2026-11-14, RTX 2000 Ada, one job, stills at 1920 × 1080 with one fixed exposure). In the hall of 64 lamps with 4 key lights, the level is 2.48 % below every light pair, and 5.3 % of the pixels change by more than 2 steps of 255. A variant that keeps key and fill for the diffuse light and gives every lamp its specular light in full equals every light pair: level +0.00 %, and no pixel changes by more than 1 step. So the missing specular light is the whole loss. The deck loses 0.31 % and the cabin 0.08 %.

One form is measured and does not work as it is. The fill added the surface's compensated specular albedo (specularWeight) times the fill's cosine, as a diffuse light of that color: one multiply and add per fill lamp. Its cost is nothing that the card shows (the hall's direct pass 1.298 ms against 1.308 ms, the deck's 1.556 ms against 1.566 ms, 3 runs each), and the shader stays at register 115 on Apple's GPU in the offline compile. But the hall became 8.46 % brighter than every light pair, with a greatest change of 92 steps. The albedo is the share of a uniform light that the surface reflects, and a far lamp's specular light toward the eye is on average about a quarter of that in this view.

Two designs to weigh, as estimates before any build:

1. A factor per lamp for the lobe's value in the lamp's direction, on the form above. The cost is the factor's: a plain GGX value at the lamp's center is most of what the full response of a point lamp costs, so this can give back much of the gain (0.45 ms of the hall's direct pass on the card).
2. A remainder per corner. The corner pass already shades every lamp in full at each corner. It also sums the specular light of all lamps, in the free fourth channel of its third image. The tile pass takes off what the key lamps keep, and writes the rest, one value per corner, into the tile's record (4 more words per tile). The direct pass adds the rest by the pixel's corner shares, after its loops, so it holds no new value across them. Expected cost: about 0.02 ms on the card and about 0.05 ms on the Mac, to be measured. Expected faults: the rest has the corner's shadows and the corner's surface, not the pixel's, so it is soft over a tile of 16 pixels, and one value per corner carries no color of the lamps.

Do not build before the user asks (the orchestrator, 2026-11-14: not this week). Depends on the room under Apple's register step (t2-x53j) if a design holds a new value across the light loops.

Acceptance: the hall's level with key and fill is within 0.5 % of every light pair, the deck and the cabin show no new visible change beside the reference, and key and fill keeps its gain on the deck's trace on the Mac and in the hall on the card.

