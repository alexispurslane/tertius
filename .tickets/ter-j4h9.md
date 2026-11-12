---
id: ter-j4h9
status: open
deps: []
links: [t8-pkag]
created: 2026-11-12T02:50:00Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [linux]
---
# The edit command's closing card draws text into its left margin on Linux

`EditCommandTests.TheClosingCardShowsTheCreditOfTheRecordingInsideItsMargins` fails on the RTX 2000 Ada pod of ter-libf (Ubuntu 22.04 container, Nix ffmpeg-headless, 2026-11-12) in both a parallel and a 2-thread run: `Brightest(0, 40)` is 152 where the card keeps the 40 columns free (less than 16). The card's text is wider on Linux, likely another font or a fallback font. Pin the font file in the dev shell, or measure the text and fit it inside the margins.
