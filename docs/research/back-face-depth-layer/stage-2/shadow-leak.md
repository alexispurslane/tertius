# Which bright strips the sun really lights, and the cause of the leak in the sun's shadow

This is the record of ticket `t1-3j2j`, of 2026-11-15. It is the diagnosis of bug ticket `t8-rjno`. It judges each bright strip of sunlight by geometry. It finds the part of the sun's shadow that lets the light through, and it names what a fix must change. The terms are those of [reference.md](reference.md) and of ARCHITECTURE.md, "Shadows".

A paragraph that starts with "Reading:" holds my interpretation. Every other sentence states a measurement, a count or what an image shows.

## Part 1: which strips the sun lights in the truth

### The three measurements

Each view is frame 239 of a film at 1920 × 1080, with `--bloom off`, `--screen-trace off` and one fixed exposure: 2.7868438 stops for `rect-lamps` and −0.3901896 stops for the cabin. The probe is the reference experiment's, with new switches (part 2). [tools/leak-still.sh](tools/leak-still.sh) runs one still.

1. The engine's direct sunlight of a pixel is a difference of linear luminance. The still under full lights gives the first term, the still without the sun (`TERTIUS_NO_SUN=1`) the second. [tools/strips.py](tools/strips.py) computes it. Its noise floor is about 0.002 W/(m²·sr): 61 pixels of `rect-lamps` fall below −0.002.
2. The path trace renders the exported frame with the sun alone and a black Earth: `REF_EARTH_BLACK=1`, and `REF_SKIP_LIGHTS` drops every lamp. It holds the truth's direct sunlight and its bounces, 256 samples, one seed.
3. The geometry's answer comes from [tools/sun_cast.py](tools/sun_cast.py). It casts a camera ray through each pixel's center to the nearest front face that the engine's G-buffer holds. From that point, it casts 256 lines to points spread evenly over the sun's disc, through every caster's triangles and every celestial body's sphere. The share of the lines that reach the sun is the point's sunlit share. A line mode does the same along a world line, at 1 mm steps.

The sun's disc has an angular radius of 0.00467 rad. In `rect-lamps` it stands 0.26° from the world's +y axis: the direction toward it is (0.0000060, 0.99999, 0.00454).

A fourth measurement joins them: [tools/engine_shadow.py](tools/engine_shadow.py) repeats the engine's cascade filter on the CPU, from the probe's dump of the four cascades at frame 239. It gives the engine's lit share at any world point, also on a strip thinner than a pixel. On the jamb of `rect-lamps`, x 1801 to 1808, each column's direct sunlight in W/(m²·sr) is 0.84 to 1.03 times that lit share.

### The table

| Strip | View | Engine: lit part, m | Engine: lit part, pixels | Engine's direct sunlight, W/(m²·sr) | Truth: lit part, m | Truth: lit part, pixels | Verdict |
|---|---|---:|---:|---|---:|---:|---|
| The far jamb of the nearest right side window, normal +z | `rect-lamps` | 0.119, from x 9.881 to 10.000 m | 10, x 1800 to 1809 | 0.02 at x 1800 to 0.37 at x 1808, 0.22 on the mean | 0.001 to 0.005 at the hull's outer face | under 0.5 | Only in the engine |
| The sill of the end window, normal +y, which the ceiling reflects | `rect-lamps` | 0.111, from z −39.889 to −40.000 m | under 0.1: the sill's top spans 0.2 rows | 0.004 on the mean of row 563, 0.009 at most | 0.001: one point at the outer edge, lit share 0.004 | 0 | Only in the engine |
| The sills of the side windows, normal +y | `rect-lamps` | 0.119, from x 9.881 to 10.000 m | 609 pixels in the frame | 1.80 at most | 0.006 at the hull's outer face | under 0.5 | Only in the engine |
| The sill strips of the cabin | The cabin of `demo-exit` | None | 0 | None | None | 0 | No such strip in this frame |

