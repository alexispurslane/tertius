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

## Notes

**2026-11-12T21:52:41Z**

Kind (b), a real Linux fault. The card names macOS fonts (Avenir Next, Menlo); Linux falls back to a wider font, and WrapStyle 2 (no wrapping) lets a long credit run past the margin. Fix: Card() starts the event with {\q0}, smart wrapping for the card only. The test TheClosingCardShowsTheCreditOfTheRecordingInsideItsMargins became a theory with a doubled credit, which was red on the Mac first (Brightest(0, 40) = 120). All 21 EditCommandTests pass on the Mac.
