---
id: ter-3yy5
status: open
deps: []
links: []
created: 2026-10-27T13:14:42Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, hair]
---
# Hair under the screen-space light: a curved or smooth-shaded card's own points block again

Found by the review of ter-qsfz (2026-10-27). The own-card rule of option A takes a hair receiver's card as the plane at right angles to its stored geometric normal, which is the interpolated vertex normal (material.glsl, sampleMaterial). On a smooth-shaded card strip that bends, such as MPFB's afro01 cards or geometry.py's cards (smooth below 35 degrees), that normal leans up to half the bend off the triangle's plane, so points of the receiver's own triangle lie far off the tested plane and block its sectors again; the darkening returns and changes along each segment. Even with the triangle's exact plane, the points of the card's next segment across a bend lie off that plane and block, so option A as a plane rule covers only flat cards.

Waiting for the user. Options seen so far: (1) the face normal from derivatives of the position in the G-buffer pass as a hair pixel's geometric normal (fixes points of the same triangle only; adds a precision term that grows with the resolution); (2) flat-shaded hair cards in the export (the strand then jumps at each bend, so the highlights show the segments); (3) the design states that hair cards must be flat (MPFB's curved cards break it); (4) a test by angle, a cone around the receiver's plane, which also skips nearby cards that lie almost in the plane; (5) a card identity in the G-buffer, which needs a new channel or target. The red test (a smooth-shaded card of two segments, turned 40 degrees) is in the ter-qsfz fixer's report.

