---
id: ter-welh
status: open
deps: []
links: []
created: 2026-10-26T17:23:43Z
type: chore
priority: 4
assignee: Alexis Purslane
parent: ter-kge6
---
# Move the Smart UV script into the model library

content/tertius.demo/assets/ships/nostromo.model/s75_uv.py, characters/astronaut.model/s75_uv.py and characters/crew.model/s75_uv.py are copies: Smart UV Project and island packing for the bake objects, and the texel density report. Move the function into tertius_blender (for example geometry or a new uv module), and leave each model a one-line script.

