---
id: ter-12xp
status: open
deps: []
links: [tiunr-nxtr, tiunr-5qf3, tiunr-5jqs, ter-9371]
created: 2026-11-04T22:46:40Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# Single bright pixels on the deck's walls in a still under the screen tracer

A headless still of demo-exit's deck at 1280 x 720 under ScreenTrace.Default, after 32 warm-up draws, shows isolated white pixels over the walls, beside the expected fine grain. The same still under --screen-trace off is smooth. Found during ter-gayt's screenshots (2026-11-04, /private/tmp/gayt-part8/shots, not kept). Find which rays bring them, such as a block ray at a weight near 4 that hits the bright lamp, and whether the slow history's clamp should hold them, and fix the cause.


## Notes

**2026-11-08T03:55:43Z**

2026-11-08: the same kind of isolated bright pixels shows across the rough floor of scripts/reflections-long under FSR, now that the pixel history rule keeps rare bright hits (/private/tmp/tertius-pixel-accept-20261108/shots/reflections-long/front.png). The mechanism is in /private/tmp/tertius-pan-cuts-20261107/report.md: one rare ray that hits the bright emitter stays in one pixel's long slow history. The box fix tiunr-nxtr keeps that light, so the specks need a spatial answer as well.

**2026-11-22T15:06:08Z**

2026-11-22, from ter-ukw6 (docs/research/back-face-depth-layer/sill-hits-on-main.md, part 2). A view with the sun in rect-lamps shows a sparkling cloud of bright specks on the ceiling, also on main after the back-face layer: the script docs/research/back-face-depth-layer/sill-hits-tools/stills-sunlit-nose.json, which runs 277,243 ticks, after which the sun shines through the nose window. Still: /Users/alexispurslane/.t3/worktrees/tertius/t3-b149a58c-eb3542ff177f4d65-evidence/part2/stills/sunlit-nose/after/s-000239.png at -1.7507356 stops. Crop docs/research/back-face-depth-layer/sill-hits-sunlit-ceiling.png, video sill-hits-sunlit-ceiling.mp4. The sill census of the ceiling's box render 500:0:780:220, draws 200 to 599, at a tolerance of 15 %, after the merge: 246,488 bright specular samples (luminance 50 or more, pre-exposed), 1.0 % of all samples. Of their light of 19,514,034, 83.4 % is real hits on the sunlit floor patch in front of the nose wall and 16.6 % real hits on the sunlit sill. At most 2.6 % can be false. So the specks are rare reflection rays that reach a small, very bright sunlit surface: this ticket's mechanism. Specks more than 40/255 above their 5 x 5 median in the crop: 154. ter-bdoh, the sparkle of rect-lamps' glossy floor, has the same signal.
