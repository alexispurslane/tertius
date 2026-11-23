---
id: t1-nyjl
status: in_progress
deps: []
links: [ter-fe63, t7-3e22, t1-cyhy]
created: 2026-11-23T17:55:13Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, experiment]
---
# Guide census: would another guided share, or a guide that weighs a sender's distance and angle, cut the bay's far-hit variance

A measurement, no engine change. The orchestrator ordered it on 2026-11-23 as the third part of the speck census (ter-fe63), for the blotches of the bay (t7-3e22's daub) as much as for the specks. The surface share's lead runs it after the census's first two parts, with the same probe.

## Why

The ray census (ter-8pco) and the ray end record (ter-pdw8) say that in the bay every far hit is on the screen, that far hits carry 0.998 to 1.002 of the diffuse variance, that the brightest sender is in the guide's pyramid, and that a smoother value at the hit changes nothing (ter-6fka's note of 2026-11-23). So the variance is in where a ray lands and in its weight. The suspect is the guide's draw: it picks a sender by the sender's light alone, without the sender's distance and angle to the shaded pixel, and it gets a fixed share of the rays.

## Terms

- **Sample**: one diffuse ray of one pixel in one draw.
- **X**: the sample's weighted light as the accumulator receives it, the ray's light over its density.
- **q**: the density per steradian with which the sample's direction was drawn. With the guide on it is the mixture `(1 - share) * qCosine + share * qGuided`.
- **qCosine**: the cosine draw's density at the sample's direction. **qGuided**: the guided draw's density at that direction and hit pixel, as `guidedDensityOf` gives it, 0 for a miss.
- **Guided share**: the share of the diffuse rays that take the guided draw, 0.25 on main (`LightPyramid.GuidedShare`).
- **Sender**: the pixel on the screen that a ray hits or that a guided draw aims at.
- **Geometry factor** of a sender for a shaded pixel: the cosine at the shaded pixel toward the sender, times the solid angle that the sender's pixel subtends there (its area times its own cosine over the squared distance).

## The method

A sample drawn with the density q can be weighed again for another density q', as long as q' is above 0 wherever the light is. The second moment of a pixel's estimate under q' is the mean over the logged samples of `X * X * q / q'`, and its variance is that mean less the squared mean of X. The ratio `q / q'` is bounded for a mixture with a share between 0 and 1, so the estimate is stable. No new take is needed for a draw whose q' the log can give per sample.

The log holds per sample: X, q, qCosine, qGuided, whether the sample took the guided draw, whether it hit, the hit pixel, the hit distance, and for a guided draw its target pixel and whether it counted.

## What to report

For the bay's four regions of the ray census (the sunlit patch, the back wall below the slot, the floor, the right wall) and the two speck boxes (the nose's ceiling box, and rect-bay's left wall at draw 130):

1. **Other guided shares.** The variance of a pixel's diffuse estimate at a guided share of 0.5 and of 0.75, as a factor against today's 0.25, by reweighting. Check the method first: reweight to 0.25 itself (the factor must be 1) and, in one region, compare a reweighted share with one real take at that share.
2. **A guide that weighs the geometry.** A guided draw that picks a sender by its light times its geometry factor for the shaded pixel, per pixel, and per block of pixels if per pixel is too dear to evaluate. Its density at a logged sample needs the sum of light times geometry factor over all senders, for the shaded pixel or its block. Say whether the logged frame's images give that sum (the depth, the normals and the guide's finest level of one frozen draw), or which dump it needs. Then the variance under that draw at the shares 0.25 and 0.5, as a factor against today.
3. **Where the variance sits by the samples' weight.** The share of the sum of X squared that comes from guided draws against cosine draws, and within each from samples by their density q in bins. The question: do rare guided draws with a small density carry the tail, or cosine draws that land on the patch by chance? The mixture's weight is the suspect for the specks, and it can be the same fault at a lower level in the mottle.
4. **What the log cannot answer.** For each of the points above, say whether the reweighting answered it or whether it needs a take, and why.

## The reading (the orchestrator's rule)

If a draw that weighs distance and angle cuts the bay's far-hit variance by a factor of 2 or more by this estimate, it becomes the next engine ticket for the blotches. If no draw cuts it, the lever is the count of samples that a pixel averages, and that is a question for the resolve and the denoiser.

## Rules

- No engine change. The probe is a scratch change, kept as a patch in the record. The record goes to docs/research/guide-census/ on the lead's branch, with its tools and tables, and merges as a record.
- The takes are frozen and at one fixed exposure, at the render scale 2/3 as in the ray census, 240 settling draws and 400 counted draws, clamps on, so that the numbers lay beside that record's.
- No cloud money. Disk and GPU lock rules as in the lead's brief. A dump of every sample of 400 draws is large: write sums per pixel where a sum is enough, and delete raw dumps that the lead created itself once the tables exist. Keep every table and picture that the record names.
- The first numbers come to the line orchestrator as an interim, because the orchestrator asked for them by name.

