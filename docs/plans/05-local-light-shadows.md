# 05 Point and spot light shadows

Ticket: `ter-s9cv`. The user approved this design.

## Goal

Let point lights and spot lights cast shadows, within a fixed budget, so that interiors such as a ship's corridors look right. Today only the sun casts shadows.

## Design

- `RenderLight` gets a `CastsShadow` flag. This is a change to Core.
- All local-light shadows share one tiled shadow atlas, a large depth texture divided into tiles. A spot light uses one perspective tile. A point light uses six tiles, one for each cube face.
- Every frame, the renderer ranks the shadow-casting lights by importance: projected size on screen, intensity and distance. Only the top lights get tiles, within a tile budget.
- The renderer caches a tile while its light and its casters do not move. A cached tile costs nothing to render.
- The renderer culls casters on the CPU against the light's range sphere or cone.
- Sampling uses PCF (percentage-closer filtering) with a depth bias scaled to the tile's texel size.

## Steps

1. Write CPU tests first: ranking, the budget, cache hits and misses, and culling.
2. Add the atlas, the shadow pass for local lights and the sampling in the lighting pass.
3. Add snapshot scenes: a spot light over a box, and a point light in a room.
4. Measure the cost with GPU timestamps for 4, 16 and 64 shadowed lights. Report the numbers. Keep the feature switchable.
