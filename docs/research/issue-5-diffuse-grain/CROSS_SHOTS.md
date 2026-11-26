# Cross-shot validation across Blue Danube film shots, issue #5

This report tests the 1:41 continuous-motion candidate (`moving-source-accum-spread`: isolated diffuse-ray-sourced specular RGBA32F physical-point accumulation with cap 64 plus radius-5 reciprocal connected spread) across four other Blue Danube shots that show visible grain. Every shot keeps true film composition, matched fixed exposure across variants, and exact prior simulation ticks and posts from `scripts/blue-danube.json`.

Every engine edit is a temporary diagnostic patch restored after each render. The committed `src/` tree has no diff, PR #14 stays draft, and issue #5 stays open.

## Shots, frame cadence and draw-reset audit

Every slice renders at 1280-by-720 output (848-by-477 internal FSR render size) with the cooked local Blue Danube art (`assets.json`). Fixed exposure comes from an unmodified auto-exposure meter run on each exact slice.

| Shot slice | Film timestamps and camera sequence | Timing mode | Fixed exposure (stops) | Measured frames (key frame) | Engine resets (`draws.jsonl`) |
|---|---|---|---:|---|---|
| `shot1-doorway` | `0:12.18..0:12.68` `terminal-camera`, CUT at `0:12.68`, `0:12.68..0:14.18` `follow-camera` (`0:13` = `s1b-000019`) | Ordinary 60 Hz (`every = 1`) | -4.0567 | `s1b` 10..69 (19) | 3: draw 0 (tick 432), cut (tick 462), planet-to-launchpad frame handoff (tick 474) |
| `shot2-cockpit-dash` (`0m27`) | `0:26.52..0:27.85` `follow-camera` (`0:27` = `s2a-000029`) | Ordinary 60 Hz (`every = 1`) | +0.5959 | `s2a` 15..74 (29) | 2: draw 0 (tick 1292), cut to `seat-camera` (tick 1372) |
| `shot2-cockpit-dash` (`0m28`) | CUT at `0:27.85`, `0:27.85..0:28.60` `seat-camera` (`0:28` = `s2b-000009`) | Ordinary 60 Hz (`every = 1`) | +0.5959 | `s2b` 5..44 (9) | Same run; `s2b` starts on the tick-1372 cut reset |
| `shot3a-arrival-early` | `4:13.72..4:15.05` `cockpit-camera` (`4:14.48` = `s3a-000046`) | True-film time-lapse (`every = 4`, 0.0667 s/frame < 0.09 s still threshold) | -0.2901 | `s3a` 16..79 (46) | 1: draw 0 only (continuous draws) |
| `shot3b-arrival-late` | `4:19.05..4:20.38` `cockpit-camera` (`4:19.82` = `s3b-000046`) | True-film time-lapse (`every = 4`, continuous draws) | +2.0804 | `s3b` 16..79 (46) | 1: draw 0 only (continuous draws) |
| `shot3c-arrival-1x` | Ticks 291,457..291,577 centered on `4:19.82` (`s3c-000060`) | Ordinary 60 Hz (`every = 1`) control matched to `4:19.82` | +2.0804 | `s3c` 16..119 (60) | 1: draw 0 only (continuous draws) |
| `shot4-berth` | `7:24.92..7:25.42` `work-camera`, CUT at `7:25.42`, `7:25.42..7:27.58` `deck-camera` (`7:27` = `s4b-000095`) | Ordinary 60 Hz (`every = 1`) | -2.9463 | `s4b` 30..129 (95) | 2: draw 0 (tick 316,923), cut (tick 316,953) |

Two timing details stand out from `draws.jsonl`:

- At `0:12.88` (tick 474), the astronaut crosses from the planet frame (`y = 6,371,002.45 m`) into the launchpad frame (`y = -2.471 m`). That reference-frame handoff resets every history (`sampleDraw = 0`), so frame `s1b-000019` (`0:13.00`) has only 7 draws of accumulated history.
- In `shot3a` and `shot3b`, true-film `"every": 4` advances `0.0667 s` per frame, below `MeteringStepSeconds` (`0.09 s`). As a result, it runs continuous non-resetting draws, unlike the earlier 1:41 `"every": 120` clip.

## Quantitative summary across all four shots

All variants compare against matched-exposure `baseline` on display-linear Rec. 709 luminance and local 4-patch RGB wash (8-output-pixel Gaussian blur). `no-diff-spec` removes only the diffuse-ray-sourced specular term in `screen_trace_resolve.comp`. `zero-diffuse` removes final indirect diffuse in `composite_pass.glsl` (run on `0:13` and `4:19.82` where `no-diff-spec` left visible structure).

