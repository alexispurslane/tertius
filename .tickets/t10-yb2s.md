---
id: t10-yb2s
status: open
deps: []
links: []
created: 2026-11-22T20:03:47Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# A window that the platform holds at a size the render scale cannot draw stays undrawn: a tiling window manager can refuse the snap

Since step 2 of ter-qt72 the swapchain draws only an output that UpscalingSetting.Supports, and VulkanRenderer.FitWindow asks the window for the snapped framebuffer (UpscalingSetting.SnapOutput) at creation and at each framebuffer resize. A platform that keeps the window at its own size defeats the request: a tiling window manager (sway, i3), a fullscreen mode at a fixed display size, or a fractional HiDPI scale that cannot reach the snapped framebuffer. Then the framebuffer stays at a size that the setting cannot draw, for example 1366 x 768 under the default frame-time target is drawable (greatest common divisor 2 gives 0.5 and 1), but 1919 x 1080 is not, and the swapchain keeps no swapchain, as for a minimized window, until the next resize. Nothing tells the player or the log why the window is black. Measured only on the development Mac at one framebuffer pixel per logical pixel, where the snap held for every request (ter-qt72 step 2 probe). The user's decisions of 2026-11-05 (ter-fkd3) rule out a letterbox, a crop, an approximate ratio and a scale outside the limits, and ask to report a genuine conflict. Open: how to report it (a renderer status, a log line, an exception from the resize) and whether such a platform size should draw at the nearest exact-ratio size inside the limits of a smaller centred output, which would be a letterbox. Linux runtime is deferred under ter-cgif.

