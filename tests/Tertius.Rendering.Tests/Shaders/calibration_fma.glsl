// The FMA probe of GpuCalibrationTests (docs/research/temporal-renderer/gpu-calibration.md, "The probes"): 16
// independent chains per invocation, four vec4, each step one FMA per chain, x = x * a + b, for the push constant's
// count of steps. a and b come from push constants, so the compiler cannot fold the chains. The native twin is
// probe_fma in tools/gpu-calibration/probes.swift.

layout(local_size_x = 256) in;

layout(set = 0, binding = 0, std430) writeonly buffer Results
{
    float results[];
};

layout(push_constant) uniform Constants
{
    uint iterations;
    float a;
    float b;
}
constants;

#define FMA_STEP                                                                                                       \
    x0 = fma(x0, a, b);                                                                                                \
    x1 = fma(x1, a, b);                                                                                                \
    x2 = fma(x2, a, b);                                                                                                \
    x3 = fma(x3, a, b);

#define FMA_START                                                                                                      \
    uint id = gl_GlobalInvocationID.x;                                                                                 \
    vec4 a = vec4(constants.a);                                                                                        \
    vec4 b = vec4(constants.b);                                                                                        \
    float start = 1.0 + float(id & 1023u) * (1.0 / 1024.0);                                                            \
    vec4 x0 = start + vec4(0.0, 0.125, 0.25, 0.375);                                                                   \
    vec4 x1 = x0 + 0.5;                                                                                                \
    vec4 x2 = x0 + 1.0;                                                                                                \
    vec4 x3 = x0 + 1.5;

#define FMA_END                                                                                                        \
    vec4 sum = x0 + x1 + x2 + x3;                                                                                      \
    results[id] = sum.x + sum.y + sum.z + sum.w;
