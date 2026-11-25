# Third-party notices

This file lists the third-party code that a build of the game carries, with the notices that its license asks for. `Tertius.Demo.csproj` copies it into the build and publish output. Jolt and joltc, Silk.NET, MoltenVK and the NuGet packages are not listed yet.

## XeGTAO

- Source: https://github.com/GameTechDev/XeGTAO, commit `a5b1686c7ea37788eeb3576b5be47f7c03db532c`.
- Used in: the GLSL port of ground-truth ambient occlusion, `src/Tertius.Rendering/Shaders/gtao.glsl`, which the four occlusion shaders include. The reference files are in `src/Tertius.Rendering/ThirdParty/XeGTAO`.
- License: MIT. The license asks that the notice goes with every substantial portion of the software, and the compiled shaders are such a portion.

```
MIT License

Copyright (C) 2016-2021, Intel Corporation 

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## AMD FidelityFX Super Resolution 3.1

- Source: https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK, tag `v1.1.4`, commit `c6efa6bf7f2027b3ec94f28578bb5965eabb9e55`, with the FSR 3.1.4 upscaler.
- Used in: the FSR 3.1 upscaler. The build compiles AMD's GLSL passes and headers in `src/Tertius.Rendering/ThirdParty/Fsr3`, with two changed declarations and one line of AMD's 3.1.6 fix, which its README records. `Fsr3Upscaler` repeats AMD's host code, `ffx_fsr3upscaler.cpp`, in C#. The tool `tools/fsr3-golden/` builds AMD's host code from the same commit and does not ship.
- License: MIT. The license asks that the notice goes with every copy and every substantial portion of the software, and the compiled shaders and the C# copy of the host are such portions.

```
This file is part of the FidelityFX SDK.

Copyright (C) 2024 Advanced Micro Devices, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files(the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and /or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions :

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## The Blue Danube art

The Blue Danube demo draws third-party models. Git holds none of their files. `bash tools/blue-danube-assets/setup.sh` downloads the pinned files and converts them into `content/tertius.demo/bin/assets/blue-danube/`, and every converted `.glb` carries the license of its source. Each model's sidecar, `content/tertius.demo/assets/blue-danube/**/*.import.json`, holds its source page and commit, the hash of every file, its authors, its license, its notice and the list of changes. The sections below repeat the notices.

### Pioneer: station, lunar shuttle and cargo pod

- Source: https://github.com/pioneerspacesim/pioneer, commit `999c9b572181d21f6c25102356b922da7937b794`: `data/models/stations/orbital_station` (the ring of 2.2 km and hub B), `data/models/ships/lunarshuttle` and `data/models/misc/cargo`.
- Authors: the Pioneer Developers. The station ring is by Szilárd Bálint.
- License: CC BY-SA 3.0, https://creativecommons.org/licenses/by-sa/3.0/. The converted models are adaptations, so they carry the same license.
- Notice: Copyright © 2008-2026 Pioneer Developers. Portions copyright © 2013-2014 Meteoric Games Ltd. Pioneer's art is licensed under CC BY-SA 3.0 (Pioneer AUTHORS.txt, "Licensing").
- Changes: the converter turns the COLLADA and DDS files into glTF binaries in engine axes, drops the marker nodes, adapts the station cladding, trim and glazing to metallic-roughness materials, and bakes the glow maps into constant emission. The station gains procedural inhabited galleries (`tools/blue-danube-assets/habitat.py`), and its maps shrink to 2048 px. The shuttle's diffuse map gets a procedural finish: a paler hull, wear, hazard stripes and the markings BD-07 and 07. Each sidecar lists its changes in full.

### Unvanquished: the passenger

- Source: https://github.com/UnvanquishedAssets/res-players_src.dpkdir, commit `bbf9e8f276b3cf3f4b3fab217f2c947d2dd25515`, `models/players/human_male`.
- Authors: Angelo (model and textures), Stannum (light armour touch-up, gloves) and RXMT (edited glove texture).
- License: CC BY-SA 3.0, https://creativecommons.org/licenses/by-sa/3.0/. The posed model is an adaptation, so it carries the same license.
- Notice: "Human male, light armour" by Angelo, with touch-ups by Stannum and an edited glove texture by RXMT, from Unvanquished, CC BY-SA 3.0.
- Changes: the converter keeps the meshes of the light suit with the helmet, drops the weapons, bakes a fixed pose with the arms down, scales the figure to 1.80 m, and flips the green channel of two normal maps to the OpenGL convention.

### Poly Haven: the props

- Source: https://polyhaven.com, the models `industrial_storage_cart` (Jule Bielitz), `industrial_caged_sconce` (Ulan Cabanilla), `metal_toolbox` (Mateusz Sadek), `modified_thermos` (Alex Weber), `metal_stool_01` (Ulan Cabanilla) and `modular_street_seating` (Stuart Attenborrow).
- License: CC0 1.0, https://creativecommons.org/publicdomain/zero/1.0/. CC0 asks for no notice. This list names the authors as a courtesy.
- Changes: none to the geometry or the textures. The converter only turns the axes and writes one glTF binary.

## Earth's surface in Blue Danube

The cooked Earth material includes a June land and ocean mosaic, a cloud composite and terrain normals. The archive includes the source URLs, credits and SHA-256 hashes in `sources.json`.

- Reto Stöckli, NASA Earth Observatory, NASA Goddard Space Flight Center. *Blue Marble Next Generation*, June 2004 base map, resampled and combined with cloud cover. NASA's media guidelines permit use in computer graphical simulations. [Base map](https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/base-map/#june) · [NASA media usage guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/)
- Solar System Scope. *Earth Clouds*, 8K, used under CC BY 4.0. Tertius resampled the cloud mask and used it to blend surface colour and roughness. [Source](https://www.solarsystemscope.com/textures/) · [License](https://creativecommons.org/licenses/by/4.0/)
- Amante, C. and B. W. Eakins (2009). *ETOPO1 1 Arc-Minute Global Relief Model*, NOAA National Geophysical Data Center, ice surface registration. Tertius sampled elevations every ten arcminutes and converted above-sea-level slopes to surface normals. NOAA permits free use and redistribution. [Dataset](https://doi.org/10.7289/V5C8276M) · [ERDDAP metadata and terms](https://coastwatch.pfeg.noaa.gov/erddap/griddap/etopo180.das)
