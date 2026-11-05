#version 460
#extension GL_GOOGLE_include_directive : require
#extension GL_EXT_samplerless_texture_functions : require
#extension GL_EXT_shader_image_load_formatted : require

#define FSR3UPSCALER_BIND_SRV_INPUT_EXPOSURE 0
#define FSR3UPSCALER_BIND_SRV_RCAS_INPUT 1
#define FSR3UPSCALER_BIND_UAV_UPSCALED_OUTPUT 2
#define FSR3UPSCALER_BIND_CB_FSR3UPSCALER 3
#define FSR3UPSCALER_BIND_CB_RCAS 4

#define LoadRCAS_Input AmdLoadRCAS_Input
#define StoreUpscaledOutput AmdStoreUpscaledOutput
#include "fsr3upscaler/ffx_fsr3upscaler_callbacks_glsl.h"
#undef LoadRCAS_Input
#undef StoreUpscaledOutput

FfxFloat32x4 LoadRCAS_Input(FfxInt32x2 pixel)
{
    return texelFetch(r_rcas_input, clamp(pixel, FfxInt32x2(0), UpscaleSize() - 1), 0);
}

void StoreUpscaledOutput(FfxInt32x2 pixel, FfxFloat32x3 color)
{
    if (all(lessThan(pixel, UpscaleSize())))
        imageStore(rw_upscaled_output, pixel, FfxFloat32x4(color, 1.0));
}

#include "fsr3upscaler/ffx_fsr3upscaler_common.h"
#include "fsr3upscaler/ffx_fsr3upscaler_rcas.h"

layout(local_size_x = 64) in;

void main()
{
    RCAS(gl_LocalInvocationID.xyz, gl_WorkGroupID.xyz, gl_GlobalInvocationID.xyz);
}