| Shot and crop | Variant | Mean change | Spatial grain change | Temporal grain change | Local wash error | Multiscale RGB RMS (`s1 / s2 / s4 / s8`) |
|---|---|---:|---:|---:|---:|---|
| `0m13` `doorway-floor` | `candidate` | -0.1% | -0.2% | -0.3% | 0.1% to 0.2% | `0.0002` (vs `0.0003` base) |
| `0m13` `doorway-floor` | `no-diff-spec` | -1.3% | +0.6% | +0.6% | 0.5% to 2.5% | `0.0002` |
| `0m13` `doorway-floor` | `zero-diffuse` | -62.3% | -4.5% | -6.5% | 50.1% to 80.1% | `0.0001` |
| `0m13` `stair-wall` | `candidate` | +0.8% | -13.7% | -17.7% | 0.6% to 1.4% | edge-masked |
| `0m13` `stair-wall` | `no-diff-spec` | -7.8% | -10.1% | -13.3% | 6.8% to 9.9% | edge-masked |
| `0m13` `stair-wall` | `zero-diffuse` | -8.8% | +3.3% | +3.8% | 8.2% to 9.8% | edge-masked |
| `0m27` `cockpit-floor` | `candidate` | +6.1% | -51.9% | -37.0% | 2.5% to 19.3% | `0.0004 / 0.0006 / 0.0007 / 0.0010` (base `0.0024 / 0.0017 / 0.0011 / 0.0011`) |
| `0m27` `cockpit-floor` | `no-diff-spec` | -15.0% | -64.4% | -59.1% | 6.3% to 31.9% | `0.0002 / 0.0003 / 0.0004 / 0.0007` |
| `0m27` `cockpit-ceiling` | `candidate` | +2.2% | -68.2% | -45.4% | 1.1% to 6.8% | `0.0012 / 0.0024 / 0.0038 / 0.0044` (base `0.0115 / 0.0104 / 0.0076 / 0.0060`) |
| `0m27` `cockpit-ceiling` | `no-diff-spec` | -9.1% | -72.4% | -48.0% | 5.3% to 21.8% | `0.0007 / 0.0010 / 0.0019 / 0.0030` |
| `0m27` `cockpit-sidewall` | `candidate` | +4.5% | -38.2% | -39.9% | 2.3% to 11.3% | `0.0003 / 0.0004 / 0.0007 / 0.0018` (base `0.0018 / 0.0012 / 0.0009 / 0.0019`) |
| `0m28` `dashboard-console` | `candidate` | +4.2% | -24.3% | -40.1% | 3.2% to 5.6% | `0.0014 / 0.0038 / 0.0063 / 0.0088` (base `0.0018 / 0.0039 / 0.0062 / 0.0086`) |
| `0m28` `dashboard-console` | `no-diff-spec` | -52.0% | -47.5% | -61.3% | 44.3% to 57.3% | `0.0009 / 0.0027 / 0.0047 / 0.0060` |
| `0m28` `seat-armrest-joint` | `candidate` | +2.3% | -3.0% | -17.7% | 1.1% to 6.1% | `0.0004 / 0.0009 / 0.0022 / 0.0076` (base `0.0008 / 0.0009 / 0.0021 / 0.0076`) |
| `4m14` (`every=4`) `upper-right-panel` | `candidate` | +5.0% | -45.2% | -31.5% | 4.1% to 12.7% | `0.0006 / 0.0014 / 0.0037 / 0.0136` (base `0.0044 / 0.0033 / 0.0039 / 0.0136`) |
| `4m14` (`every=4`) `right-side-wall` | `candidate` | +8.0% | -24.6% | -17.8% | 2.9% to 22.6% | `0.0006 / 0.0008 / 0.0009 / 0.0012` (base `0.0029 / 0.0022 / 0.0013 / 0.0012`) |
| `4m19` (`every=4`) `upper-right-panel` | `candidate` | +7.1% | -69.4% | -51.1% | 4.8% to 15.0% | `0.0024 / 0.0045 / 0.0055 / 0.0132` (base `0.0161 / 0.0130 / 0.0071 / 0.0124`) |
| `4m19` (`every=4`) `upper-right-panel` | `no-diff-spec` | -15.7% | -67.3% | -51.0% | 11.7% to 31.2% | `0.0006 / 0.0008 / 0.0021 / 0.0105` |
| `4m19` (`every=4`) `upper-right-panel` | `zero-diffuse` | -26.7% | +24.9% | +19.6% | 7.9% to 72.5% | `0.0172 / 0.0131 / 0.0064 / 0.0121` |
| `4m19` (`every=4`) `right-side-wall` | `candidate` | +12.0% | -65.4% | -48.7% | 6.5% to 27.0% | `0.0011 / 0.0021 / 0.0023 / 0.0022` (base `0.0087 / 0.0070 / 0.0043 / 0.0025`) |
| `4m19` (`every=4`) `right-side-wall` | `no-diff-spec` | -33.4% | -65.2% | -44.6% | 19.7% to 84.1% | `0.0001 / 0.0001 / 0.0002 / 0.0010` |
| `4m19` (`every=4`) `right-side-wall` | `zero-diffuse` | -13.5% | +2.5% | +3.5% | 9.8% to 33.5% | `0.0080 / 0.0064 / 0.0038 / 0.0023` |
| `4m19` (`every=1`) `upper-right-panel` | `candidate` | +7.6% | -69.6% | -55.9% | 5.3% to 15.9% | `0.0022 / 0.0041 / 0.0053 / 0.0122` (base `0.0161 / 0.0129 / 0.0079 / 0.0115`) |
| `4m19` (`every=1`) `right-side-wall` | `candidate` | +12.8% | -65.5% | -55.8% | 7.1% to 28.0% | `0.0009 / 0.0016 / 0.0019 / 0.0020` (base `0.0075 / 0.0061 / 0.0037 / 0.0023`) |
| `4m19` (`every=1`) `wall-sun-edge` | `candidate` | +5.7% | -40.0% | -33.3% | 1.7% to 25.7% | `0.0011 / 0.0022 / 0.0030 / 0.0055` (base `0.0096 / 0.0078 / 0.0050 / 0.0058`) |
| `7m27` `berth-floor-center` | `candidate` | +7.0% | -76.8% | -38.2% | 4.5% to 9.5% | `0.0005 / 0.0008 / 0.0009 / 0.0010` (base `0.0028 / 0.0025 / 0.0016 / 0.0011`) |
| `7m27` `berth-floor-center` | `no-diff-spec` | -78.7% | -88.4% | -76.3% | 65.7% to 86.3% | `0.0001 / 0.0001 / 0.0002 / 0.0004` |
| `7m27` `berth-ramp-joint` | `candidate` | +10.7% | -32.5% | -32.1% | 8.2% to 13.9% | `0.0009 / 0.0018 / 0.0018 / 0.0018` (base `0.0035 / 0.0034 / 0.0024 / 0.0018`) |
| `7m27` `berth-booth-wall` | `candidate` | +2.4% | -1.7% | -4.9% | 1.3% to 4.9% | `0.0003 / 0.0007 / 0.0016 / 0.0024` (base `0.0014 / 0.0013 / 0.0017 / 0.0024`) |

