---
id: t1-nyjl
status: in_progress
deps: []
links: [ter-fe63, t7-3e22]
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
