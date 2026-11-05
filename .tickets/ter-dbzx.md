---
id: ter-dbzx
status: open
deps: []
links: []
created: 2026-11-04T22:34:09Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# The accumulator's finiteSample relies on isnan and isinf, which a driver may fold to false without SignedZeroInfNanPreserve

temporal.glsl's finiteSample (!isnan && !isinf), which accumulate_temporal.comp uses to drop a sample that is not finite, works on MoltenVK. ARCHITECTURE.md, 'Portable Vulkan', says that without SignedZeroInfNanPreserve a driver may assume that no float is a NaN or an infinity, so the test can fold to true and a NaN sample can enter a history for good. Either prove that no writer of a sample image can produce a NaN or an infinity, and drop the test, or test the bits (floatBitsToUint and the exponent mask), which no float assumption folds. Found during ter-gayt's doc update.

