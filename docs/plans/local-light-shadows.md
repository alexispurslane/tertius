# Point and spot light shadows

Ticket: `ter-s9cv`. The user approved this design.

## Goal

Let point lights and spot lights cast shadows, within a fixed budget, so that interiors such as a ship's corridors look right. Today only the sun casts shadows.

## Design

- `RenderLight` is a union of `PointRenderLight` and `SpotRenderLight`, so both case records get a `CastsShadow` flag, and the union stores it once for both. This is a change to Core.
- All local-light shadows share one tiled shadow atlas, a large depth texture divided into tiles. A spot light uses one perspective tile. A point light uses six tiles, one for each cube face.
- Every frame, the renderer ranks the shadow-casting lights by importance: projected size on screen, intensity and distance. Only the top lights get tiles, within a tile budget.
- The renderer caches a tile while its light and its casters do not move. A cached tile costs nothing to render.
- The renderer culls casters on the CPU against the light's range sphere or cone.
- Sampling uses PCF (percentage-closer filtering) with a depth bias scaled to the tile's texel size.

## Lights instead of one sun

On 2026-10-11, the user moved a second goal into this epic: the sun stops being a special case. This part is not designed yet. It needs two designs, and the user chooses between them before the work starts.

How the sun works today:

- A scene sets `World.SunFrame` (one frame), `World.SunColor` and `World.SunIntensity`.
- Each frame, the snapshot takes the sun's direction from the camera toward that frame's origin. It takes the sun's angular radius from the render radius of the frame's owner.
- The brightness is a fixed number from the scene. It does not fall off with the distance to the star.
- Only the sun gets cascaded shadow maps, far shadows (the sphere tests in the lighting shader), and baking into impostor captures. Every other light is a point or spot light with a range, no disc and no shadows.

The goal:

- A light is a property of an entity. A star's data gives its power and its radius, as a lamp's data does.
- Each frame, the renderer sees every light from the camera. The direction and the angular radius come from where the light is. The brightness comes from its power over its distance squared, so sunlight dims on its own as a ship travels outward.
- Whether a light is far follows from geometry, not from a flag. Take a light whose distance is huge next to the 2 km region of the shadow maps. It lights that region from one direction, as the sun does. A near light stays a point or spot light with a range.
- Every far light gets what the sun gets now: far shadows tested against its own disc, the far-shadow pruning rule measured against its own angular radius, cascaded shadow maps, and baking into impostor captures.

Points for the two designs:

- The light's data: power in watts, or an intensity at a reference distance.
- The rule that makes a light far, and how a light that crosses it changes over without a pop.
- The budget for cascades. Each far light with maps adds four cascades, so probably only the brightest one or two far lights get them. Every far light keeps the cheap sphere tests, except one so dim beside the brightest that its shadows cannot show.
- The per-pixel cost of far shadows, which grows with the number of far lights times the number of spheres.
- What replaces `World.SunFrame`, `SunColor` and `SunIntensity`, and how scenes and content files move over.
- How brightness that varies with distance interacts with exposure (ticket `ter-zzyr`) and bloom (ticket `ter-63wr`).

The far shadows that this builds on come from ticket `ter-uucg`: every caster is also a sphere of its bounding radius, which never shadows a point inside it. A caster in the shadow maps uses its sphere only for receivers beyond the maps' reach. Generalizing to many far lights runs those same sphere tests once per far light.

## Steps

1. Write CPU tests first: ranking, the budget, cache hits and misses, and culling.
2. Add the atlas, the shadow pass for local lights and the sampling in the lighting pass.
3. Add snapshot scenes: a spot light over a box, and a point light in a room.
4. Measure the cost with GPU timestamps for 4, 16 and 64 shadowed lights. Report the numbers. Keep the feature switchable.
5. Design lights instead of one sun (see the section above): two designs, and the user chooses.
6. Write its tests first, then replace the special-cased sun. Measure the cost of one, two and four far lights, with and without cascades. Report the numbers.
