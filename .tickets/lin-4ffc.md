---
id: lin-4ffc
status: open
deps: []
links: []
created: 2026-11-15T16:01:38Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The retained deviation's rounding test weighs the step with σr's weight, not the fast history's: at a length of 32 it drops real steps up to 2.1 % of the light

Found in the review of t8-69v7, 2026-11-15. accumulate_temporal.comp on the Design X line zeroes a sample's step when abs(step) * weight <= BlendRounding * before, where weight = 1 / min(historyLength, DeviationFrames) is σr's weight and before = luminance(fast). The step goes from the fast history F, whose blend has the weight 1 / min(historyLength, fastCapOf(cap)). The store's rounding stalls F, so the bound that it needs is abs(step) / fastLength <= BlendRounding * before. The two weights are equal only up to the fast cap's length, 4. At a length of 32 the test zeroes real steps up to 32 / 1536, about 2.1 % of F (main's form zeroes up to 0.26 %). For Gaussian noise with a deviation of 1 % to 2 % of the light, that drops most steps, and σr comes out about 12 % to 50 % low (the reviewer's estimate, not measured). The form predates t8-69v7, which halved its slack. The fix: compare abs(step) / fastLength with BlendRounding * before, and add a test that a long history's retained deviation of noise at 1 % of the light follows its samples' deviation.

