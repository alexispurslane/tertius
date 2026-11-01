---
id: ter-xkex
status: closed
deps: [ter-r5hb]
links: []
created: 2026-11-01T01:19:16Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering, reflections]
---
# Reflections: dark streaks on the cabin's lower outer wall on demo-exit's deck

Found by the orchestrator on 2026-10-31, in the same deck-late capture as the screen-space light band (script captures/deck-fringe-2026-10-31/late.json, main cae67c5e, MaxSteps 128). Under the window sills of the cabin's long outer wall, thin dark streaks run down and to the right across the lower wall (captures/deck-fringe-2026-10-31/fringe-3x.png, top half). With --reflections off they are gone (captures/deck-fringe-2026-10-31/ab-refl-3x.png). With --screen-space-light off they stay (captures/deck-fringe-2026-10-31/ab-ssl-3x.png).

ter-r5hb raises the default MaxSteps from 128 to 256, and its builder saw the deck's reflections move toward the 1024-step reference. So check again after ter-r5hb merges: render deck-late at the new default before any other work. If the streaks are gone, close this ticket with the image.

## Acceptance Criteria

After ter-r5hb merges, deck-late at the default reflections shows no streaks on the cabin's lower wall, or the ticket names their cause and a fix.

## Notes

**2026-11-01T01:19:31Z**

The evidence (the script, the full deck-late.png and the 3x crops) is in captures/deck-fringe-2026-10-31/, which git ignores, so it lives only on this machine. The script re-creates every image: dotnet run -c Release --project src/Tertius.Demo -- --script captures/deck-fringe-2026-10-31/late.json --headless --out <folder>, plus one switch for the A/B runs.

**2026-11-01T01:45:25Z**

Closed by ter-r5hb (orchestrator, 2026-10-31). The same script on main ea5d806c, with the default reflections at 256 steps, shows no streaks on the cabin's lower wall: captures/deck-fringe-2026-10-31/deck-late-256-steps-3x.png against fringe-3x.png (128 steps). So the streaks were rays that ran out of steps before they reached the sill. The floor band of ter-k96l is unchanged in the same image.
