---
id: t1-nyjl
status: open
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

