#ifndef TERTIUS_SPECULAR_HISTORY_GLSL
#define TERTIUS_SPECULAR_HISTORY_GLSL

#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
#define SPECULAR_HISTORY_FORMAT rgba32f
// Every precise history keeps the light-change age (ticket tiunr-7haj). The diffuse signal's spatial dispatch beside it
// keeps its half-precision history and takes the light age alone: its light-age variant (ticket t7-3e22,
// ShaderVariant.LightAge).
#define TERTIUS_LIGHT_AGE
#else
#define SPECULAR_HISTORY_FORMAT rgba16f
#endif

#endif
