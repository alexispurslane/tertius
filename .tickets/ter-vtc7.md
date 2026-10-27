---
id: ter-vtc7
status: in_progress
deps: [ter-a24f, ter-k1ne, ter-hx3r, ter-3dp5]
links: []
created: 2026-10-27T03:59:20Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
---
# The bounce light's source: a lamp panel's glow bounces on top of its lamp, and highlights bounce as if diffuse

The sectors pass reads the direct image as the light that bounces. That image holds the direct light plus emission, and the specular part toward the eye.

1. Emission. A lamp's panel glows with its lamp's power (DemoScenes.LampPanelMaterial: LampColor · P / (π A)), and its point light already sends that power. So the lamp lights the wall twice, and its small bright panel, which few slices and steps hit, leaves soft bright blotches of 10 to 20 px on demo-exit's cabin wall beside the lamp at every radius. An experiment (branch exp-7s3g-lamp-blotches, worktree .claude/worktrees/exp-7s3g-lamp, uncommitted) subtracted the G-buffer's pre-exposed emission from the half-resolution copy of the direct image in occlusion_depth.comp, for lit pixels only (impostors keep theirs). The blotches went and the wall looked like the setting off. Twice the slices did not remove them. The sun's sphere already follows the rule that a source lights the scene through its light, not through its body (DemoScenes, the sun's material). Waits for the user: whether emission bounces at all, because glows without a light (markers, screens) would then light nothing near them.

2. Specular. At R 2 m, pbr-spheres' floor shows bright dots: the spheres' highlights toward the eye bounce as if every direction saw them. Leaving the specular lobe out of the source removed them. The honest source is the diffuse outgoing light, the same in every direction. The experiment computed directLight twice, which doubled the direct pass; a real fix splits the sum in one loop.

Stills and sheets: the session scratchpad, lampx-*.


## Notes

**2026-10-27T05:53:11Z**

The specular part waits for skin and hair Part 4 (ter-k1ne), whose DirectTerms splits directLight's sum into its diffuse and specular parts in one loop; the emission part waits for the user.

**2026-10-27T08:42:08Z**

2026-10-27: now also waits for Part 5 (ter-hx3r), which separates the diffuse light D in the direct and ambient passes for the skin scatter. The specular part of this ticket reads the same split, so it builds on the epic branch after Part 5.

**2026-10-27T09:41:05Z**

2026-10-27: the specular part, settled by the orchestrator by the one-store rule: Part 5's scatter image already holds the diffuse direct light of skin pixels. It becomes the one store of the diffuse direct light of every lit pixel (RGB the diffuse part of DirectTerms, A the skin mask), and it exists when the scatter or the bounce light is on. The bounce light's half-resolution copy (occlusion_depth.comp) reads that image in place of the direct image, so highlights toward the eye no longer bounce. The copy keeps adding the G-buffer's pre-exposed emission, as today, until the user answers the emission question; that answer then removes or keeps one term. Builds after Part 6 (ter-3dp5), so that Part 6's cost numbers stay true, on main after the epic merges.

**2026-10-27T11:48:41Z**

2026-10-27, the emission part, the user's answer: a glowing surface lights what is near it only if it has no light of its own. Model: an entity sends its power once. An entity that holds a light sends it through that light, and its glowing surface is the face of that light, not a second source (the demo's lamps: one entity with the panel mesh and the LampLight). So the bounce light's copy adds a pixel's emission only where the pixel's entity holds no light. The renderer knows this per instance from the render row (an entity holds one light), so the G-buffer needs one bit per pixel. The blotches beside the demo lamps go, and a screen or another glow without a light still lights its surroundings.
