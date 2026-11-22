using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Silk.NET.Vulkan;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    private static readonly ReferenceCrop HistoryReflectionRegion = new(Left: 320, Top: 180, Width: 640, Height: 480);

    private static int CaptureHistory(string[] args)
    {
        var request = HistoryRequest.Parse(args);
        CreateNewOutput(request.Output);
        try
        {
            CaptureHistoryFrames(request);
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(request.Output, "failure.txt"), error.ToString());
            throw;
        }
    }

    private static void CaptureHistoryFrames(HistoryRequest request)
    {
        Environment.SetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_PROTOTYPE", "1");
        Environment.SetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_NEIGHBOR_CAP", "0");
        Environment.SetEnvironmentVariable("TERTIUS_REFLECTION_SHARING", "1");
        Environment.SetEnvironmentVariable(
            "TERTIUS_SPECULAR_HISTORY",
            request.Control switch
            {
                SpecularHistoryMode.Original => "original",
                SpecularHistoryMode.PreciseCapped => "precise-capped",
                SpecularHistoryMode.PreciseUncapped => "precise-uncapped",
            }
        );
        Environment.SetEnvironmentVariable(
            "TERTIUS_DEBUG_JITTER",
            request.Jitter switch
            {
                DebugJitter.Upscaler => "upscaler",
                DebugJitter.None => "none",
                DebugJitter.Always => "always",
                DebugJitter.Held => "held",
            }
        );
        var instrumentation = HistoryInstrumentation.Selected();
        Dictionary<string, string> identities = RequireHistorySources(request.Output);
        HistorySequence schedule = request.Schedule;
        float roughness = request.Roughness == "smooth" ? 0.045f : 0.2f;
        File.WriteAllText(
            Path.Combine(request.Output, "request.json"),
            JsonSerializer.Serialize(
                new
                {
                    Event = request.Event.ToString(),
                    Material = request.Roughness,
                    ControlMode = request.Control.ToString(),
                    request.WarmUpDraws,
                    schedule.MeasuredFrames,
                    Fps = 60,
                    EventFrame = request.Event == HistoryEvent.Static ? (int?)null : 60,
                    SamplePlanForcedFullRate = request.Control != SpecularHistoryMode.Original,
                    ReflectionRaysForcedFullRate = request.Control != SpecularHistoryMode.Original,
                    DiffuseRayAllocationUnchanged = true,
                    Width = 1280,
                    Height = 720,
                    Roughness = roughness,
                    ExposureStops = ComparisonExposure,
                    Upscaling = request.Fsr
                        ? "Fsr3Upscaling(FixedRenderScale(1), sharpness 0): jittered, native 1280x720 render"
                        : "NoUpscaling",
                    Camera = request.Camera.ToString(),
                    DebugJitter = request.Jitter.ToString(),
                    HistoryJitter = "Pixel: the renderer's only rule since tiunr-4b2g. A history texel stands for its whole pixel and moves only with the scene's motion; a pixel whose own place holds less than LeastHistoryWeight of passing taps reads the jitter-corrected place.",
                    request.EveryPng,
                    OutputHdrFiles = "measured-output-hdr.f32 holds, per measured frame, the final image before bloom and tone mapping (FSR's upscaled output, or the HDR image without an upscale) over the original 80x160 crop: little-endian float32 RGB, pre-exposure undone, frame-major then row-major. draws.jsonl FixedCropOutputPlateRedMean is its red channel summed over the crop's plate pixels, over all crop pixels.",
                    TraceFiles = "When control-trace is selected (tiunr-4b2g): measured-trace-crop.f32.gz holds, per measured frame, the 9 RGBA32F trace layers (TemporalSignal.TraceLayers) of the original 80x160 crop, layer-major then row-major. trace-NNNN.f32.gz holds all 9 layers of the whole 1280x720 screen in frames 0, 64 and the last. measured-trace-flags.u8.gz holds, per measured frame, one ScreenFlag byte per pixel of the whole 1280x720 screen, row-major (bit 0 anti-lag cut, 1 clamp moved, 2 read at the pixel place, 3 the pixel place holds less than LeastHistoryWeight, 4 hit, 5 reprojection lost more than half a frame, 6 reflected place won, 7 surface). trace-sums.f32 holds one float32 1280x720 raster per sum of TraceWriters.SumNames over the measured frames. screen-classes.u8 is the whole screen at frame 0: 0 sky, 1 plate, 2 another surface (the emitter). draws.jsonl Trace holds per-frame length-change sums of the plate, the plate pixels whose sample hit a surface, and both inside the crop.",
                    TraceSums = TraceWriters.SumNames,
                    CameraPath = request.Camera == HistoryCamera.Walk
                        ? "Frames 0-59 still at (0, 3, 6), pitch -30 degrees. Frames 60-119 pan left (yaw about +Y) at 10 degrees/s to 10 degrees. Frames 120-239 walk along the panned camera's right vector at 1 m/s (2 m). Previous pose is the frame before; warm-up still."
                        : "Still at (0, 3, 6), pitch -30 degrees.",
                    Bloom = "NoBloom",
                    Crop = ComparisonCrop,
                    ReflectionRegion = HistoryReflectionRegion,
                    Quantity = "Scene-linear outgoing visible-plate specular red radiance, W/(m^2 sr). Material factor once; pre-exposure undone.",
                    FloatFormat = "Little-endian float32 scalar, frame-major then original 80x160 crop row-major, no resizing.",
                    PngFrames = "Final real renderer output, frame-0000.png onwards, zero-based measured-frame index. Static retains only first and last.",
                    PrecisionLimit = "No configured age cap in PreciseUncapped. Float32 unit increments eventually round away at 16777216 accepted frames; small RGB updates also face float32 rounding.",
                    Sharing = "Unchanged H13 output reconstruction; sample directions, fast history, rejection, clamp, anti-lag and distance guide remain unchanged.",
                    HistoryInstrumentation = instrumentation.ToString(),
                    HistoryRejection = request.Control.KeepsLightAge
                        ? "Light-change age (tiunr-py6q): per-pixel exact light (demodulated direct diffuse plus sent glow, pre-exposure undone) and its age; a change leaves the reprojected 3x3 range by more than 2%. Each ray brings its hit pixel's age; a sample of at least 25% of the slow luminance cuts the retained length to its youngest ray age before the blend."
                        : "Original H13 history: existing receiver, reflected-distance and clamp/anti-lag retention only, no light-change age.",
                    UncoveredFiles = "When light-age-diagnostic is selected: measured-seen.u16.gz is frame-major full 1280x720, one little-endian uint16 per pixel: 0 when the specular sample's rays hit no never-changed surface, else the fewest frames seen among those hits plus 1. measured-length-before-blend.f32.gz is the same raster, float32: the slow length just before the blend, which no light-age cut shortens since 2026-11-13, -1 without a sample. uncovered-receivers.bin holds one 17-byte record per plate receiver with a seen hit: uint16 frame, uint32 pixel, uint16 frames seen, float32 length before the blend, float32 accepted length after the draw, uint8 flags (bit 0 sample holds the 25% cut share, bit 1 in the grain population). draws.jsonl holds per-frame plate-receiver length histograms and grain bins.",
                    LightAgeFiles = "With a precise history (capped or uncapped), which always keeps the light-change age (tiunr-7haj): measured-sample-age.u16 is frame-major original 80x160 crop, one little-endian uint16 per pixel: 0 no evidence, else the specular sample's youngest ray age plus 1. measured-light-age.f32 is the same crop, two float32 per pixel: exact light, age (-1 no evidence). draws.jsonl counts whole-screen surface pixels, age resets and evidence pixels per measured frame. All empty for the original history.",
                },
                JsonOptions
            )
        );
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Width = 1280,
                Height = 720,
                FramesInFlight = 1,
                CapturesInFlight = false,
                Validation = true,
                Upscaling = request.Fsr ? new Fsr3Upscaling(new FixedRenderScale(1), 0) : new NoUpscaling(),
                WarmUpFrames = 0,
                Exposure = new FixedExposure(ComparisonExposure),
                Bloom = new NoBloom(),
                EnvironmentMap = new NoEnvironmentMap(),
                Subsurface = new NoSubsurface(),
                Impostors = false,
                CascadedLights = 0,
                LocalShadows = new NoLocalShadows(),
                Deformation = false,
                ScreenTrace = ScreenTrace.Default,
            }
        );
        try
        {
            RequireHistoryResources(renderer, request.Control, instrumentation);
            string[] selectedShaders =
            [
                "accumulate_temporal.comp",
                "accumulate_spatial.comp",
                "screen_trace_plan.comp",
                "screen_trace_block_rays.comp",
                "screen_trace_own_rays.comp",
                "screen_trace_resolve.comp",
                "composite.frag",
            ];
            File.WriteAllText(
                Path.Combine(request.Output, "selected-shaders.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        Engine = selectedShaders.Select(renderer.ScreenTraceSignals.Specular.HistoryShader),
                        Probe = ReflectionComparisonProbe
                            .OutputGroups.Select(group =>
                                renderer.ScreenTraceSignals.Specular.HistoryShader(group.Shader)
                            )
                            .Append(
                                renderer.ScreenTraceSignals.Specular.HistoryShader("reflection_comparison_shared.frag")
                            ),
                        ActualReconstructionAttachment = 8,
                        UnfilteredIdentityChannel = 1,
                        SlowStorage = renderer.ScreenTraceSignals.Specular.Slow(0).Format.ToString(),
                        SampleFastAndGuideStorage = Format.R16G16B16A16Sfloat.ToString(),
                    },
                    JsonOptions
                )
            );
            MeshData geometry = Primitives.Box(new(0.5f));
            var probe = ReflectionComparisonProbe.On(renderer, geometry);
            RenderSnapshot scene = ComparisonScene(renderer, roughness, geometry);
            MaterialId dark = renderer.CreateMaterial(
                new(
                    BaseColor: new(Vector3.Zero, 1),
                    Roughness: 1,
                    Reflectance: 0,
                    Emissive: Vector3.Zero,
                    Model: new StandardShading(Metallic: 0)
                )
            );
            MaterialId dim = renderer.CreateMaterial(
                new(
                    BaseColor: new(Vector3.Zero, 1),
                    Roughness: 1,
                    Reflectance: 0,
                    Emissive: new(5440),
                    Model: new StandardShading(Metallic: 0)
                )
            );
            MaterialId bright = scene.Instances[1].Material;
            using var draws = new StreamWriter(Path.Combine(request.Output, "draws.jsonl")) { AutoFlush = true };
            using var actual = new BinaryWriter(
                File.Create(Path.Combine(request.Output, "measured-plate-specular-r.f32"))
            );
            using var raw = new BinaryWriter(File.Create(Path.Combine(request.Output, "measured-specular-r.f32")));
            using var unfiltered = new BinaryWriter(
                File.Create(Path.Combine(request.Output, "measured-unfiltered-specular-r.f32"))
            );
            using var ages = new BinaryWriter(File.Create(Path.Combine(request.Output, "measured-history-alpha.f32")));
            using var surfaces = new BinaryWriter(File.Create(Path.Combine(request.Output, "measured-surfaces.u8")));
            using var sampleAges = new BinaryWriter(
                File.Create(Path.Combine(request.Output, "measured-sample-age.u16"))
            );
            using var lightAges = new BinaryWriter(File.Create(Path.Combine(request.Output, "measured-light-age.f32")));
            using var uncovered = new UncoveredWriters(request.Output);
            using var trace = new TraceWriters(request.Output, ComparisonCrop);
            using var outputHdr = new BinaryWriter(
                File.Create(Path.Combine(request.Output, "measured-output-hdr.f32"))
            );
            for (int frame = -request.WarmUpDraws; frame < schedule.MeasuredFrames; frame++)
            {
                HistoryFrameState state = schedule.At(frame);
                RenderInstance emitter = scene.Instances[1];
                emitter.Position = new(state.EmitterX, 1, -2);
                emitter.PreviousPosition = new(state.PreviousEmitterX, 1, -2);
                emitter.Material = state.Emission switch
                {
                    0 => dark,
                    5440 => dim,
                    _ => bright,
                };
                scene.Instances[1] = emitter;
                scene.Lights[0] = EmitterLight(
                    position: emitter.Position,
                    previousPosition: emitter.PreviousPosition,
                    radiance: state.Emission
                );
                scene.Camera.Position = state.Camera.Position;
                scene.Camera.Rotation = state.Camera.Rotation;
                scene.Camera.PreviousPosition = state.PreviousCamera.Position;
                scene.Camera.PreviousRotation = state.PreviousCamera.Rotation;
                scene.Tick = state.Tick;
                scene.Time = state.Time;
                if (request.SavesPng(frame))
                    renderer.CaptureNextFrame(
                        new PngCaptureSink(Path.Combine(request.Output, $"frame-{frame:D4}.png"))
                    );
                renderer.Render(scene, 1);
                object measurement =
                    frame < 0
                        ? new { Phase = "WarmUp" }
                        : MeasureHistoryFrame(
                            renderer: renderer,
                            probe: probe,
                            request: request,
                            frame: frame,
                            actual: actual,
                            raw: raw,
                            unfiltered: unfiltered,
                            ages: ages,
                            surfaces: surfaces,
                            lightAge: (sampleAges, lightAges),
                            uncovered: uncovered,
                            trace: trace,
                            outputHdr: outputHdr
                        );
                draws.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            MeasuredFrame = frame,
                            state.Tick,
                            state.Time,
                            renderer.DrawnFrames,
                            renderer.LastDraw.DrawIndex,
                            renderer.LastDraw.Reset,
                            state.EmitterX,
                            state.PreviousEmitterX,
                            state.Emission,
                            CameraPosition = new[]
                            {
                                state.Camera.Position.X,
                                state.Camera.Position.Y,
                                state.Camera.Position.Z,
                            },
                            CameraYawDegrees = state.Camera.YawDegrees,
                            scene.CameraCuts,
                            ReceiverPosition = new[]
                            {
                                scene.Instances[0].Position.X,
                                scene.Instances[0].Position.Y,
                                scene.Instances[0].Position.Z,
                            },
                            ReflectionOwnRays = frame < 0 ? (int?)null : renderer.ReadOwnRayCounts().Reflection,
                            Measurement = measurement,
                        }
                    )
                );
                if (
                    renderer.DrawnFrames != state.Tick
                    || renderer.LastDraw.Reset != (state.Tick == 1)
                    || renderer.LastDraw.DrawIndex != state.Tick - 1
                )
                    throw new InvalidOperationException(
                        $"History clock/reset mismatch at measured frame {frame}. Retained draws.jsonl describes the actual draw."
                    );
                if (probe.Size != (1280, 720) || renderer.OutputSize != probe.Size)
                    throw new InvalidOperationException("History capture requires native 1280x720 input and output.");
                if (renderer.ValidationMessages.Count != 0)
                    throw new InvalidOperationException(
                        $"Renderer validation failed at measured frame {frame}; all messages retained after disposal."
                    );
            }
            trace.Finish();
        }
        finally
        {
            renderer.Dispose();
            File.WriteAllText(
                Path.Combine(request.Output, "validation.json"),
                JsonSerializer.Serialize(renderer.ValidationMessages, JsonOptions)
            );
        }
        if (renderer.ValidationMessages.Count != 0)
            throw new InvalidOperationException(
                "Renderer validation failed; validation.json retains all messages, including disposal."
            );
        foreach ((string file, string expected) in identities)
            if (MetricFiles.Hash(file) != expected)
                throw new InvalidDataException($"Capture source or loaded assembly changed during the run: {file}.");
        File.Copy(Path.Combine(request.Output, "request.json"), Path.Combine(request.Output, "metadata.json"));
        File.WriteAllText(
            Path.Combine(request.Output, "complete.json"),
            JsonSerializer.Serialize(
                new
                {
                    Passed = true,
                    RendererDraws = request.WarmUpDraws + schedule.MeasuredFrames,
                    schedule.MeasuredFrames,
                    ValidationMessages = 0,
                    IdentitiesUnchanged = true,
                },
                JsonOptions
            )
        );
    }

    private static void RequireHistoryResources(
        VulkanRenderer renderer,
        SpecularHistoryMode control,
        HistoryInstrumentation instrumentation
    )
    {
        TemporalSignal specular = renderer.ScreenTraceSignals.Specular;
        Format expected =
            control == SpecularHistoryMode.Original ? Format.R16G16B16A16Sfloat : Format.R32G32B32A32Sfloat;
        bool formats =
            specular.Slow(0).Format == expected
            && specular.Sample(0).Format == Format.R16G16B16A16Sfloat
            && specular.HitImages(0, 0).LastFast.Format == Format.R16G16B16A16Sfloat
            && specular.ReflectionGuide(0).Format == Format.R16G16B16A16Sfloat;
        bool lightAgeMatches =
            specular.Instrumentation == instrumentation
            && (specular.SampleAge(0) is not null) == control.KeepsLightAge
            && (renderer.ScreenTraceImages.LightAge(0) is not null) == control.KeepsLightAge
            && (specular.UncoveredDiagnostic(0) is not null)
                == (instrumentation == HistoryInstrumentation.LightAgeDiagnostic)
            && (specular.AccumulationTrace(0) is not null) == (instrumentation == HistoryInstrumentation.ControlTrace);
        if (
            specular.HistoryMode != control
            || !formats
            || !renderer.ShadingConstants.ReflectionSharingPrototype
            || !lightAgeMatches
        )
            throw new InvalidOperationException(
                "The loaded renderer does not implement the requested H13 history/storage control."
            );
    }

    private static object MeasureHistoryFrame(
        VulkanRenderer renderer,
        ReflectionComparisonProbe probe,
        HistoryRequest request,
        int frame,
        BinaryWriter actual,
        BinaryWriter raw,
        BinaryWriter unfiltered,
        BinaryWriter ages,
        BinaryWriter surfaces,
        (BinaryWriter Samples, BinaryWriter Records) lightAge,
        UncoveredWriters uncovered,
        TraceWriters trace,
        BinaryWriter outputHdr
    )
    {
        float[][] fields = [.. Enumerable.Range(0, 8).Select(probe.Read)];
        float[] fullOutput = probe.Read(8);
        float[] output = CropAttachment(fullOutput, 2);
        float[] fullHistory = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0));
        float[] history = CropAttachment(fullHistory, 4);
        object? lightAgeCounts = null;
        if (renderer.ScreenTraceSignals.Specular.SampleAge(0) is { } sampleAge)
        {
            // The file keeps the light code alone: the motion bit is the accumulator's evidence, not an age.
            foreach (float code in CropAttachment(renderer.ReadImage(sampleAge), 1))
                lightAge.Samples.Write((ushort)((ushort)code & TemporalSignal.LightCodeMask));
            float[] record = renderer.ReadLightAge();
            foreach (float value in CropAttachment(record, 2))
                lightAge.Records.Write(value);
            int surfacePixels = 0,
                resets = 0,
                evidence = 0,
                firstShown = 0;
            for (int pixel = 0; pixel < record.Length / 2; pixel++)
            {
                if (record[2 * pixel] < 0)
                    continue;
                surfacePixels++;
                resets += record[2 * pixel + 1] == 0 ? 1 : 0;
                evidence += record[2 * pixel + 1] >= 0 ? 1 : 0;
                firstShown += record[2 * pixel + 1] == ScreenTraceImages.NoLightAge ? 1 : 0;
            }
            lightAgeCounts = new
            {
                SurfacePixels = surfacePixels,
                Resets = resets,
                Evidence = evidence,
                FirstShown = firstShown,
            };
        }
        float[][] cropFields = [.. fields.Select(field => CropAttachment(field, 2))];
        int count = ComparisonCrop.Width * ComparisonCrop.Height;
        float[] inputs = new float[count * 16];
        float[] source = new float[count * 15];
        float[] alpha = new float[count];
        byte[] classification = new byte[count];
        (float stops, float previousStops) = renderer.ReadExposureStops();
        if (stops != ComparisonExposure || previousStops != ComparisonExposure)
            throw new InvalidDataException("The history control requires exactly the fixed exposure in both frames.");
        double scale = Math.Pow(2, stops);
        double sum = 0;
        double unfilteredSum = 0;
        for (int pixel = 0; pixel < count; pixel++)
        {
            for (int field = 0; field < 8; field++)
                cropFields[field].AsSpan(2 * pixel, 2).CopyTo(inputs.AsSpan(16 * pixel + 2 * field, 2));
            inputs.AsSpan(16 * pixel + 1, 15).CopyTo(source.AsSpan(15 * pixel, 15));
            float value = (float)(output[2 * pixel] / scale);
            float original = (float)(output[2 * pixel + 1] / scale);
            if (!float.IsFinite(value) || value < 0 || output[2 * pixel + 1] != inputs[16 * pixel])
                throw new InvalidDataException(
                    $"Actual H13 attachment 8/unfiltered identity failed at frame {frame}, crop pixel {pixel}."
                );
            bool plate = inputs[16 * pixel + 2] < 0.5f;
            classification[pixel] = (byte)(plate ? ReferenceSurface.Plate : ReferenceSurface.Emitter);
            float contribution = plate ? value : 0;
            actual.Write(contribution);
            raw.Write(value);
            unfiltered.Write(original);
            surfaces.Write(classification[pixel]);
            alpha[pixel] = history[4 * pixel + 3];
            if (!float.IsFinite(alpha[pixel]) || alpha[pixel] < 1)
                throw new InvalidDataException(
                    $"Invalid accepted history length at frame {frame}, crop pixel {pixel}."
                );
            ages.Write(alpha[pixel]);
            sum += contribution;
            unfilteredSum += plate ? original : 0;
        }
        // The final image before bloom and tone mapping: FSR's output, or the HDR image without an upscale.
        float[] upscaled = CropAttachment(renderer.ReadPostHdr(), 4);
        double outputPlateSum = 0;
        for (int pixel = 0; pixel < count; pixel++)
        {
            for (int channel = 0; channel < 3; channel++)
                outputHdr.Write((float)(upscaled[4 * pixel + channel] / scale));
            outputPlateSum += classification[pixel] == (byte)ReferenceSurface.Plate ? upscaled[4 * pixel] / scale : 0;
        }
        double regionSum = 0;
        int regionPixels = 0;
        for (
            int row = HistoryReflectionRegion.Top;
            row < HistoryReflectionRegion.Top + HistoryReflectionRegion.Height;
            row++
        )
        for (
            int column = HistoryReflectionRegion.Left;
            column < HistoryReflectionRegion.Left + HistoryReflectionRegion.Width;
            column++
        )
        {
            int pixel = row * 1280 + column;
            if (fields[1][2 * pixel] >= 0.5f || fields[4][2 * pixel + 1] <= 0)
                continue;
            regionSum += fullOutput[2 * pixel] / scale;
            regionPixels++;
        }
        bool diagnostic = frame == 0 || frame == request.Schedule.MeasuredFrames - 1 || frame is 59 or 60 or 119 or 120;
        if (diagnostic)
        {
            string folder = Path.Combine(request.Output, $"diagnostic-{frame:D4}");
            Directory.CreateDirectory(folder);
            WriteFloats(Path.Combine(folder, "probe-inputs.f32"), inputs);
            WriteFloats(Path.Combine(folder, "source-history.f32"), history);
            WriteFloats(Path.Combine(folder, "reconstructed-output.f32"), output);
            WriteFloats(
                Path.Combine(folder, "reflection-distance-guide.f32"),
                CropAttachment(renderer.ReadImage(renderer.ScreenTraceSignals.Specular.ReflectionGuide(0)), 4)
            );
        }
        if (frame == 0)
        {
            bool[] plateMask = PlateMask(fields);
            File.WriteAllBytes(
                Path.Combine(request.Output, "screen-classes.u8"),
                [
                    .. Enumerable
                        .Range(0, plateMask.Length)
                        .Select(pixel => ScreenClass(plateMask[pixel], fields[4][2 * pixel + 1])),
                ]
            );
            File.WriteAllBytes(Path.Combine(request.Output, "surface-classification.u8"), classification);
            File.WriteAllText(
                Path.Combine(request.Output, "raster-metadata.json"),
                JsonSerializer.Serialize(probe.NumericMetadata, JsonOptions)
            );
        }
        object uncoveredCounts = uncovered.Measure(
            renderer: renderer,
            frame: frame,
            plate: PlateMask(fields),
            output: fullOutput,
            scale: scale,
            history: fullHistory
        );
        object? traced = trace.Measure(
            renderer: renderer,
            frame: frame,
            plate: PlateMask(fields),
            history: fullHistory,
            dumpWhole: frame == 0 || frame == 64 || frame == request.Schedule.MeasuredFrames - 1
        );
        Array.Sort(alpha);
        return new
        {
            Phase = "Measured",
            Trace = traced,
            LightAge = lightAgeCounts,
            Uncovered = uncoveredCounts,
            SourceInputsSha256 = Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(source.AsSpan()))),
            SourceInputs = "All 15 probe input fields except outgoing specular, exact original crop float32 bytes.",
            FixedCropPlateRadianceSum = sum,
            FixedCropPlateRadianceMean = sum / count,
            FixedCropUnfilteredPlateRadianceMean = unfilteredSum / count,
            FixedCropOutputPlateRedMean = outputPlateSum / count,
            ReflectionRegionPlateRadianceSum = regionSum,
            ReflectionRegionPlateRadianceMean = regionSum / regionPixels,
            ReflectionRegionPlatePixels = regionPixels,
            HistoryAlpha = new
            {
                Min = alpha[0],
                P10 = alpha[(count - 1) / 10],
                P25 = alpha[(count - 1) / 4],
                Median = alpha[(count - 1) / 2],
                P75 = alpha[(count - 1) * 3 / 4],
                P90 = alpha[(count - 1) * 9 / 10],
                Max = alpha[^1],
            },
        };
    }

    /// <summary>A pixel's class in screen-classes.u8: 0 sky, 1 plate, 2 another surface.</summary>
    private static byte ScreenClass(bool plate, float distance)
    {
        if (plate)
            return 1;
        return distance > 0 ? (byte)2 : (byte)0;
    }

    internal static Dictionary<string, string> RequireHistorySources(string path)
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Assembly assembly in new[] { typeof(VulkanRenderer).Assembly, typeof(Program).Assembly })
        {
            using Stream stream =
                assembly.GetManifestResourceStream("Tertius.HistorySources.sha256")
                ?? throw new InvalidDataException($"{assembly.Location} lacks the compiled history source identity.");
            using var reader = new StreamReader(stream);
            string manifest = reader.ReadToEnd();
            File.WriteAllText(Path.Combine(path, assembly.GetName().Name + "-sources.sha256"), manifest);
            foreach (string row in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = row.TrimEnd('\r').Split('|');
                string expected = parts[1].ToLowerInvariant();
                if (!File.Exists(parts[0]) || MetricFiles.Hash(parts[0]) != expected)
                    throw new InvalidDataException($"Loaded source does not match compiled source: {parts[0]}.");
                identities[parts[0]] = expected;
            }
            identities[assembly.Location] = MetricFiles.Hash(assembly.Location);
            string[] shaders =
            [
                .. assembly
                    .GetManifestResourceNames()
                    .Where(name => name.EndsWith(".spv", StringComparison.Ordinal))
                    .Order(),
            ];
            Dictionary<string, string> hashes = shaders.ToDictionary(
                name => name,
                name =>
                {
                    using Stream shader =
                        assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException(name);
                    return Convert.ToHexStringLower(SHA256.HashData(shader));
                }
            );
            File.WriteAllText(
                Path.Combine(path, assembly.GetName().Name + "-shaders.json"),
                JsonSerializer.Serialize(hashes, JsonOptions)
            );
        }
        File.WriteAllText(Path.Combine(path, "identities.json"), JsonSerializer.Serialize(identities, JsonOptions));
        return identities;
    }
}

