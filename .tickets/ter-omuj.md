---
id: ter-omuj
status: open
deps: [ter-1qce, ter-n7lw]
links: []
created: 2026-10-26T17:47:55Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-kge6
tags: [assets, blender, humans]
---
# Hair cards of a scripted human show their straight edges: make card hair read as hair

The user asked on 2026-10-26 for future work on this. The crew member of ter-1qce (content/tertius.demo/assets/characters/crew.model, MakeHuman's afro01 from the CC0 system asset pack, through MPFB) shows its hair cards starkly: straight dark edges in a grid pattern up close (scratchpad render 1qce-renders/final/crew-face.png). bob02 rendered nearly white and braid01 covered an eye. The cards export as glTF BLEND. Things to try: the alpha of each card fading toward its edges, alpha-tested cards (MASK) with a cutoff and alpha to coverage instead of a sorted blend, more cards or cards that follow the scalp, a hair shading model with a tangent-based highlight, and checking how the engine draws BLEND cards (sorting, shadows, depth). The result must survive the build's export to .glb and the engine's importer, and must stay repeatable. Look at the renders up close and at the game's distance.