## Out of scope

- Any change of the guide, the guided share or the resolve.
- The reflection rays, apart from what parts 1 and 2 of the census already take in the speck boxes.


## Notes

**2026-11-23T18:33:23Z**

2026-11-23, line orchestrator: the first numbers are in (the surface share's lead, thread thread:delegated-task:command%3Amcp%3A1606afbd-dec8-4bf8-a33c-bc8fce97f948%3Adelegate-task%3Aslabs-3-t1-r9vj-lead-1, worktree t3-2b720d96-b40221aeff8f27f7; the record goes to docs/research/guide-census/ on its branch). Takes: bay-sunbeam at 2/3, -4.089 stops, 240 settling and 400 counted draws, the four regions of the ray census; the nose's ceiling box from ter-fe63's take; rect-bay's left wall, draws 125 to 135. The caps cut nothing in these regions, so the reweighting before the caps is exact. The check passes: reweighting to 0.25 gives a factor of 1, and a real take at a share of 0.5 gives per region 1.004, 0.675, 0.757 and 0.535 against the reweighted 1.001, 0.681, 0.759 and 0.534. The variance of one diffuse sample as a factor against today (light-only guide, share 0.25), in the order share 0.5, share 0.75, geometry guide at 0.25, at 0.5, at 0.75: bay sunlit patch 1.00, 1.11, 0.79, 0.66, 0.63; bay back wall 0.68, 0.66, 0.75, 0.48, 0.49; bay floor 0.76, 0.77, 0.87, 0.64, 0.64; bay right wall 0.53, 0.41, 0.94, 0.50, 0.38; the nose's box 1.68, 3.81, 0.98, 1.69, 3.88; rect-bay's left wall (11 draws) 0.67, 0.77, 0.71, 0.48, 0.61. The geometry guide picks a sender by its light times its geometry factor; its normalizing sum comes from one draw's dump with senders in 4 x 4 blocks. By the orchestrator's rule the geometry draw is not the next engine ticket: at the same share it cuts the bay's far-hit second moment by 1.05 to 1.27 times, never 2. A higher share does most of the work on the walls and the floor and costs the nose's box 1.7 to 3.8 times. Where the second moment sits: on the bay's walls guided draws carry it at ordinary densities (right wall 80.8 percent, of which 74.5 percent from the 21.0 percent of samples at log2 q = 0; back wall 61.7 percent; floor 55.3 percent; rect-bay's left wall 70.6 percent). On the sunlit patch and in the nose's box cosine draws that land on the bright patch by chance carry it (guided 27.7 and 11.8 percent). My reading, a hypothesis and not a measurement: the share rows fit variance = K (1 / share - 1) + c, which is the variance of a coin flip. The guided draw is chosen by a hash per pixel and draw, so the count of guided draws among a pixel's averaged samples is binomial, and guided draws with steady values then carry that count's noise. By the fit the coin flip is about 70 percent of the right wall's variance, 48 percent of the back wall's, 36 percent of the floor's and none of the sunlit patch's. Ordered from the lead on my own judgment, for the orchestrator to reverse: step 1 from the existing log, the split of each pixel's variance into the part between the two kinds of draw and the part within them at the shares 0.25, 0.5 and 0.75, and the best share per 8 x 8 block chosen on the first 200 draws and evaluated on the last 200; step 2, one probe switch that gives every pixel exactly one guided draw in 4 draws and every 2 x 2 block one per draw, with one take for the measured factor and the frozen bay at 1:1 beside main. Chart: t3-1b834d57-evidence/census/guide-factors-chart.png.

**2026-11-23T18:37:05Z**

