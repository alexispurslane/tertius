# Blue Danube surface materials

## Earth

The globe uses the June 2004 Blue Marble base map by Reto Stöckli and NASA Earth Observatory. ETOPO1 Ice Surface by NOAA supplies heights in metres. Earth Clouds by Solar System Scope supplies cloud cover under CC BY 4.0. The cook resamples that cloud image into the colour and roughness maps. The film's closing card also credits the cloud image. The [source record](../../../content/tertius.demo/assets/blue-danube/earth/sources.json) gives the original links, licences, changes and file hashes.

The camera above the Caucasus looks down from 350 km, with the Sun 21 degrees above the terrain. The physical normals use the slope between neighbouring elevation samples. Water has a level normal. Increasing cloud cover reduces the contribution of the ground's slope.

| View | Pixels measured | Pixels changed by physical normals | Mean absolute change per RGB channel (0–255) | Pixels changed by 8× slopes |
|---|---|---:|---:|---:|
| Caucasus | Whole 1280 × 720 frame | 63.40% | 1.048 | 75.76% |
| Globe | Central disc of radius 250 pixels | 27.06% | 0.169 | 43.79% |

Each percentage counts pixels where any channel changed by at least one code value against level normals. Independent repeats of both physical views matched exactly. The 8× captures show the effect of exaggeration. The film uses the physical slopes. The [capture script](normals/earth.json) and images sit beside the [Caucasus](normals/caucasus-measurements.json) and [globe](normals/globe-measurements.json) measurements.

## Industrial materials

| Surface | Physical scale | Finish |
|---|---|---|
| Spoke | The spoke vertices span 3,464.95 m end to end. The cooked texture coordinates span 108.28 tiles along that axis. Each tile covers 32 m lengthwise, with 22 repeats around a radius of 56.7 m. | Staggered 6.4 × 4 m painted panels rise 32 mm above the joints. A recessed hatch has two exposed hinges. |
| Hub and ring | The structural atlas covers approximately 1 m per texel on the hub and 0.6 m on the outer ring. | Smoothed height steps around the existing truss bays separate cladding, trim and gaskets. |
| Gallery | Each of 384 bays spans about 31.6 m along the rim and 24 m radially. | Panels rise 55 mm. The exposed frames, painted panels and seals each have their own roughness. |
| Cargo pod | One full interval of texture coordinates covers 4.479 m on the access face. | Relief follows the source door's octagonal collar, inset lid, hinges and locking ribs. The hardware exposes metal. |

Pioneer Developers created the station and pod. Szilárd Bálint created the station ring. The adapted models retain CC BY-SA 3.0. See the [source credits and changes](../../../THIRD-PARTY-NOTICES.md).

### Matched lighting captures

Both runs use the same [four cameras](normals/hulls.json) and Sun direction. A diagnostic copy of each cooked model substitutes level tangent normals (128, 128, 255) at the original resolution. A binary comparison confirmed that both copies retain all original mesh and image bytes. Only the four normal images point to replacement bytes. Both copies share the mesh data, tangents, colour and metallic-roughness maps. Autoexposure responded to the changed shading by at most 0.018 stops, on the gallery. The differences below include that response.

| View | Level normals | Authored normals | Pixels changed | Mean absolute RGB change (0–255) |
|---|---|---|---:|---:|
| Spoke | [Frame](normals/spoke-neutral.png) | [Frame](normals/spoke-physical.png) | 16.37% | 0.195 |
| Hub | [Frame](normals/hub-above-neutral.png) | [Frame](normals/hub-above-physical.png) | 12.20% | 0.173 |
| Gallery | [Frame](normals/gallery-neutral.png) | [Frame](normals/gallery-physical.png) | 87.00% | 1.835 |
| Cargo | [Frame](normals/pod-view-neutral.png) | [Frame](normals/pod-view-physical.png) | 14.51% | 0.454 |

The hub view includes portions of the spokes. The cargo view shows the octagonal door, cross-shaped locking ribs and hinges on three pods. Each percentage uses the entire 1280 × 720 frame. The [measurements](normals/hull-measurements.json) include peak changes.

### Opaque station surfaces

The follow-up cook traces recesses in Pioneer's original texture coordinates. The scale is the median metres per full texture interval on non-emitting triangles with measurable texture area. Each normal map also serves the material's glow copies. Their exported emission colours and strengths match the earlier cook exactly.

| Surface | Metres per texture interval | Authored depth | Triangles with normal maps | Triangles sampled on a sloping edge |
|---|---:|---|---:|---:|
| Facade | 104 | 50–75 mm at covers, cladding and window reveals | 50,791 | 7,516 |
| Floor | 212 | 35 mm deck joints, 80 mm service recesses | 51,959 | 6,614 |
| Tunnels | 132 | 110 mm behind the diagonal braces | 1,888 | 592 |
| Strut | 91 | 45 mm sleeve joints, 80 mm sockets | 3,168 | 732 |

The last column counts triangles where at least one of 78 evenly spaced interior samples differs from a level normal by more than one code value in red or green. This can miss thin edges. The flat portions of each plate share the same normal map. The deck paint follows the plates, and the window recesses have level interiors.

The [hub tunnel closeup](normals/hub-tunnel-pair.png) shows the previous cook at left and the new cook at right. Both captures used the Release renderer and the art preview's `hub-above` camera. For these diagnostic copies, both station root nodes moved by (294, 373, −338.5) metres to put the camera inside the upper hub. The highlight follows the lip beside the diagonal brace. At 1280 × 720, 80.78% of pixels changed by at least one RGB code value. The mean absolute channel difference was 1.45 out of 255, including a 0.016-stop exposure change.

## Verification

The Checked Host coverage run passed 2,168 tests and skipped eight. The Earth material loader covered all 27 measured lines and 90% of its branches. The mutation runner killed all ten selected Demo mutations. The first version of M06 failed to compile. The revised mutation removed the inspection scene's globe maps at their assignment and the scene test caught it.

| Mutation | Change caught by the tests |
|---|---|
| M01 | Wrap the north pole onto Antarctica |
| M02 | Upload the satellite colour texture as linear data |
| M03 | Bind roughness in place of terrain normals |
| M04 | Gamma-decode roughness |
| M05 | Omit the film's globe maps |
| M06 | Omit the inspection scene's globe maps |
| M07 | Draw the navigation grid in the exit shot |
| M08 | Stop rotating the continents after the first spin sample |
| M09 | Allow film capture with the Earth pack missing |
| M10 | Remove the cloud licence from the closing card |

The assets dev shell passed all 31 Python tests, including the cooked-model checks for normals, tangents and metallic-roughness maps. The Release run of the Blue Danube and art-preview Host classes passed 94 tests and skipped one opt-in measurement. The earlier full Checked run recorded passes for eight projects. Its log stops in Rendering without a suite verdict.
