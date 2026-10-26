# The crew member

A member of a ship's crew in a navy jumpsuit with orange arm bands, a belt and dark boots, about 1.66 m tall. The
face and the hair are in view. One piece, `Crew`, with its origin on the ground between the boots. The front faces
-Y. The person stands relaxed: the arms hang at the sides, the elbows bend a little, and the fingers curl a little.
The model is static: the pose is frozen into the meshes, and the model has no rig and no animation.

Build it with `tools/blender/build.sh content/tertius.demo/assets/characters/crew.model --out <folder> --views`.

## Where each part comes from

| Script | What it makes |
|---|---|
| `s00_scene.py` | The collections and the constant materials of the small parts |
| `s10_human.py` | The person, made by MPFB's own code: body, skin, eyes, eyebrows, eyelashes, teeth, tongue, hair, rig and pose |
| `s20_jumpsuit.py` | The jumpsuit, the collar, the cuffs, the pockets, the belt and buckle, the zipper, and the metaball boots |
| `s70_materials.py` | The baked jumpsuit fabric: panels, seams, stitching, twill, the arm bands, the ship patch and wear |
| `s75_uv.py`, `s80_assemble.py`, `s90_checks.py` | UV maps, the root piece, and the attachment check |

The person comes from MPFB and the MakeHuman system assets. The scripts build every garment: the jumpsuit, its
trims, the belt, the zipper and the boots. No clothes asset and no finished model of a person or a garment is used.

## The person

`s10_human.py` calls `tertius_blender.mpfb` (tools/README.md, "Humans from MPFB"). `parameters.human` is MPFB's own
human description:

| Part | Value |
|---|---|
| Body | The macro sliders of `phenotype`: gender 0.3, age 0.45, muscle 0.55, weight 0.5, proportions 0.6, height 0.55, and a mix of races |
| Skin | `young_african_female`, as a MakeSkin material |
| Eyes, eyebrows, eyelashes | `high-poly`, `eyebrow001`, `eyelashes01` |
| Teeth, tongue | `teeth_base`, `tongue01` |
| Hair | `afro01`, hair cards with an alpha texture |
| Rig | `game_engine` |

`parameters.pose` turns bones of the rig, in degrees from the rest pose. `mpfb.freeze` then applies the pose, the
shape keys and the helper mask, removes the rig, and keeps the bone weights as vertex groups. The jumpsuit script
reads those vertex groups to choose the faces of each garment. The asset pipeline importer ignores skins, so the
frozen meshes carry the pose into the game.

The body faces under the jumpsuit and the boots go away. Only the head, the neck, the hands and two rows of faces
under each opening of the cloth stay.

## Known limits

- The pose is frozen. The model cannot move.
- The hair is a set of alpha cards. Up close, the cards show as dark straight edges in the curls.
- The hair, the eyebrows and the eyelashes draw with alpha blending (`BLEND` in glTF), so their sort order can show
  in the engine.
- No facial expression: MPFB 2.0.18 ships no expression data, and the MakeHuman system assets hold none.
