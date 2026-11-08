---
id: tiunr-py6q
status: open
deps: [tiunr-4b2g]
links: [tiunr-ax4c, tiunr-7haj]
created: 2026-11-07T17:27:51Z
type: task
priority: 2
assignee: Alexis Purslane
parent: tiunr-463x
---
# Keep reflected-light change evidence relevant to the long history

Follow-up from paired-ray experiment tiunr-ax4c. On a 20% dimming change after 1024 warm-up draws, matching reference rays shorten history at 2117/12486 plate pixels on the change draw, and at zero plate pixels on the following 179 measured draws. The reference record renews every draw. Both matching evaluations then see the new light while the long radiance average still contains old illumination. Dimming reaches persistent 90% adjustment in 118 updates, versus more than 180 without the check. Static rough and smooth measured output is unchanged. Investigate how evidence can remain relevant to contributors retained in the long history without treating independent ray directions as a scene change. This ticket records the limitation only. No next design, implementation, or automatic acceptance is authorized.

## Acceptance Criteria

A user-selected follow-up defines which retained contributions its evidence validates and measures static accuracy/noise plus subtle-change response against the sealed uncapped and paired-ray controls. Do not claim exact long-history provenance from one reference ray.


## Notes

**2026-11-07T18:11:01Z**

**2026-11-07: user selected the agreement test with a drift sum**

Diagnosis without new data: the only history shortening is the box clamp in accumulate_spatial.comp step 4. Its half width is 1.5 times the spread of the 3x3 fast histories, not the standard error of their mean. On the rough plate, the capped run (17.74 W/(m² sr) output noise at length 8.67, pre-event mean about 103.6) puts one sample's standard deviation at no less than 69% of the mean. A fast history keeps one seventh of a sample's variance, so the box is at least ±39% of the mean. A 20% dimming stays inside it and a switch-off leaves it, which matches the measured 10-update switch-off and the unfinished dimming.

Selected model: the slow history stays valid while it agrees with recent light within the standard error of that agreement. The gap between recent light and the slow history persists until the slow history catches up, so this evidence stays relevant to the long history. The paired ray compared two traces of one path, which agree again after the change frame.

User choices (AskUserQuestion, 2026-11-07): pool 3x3 same-surface neighbors per frame, and keep a two-sided drift sum (CUSUM) across frames. Per frame: z = mean(sample - slow) over the 3x3 window divided by its standard error from the same residuals, clamped to a limit. Rising and falling sums lose a fixed allowance each frame. Above a threshold, the length falls to the fast cap (4) and both sums reset. Agent-settled forks: response is the fast-cap floor, standard error comes from the neighbors each frame with no stored variance, the box clamp stays. One new 4-byte image per signal (3.5 MiB at 1280x720). The temporal dispatch reprojects the sums with the slow history. No rays. Estimator, receiver tests, H13 and the distance guide stay unchanged.

Gaussian-model predictions, not measurements: tuning near allowance 0.2 and threshold 17 gives about one false shortening per 19,000 updates per pixel per direction. A 20% dimming is found in about 25 to 45 updates, and a 10% dimming in about 70 to 130 updates. The saved captures keep per-frame output and history length but not per-frame samples, so they cannot replay the test offline.

**2026-11-07T19:15:56Z**

**2026-11-07: drift-sum agreement test built and measured, not accepted**

The paired-ray check (tiunr-ax4c) is removed. Every file that it owned matches its state before the check byte for byte.

TERTIUS_REFLECTION_HISTORY_AGREEMENT=drift is an opt-in mode of the precise specular history. The innovation is the luminance of the limited sample minus the reprojected slow history. The 3x3 same-surface window gives a score, its mean over its standard error, limited to ±4. A two-sided CUSUM with threshold 17 cuts the history to the fast cap of four. It takes 24 bytes per pixel (RG32F sums and an RGBA32F scratch), or 21.09 MiB at 1280x720. The signal set gets two more storage images: 11 in the accumulate stage, under the required 15. The ticket's estimate was 4 bytes. The sums use fp32 because Vulkan leaves half-float rounding to the implementation, and the scratch hands innovations to the neighbors.

