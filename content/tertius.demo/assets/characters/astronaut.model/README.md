# The astronaut

A person in an EVA spacesuit, about 1.95 m tall in the helmet, standing in an A-pose. One piece, `Astronaut`, with its
origin on the ground between the boots. The front faces -Y. The model is static: it has no rig and no animation.

Build it with `tools/blender/build.sh content/tertius.demo/assets/characters/astronaut.model --out <folder> --views`.

## Where each part comes from

| Script | What it makes |
|---|---|
| `s00_scene.py` | The collections and the constant materials |
| `s10_body.py` | The human body from parameters (`parameters.human`), with the MakeHuman data of MPFB, the joints, and slightly curled fingers |
| `s20_shells.py` | The head, the soft suit, the gloves with fingertip caps, and the metaball boots, all fitted to the body |
| `s30_hardware.py` | The neck ring, waist ring, shoulder and thigh bearings, wrist disconnects, gauntlets, boot cuffs, rands and soles |
| `s40_helmet.py` | The bubble, the hard shell, the gold visor, the lamps and camera, the comm cap, the headset and the irises |
| `s50_backpack.py` | The backpack with its oxygen pack, the chest box, the hoses and the cuff checklist |
| `s70_materials.py` | The baked materials: suit fabric with seams, stripes, a patch and dust; backpack panels |
| `s75_uv.py`, `s80_assemble.py`, `s90_checks.py` | UV maps, the root piece, and the attachment check |

Only the body and the head come from a generator. The suit, helmet, visor, gloves, boots and backpack are built by
these scripts. No finished model of a person, a suit or a part of one is used.

## The MPFB data

The body is the MakeHuman base mesh (hm08) with its macro targets, as MPFB 2.0.18 ships them. `s10_body.py` reads the
files and sums the targets with MPFB's own weight formula. It runs no code of the add-on.

| What | Value |
|---|---|
| Name | MPFB 2.0.18 (MakeHuman plugin for Blender) |
| Source | https://extensions.blender.org/add-ons/mpfb/ |
| File | https://extensions.blender.org/download/sha256:c075b3e9b94b8e5556a5f03f4653602d02ea306de08126e8f55ed6dee28a2dad/add-on-mpfb-v2.0.18.zip |
| Size | 45,956,992 bytes |
| SHA-256 | c075b3e9b94b8e5556a5f03f4653602d02ea306de08126e8f55ed6dee28a2dad |
| License | The add-on code: GPL-3.0-or-later. The base mesh, the targets and the rig weights: CC0 (MakeHuman assets). |
| Pin | `native/mpfb.nix`, a fixed-output fetch. The assets shell gives the unpacked folder as `TERTIUS_MPFB`. |

The model reads these files from `$TERTIUS_MPFB/data`: `3dobjs/base.obj`, `targets/macrodetails/**`,
`mesh_metadata/basemesh_vertex_groups.json` and `rigs/standard/weights.game_engine.json`.

## Known limits

- An A-pose with no rig, so it cannot move.
- The armpits: where the inner upper arm meets the chest, the two suit surfaces cross and leave a small crease.
- The fingers curl only a little (`parameters.pose`), by linear blend skinning with the rig's weights; the thumbs keep the base mesh's pose.
- The clear bubble relies on alpha blending (`M_Bubble`, alpha 0.12). The gold visor is opaque.