The jamb's lit share in the engine rises from 0.018 at x 1800 to 0.47 at x 1809. In the truth it is 0.5 at the outer face, 0.27 at 1 mm in and 0 at 2 to 5 mm in. The lit width in the truth grows with the height below the wall above: 1 mm at y 0.47 m, 3 mm at y −0.006 m, 5 mm at y −0.48 m. The engine's lit width is 0.119 m at all three heights.

All jambs and sills of the frame:

- 7,143 pixels of `rect-lamps` carry more than 0.002 W/(m²·sr) of the engine's direct sunlight. The truth lights 103 of them, each within 5 mm of the hull's outer face.
- Those 103 pixels carry 6.7 of the 835 W/(m²·sr) summed over the 7,143 pixels.
- The jambs, normal +z, hold 4,385 of the pixels and 742 of the sum. The sills, normal +y, hold 609 pixels and 38 of the sum. 1,976 pixels see no surface at their center, because they lie on a strip's edge with an opening behind it.
- The path trace on the jamb, rows 500 to 579, has a median of 0 in every part. Its mean is 0.105 on x 1800 to 1809, 0.068 on x 1789 to 1799 and 0.079 on the room half. 6 % to 8 % of its pixels are single bright samples above 0.5.

The cabin:

- The line cast at every second pixel, 518,400 points, finds no sunlit point. 6,765 of the points at every eighth pixel face the sun. The hub's walls, instance 8, block the line to the sun at 4,463 of them, and the cabin's own walls, instance 38, at 2,244.
- The CPU copy of the engine's filter gives no visible point any lit share. Every point reads cascade 0, with the stored caster 1.9 m to 21 m nearer the sun.
- The still's sun-only light is 0.70 W/(m²·sr) on the mean of the hub's walls, which the camera sees through the windows. On the cabin's own surfaces it is 0.0011 on the mean.
- The path trace with the sun alone and a black Earth covers the hub's walls in x 1500 to 1799, y 480 to 719. Its mean there is 0.37 W/(m²·sr), and the engine's sun-only mean is 0.45.

Reading: no strip of direct sunlight exists in the cabin's frame. The sun-only light on the hub's walls is not direct sunlight. Direct light on a wall that faces the sun at a cosine of 0.74 is about 160 W/(m²·sr), and the engine's map shadows those walls. It is light that the sun sends in by other paths, and Earth's light, which also goes dark without the sun. The cabin's thin bright strips of the handoff must have another source in another frame or view.

### The crops

![The jamb of rect-lamps in six panels](leak-rect-lamps-jamb.png)

[leak-rect-lamps-jamb.png](leak-rect-lamps-jamb.png): the jamb, x 1756 to 1815, y 470 to 609, at three times its size. The panels, from left to right:

1. The engine under full lights.
2. The engine without the sun.
3. The engine's direct sunlight.
4. The path trace with the sun alone.
5. The engine's lit share from the CPU copy of its filter.
6. The geometry's lit share.

The engine's white strip at the outer edge shows in panels 1, 3 and 5. The path trace shows scattered single bright samples over the whole crop and no strip. The geometry's panel is black on the whole jamb. Dark red marks pixels that see no surface.

![The end sill of rect-lamps in six panels](leak-rect-lamps-end-sill.png)

[leak-rect-lamps-end-sill.png](leak-rect-lamps-end-sill.png): the end window's lower edge, x 900 to 1019, y 556 to 570, at six times its size. It holds the same panels, from top to bottom. The engine shows a dotted bright row at the sill, and its direct sunlight shows the same row. The path trace shows only scattered single samples. Neither map has a pixel center on the sill, because its top spans 0.2 rows.

Reading: every strip that the engine lights is a leak, but for its outer 1 to 6 mm. The sun stands almost overhead and the hull's roof hides the whole room from it. The engine's lit part is the outer 2 texels of cascade 0 on every jamb and sill (part 2).
