---
id: ter-0ok3
status: open
deps: []
links: []
created: 2026-10-26T17:23:43Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-kge6
---
# Skinned humans: export the MPFB rig and weights once the engine can skin

tertius_blender.mpfb.freeze applies the pose and removes the game_engine rig, because the importer ignores skins (AS2005) and the renderer has no skinning (decision 17 of docs/plans/asset-pipeline.md). Once skinning and animation exist, keep the rig and the bone weights in the .glb (glTF skin with JOINTS_0/WEIGHTS_0), and let the pose come from animation. The vertex groups that freeze keeps already hold the weights. MPFB 2.0.18 ships no expressions, so faces need their own solution (MPFB's face rig or shape keys).