Native captures, paired-ray matrix, 1,024 warm-up draws, rough plate:
- Allowance 0.2 (selected): dimming persistent 90% after 57 updates (control: not within 180). Static rough noise RMS +101%, smooth +290%. Static rough falls to the fast cap at 4.44% of plate pixels per frame (control 0.87%).
- Allowance 1.25 (the one tuning change): static rough noise +29%, smooth +41%. Static rough falls 1.53% per frame. Dimming is the same as the control (unfinished 16.9% after 180 updates, control 17.3%).
- In every mode, the fall share after the dimming is within 0.1 percentage points of the share before it. The faster dimming at 0.2 comes from histories that stay short everywhere.

Cause: on the static rough plate the window score averages -0.84, not 0. Rough rays are right-skewed, and each window estimates its own standard error. A window without a bright sample has a negative mean and a small error. 69,426 of 70,403 static falls at 0.2 followed a larger falling sum. No allowance separates static noise from a 20% dimming.

With the test off, the control is byte-identical to the sealed paired-ray control in all 7 cases (1,239 files). A GPU readback across the old and new builds matches bit for bit. Four drift behavior tests fail at 1.25 (dimming, brightening, trip, camera-cut precondition). All other Rendering tests pass in Checked and Release. The working tree keeps 1.25, opt-in, uncommitted and unmerged. Evidence and report: /private/tmp/tertius-drift-history-20261107/report.md.

**2026-11-07T19:28:06Z**

**2026-11-07: user selected the light-change age (design Y)**

Analysis of the drift failure: a rough-plate ray is mostly dark, with rare bright emitter hits. A 20% emitter dimming changes only the brightness of those rare hits. The dark rays and the order of rays stay the same, so median-based and rank-based tests cannot see the change. The change information is in the exact part of the hit light: direct light from the light loop and glow, which have no noise.

Design Y (user choice, AskUserQuestion 2026-11-07, chosen over a fair noise test with a long-term noise record): each screen pixel keeps a light-age record, the number of frames since its exact light last changed. Each ray brings the age of its hit back to its receiver. The receiver's memory may not hold more frames than that age, so it holds only light from after the change and grows back with the age. No rays are added. The box clamp stays.

Agent-settled forks: exact light is view-independent (demodulated direct diffuse light plus glow), so camera motion does not change it. A change is more than 2% outside the reprojected 3x3 range of the last frame, which tolerates jitter at shadow edges. A new or disoccluded surface, a reset and a sky miss give no evidence. A ray cuts only if its light is at least 25% of the receiver's slow history. Specular signal only in this experiment. Y alone means that the drift mode leaves the working tree.

Known limits: changes only in bounced light, light that the screen cannot show, and rays that now hit a different surface (a moving reflected object) give no evidence. The box clamp still covers large changes.

**2026-11-07T20:27:11Z**

**2026-11-07: light-change age built and measured, not accepted**

The drift mode is removed. The code tree is again git tree 77300791, the drift control build's own code tree, and git status matches the sealed post-removal status.

TERTIUS_REFLECTION_HISTORY_AGREEMENT=light-age is an opt-in mode of the precise specular history. The ray plan keeps a per-pixel record of the exact light (demodulated direct diffuse plus sent glow, pre-exposure undone) and its age. More than 2% outside the reprojected 3x3 range resets the age to 0. A record without history, or that never saw its light change, has no evidence. Each hit carries its pixel's age in the 15 free bits of the ray record's w. A sample of at least 25% of the slow luminance cuts the length to its youngest ray age before the blend. It takes 18 bytes per pixel (15.82 MiB at 1280x720), 3 more storage images in the tracer set and 1 in the signal set, and no rays.

Native captures, 1,024 warm-up draws:
- Rough 20% dimming: persistent 90% adjustment after 36 updates (control: not within 180, paired ray 118, drift k=0.2 57). Unfinished after 180 updates: 1.4% (control 17.3%).
- Static rough and smooth: byte-identical to the control. No age reset and no cut in any static frame. A jittered lit test scene also had no reset.
- Plate cuts on the dimming frame 12.96%, then 0.24% per frame over frames 61-239. Screen age resets only on the event frame, 0.29%.
- Switch-off and motion respond as the control. Motion is byte-identical: uncovered pixels have no history, the known limit.
- Cost: the halo is grainier for about 40 frames after the change (crop temporal variation +6.3% over frames 61-100, -6.6% over 140-239).

