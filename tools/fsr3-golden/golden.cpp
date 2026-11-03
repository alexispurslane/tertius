// Tool code (tools/fsr3-golden/README.md): prints the golden values of FSR 3.1.4's host calculations as JSON. It links
// AMD's own host code, ffx_fsr3upscaler.cpp of FidelityFX SDK v1.1.4, and gives it a recording backend: a set of the
// backend callbacks that keeps every resource description, staged constant buffer and scheduled job instead of touching a
// GPU. So each value below comes from AMD's code, never from the engine's C# host that the tests check against it.
//
// The engine's choices (docs/plans/temporal-renderer.md, section 5) are the inputs: HDR color, reversed infinite depth
// (cameraNear = FLT_MAX, cameraFar = the near plane, as AMD's sample passes it), motion vectors at the render size with
// the render size as their scale, no jitter cancellation, no automatic exposure, no sharpening, 1 m per view unit, and a
// device whose wave lanes are 32 wide, so that AMD's host takes the reference Lanczos and 32-bit math.

#include <cfloat>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#include <FidelityFX/host/ffx_fsr3upscaler.h>
// The private layout of the constants, to print its offsets.
#include "ffx_fsr3upscaler_private.h"

namespace
{

constexpr uint32_t ContextFlags = FFX_FSR3UPSCALER_ENABLE_HIGH_DYNAMIC_RANGE | FFX_FSR3UPSCALER_ENABLE_DEPTH_INVERTED |
                                  FFX_FSR3UPSCALER_ENABLE_DEPTH_INFINITE;

struct Resource
{
    std::string name;
    FfxResourceDescription description;
};

struct Job
{
    std::string kind;
    std::string label;
    std::string target;
    float color[4];
    uint32_t dimensions[3];
};

// What AMD's code handed to the backend since the last reset of the recorder.
struct Recorder
{
    std::vector<Resource> resources;
    std::vector<Job> jobs;
    std::vector<std::vector<uint32_t>> staged;
    std::vector<uint32_t> stagedSizes;
} recorder;

// Storage for the staged constant buffers, which AMD's code points its jobs at.
uint32_t stagingWords[4096];
size_t stagingUsed = 0;

std::string Narrow(const wchar_t* text)
{
    std::string result;
    for (; text != nullptr && *text != L'\0'; ++text)
        result.push_back(static_cast<char>(*text));
    return result;
}

std::string ResourceName(FfxResourceInternal resource)
{
    if (resource.internalIndex < 0 || static_cast<size_t>(resource.internalIndex) >= recorder.resources.size())
        return "null";
    return recorder.resources[resource.internalIndex].name;
}

FfxVersionNumber GetSdkVersion(FfxInterface*)
{
    return FFX_SDK_MAKE_VERSION(1, 1, 4);
}

FfxErrorCode GetEffectGpuMemoryUsage(FfxInterface*, FfxUInt32, FfxEffectMemoryUsage*)
{
    return FFX_OK;
}

FfxErrorCode CreateBackendContext(FfxInterface*, FfxEffect, FfxEffectBindlessConfig*, FfxUInt32* effectContextId)
{
    *effectContextId = 0;
    return FFX_OK;
}

FfxErrorCode GetDeviceCapabilities(FfxInterface*, FfxDeviceCapabilities* capabilities)
{
    *capabilities = {};
    capabilities->maximumSupportedShaderModel = FFX_SHADER_MODEL_6_5;
    capabilities->waveLaneCountMin = 32;
    capabilities->waveLaneCountMax = 32;
    capabilities->fp16Supported = false;
    return FFX_OK;
}

FfxErrorCode DestroyBackendContext(FfxInterface*, FfxUInt32)
{
    return FFX_OK;
}

FfxErrorCode CreateResource(FfxInterface*, const FfxCreateResourceDescription* description, FfxUInt32,
                            FfxResourceInternal* resource)
{
    resource->internalIndex = static_cast<int32_t>(recorder.resources.size());
    recorder.resources.push_back({Narrow(description->name), description->resourceDescription});
    return FFX_OK;
}

FfxErrorCode RegisterResource(FfxInterface*, const FfxResource* external, FfxUInt32, FfxResourceInternal* resource)
{
    resource->internalIndex = static_cast<int32_t>(recorder.resources.size());
    recorder.resources.push_back({Narrow(external->name), external->description});
    return FFX_OK;
}

FfxResource GetResource(FfxInterface*, FfxResourceInternal)
{
    return {};
}

FfxErrorCode UnregisterResources(FfxInterface*, FfxCommandList, FfxUInt32)
{
    return FFX_OK;
}

FfxResourceDescription GetResourceDescription(FfxInterface*, FfxResourceInternal resource)
{
    return recorder.resources[resource.internalIndex].description;
}

FfxErrorCode DestroyResource(FfxInterface*, FfxResourceInternal, FfxUInt32)
{
    return FFX_OK;
}

FfxErrorCode StageConstantBufferData(FfxInterface*, void* data, FfxUInt32 size, FfxConstantBuffer* constantBuffer)
{
    uint32_t words = (size + 3) / 4;
    uint32_t* destination = stagingWords + stagingUsed;
    stagingUsed += words;
    std::memset(destination, 0, words * 4);
    std::memcpy(destination, data, size);
    constantBuffer->data = destination;
    constantBuffer->num32BitEntries = words;
    recorder.staged.emplace_back(destination, destination + words);
    recorder.stagedSizes.push_back(size);
    return FFX_OK;
}

FfxErrorCode CreatePipeline(FfxInterface*, FfxEffect, FfxPass pass, uint32_t, const FfxPipelineDescription* description,
                            FfxUInt32, FfxPipelineState* pipeline)
{
    *pipeline = {};
    pipeline->passId = pass;
    wcscpy_s(pipeline->name, description->name);
    return FFX_OK;
}

FfxErrorCode DestroyPipeline(FfxInterface*, FfxPipelineState*, FfxUInt32)
{
    return FFX_OK;
}

FfxErrorCode ScheduleGpuJob(FfxInterface*, const FfxGpuJobDescription* description)
{
    Job job = {};
    job.label = Narrow(description->jobLabel);
    switch (description->jobType)
    {
    case FFX_GPU_JOB_CLEAR_FLOAT:
        job.kind = "clear";
        job.target = ResourceName(description->clearJobDescriptor.target);
        std::memcpy(job.color, description->clearJobDescriptor.color, sizeof(job.color));
        break;
    case FFX_GPU_JOB_COMPUTE:
        job.kind = "dispatch";
        job.label = Narrow(description->computeJobDescriptor.pipeline.name);
        std::memcpy(job.dimensions, description->computeJobDescriptor.dimensions, sizeof(job.dimensions));
        break;
    case FFX_GPU_JOB_DISCARD:
        job.kind = "discard";
        job.target = ResourceName(description->discardJobDescriptor.target);
        break;
    default:
        job.kind = "other";
        break;
    }
    recorder.jobs.push_back(job);
    return FFX_OK;
}

FfxErrorCode ExecuteGpuJobs(FfxInterface*, FfxCommandList, FfxUInt32)
{
    return FFX_OK;
}

FfxInterface RecordingBackend()
{
    FfxInterface backend = {};
    backend.fpGetSDKVersion = GetSdkVersion;
    backend.fpGetEffectGpuMemoryUsage = GetEffectGpuMemoryUsage;
    backend.fpCreateBackendContext = CreateBackendContext;
    backend.fpGetDeviceCapabilities = GetDeviceCapabilities;
    backend.fpDestroyBackendContext = DestroyBackendContext;
    backend.fpCreateResource = CreateResource;
    backend.fpRegisterResource = RegisterResource;
    backend.fpGetResource = GetResource;
    backend.fpUnregisterResources = UnregisterResources;
    backend.fpGetResourceDescription = GetResourceDescription;
    backend.fpDestroyResource = DestroyResource;
    backend.fpStageConstantBufferDataFunc = StageConstantBufferData;
    backend.fpCreatePipeline = CreatePipeline;
    backend.fpDestroyPipeline = DestroyPipeline;
    backend.fpScheduleGpuJob = ScheduleGpuJob;
    backend.fpExecuteGpuJobs = ExecuteGpuJobs;
    backend.device = reinterpret_cast<FfxDevice>(&recorder);
    return backend;
}

void ResetRecorder()
{
    recorder = {};
    stagingUsed = 0;
}

// A JSON writer that keeps commas right.
struct Json
{
    bool first = true;