2026-11-23, line orchestrator: step 1 from the log refutes my coin-flip hypothesis. The lead split the variance of one sample per pixel under a share s into the part between the two kinds of draw, s (1 - s) (mG - mC)^2, and the part within them, s vG + (1 - s) vC, where mG and vG are the mean and variance of the reweighted X over the pixel's guided draws and mC and vC the same over its cosine draws. Pooled per region as a factor against today's total variance, between / within at the shares 0.25, 0.5 and 0.75: bay sunlit patch 0.009 / 0.991, 0.020 / 0.980, 0.052 / 1.053; bay back wall 0.020 / 0.980, 0.010 / 0.671, 0.073 / 0.585; bay floor 0.023 / 0.977, 0.007 / 0.754, 0.026 / 0.749; bay right wall 0.142 / 0.858, 0.037 / 0.497, 0.002 / 0.404; the nose's box 0.040 / 0.959, 0.370 / 1.332, 1.425 / 2.462; rect-bay's left wall (11 draws, noisy) 0.220 / 0.695, 0.122 / 0.525, 0.138 / 0.573. So the coin flip is 0.9 to 14.2 percent of today's variance in the bay, against my fit's 0 to 70 percent, and an exact count of guided draws gives at most 1.17 times on the right wall. The 1 / share shape of the share rows comes from the within part: a guided draw's weight on light that only the guide reaches scales as 1 / share, and so does its spread. A share per 8 x 8 block, chosen on the first 200 draws and evaluated on the last 200, gives 0.94 (patch), 0.60 (back wall), 0.71 (floor), 0.40 (right wall), 0.91 (nose) and 1.07 (rect-bay's left wall, noisy); the chosen shares run from 0.05 to 0.10 on the patch and the nose to 0.75 on the right wall. The best uniform share per region gives 1.00, 0.66, 0.76, 0.41 and 1.00, so a share per place adds little over a share per region. Also from the lead: a pixel's averaged samples are its 2 x 2 block's ray, and own rays are 0.0 to 0.3 percent of the samples in these regions. My settlements, for the orchestrator to reverse: (1) step 2 as ordered (an exact pattern of guided draws) is dropped with no take, because its predicted factors are 0.99, 0.98, 0.98 and 0.86. (2) In its place the picture that tests the lever the tables show: the frozen bay at the guided shares 0.25, 0.5 and 0.75 at 1:1 with the daub of each crop and each region's mean, and the nose's ceiling box at 0.25 beside 0.5 for the cost. The switch is the share's constant, as in the check take. (3) One more number from the log: the variance within the guided draws split into the part from arrival, p (1 - p) mA^2, and the part from the spread among arriving draws, p vA, to say whether the guide lacks visibility or the sender's weight.

**2026-11-23T18:39:39Z**

Orchestrator, 2026-11-23, on the first numbers. My suspect is refuted as a large lever: a guide that also weighs the sender's distance and angle gives 1.05 to 1.27 times at the same share, never 2. It is not the next engine ticket. The line's settlements stand: no take with an exact count of guided draws, the stills of the guided share at 0.25, 0.5 and 0.75, and the split of the variance within the guided draws.

One more set of stills from the same takes, because the census says that the count of samples that a pixel averages is the main lever: the frozen bay with the diffuse history's cap at 32 (today), 64 and 128, at 1:1 with the daub and each region's mean, and the pair (share 0.5, cap 64). If the cap is one constant, it is a switch like the share. Reading: if no cap shows a cleaner bay at 1:1, the count of samples in time is not the lever either, and the blotch is a correlated error (the block's shared ray, the filter's pool), which more history cannot remove. If a cap of 64 or 128 shows clearly, the question becomes what a longer history costs when the light changes, and rect-bay's film at that cap answers it. No rule of the temporal stack changes before these pictures.

**2026-11-23T18:45:14Z**

2026-11-23, line orchestrator of the back-face line: the cap is one place, and my answer on the blotch's size.

**The cap.** The diffuse history's cap is one place: `SignalParameters.Diffuse` (src/Tertius.Rendering/Frame/SignalParameters.cs), the fields `Cap: 32` and `SmoothCap: 32`. It is a switch like the share. `DeviationFrames = 32.0` in accumulate_temporal.comp is the deviation's window, a separate constant, and stays. The lead takes at least 4 times the cap in settling draws before each still.

**The blotch's size, by argument and from one existing picture.** Source: `docs/research/t0-qvm1-region-cap-takes/pics/blotch-bay-26.png`, the blurred relative difference of the frozen bay's still from its late mean at draw 890. The map is drawn at half of the output size.
- On the floor and the right wall the blobs are about 80 to 160 output pixels across.
- In the dark upper left the mottle is finer, about 16 to 24 output pixels.
- Nothing in the map has the size of the block of 2 x 2 render pixels, which is 3 output pixels at a render scale of 2/3. The daub measure cannot see that size at all: its 5 x 5 median and its blur of 3 pixels remove it, and its band keeps the scales from about 6 to 70 pixels.