internal enum HistoryEvent
{
    Static,
    Move,
    Off,
    Dim,
}

/// <summary>The camera of a history capture: still, or the deterministic pan and walk of <see cref="HistorySequence"/>.</summary>
internal enum HistoryCamera
{
    Still,
    Walk,
}

/// <summary>A camera pose of a history capture: its eye, its yaw about +Y in degrees, and the rotation of both.</summary>
internal readonly record struct HistoryPose(Vector3 Position, float YawDegrees)
{
    private static readonly Quaternion Pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 6);

    public Quaternion Rotation => Quaternion.CreateFromAxisAngle(Vector3.UnitY, YawDegrees * MathF.PI / 180) * Pitch;
}

internal readonly record struct HistoryFrameState(
    long Tick,
    double Time,
    float EmitterX,
    float PreviousEmitterX,
    float Emission,
    bool SavePng,
    HistoryPose Camera,
    HistoryPose PreviousCamera
);

internal readonly record struct HistorySequence(HistoryEvent Event, int WarmUpDraws, HistoryCamera Camera)
{
    /// <summary>The pan's rate in degrees per second and its end, and the walk's speed in m/s, at 60 frames per second.</summary>
    private const float PanDegreesPerSecond = 10;
    private const int PanFrames = 60;
    private const float WalkMetersPerSecond = 1;

    public int MeasuredFrames => Event == HistoryEvent.Static && Camera == HistoryCamera.Still ? 128 : 240;

    public HistoryFrameState At(int frame)
    {
        long tick = (long)WarmUpDraws + frame + 1;
        float emission = Event switch
        {
            HistoryEvent.Off when frame >= 60 => 0,
            HistoryEvent.Dim when frame >= 60 => 5440,
            HistoryEvent.Static or HistoryEvent.Move or HistoryEvent.Off or HistoryEvent.Dim => 6800,
        };
        bool save = frame >= 0 && (MeasuredFrames == 240 || frame == 0 || frame == MeasuredFrames - 1);
        return new(
            Tick: tick,
            Time: tick / 60.0,
            EmitterX: EmitterXAt(frame),
            PreviousEmitterX: EmitterXAt(frame - 1),
            Emission: emission,
            SavePng: save,
            Camera: PoseAt(frame),
            PreviousCamera: PoseAt(frame - 1)
        );
    }

    /// <summary>
    /// The camera at <paramref name="frame"/>: still before frame 60, a pan to the left at PanDegreesPerSecond over frames
    /// 60 to 119, then a walk along the panned camera's right at WalkMetersPerSecond.
    /// </summary>
    private HistoryPose PoseAt(int frame)
    {
        var start = new Vector3(0, 3, 6);
        if (Camera == HistoryCamera.Still)
            return new(start, 0);
        float yaw = Math.Clamp(frame - 59, 0, PanFrames) * PanDegreesPerSecond / 60;
        var right = Vector3.Transform(
            Vector3.UnitX,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180)
        );
        float walked = Math.Max(frame - 59 - PanFrames, 0) * WalkMetersPerSecond / 60;
        return new(start + walked * right, yaw);
    }

    private float EmitterXAt(int frame) =>
        Event == HistoryEvent.Move ? -1 + 2 * Math.Clamp((frame - 59) / 60f, 0, 1) : -1;
}

