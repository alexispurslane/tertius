// The thickness of what is solid behind a stored surface. The screen tracer's walk and the
// environment map's near entry read it, so that both see one thickness (docs/plans/environment-map-distances.md,
// decision 20). CellBounds.ThicknessShare is the C# copy. Change both together.
#ifndef TERTIUS_THICKNESS_GLSL
#define TERTIUS_THICKNESS_GLSL

// κ: a surface at view depth z fills the depths from z to z (1 + κ) behind it, along its view ray, and a surface at
// distance D from the eye fills D to D (1 + κ) along the eye's ray.
const float ThicknessShare = 0.1;

#endif
