# The output dither's gate: does the 8-bit output show a step unstretched? (t1-m0p7)

The ticket's gate (the line orchestrator, 2026-11-23): before any build, look at unstretched 1:1 crops of a smooth, dark
wall. If no step shows, the ticket waits with the crops as its record. If a step shows, the design in the ticket stands.
This record builds nothing.

## The stills

The binary is `/private/tmp/t0-gbgj/bins/main`, a Release build of the line's code at `72426c219`. Each still is a
headless still of 33 draws (main's still rule: 32 warm-up draws and the shown draw) at 1920 × 1080 output, with the
headless render scale of 2/3 and FSR 3.1. `stills/` keeps each still's `exposure.json`.

| Crop | Scene, camera and script | Exposure | Place in the still |
|---|---|---|---|
| `bay-back-wall` | demo-surface, `bay-camera`, 420 ticks: the bay's back wall below the slot (t1-af5w's `views/bay-sunbeam.json`) | fixed −4.0 stops | x 580, y 430, 380 × 65 |
| `cabin-left-wall` | orbit, `person`, 60 ticks (`stills/orbit-cabin.json`): the fair cabin switch's view, with all lamps on | fixed 4.08 stops, the fair take's | x 0, y 230, 400 × 250 |
| `auto-cabin-left-wall` | the same | automatic, settled at 3.909 stops | the same |

The fair cabin take switches two lamps off through the survey probe's `TERTIUS_PROBE_LAMP_OFF`, which a clean build
lacks, so its crop keeps every lamp on. The cabin's left wall between the ceiling line and the windows is its darkest
smooth wall. The bay's back wall below the slot is a narrow band between the slot's rim and the floor line, so its crop
is small: a wider one took in the rim (code 8) or the shade of the corner.

## The crops and the numbers

`tools/crops.py` cuts each crop from the PNG with no scaling and checks that its bytes equal the still's at that place
(all three equal). For finding steps only, it writes each crop stretched 12 times around each channel's own 36 px
Gaussian blur, the stretch of the guide census's `bands.py` (`crops/*-stretched.png`). `crops/numbers.json` holds the
numbers below. A run is a stretch of pixels of one G code along the crop's steepest direction, on lines 4 px apart; runs
that touch a line's end are left out.

| Crop | R codes | G codes | B codes | Mean slope, G codes per px | Runs: least / median / greatest, px | Share of 1 px runs |
|---|---|---|---|---|---|---|
| `bay-back-wall` | 102–146 | 102–146 | 105–149 | 0.25 | 1 / 3 / 13 | 0.26 |
| `cabin-left-wall` | 83–106 | 77–99 | 72–93 | 0.061 | 1 / 4 / 120 | 0.25 |
| `auto-cabin-left-wall` | 76–98 | 70–91 | 66–85 | 0.057 | 1 / 4 / 107 | 0.26 |

The stretched cabin crops show the contour rings of one code each, broken up by the grain. The stretched bay crop shows
grain and bright specks, and no rings: one code there is about 4 px wide.

## The reading of the unstretched crops

Opened with the Read tool at 1:1, before and apart from the numbers:

- `bay-back-wall`: no step. A smooth vertical gradient under even grain.
- `cabin-left-wall`: a step shows, faintly. Faint near-vertical bands lie in the lower half, at about x 30 to 260 and
  y 130 to 250 of the crop, about 35 to 45 px apart. The stretched crop shows that they are the lower arcs of the
  contour rings. The upper half shows none.
- `auto-cabin-left-wall`: a step shows, more faintly, as the same bands at the same places.

So a step shows unstretched where the gradient is shallowest, about 0.06 codes per px. There, a band of one code is 15
to 120 px wide, and the grain does not hide its edge. Where the gradient is steep, as on the bay's back wall, the grain
hides it. By the gate's rule the ticket's design stands. The reading is of one display path (the Read tool's view of
the PNG), not of the user's display.
