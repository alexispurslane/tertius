# The astronaut

A person in an EVA spacesuit, about 1.95 m tall in the helmet. The face shows inside the helmet. One piece,
`Astronaut`, with its origin on the ground between the boots. The front faces -Y. The person stands relaxed: the arms
hang a little away from the sides, the elbows bend a little, and the fingers curl a little. The model is static: the
pose is frozen into the meshes, and the model has no rig and no animation.

Build it with `tools/blender/build.sh content/tertius.demo/assets/characters/astronaut.model --out <folder> --views`.

## Where each part comes from

| Script | What it makes |
|---|---|
| `s00_scene.py` | The collections and the constant materials |
| `s10_body.py` | The person, made by MPFB's own code: body, skin, eyes, eyebrows, eyelashes, teeth, tongue, rig and pose, and the joints |
| `s20_shells.py` | The head with its skin, the soft suit, the gloves with fingertip caps, and the metaball boots, all fitted to the body |
| `s30_hardware.py` | The neck ring, waist ring, shoulder and thigh bearings, wrist disconnects, gauntlets, boot cuffs, rands and soles |
| `s40_helmet.py` | The bubble, the hard shell, the gold visor, the lamps and camera, the comm cap and the headset |
| `s50_backpack.py` | The backpack with its oxygen pack, the chest box, the hoses and the cuff checklist |
| `s70_materials.py` | The baked materials: suit fabric with seams, stripes, a patch and dust; backpack panels |
| `s75_uv.py`, `s80_assemble.py`, `s90_checks.py` | UV maps, the root piece, and the attachment check |

Only the person comes from a generator: MPFB and the MakeHuman system assets. The suit, helmet, visor, gloves, boots
and backpack are built by these scripts. No clothes asset and no finished model of a person, a suit or a part of one
is used.

## The person

`s10_body.py` calls `tertius_blender.mpfb` (tools/README.md, "Humans from MPFB"). `parameters.human` is MPFB's own
human description:

| Part | Value |
|---|---|
| Body | The macro sliders of `phenotype`: gender 0.8, age 0.55, muscle 0.6, weight 0.55, proportions 0.75, height 0.6, and a mix of races |
| Skin | `middleage_caucasian_male`, as a MakeSkin material |
| Eyes, eyebrows, eyelashes | `high-poly`, `eyebrow002`, `eyelashes02` |
| Teeth, tongue | `teeth_base`, `tongue01` |
| Hair | None: the comm cap of `s40_helmet.py` covers the head |
| Rig | `game_engine` |

`parameters.pose` turns bones of the rig, in degrees from the rest pose. `mpfb.freeze` then applies the pose, the
shape keys and the helper mask, removes the rig, and keeps the bone weights as vertex groups. The shell script reads
those vertex groups to choose the faces of each shell. The head shell is the head of the skin with its MakeSkin
material. The eyes, the eyebrows, the eyelashes, the teeth and the tongue stay as they are, inside the helmet.

## Known limits

- The pose is frozen. The model cannot move.
- The armpits: where the inner upper arm meets the chest, the two suit surfaces cross and leave a small crease.
- The eyebrows and the eyelashes draw with alpha blending (`BLEND` in glTF), behind the alpha-blended bubble.
- The clear bubble relies on alpha blending (`M_Bubble`, alpha 0.12). The gold visor is opaque.