    void Separate()
    {
        if (!first)
            std::printf(",");
        first = false;
    }
    void Key(const char* key)
    {
        Separate();
        std::printf("\"%s\":", key);
        first = true;
    }
    void Open(char bracket)
    {
        Separate();
        std::printf("%c", bracket);
        first = true;
    }
    void Close(char bracket)
    {
        std::printf("%c", bracket);
        first = false;
    }
    // A float as the shortest decimal that reads back to the same 32-bit value.
    void Float(float value)
    {
        Separate();
        std::printf("%.9g", value);
    }
    void Int(long long value)
    {
        Separate();
        std::printf("%lld", value);
    }
    void Bool(bool value)
    {
        Separate();
        std::printf(value ? "true" : "false");
    }
    void String(const std::string& value)
    {
        Separate();
        std::printf("\"%s\"", value.c_str());
    }
} json;

void FloatField(const char* key, float value)
{
    json.Key(key);
    json.Float(value);
}

void IntField(const char* key, long long value)
{
    json.Key(key);
    json.Int(value);
}

void PrintJitter()
{
    json.Key("phaseCounts");
    json.Open('[');
    const int32_t pairs[][2] = {{1920, 1920}, {1280, 1920}, {1440, 1920}, {960, 1920}, {1000, 1920}, {1288, 1920},
                                {1536, 1920}, {640, 1920},  {1280, 1280}, {1279, 1920}, {8, 1920},    {1920, 2560}};
    for (const auto& pair : pairs)
    {
        json.Open('{');
        IntField("renderWidth", pair[0]);
        IntField("outputWidth", pair[1]);
        IntField("phaseCount", ffxFsr3UpscalerGetJitterPhaseCount(pair[0], pair[1]));
        json.Close('}');
    }
    json.Close(']');

    json.Key("offsets");
    json.Open('[');
    const int32_t phaseCounts[] = {1, 8, 18, 32, 72};
    for (int32_t phaseCount : phaseCounts)
    {
        for (int32_t index : {0, 1, 2, 3, 7, 8, 17, 18, 31, 32, 71, 72, 1000, 123457})
        {
            float x = 0, y = 0;
            ffxFsr3UpscalerGetJitterOffset(&x, &y, index, phaseCount);
            json.Open('{');
            IntField("index", index);
            IntField("phaseCount", phaseCount);
            FloatField("x", x);
            FloatField("y", y);
            json.Close('}');
        }
    }
    json.Close(']');
}

const char* FormatName(FfxSurfaceFormat format)
{
    switch (format)
    {
    case FFX_SURFACE_FORMAT_R8_UNORM:
        return "R8_UNORM";
    case FFX_SURFACE_FORMAT_R16_FLOAT:
        return "R16_FLOAT";
    case FFX_SURFACE_FORMAT_R16G16_FLOAT:
        return "R16G16_FLOAT";
    case FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT:
        return "R16G16B16A16_FLOAT";
    case FFX_SURFACE_FORMAT_R32_UINT:
        return "R32_UINT";
    case FFX_SURFACE_FORMAT_R32_FLOAT:
        return "R32_FLOAT";
    case FFX_SURFACE_FORMAT_R8G8B8A8_UNORM:
        return "R8G8B8A8_UNORM";
    case FFX_SURFACE_FORMAT_R16_SNORM:
        return "R16_SNORM";
    case FFX_SURFACE_FORMAT_R32G32_FLOAT:
        return "R32G32_FLOAT";
    case FFX_SURFACE_FORMAT_R32G32B32A32_FLOAT:
        return "R32G32B32A32_FLOAT";
    default:
        return "other";
    }
}

void PrintResources(const std::vector<Resource>& resources)
{
    json.Open('[');
    for (const Resource& resource : resources)
    {
        json.Open('{');
        json.Key("name");
        json.String(resource.name);
        json.Key("format");
        json.String(FormatName(resource.description.format));
        IntField("width", resource.description.width);
        IntField("height", resource.description.height);
        IntField("mipCount", resource.description.mipCount);
        json.Close('}');
    }
    json.Close(']');
}

FfxResource External(const wchar_t* name, uint32_t width, uint32_t height, FfxSurfaceFormat format)
{
    FfxResource resource = {};
    resource.resource = &recorder;
    resource.description.type = FFX_RESOURCE_TYPE_TEXTURE2D;
    resource.description.format = format;
    resource.description.width = width;
    resource.description.height = height;
    resource.description.depth = 1;
    resource.description.mipCount = 1;
    wcscpy_s(resource.name, name);
    return resource;
}

void PrintConstants(const std::vector<uint32_t>& words)
{
    Fsr3UpscalerConstants constants;
    std::memcpy(&constants, words.data(), sizeof(constants));
    json.Open('{');
    IntField("renderSizeX", constants.renderSize[0]);
    IntField("renderSizeY", constants.renderSize[1]);
    IntField("previousFrameRenderSizeX", constants.previousFrameRenderSize[0]);
    IntField("previousFrameRenderSizeY", constants.previousFrameRenderSize[1]);
    IntField("upscaleSizeX", constants.upscaleSize[0]);
    IntField("upscaleSizeY", constants.upscaleSize[1]);
    IntField("previousFrameUpscaleSizeX", constants.previousFrameUpscaleSize[0]);
    IntField("previousFrameUpscaleSizeY", constants.previousFrameUpscaleSize[1]);
    IntField("maxRenderSizeX", constants.maxRenderSize[0]);
    IntField("maxRenderSizeY", constants.maxRenderSize[1]);
    IntField("maxUpscaleSizeX", constants.maxUpscaleSize[0]);
    IntField("maxUpscaleSizeY", constants.maxUpscaleSize[1]);
    FloatField("deviceToViewDepth0", constants.deviceToViewDepth[0]);
    FloatField("deviceToViewDepth1", constants.deviceToViewDepth[1]);
    FloatField("deviceToViewDepth2", constants.deviceToViewDepth[2]);
    FloatField("deviceToViewDepth3", constants.deviceToViewDepth[3]);
    FloatField("jitterOffsetX", constants.jitterOffset[0]);
    FloatField("jitterOffsetY", constants.jitterOffset[1]);
    FloatField("previousFrameJitterOffsetX", constants.previousFrameJitterOffset[0]);
    FloatField("previousFrameJitterOffsetY", constants.previousFrameJitterOffset[1]);
    FloatField("motionVectorScaleX", constants.motionVectorScale[0]);
    FloatField("motionVectorScaleY", constants.motionVectorScale[1]);
    FloatField("downscaleFactorX", constants.downscaleFactor[0]);
    FloatField("downscaleFactorY", constants.downscaleFactor[1]);
    FloatField("motionVectorJitterCancellationX", constants.motionVectorJitterCancellation[0]);
    FloatField("motionVectorJitterCancellationY", constants.motionVectorJitterCancellation[1]);
    FloatField("tanHalfFOV", constants.tanHalfFOV);
    FloatField("jitterPhaseCount", constants.jitterPhaseCount);
    FloatField("deltaTime", constants.deltaTime);
    FloatField("deltaPreExposure", constants.deltaPreExposure);
    FloatField("viewSpaceToMetersFactor", constants.viewSpaceToMetersFactor);
    FloatField("frameIndex", constants.frameIndex);
    FloatField("velocityFactor", constants.velocityFactor);
    FloatField("reactivenessScale", constants.reactivenessScale);
    FloatField("shadingChangeScale", constants.shadingChangeScale);
    FloatField("accumulationAddedPerFrame", constants.accumulationAddedPerFrame);
    FloatField("minDisocclusionAccumulation", constants.minDisocclusionAccumulation);
    json.Close('}');
}

void PrintWords(const std::vector<uint32_t>& words, uint32_t bytes)
{
    json.Open('[');
    for (uint32_t word = 0; word < bytes / 4; ++word)
        json.Int(words[word]);
    json.Close(']');
}

struct Frame
{
    uint32_t renderWidth;
    uint32_t renderHeight;
    float jitterX;
    float jitterY;
    float frameMilliseconds;
    float preExposure;
    float verticalFov;
    bool reset;
};

struct Sequence
{
    const char* name;
    uint32_t maxRenderWidth;
    uint32_t maxRenderHeight;
    uint32_t outputWidth;
    uint32_t outputHeight;
    float nearMeters;
    std::vector<Frame> frames;
};

void PrintSequence(const Sequence& sequence)
{
    ResetRecorder();
    FfxFsr3UpscalerContextDescription description = {};
    description.flags = ContextFlags;
    description.maxRenderSize = {sequence.maxRenderWidth, sequence.maxRenderHeight};
    description.maxUpscaleSize = {sequence.outputWidth, sequence.outputHeight};
    description.backendInterface = RecordingBackend();
    static FfxFsr3UpscalerContext context;
    if (ffxFsr3UpscalerContextCreate(&context, &description) != FFX_OK)
    {
        std::fprintf(stderr, "ffxFsr3UpscalerContextCreate failed\n");
        std::exit(1);
    }

    json.Open('{');
    json.Key("name");
    json.String(sequence.name);
    IntField("maxRenderWidth", sequence.maxRenderWidth);
    IntField("maxRenderHeight", sequence.maxRenderHeight);
    IntField("outputWidth", sequence.outputWidth);
    IntField("outputHeight", sequence.outputHeight);
    FloatField("nearMeters", sequence.nearMeters);
    json.Key("resources");
    PrintResources(recorder.resources);

    FfxFsr3UpscalerSharedResourceDescriptions shared = {};
    ffxFsr3UpscalerGetSharedResourceDescriptions(&context, &shared);
    json.Key("sharedResources");
    PrintResources({{Narrow(shared.dilatedDepth.name), shared.dilatedDepth.resourceDescription},
                    {Narrow(shared.dilatedMotionVectors.name), shared.dilatedMotionVectors.resourceDescription},
                    {Narrow(shared.reconstructedPrevNearestDepth.name),
                     shared.reconstructedPrevNearestDepth.resourceDescription}});

    json.Key("frames");
    json.Open('[');
    for (const Frame& frame : sequence.frames)
    {
        recorder.jobs.clear();
        recorder.staged.clear();
        recorder.stagedSizes.clear();
        stagingUsed = 0;
        // The external images live at the largest render size, as the engine's do (docs/plans/temporal-renderer.md,
        // "The images").
        FfxFsr3UpscalerGenerateReactiveDescription reactive = {};
        reactive.commandList = reinterpret_cast<FfxCommandList>(&recorder);
        reactive.colorOpaqueOnly = External(L"OpaqueOnly", sequence.maxRenderWidth, sequence.maxRenderHeight,
                                            FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT);
        reactive.colorPreUpscale =
            External(L"Color", sequence.maxRenderWidth, sequence.maxRenderHeight, FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT);
        reactive.outReactive =
            External(L"Reactive", sequence.maxRenderWidth, sequence.maxRenderHeight, FFX_SURFACE_FORMAT_R8_UNORM);
        reactive.renderSize = {frame.renderWidth, frame.renderHeight};
        // The values of AMD's sample (samples/fsrapi/fsrapirendermodule.cpp).
        reactive.scale = 1.0f;
        reactive.cutoffThreshold = 0.2f;
        reactive.binaryValue = 0.9f;
        reactive.flags = FFX_FSR3UPSCALER_AUTOREACTIVEFLAGS_APPLY_TONEMAP | FFX_FSR3UPSCALER_AUTOREACTIVEFLAGS_APPLY_THRESHOLD |
                         FFX_FSR3UPSCALER_AUTOREACTIVEFLAGS_USE_COMPONENTS_MAX;
        ffxFsr3UpscalerContextGenerateReactiveMask(&context, &reactive);

        FfxFsr3UpscalerDispatchDescription dispatch = {};
        dispatch.commandList = reinterpret_cast<FfxCommandList>(&recorder);
        dispatch.color =
            External(L"Color", sequence.maxRenderWidth, sequence.maxRenderHeight, FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT);
        dispatch.depth =
            External(L"Depth", sequence.maxRenderWidth, sequence.maxRenderHeight, FFX_SURFACE_FORMAT_R32_FLOAT);
        dispatch.motionVectors = External(L"Motion", sequence.maxRenderWidth, sequence.maxRenderHeight,
                                          FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT);
        dispatch.reactive = reactive.outReactive;
        dispatch.dilatedDepth = External(L"FSR3UPSCALER_DilatedDepth", sequence.maxRenderWidth, sequence.maxRenderHeight,
                                         FFX_SURFACE_FORMAT_R32_FLOAT);
        dispatch.dilatedMotionVectors = External(L"FSR3UPSCALER_DilatedVelocity", sequence.maxRenderWidth,
                                                 sequence.maxRenderHeight, FFX_SURFACE_FORMAT_R16G16_FLOAT);
        dispatch.reconstructedPrevNearestDepth =
            External(L"FSR3UPSCALER_ReconstructedPrevNearestDepth", sequence.maxRenderWidth, sequence.maxRenderHeight,
                     FFX_SURFACE_FORMAT_R32_UINT);
        dispatch.output =
            External(L"Output", sequence.outputWidth, sequence.outputHeight, FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT);
        dispatch.jitterOffset = {frame.jitterX, frame.jitterY};
        dispatch.motionVectorScale = {static_cast<float>(frame.renderWidth), static_cast<float>(frame.renderHeight)};
        dispatch.renderSize = {frame.renderWidth, frame.renderHeight};
        dispatch.upscaleSize = {sequence.outputWidth, sequence.outputHeight};
        dispatch.enableSharpening = false;
        dispatch.sharpness = 0.0f;
        dispatch.frameTimeDelta = frame.frameMilliseconds;
        dispatch.preExposure = frame.preExposure;
        dispatch.reset = frame.reset;
        dispatch.cameraNear = FLT_MAX;
        dispatch.cameraFar = sequence.nearMeters;
        dispatch.cameraFovAngleVertical = frame.verticalFov;
        dispatch.viewSpaceToMetersFactor = 1.0f;
        if (ffxFsr3UpscalerContextDispatch(&context, &dispatch) != FFX_OK)
        {
            std::fprintf(stderr, "ffxFsr3UpscalerContextDispatch failed\n");
            std::exit(1);
        }

        json.Open('{');
        IntField("renderWidth", frame.renderWidth);
        IntField("renderHeight", frame.renderHeight);
        FloatField("jitterX", frame.jitterX);
        FloatField("jitterY", frame.jitterY);
        FloatField("frameMilliseconds", frame.frameMilliseconds);
        FloatField("preExposure", frame.preExposure);
        FloatField("verticalFov", frame.verticalFov);
        json.Key("reset");
        json.Bool(frame.reset);
        // The generator stages its constants first, then the upscale stages the main constants, SPD's and RCAS's.
        json.Key("reactiveWords");
        PrintWords(recorder.staged[0], recorder.stagedSizes[0]);
        json.Key("constants");
        PrintConstants(recorder.staged[1]);
        json.Key("spdWords");
        PrintWords(recorder.staged[2], recorder.stagedSizes[2]);
        json.Key("jobs");
        json.Open('[');
        for (const Job& job : recorder.jobs)
        {
            json.Open('{');
            json.Key("kind");
            json.String(job.kind);
            json.Key("label");
            json.String(job.label);
            if (job.kind == "clear" || job.kind == "discard")
            {
                json.Key("target");
                json.String(job.target);
            }
            if (job.kind == "clear")
            {
                json.Key("color");
                json.Open('[');
                for (float channel : job.color)
                    json.Float(channel);
                json.Close(']');
            }
            if (job.kind == "dispatch")
            {
                IntField("x", job.dimensions[0]);
                IntField("y", job.dimensions[1]);
                IntField("z", job.dimensions[2]);
            }
            json.Close('}');
        }
        json.Close(']');
        json.Close('}');
    }
    json.Close(']');
    json.Close('}');
}

// AMD's own jitter for frame `index` of a sequence whose render width is `renderWidth`, negated as AMD's sample passes
// it to the dispatch.
Frame Jittered(int32_t index, uint32_t renderWidth, uint32_t renderHeight, uint32_t outputWidth, Frame frame)
{
    float x = 0, y = 0;
    ffxFsr3UpscalerGetJitterOffset(&x, &y, index, ffxFsr3UpscalerGetJitterPhaseCount(renderWidth, outputWidth));
    frame.renderWidth = renderWidth;
    frame.renderHeight = renderHeight;
    frame.jitterX = -x;
    frame.jitterY = -y;
    return frame;
}

std::vector<Sequence> Sequences()
{
    const float fov = 1.0471975512f;
    std::vector<Sequence> sequences;

    // The budget's sizes: a render size of 1280 x 720 within a largest render size of 1920 x 1080. The frames hold a
    // reset, changes of the render size up and back, an exposure that changes, and frame times outside AMD's clamp.
    Sequence budget = {"budget", 1920, 1080, 1920, 1080, 0.05f, {}};
    const uint32_t sizes[][2] = {{1280, 720}, {1280, 720}, {1280, 720}, {1440, 816}, {1440, 816}, {1920, 1080},
                                 {1920, 1080}, {1280, 720}, {960, 544},  {960, 544},  {1280, 720}, {1280, 720}};
    const float times[] = {16.6666667f, 16.6666667f, 33.3f, 0.5f, 2000.0f, 16.6666667f,
                           8.0f,        16.6666667f, 16.6666667f, 16.6666667f, 16.6666667f, 16.6666667f};
    const float exposures[] = {1.0f, 1.0f, 2.0f, 2.0f, 0.25f, 0.0078125f, 1.41421354f, 1.0f, 1.0f, 3.0f, 3.0f, 0.5f};
    for (int32_t index = 0; index < 12; ++index)
    {
        Frame frame = {};
        frame.frameMilliseconds = times[index];
        frame.preExposure = exposures[index];
        frame.verticalFov = index < 6 ? fov : 0.8f;
        frame.reset = index == 0 || index == 9;
        budget.frames.push_back(Jittered(index, sizes[index][0], sizes[index][1], 1920, frame));
    }
    sequences.push_back(budget);

    // Odd sizes, so that every division and halving of a size rounds.
    Sequence odd = {"odd", 641, 361, 1001, 563, 0.05f, {}};
    const uint32_t oddSizes[][2] = {{427, 241}, {427, 241}, {641, 361}, {333, 187}, {427, 241}};
    for (int32_t index = 0; index < 5; ++index)
    {
        Frame frame = {};
        frame.frameMilliseconds = 16.6666667f;
        frame.preExposure = 1.0f;
        frame.verticalFov = 1.3f;
        frame.reset = index == 0;
        odd.frames.push_back(Jittered(index, oddSizes[index][0], oddSizes[index][1], 1001, frame));
    }
    sequences.push_back(odd);

    // A small native size, the size of the GPU test.
    Sequence small = {"small", 64, 48, 96, 72, 0.05f, {}};
    for (int32_t index = 0; index < 4; ++index)
    {
        Frame frame = {};
        frame.frameMilliseconds = 1000.0f / 60.0f;
        frame.preExposure = index < 2 ? 1.0f : 4.0f;
        frame.verticalFov = fov;
        frame.reset = index == 0;
        small.frames.push_back(Jittered(index, 64, 48, 96, frame));
    }
    sequences.push_back(small);
    return sequences;
}

void PrintLayout()
{
    json.Key("constantsLayout");
    json.Open('{');
    IntField("size", sizeof(Fsr3UpscalerConstants));
    IntField("jitterOffset", offsetof(Fsr3UpscalerConstants, jitterOffset));
    IntField("tanHalfFOV", offsetof(Fsr3UpscalerConstants, tanHalfFOV));
    IntField("deltaPreExposure", offsetof(Fsr3UpscalerConstants, deltaPreExposure));
    IntField("minDisocclusionAccumulation", offsetof(Fsr3UpscalerConstants, minDisocclusionAccumulation));
    json.Close('}');
}

} // namespace

int main()
{
    json.Open('{');
    json.Key("source");
    json.String("FidelityFX SDK v1.1.4, c6efa6bf7f2027b3ec94f28578bb5965eabb9e55, upscaler 3.1.4");
    PrintJitter();
    PrintLayout();
    json.Key("sequences");
    json.Open('[');
    for (const Sequence& sequence : Sequences())
        PrintSequence(sequence);
    json.Close(']');
    json.Close('}');
    std::printf("\n");
    return 0;
}
