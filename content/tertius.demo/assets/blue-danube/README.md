# The Blue Danube art

This folder holds one sidecar for each third-party model of the Blue Danube demo. Git holds no model file. Set up a checkout once, from the repository's root:

```bash
bash tools/blue-danube-assets/setup.sh
```

The command downloads the files that the sidecars pin, checks their SHA-256, and converts each model into `content/tertius.demo/bin/assets/blue-danube/<model path>/model.glb`. tools/README.md, "The Blue Danube art", describes the command, the sidecar keys and the runtime loader. THIRD-PARTY-NOTICES.md holds the notices.

| Sidecar | Model | Source | License |
|---|---|---|---|
| `orbital-station/orbital_station_2-2k.model.import.json` | The station: ring of 2.2 km and hub, with procedural galleries | Pioneer | CC BY-SA 3.0 |
| `lunar-shuttle/lunarshuttle.model.import.json` | The lunar shuttle, with a procedural finish | Pioneer | CC BY-SA 3.0 |
| `cargo-pod/cargo.model.import.json` | The cargo pod | Pioneer | CC BY-SA 3.0 |
| `crew/human_male.blend.import.json` | The passenger: light suit with helmet, arms down, 1.80 m | Unvanquished | CC BY-SA 3.0 |
| `props/industrial_storage_cart.gltf.import.json` | A storage cart | Poly Haven | CC0 1.0 |
| `props/industrial_caged_sconce.gltf.import.json` | A kit of caged wall lamps | Poly Haven | CC0 1.0 |
| `props/metal_toolbox.gltf.import.json` | A toolbox | Poly Haven | CC0 1.0 |
| `props/modified_thermos.gltf.import.json` | A thermos | Poly Haven | CC0 1.0 |
| `props/metal_stool_01.gltf.import.json` | A stool | Poly Haven | CC0 1.0 |
| `props/modular_street_seating.gltf.import.json` | A kit of benches | Poly Haven | CC0 1.0 |

The reference of a model is its sidecar's path without `.import.json`, in the package `tertius.demo`: `tertius.demo:blue-danube/cargo-pod/cargo.model`. `src/Tertius.Demo/BlueDanubeArt.cs` lists the references, and a test checks that the list matches the sidecars.

To add a model, write its sidecar with the URL, size and SHA-256 of every file, have its file list approved, add its reference to `BlueDanubeArt`, and run the setup command.

The same setup cooks Earth's colour, roughness and topographic normals into `bin/assets/blue-danube/earth/material.zip`. Its source data, credits and usage terms are pinned in [earth/sources.json](earth/sources.json).
