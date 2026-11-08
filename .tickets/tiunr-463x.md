---
id: tiunr-463x
status: in_progress
deps: [tiunr-k2wn, tiunr-d3la]
links: []
created: 2026-11-07T00:56:38Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-lmmx
---
# Design B: guide existing reflection reconstruction with temporal variance

Starting from the exact integrated H13 commit, add per-pixel temporal first/second moments of the accepted raw sample stream to the existing temporal owner. Use measured temporal fluctuation to guide reconstruction; do not add a parallel radiance accumulator or rays. H07 spatial residuals are not this statistic. Return actual frames, separate means and noise/error observables. No automatic promotion.


## Notes

**2026-11-07T01:04:05Z**

Ready for isolated T3 worktree launch from the same H13 integration commit as A. Design adds measured temporal-fluctuation statistics to the existing temporal owner, not another radiance accumulator. Fresh execution bounds/setup in external AB-BRIEF.md. Ray generation stays fixed. No workflows or extra agents.

**2026-11-07T01:26:41Z**

Parent inspected B failure and verified artifact seal plus unchanged old-manifest receipts. red22=16pass/6assertionfail; attemptedgreen147=83pass/64fail, all64 include image-format mismatch. Candidate invalid, not denoising evidence. Rendering3/3,tool1/3,test2/4;captures0/2,metrics0/1. Red-runtime identity/probe-consumption gaps remain disclosed. RELEASE confirmed. Follow-up bug tiunr-d3la records disabled32-bit/16-bit bindings; no retry, heavy command, new child, integration or acceptance started. Receipt C/parent-inspections/B-h13-temporal-variance-v1/inspection.json. H13 code unchanged.

**2026-11-07T02:21:43Z**

B repair remains invalid, not an algorithmic quality result. Parent seal/XML verification confirms109/148 focused passes and39 GPU resource-validation failures. Format mismatch no longer appears; actual probe's temporal-variance behavior passes. Zero native captures,frames,metrics or B images. Follow-up tiunr-pzhn; H13 unchanged. No new allocation, child, integration or closure.

### November 6, 2026: measured B result after resource repair

B now passes 148/148 focused checks and both native captures without Vulkan validation messages. Parent verified the loaded engine, actual probe consumption, sealed files, links, runtime/report inputs and compressed tables. C/B-h13-resource-repair-v3 is the successful evidence root. Earlier failures remain immutable. No parent heavy command or candidate integration occurred.

Against H13, rough temporal noise RMS changes from 21.143515 to 20.494741 W/(m² sr), a 3.0684% reduction. Persistent 32-frame mean-reference error RMS changes from 30.199067 to 30.756077 W/(m² sr), a 1.8445% increase. The column-mean 5–95% width within the crop changes from 25.243299 to 25.608837 native pixels. Reference width is 24.471147 pixels. Missing/reference changes from 4.375675% to 4.196462%; extra/reference changes from 8.746474% to 9.211141%. Smooth plate arrays are byte-identical across all 32 frames. Raw/emitter arrays have separate identities.

The visible rough clumps remain. Parent display comparison finds 4,582 changed pixels in draw95 and 5,530 in the mean, out of 12,800 each. Neither smooth display image changes. This is a modest noise/accuracy tradeoff, not an accuracy-preserving improvement. H13 remains the baseline. Static correlated frames, the crop-limited width and live-feedback versus one-bounce-reference differences still limit interpretation. Parent receipt and labeled sheets: C/parent-inspections/B-h13-resource-repair-v3. No quality acceptance, integration or closure.