internal readonly record struct HistoryRequest(
    HistoryEvent Event,
    string Roughness,
    SpecularHistoryMode Control,
    string Output,
    int WarmUpDraws,
    bool Fsr,
    HistoryCamera Camera,
    DebugJitter Jitter,
    bool EveryPng
)
{
    /// <summary>The draws of the capture: its warm-up draws and measured frames.</summary>
    public HistorySequence Schedule => new(Event, WarmUpDraws, Camera);

    /// <summary>Whether measured frame <paramref name="frame"/> saves its final PNG: every one, or the schedule's.</summary>
    public bool SavesPng(int frame) => frame >= 0 && (EveryPng || Schedule.At(frame).SavePng);

    public static HistoryRequest Parse(string[] args)
    {
        if (args is not ["--history", var change, var roughness, var control, .. var options])
            throw new ArgumentException(
                "Usage: --history <static|move|off|dim> <smooth|rough> <original|capped|uncapped> --out <newdir> [--warmup N] [--upscaling none|fsr] [--camera still|walk] [--jitter upscaler|none|always|held] [--png all|ends]"
            );
        HistoryEvent historyEvent = change switch
        {
            "static" => HistoryEvent.Static,
            "move" => HistoryEvent.Move,
            "off" => HistoryEvent.Off,
            "dim" => HistoryEvent.Dim,
            _ => throw new ArgumentException("History event is static, move, off or dim."),
        };
        SpecularHistoryMode mode = control switch
        {
            "original" => SpecularHistoryMode.Original,
            "capped" => SpecularHistoryMode.PreciseCapped,
            "uncapped" => SpecularHistoryMode.PreciseUncapped,
            _ => throw new ArgumentException("History control is original, capped or uncapped."),
        };
        if (roughness is not ("smooth" or "rough"))
            throw new ArgumentException("History roughness is smooth or rough.");
        string output = "";
        int warmUp = 1024;
        bool fsr = false;
        HistoryCamera camera = HistoryCamera.Still;
        DebugJitter jitter = DebugJitter.Upscaler;
        bool everyPng = false;
        for (int index = 0; index < options.Length; index += 2)
        {
            if (index + 1 == options.Length)
                throw new ArgumentException("Each history option requires a value.");
            if (options[index] == "--out" && output.Length == 0)
                output = options[index + 1];
            else if (options[index] == "--warmup" && int.TryParse(options[index + 1], out int parsed) && parsed >= 0)
                warmUp = parsed;
            else if (options[index] == "--upscaling" && options[index + 1] is "none" or "fsr")
                fsr = options[index + 1] == "fsr";
            else if (options[index] == "--camera" && options[index + 1] is "still" or "walk")
                camera = options[index + 1] == "walk" ? HistoryCamera.Walk : HistoryCamera.Still;
            else if (options[index] == "--png" && options[index + 1] is "all" or "ends")
                everyPng = options[index + 1] == "all";
            else if (options[index] == "--jitter")
                jitter = options[index + 1] switch
                {
                    "upscaler" => DebugJitter.Upscaler,
                    "none" => DebugJitter.None,
                    "always" => DebugJitter.Always,
                    "held" => DebugJitter.Held,
                    _ => throw new ArgumentException($"Invalid history option: --jitter {options[index + 1]}."),
                };
            else
                throw new ArgumentException($"Invalid history option: {options[index]} {options[index + 1]}.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        if (camera == HistoryCamera.Walk && historyEvent != HistoryEvent.Static)
            throw new ArgumentException("A walking camera keeps the emitter still: use the static event.");
        return new(
            Event: historyEvent,
            Roughness: roughness,
            Control: mode,
            Output: output,
            WarmUpDraws: warmUp,
            Fsr: fsr,
            Camera: camera,
            Jitter: jitter,
            EveryPng: everyPng
        );
    }
}