**It matches the diffuse filter's steps.** The thin kernel has 6 steps, a B-spline of (1, 2, 1) / 4 with taps 1, 2, 4, 8, 16 and 32 render pixels apart, and reaches 63 render pixels along each axis (DiffuseFilter.cs). Where no stop acts, the six steps together are close to a Gaussian with a deviation of sqrt(0.5 * (1 + 4 + 16 + 64 + 256 + 1024)) = 26 render pixels. White noise through that filter has blobs with a full width at half maximum of about 2 * 1.18 * sqrt(2) * 26 = 87 render pixels, which is about 130 output pixels at 2/3. That is the floor's and the right wall's size. The finer mottle of the dark upper left is the size of the steps up to 4 or 8 render pixels: there the filter's stops end its reach earlier.

**What the size names, and what it does not.** The size names the filter's pool as the shape of the error, and it clears the block's shared ray as the shape. It does not say that the error is correlated in a way that more history cannot remove. Independent noise through this filter looks exactly like this: any filter turns the noise that it leaves into blobs of its own reach. Two numbers separate the two cases, and the lead takes both from the stills and the log that it has:
1. **The noise budget.** An unstopped filter averages about 4 pi 26^2 = 8,560 render pixels, which are about 2,140 block rays, and a history at its cap of 32 holds about 2 * 32 - 1 = 63 draws. So independent noise leaves a relative deviation of about one sample's relative deviation over sqrt(2,140 * 63) = 367. The bay's still has an RMS of 0.0147 at draw 890 (stills.md of the same record). That needs a relative deviation of one sample of about 5. If the log's value per region is near 5 or above, the blotch is plain noise and falls with the count of samples. If it is far below 5, an error repeats between draws or between rays, and more history cannot remove it. The feedback step writes the filtered light back into the slow history, so the true count is larger than this estimate, and the estimate is a bound from the lenient side.
2. **The caps.** A blotch from independent noise keeps its size at a cap of 64 and of 128 and loses amplitude, about 1.41 times per doubling. A blotch that keeps its amplitude is a correlated error.

Ordered from the lead with the cap stills: the half width at half maximum of the radial autocorrelation of the still's relative difference from its late mean, per region and per cap, in output and in render pixels, and the relative deviation of one diffuse sample per region.

**2026-11-23T18:47:35Z**

2026-11-23, line orchestrator: the stills of the guided share exist, and they show nothing. Frozen bay (bay-sunbeam, 2/3 = 1280 x 720, -4.089 stops fixed, capture 19 of 20 captures of 33 draws) at the shares 0.25, 0.5 and 0.75; crops at 1:1 in the lead's evidence folder t3-2b720d96-b40221aeff8f27f7-evidence/guide/ (bay-sunlit-patch.png, bay-back-wall-below-the-slot.png, bay-floor.png, zoom-floor-left.png, bay-right-wall.png, zoom-right-wall-top.png, nose-box-0.25-0.5.png, daub.txt). I looked at them: the three shares look the same in every crop. The daub against an independent reference (the mean of captures 30 to 99 of a 100-capture take at 0.25), daub / broad, at 0.25, 0.5, 0.75: whole image 0.0207 / 0.0126, 0.0209 / 0.0121, 0.0216 / 0.0134; sunlit patch 0.0281 / 0.0062, 0.0278 / 0.0074, 0.0284 / 0.0077; back wall 0.0132 / 0.0152, 0.0161 / 0.0058, 0.0151 / 0.0095; floor 0.0233 / 0.0128, 0.0270 / 0.0221, 0.0205 / 0.0174; right wall 0.0318 / 0.0126, 0.0318 / 0.0132, 0.0315 / 0.0080. Region means against main stay within 1 %. So a variance of one diffuse sample that is 1.3 to 2.5 times lower does not move the daub. The nose's ceiling box at 0.25 beside 0.5: the cloud looks the same; capture specks 3,719.5 against 3,752.5 (median), largest 13,095 against 13,068; the measured variance of one diffuse sample rises 1.65 times (predicted 1.68); the cloud is the reflection signal. The lead's suspect, which I take as the next step before the cap stills: the bay's walls and floor have a roughness of 0.38, below MaxRoughness of 0.5, so every pixel traces an unguided reflection ray, and the specular signal has no spatial filter. What the crops show on the right wall's top and the floor's lower left is a grain of 1 to 3 pixels, which a signal without a spatial filter leaves, not the diffuse filter's blobs. Ordered: the frozen bay at 0.25 with the diffuse signal alone and the specular signal alone, each with its daub against a reference of its own mode, and the diffuse signal alone at 0.75; then the cap stills in the full picture and the cap of 128 in the diffuse mode. Also from the lead: the take with an exact count of guided draws had run before my message; its factor of one sample's variance is 1.00, which says nothing, because an exact count changes only the variance of a pixel's mean. The arrival split is its own finding and its own ticket (t1-cyhy): 1.4 to 32.4 % of the bay's guided draws arrive, and arrival carries 84 to 87 % of the guided draws' variance.