## Per-shot visual findings and source separation

### 0:13 doorway (`0m13-doorway-contact.png`, `0m13-doorway-slow.mp4`)

- On `stair-wall` and `doorway-jamb-joint`, `no-diff-spec` turns the shadowed left stair wall smooth black, while `zero-diffuse` keeps the bright speckles intact (+3.3% spatial). That confirms diffuse-ray-sourced specular causes the stair-wall sparkles. `candidate` cuts those sparkles (-13.7% spatial, -17.7% temporal) and keeps the vertical doorway-jamb material joint crisp.
- On `doorway-floor`, `no-diff-spec` removes only -1.3% mean brightness and keeps mottled floor shading. By contrast, `zero-diffuse` drops floor brightness by -62.3% while diffuse-ray-specular sparkles stay on top. As a result, the floor's broad mottling comes from indirect diffuse plus the floor's concrete surface texture, not diffuse-ray specular alone.
- Remaining defect: frame `s1b-000019` (`0:13.00`) sits only 7 draws after the tick-474 planet-to-launchpad frame-handoff reset. With only 7 draws accumulated, `candidate`'s radius-5 spread turns rare sunlit bounce hits into visible soft clouds on the left stair wall.

### 0:27 cockpit floor and 0:28 dashboard (`0m27-cockpit-floor-contact.png`, `0m28-dashboard-contact.png`)

- At `0:27`, `no-diff-spec` removes every confetti speckle on the cockpit floor, ceiling and dark sidewall. `candidate` clears the sharp confetti across all three surfaces (`cockpit-floor` `s1: 0.0024 -> 0.0004`, `cockpit-ceiling` `s1: 0.0115 -> 0.0012`). It keeps the doorway bounce sheen, yellow floor stripes and ceiling rivets crisp.
- At `0:28`, `no-diff-spec` removes all sandpaper grain on `dashboard-console` (-52.0% mean, -47.5% spatial, -61.3% temporal) and `seat-armrest-joint` (-37.2% mean). Nine draws after the tick-1372 cut to `seat-camera`, `candidate` cuts console spatial grain by -24.3% and temporal grain by -40.1%. Cyan screen edges and the seat-armrest joint stay sharp.
- Remaining defect: soft medium-scale clouds stay visible on the dark floor and ceiling near the bright sunlit doorway at `0:27`. Mild mottling also stays on the dark lower console face at `0:28` during the first few frames after the cut.

