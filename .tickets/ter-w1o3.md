---
id: ter-w1o3
status: in_progress
deps: []
links: []
created: 2026-11-12T06:45:29Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Reflection rays stop after 128 steps instead of 256: 0.55 ms on the deck

Sweep proposal (/private/tmp/tertius-sweep-20261111/addendum.md, section A10). WAITS FOR THE USER: it changes decision 41, which is the user's. The change is one number: ScreenTrace.Default gets ReflectionSteps: 128 (today 256); decision 41, the design document and ARCHITECTURE.md follow. Measured on the deck in take 5 between two baselines: frame span -0.55 ms, reflection rays 2.69 -> 2.14 ms. Image: in 57 captures of 14 more scripts the mean changes by at most 0.03 %; the deck falls by 0.04 % (16.7 % of pixels change, nearly all by one gray level of 255, 0.19 % by more). Mirror balls unchanged at 256/128/96/64; the doorway scene breaks between 96 and 64 (a cap of 64 brightens the whole floor), so 128 is proposed, not 96 (0.85 ms). Why it helps: a GPU group of 32 lanes runs until its longest walk ends. The bounce-light cap stays at 62 (a cap of 32 darkens the deck by 5 %). Builder must close: (1) captures at render sizes above 1280 x 720, which need more steps for the same ray; (2) one film with a moving camera at both caps; (3) a Linux timing on the cloud workbench.

