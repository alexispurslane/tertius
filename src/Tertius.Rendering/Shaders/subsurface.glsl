// The constants of the skin scatter's passes (docs/plans/skin-and-hair.md, "The two passes"). ShaderConstants.Subsurface
// sets the specialization constant for the renderer's life.
#ifndef TERTIUS_SUBSURFACE_GLSL
#define TERTIUS_SUBSURFACE_GLSL

// n, the taps on each side of the kernel's center, 1 to MaxScatterTapsPerSide (SeparableSubsurface.TapsPerSide).
layout(constant_id = 2) const int scatterTapsPerSide = 1;

// SeparableSubsurface.MaxTapsPerSide: the kernel buffer holds 2 · 16 + 1 taps.
const int MaxScatterTapsPerSide = 16;

#endif