### 4:14.48 and 4:19.82 arrival (`4m14-arrival-tl-contact.png`, `4m19-arrival-tl-contact.png`, `4m19-arrival-1x-contact.png`)

- Source separation at `4:19.82` confirms that `zero-diffuse` keeps all bright snow on `upper-right-panel` (+24.9% spatial) and `right-side-wall` (+2.5% spatial). `no-diff-spec` clears 100% of the snow on `right-side-wall` (`s1: 0.0087 -> 0.0001`) and most snow on `upper-right-panel` (`s1: 0.0161 -> 0.0006`). Only faint fine grain stays on the panel from the roughly 10% dedicated reflection ray in the 0.40 to 0.50 roughness hand-over band.
- `candidate` removes the harsh snow on both `4:14.48` and `4:19.82`. At `4:19.82`, `every = 4` time-lapse and `every = 1` ordinary motion give nearly equal spatial cuts (`upper-right-panel`: -69.4% vs -69.6%, `right-side-wall`: -65.4% vs -65.5%). Ordinary 60 Hz (`every = 1`) gives stronger temporal cuts (-55.9% vs -51.1% on the panel, -33.3% vs -18.7% on `wall-sun-edge`) because the sunlit strip moves four times slower per draw.
- The bright sawtooth sunlit edge on `wall-sun-edge` stays visually sharp in both `every = 4` and `every = 1` with no smear across the moving shadow boundary.
- Remaining defects: soft cloudy blotches persist on `upper-right-panel` (`s8 = 0.0132` vs `0.0124` baseline) and `right-side-wall` under `candidate`. Mean brightness rises +12.0% to +12.8% on `right-side-wall` (`wash` 6.5% to 28.0%) and +19.3% on `4:14.48` `cockpit-floor` because unclipped rare hits accumulate energy that production's narrow-box clamp clipped. Faint residual grain from the ~10% dedicated reflection ray stays on `upper-right-panel`.

### 7:27 berth floor (`7m27-berth-floor-contact.png`, `7m27-berth-floor-slow.mp4`)

- `no-diff-spec` removes the berth floor's gravel grain (`berth-floor-center` -88.4% spatial, `s1: 0.0028 -> 0.0001`). That confirms diffuse-ray-sourced specular causes the berth-floor noise, though removal wipes out -78.7% of the floor's skylight reflection brightness.
- `candidate` cuts `berth-floor-center` spatial grain by -76.8% (`s1: 0.0028 -> 0.0005`, `s2: 0.0025 -> 0.0008`, `s4: 0.0016 -> 0.0009`, `s8: 0.0011 -> 0.0010`). It keeps the overhead bay reflection sheen (+7.0% mean, wash 4.5% to 9.5%), crisp floor tile lines, and clean `berth-ramp-joint` and `berth-booth-wall` seams.
- Remaining defects: mild large-scale mottling remains on the distant floor and along the ramp-to-floor shadow seam (`berth-ramp-joint` wash 8.2% to 13.9%). The first few frames after the tick-316,953 cut also show temporary soft blotches while history builds from draw 0.

## Camera-cut transition checks

All three real camera cuts reset cleanly on post-cut frame 0 with no ghosting or leakage from the pre-cut view (`evidence/cross-shots/*-cut-check.png`):

| Camera cut | Pre-cut -> post-cut camera | `candidate` post-cut f0 mean change | `candidate` post-cut f0 paired RMS | Visual cut-strip check |
|---|---|---:|---:|---|
| `0:12.68` (tick 462) | `terminal-camera` -> `follow-camera` | +0.01% | 0.00142 | Clean reset, zero bleed from terminal interior |
| `0:27.85` (tick 1372) | `follow-camera` -> `seat-camera` | +0.67% | 0.01945 | Clean reset, zero bleed from cabin doorway |
| `7:25.42` (tick 316,953) | `work-camera` -> `deck-camera` | -0.02% | 0.01200 | Clean reset, temporary soft blotches on f0..f2 while history warms |

## Retained evidence and reproduction

Compact evidence sits under `evidence/cross-shots/`: `shots-results.json`, all seven `*-contact.png` sheets, all three `*-cut-check.png` strips, all seven `*-slow.mp4` comparison clips (12 fps), temporary patches, settings JSONs and `draws.jsonl` logs.

```powershell
nix develop --command python3 docs/research/issue-5-diffuse-grain/shots_render.py shot1-doorway baseline captures/issue5-shots/shot1-doorway/baseline --exposure -4.056653
nix develop --command python3 docs/research/issue-5-diffuse-grain/shots_render.py shot1-doorway candidate captures/issue5-shots/shot1-doorway/candidate --exposure -4.056653
nix develop --command python3 docs/research/issue-5-diffuse-grain/shots_analysis.py captures/issue5-shots captures/issue5-shots/analysis
```
