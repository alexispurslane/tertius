# Outgoing light across grid levels

The directional light volume stores surface data in three grids with cell widths of 0.25, 0.5 and 1 m. In the baseline for [#22](https://github.com/alexispurslane/tertius/issues/22), a cell that inherited surface data from a finer grid combined the surfaces before it sampled direct light and shadows. The combined color could lose red tint. A shadow over neighboring surfaces could darken a red reflector that remained in sunlight.

## Baseline regression cases

The receiver is a matte white panel. Its front face is 2.96 m from the camera along the view axis. The 0.25 m grid gives no light at that distance. The 0.5 m grid gives the full share. Each test reads the red minus green value at the receiver's center pixel from the scene-linear buffer, in W/(m² sr).

| Scene | Baseline red minus green | Required lower bound |
|---|---:|---:|
| Red strips beside neutral strips in the same coarse cells | 0.00155 | 0.07032 |
| A red reflector in sunlight beside shadowed geometry in the same coarse lighting group | 0.00323 | 0.04370 |

Both tests fail on `d3123fe715dea1fd141649271c315096712d72a2`. Four controls pass at that revision. A red reflector that straddles the fine grid face keeps lighting the coarse receiver. A fine-grid emitter supplies light when the scene has no direct lights. An intervening sheet blocks most red light. A red reflector lit by a yellow emitter adds red tint in the second sweep.