With the mode off, the output is bit-identical to the control: cross-build GPU readbacks of the accumulator and of a full tracer scene match with the setting unset, empty and off, and all 68 control shader modules are byte-identical. The control captures equal the sealed paired-ray and drift controls in all 7 cases. Tests: 133 focused plus 65 nested pass in Checked and Release, full Rendering 2,558 Checked and 2,557 Release with 0 failures and the 10 existing skips. Opt-in, uncommitted and unmerged. Evidence and report: /private/tmp/tertius-light-age-history-20261107/report.md.

**2026-11-07T20:43:36Z**

**2026-11-07: user idea, a starting age for uncovered surfaces**

The user asked for a chosen starting age A for newly uncovered surfaces, between today's "no evidence" and "changed just now". After a cut to A + k frames, the stale share falls as A / (A + k + 1), so A must be small (about 0 to 4) to help a subtle moving object. Grain grows only during camera motion, where rays hit freshly uncovered edges. User choice: measure how often that happens under FSR jitter and a moving camera, compute the trade-off for each A, then confirm the chosen A with a capture that has a subtle moving object. The measurement run (instrumentation only, no output change) is delegated. The confirmation waits for its report.

**2026-11-07T21:43:12Z**

**2026-11-07: uncovered-surface age measured (instrumentation only), no rule built**

The light-age record now counts frames on screen for a never-changed surface in its existing age channel: −(N + 1), N = 0 in the frame that first shows it. A negative value still gives no evidence, so the receiver rule is unchanged. TERTIUS_REFLECTION_HISTORY_AGREEMENT=light-age-diagnostic adds an RGBA32F diagnostic (tracer binding 22, signal binding 12) that only the capture tool and the tests read: the fewest frames seen among the never-changed surfaces that the specular sample's rays hit, and the length before the blend. The capture tool takes --upscaling fsr (FSR 3.1 at scale 1) and --camera walk (still 0-59, pan left 10 degrees/s 60-119, walk right 1 m/s 120-239).

Identity: the still captures without FSR are byte-identical to the sealed light-age run, control and light-age, including sample ages. The diagnostic renders byte-identically to plain light-age, also under motion. The light-age mode itself differs from the control under camera motion by one-ulp rounding of fractional lengths from frame 168 (rough only, no cut), in its unchanged temporal module.

Jitter (FSR, still): age resets 0%. First-shown surface pixels 0.0197% per frame, 676 edge pixels every fourth frame (0.078% in that frame). No receiver hit a first-shown surface.

FSR shortens histories before any new rule: the rough reflection crop's median accepted length falls from 90 to 13 frames, smooth from 1,152 to 239, and still-camera noise RMS rises from 5.53 to 11.93 (rough) and from 1.00 to 13.81 (smooth). About 85% of the mean per-frame length loss is in the spatial dispatch (box clamp or anti-lag). User, 2026-11-07: fix this before anything else that shortens histories.

Motion (measured): of 862,631 plate receivers per frame, 0.27% (rough) and 0.25% (smooth) hit a never-changed surface (the black sky leaves only the emitter). About 8 per frame hit a surface shown 32 or fewer frames ago and 1.5 one shown for the first time. Computed for A = 0, 1, 2, 4, 8, 16, 32: 43 to 32 (rough) and 33 to 27 (smooth) cut receivers per frame, 0.003% to 0.005% of plate receivers; predicted noise change on the receivers that see the emitter, from k / sqrt(M): rough +1.94% to +0.17%, smooth +4.12% to +0.35%. A = 4: +0.66% rough, +1.45% smooth, stale share 26.7% after 10 frames and 9.8% after 36.

Recommendation: fix the FSR shortening, measure again, then A = 4 for both roughnesses (the largest A below 10% stale share after 36 frames, the light age's dimming standard). This scene has one hit target. Opt-in, uncommitted and unmerged. Evidence and report: /private/tmp/tertius-uncovered-age-20261107/report.md.
