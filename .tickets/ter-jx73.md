---
id: ter-jx73
status: open
deps: [ter-ai0t]
links: []
created: 2026-10-11T21:41:22Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [host, input]
---
# Measure and cut the delay of mouse look in the person's view

In free play, the mouse turns the person's view through the simulation. The window posts an InputEvent with World.Post. The post joins the routing at the end of the next tick, PersonControl handles it in the tick after that and writes the view offset, and the renderer then blends the camera between the last two ticks. So a turn shows two to three ticks late, 33 to 50 ms at 60 ticks a second, plus the blend. The F1 fly camera turns on the render thread instead, with no delay.

Measure how late a turn shows in the window, and decide whether it needs a cut. One way: the render thread applies the mouse movements that the game has not seen yet on top of the person's view offset, as the fly camera does, and drops them when the snapshot catches up. A script must still render the same images, so it changes nothing during a script.

## Acceptance Criteria

The delay of a mouse turn in the person's view is measured and written down. If it is cut, a scripted run renders the same images as before.

