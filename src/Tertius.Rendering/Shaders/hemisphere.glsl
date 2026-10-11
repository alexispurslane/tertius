// The hemisphere ambient light's up, shared by the G-buffer pass, which stores the sky fraction, and by the passes
// that shade a surface themselves.
#ifndef TERTIUS_HEMISPHERE_GLSL
#define TERTIUS_HEMISPHERE_GLSL

// How much of the hemisphere ambient light a surface at `position` with `normal` gets from the sky: 1 facing straight
// up, 0 straight down, 0.5 sideways. `hemisphere` is the instance's (frame.glsl): up along xyz for w 1, away from the
// point xyz for w 2, and nowhere for w 0, which gives 0.5 everywhere.
float skyFraction(vec4 hemisphere, vec3 position, vec3 normal)
{
    if (hemisphere.w < 0.5)
        return 0.5;
    vec3 away = position - hemisphere.xyz;
    vec3 up = hemisphere.w < 1.5 ? hemisphere.xyz : away * inversesqrt(max(dot(away, away), 1e-20));
    return clamp(dot(normal, up) * 0.5 + 0.5, 0.0, 1.0);
}

#endif
