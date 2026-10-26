---
id: ter-1qce
status: in_progress
deps: []
links: []
created: 2026-10-26T15:56:52Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-kge6
tags: [assets, experiment, blender]
---
# Run the MPFB add-on headless in scripted models, for a complete human: its own sliders, skin, eyes, brows, lashes, teeth, hair and rig

The user asked on 2026-10-26 for an agent that builds on ter-sdmg (branch ter-sdmg-spacesuit) and uses the MPFB add-on itself instead of a hand copy of its data loader. In the user's words: "attempt to actually use the make-human plug-in so that it can properly test out the plug-ins own logic for the different sliders and also all of the other features that not using the plug-in would leave out like hair because I want the procedural model generation to actually be able to do a complete human." The ter-sdmg agent read MPFB's data files directly and copied its slider formula, because the build tool starts Blender with --factory-startup, MPFB expects to be an installed extension, and it feared writes into the read-only Nix store. A later log of its runs also shows that Blender's own extension manager (bl_pkg) fails to register in the Nix Blender: its Python has no requests module. One background agent (Opus, high effort) does the work on its own branch from ter-sdmg-spacesuit. Downloads: MPFB and MakeHuman's own assets for the parts of a human body (skins, eyes, eyebrows, eyelashes, teeth, tongue, hair) from their official sources, under a free license, pinned by hash in Nix. No clothes and no other finished models.

