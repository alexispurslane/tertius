using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    private static readonly Double3 ComparisonEye = new(0, 3, 6);
    private static readonly ReferenceCrop ComparisonCrop = new(Left: 530, Top: 240, Width: 80, Height: 160);
    private const float ComparisonExposure = -6.8f;

    private static int CaptureLive(string roughnessName, string mode, string path, bool diagnosticOnly = false, bool geometryAudit = false)
    {
        if (roughnessName is not ("smooth" or "rough") || mode is not ("off" or "on" or "uncapped" or "sharing"))
            throw new ArgumentException("Live cases are smooth/rough and off/on/uncapped/sharing.");
        CreateNewOutput(path);
        bool sharing = mode == "sharing";
        Environment.SetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_PROTOTYPE", mode == "off" ? null : "1");
        Environment.SetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_NEIGHBOR_CAP", mode is "uncapped" or "sharing" ? "0" : null);
        Environment.SetEnvironmentVariable("TERTIUS_REFLECTION_SHARING", sharing ? "1" : null);
        using var renderer = VulkanRenderer.CreateHeadless(new()
        {
            Width = 1280, Height = 720, FramesInFlight = 1, CapturesInFlight = false,
            Upscaling = new NoUpscaling(), WarmUpFrames = 0, Exposure = new FixedExposure(ComparisonExposure),
            Bloom = new NoBloom(), EnvironmentMap = new NoEnvironmentMap(), Subsurface = new NoSubsurface(),
            Impostors = false, CascadedLights = 0, LocalShadows = new NoLocalShadows(), Deformation = false,
            ScreenTrace = ScreenTrace.Default,
        });
        MeshData boxGeometry = Primitives.Box(new(0.5f));
        ReflectionComparisonProbe probe = ReflectionComparisonProbe.On(renderer, boxGeometry);
        float roughness = roughnessName == "smooth" ? 0.045f : 0.2f;
        RenderSnapshot scene = ComparisonScene(renderer, roughness, boxGeometry);
        GeometryAuditPass? audit = geometryAudit
            ? GeometryAuditPass.On(renderer, MaterialStore.Slot(scene.Instances[0].Material), MaterialStore.Slot(scene.Instances[1].Material))
            : null;
        var draws = new List<object>();
        var auditDraws = new List<object>();
        var measured = new List<double[]>();
        float[]? firstInputs = null;
        ComparisonMeasurement? preflight = null;
        var fixture = new ComparisonFixture(
            Camera: ComparisonCamera, Crop: ComparisonCrop, Emitter: ComparisonEmitter(6800), Roughness: roughness, ExposureStops: ComparisonExposure
        );
        double emission = 0;
        double mostEmitterSpecular = 0;
        double leastEmission = double.PositiveInfinity;
        double maximumFactorRelativeError = 0;
        using var frames = new BinaryWriter(File.Create(Path.Combine(path, "measured-specular-r.f32")));
        using var plateFrames = new BinaryWriter(File.Create(Path.Combine(path, "measured-plate-specular-r.f32")));
        using var emitterFrames = new BinaryWriter(File.Create(Path.Combine(path, "measured-emitter-specular-r.f32")));
        using var unfilteredFrames = new BinaryWriter(File.Create(Path.Combine(path, "measured-unfiltered-specular-r.f32")));
        using BinaryWriter? hitFrames = sharing ? new(File.Create(Path.Combine(path, "measured-reflection-hit-distance.f32"))) : null;
        using BinaryWriter? guideFrames = sharing ? new(File.Create(Path.Combine(path, "measured-reflection-distance-guide.f32"))) : null;
        int drawCount = diagnosticOnly ? 1 : 96;
        for (int draw = 0; draw < drawCount; draw++)
        {
            scene.Tick = draw + 1;
            scene.Time = (draw + 1) / 60.0;
            renderer.Render(scene, 1);
            bool expectedDraw = renderer.DrawnFrames == draw + 1 && renderer.LastDraw.Reset == (draw == 0);
            GeometryAuditFrame? completedAudit = audit?.ReadCompleted(ComparisonCrop);
            bool snapshotsUnchanged = true;
            if (completedAudit is { } completed)
            {
                var snapshotHashes = completed.Snapshots.Select(snapshot => new
                {
                    snapshot.Name, Bytes = snapshot.Before.Length,
                    BeforeSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshot.Before)),
                    AfterSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshot.After)),
                    Equal = snapshot.Before.AsSpan().SequenceEqual(snapshot.After),
                }).ToArray();
                snapshotsUnchanged = snapshotHashes.All(snapshot => snapshot.Equal);
                auditDraws.Add(new { Draw = draw, renderer.LastDraw.DrawIndex, Snapshots = snapshotHashes, Audit = completed.Metadata });
                File.WriteAllText(Path.Combine(path, "audit-draws.json"), JsonSerializer.Serialize(auditDraws, JsonOptions));
            }
            if (draw > 0 && draw < 64 && expectedDraw && snapshotsUnchanged)
                continue;
            (float stops, float previousStops) = renderer.ReadExposureStops();
            double scale = Math.Pow(2, stops);
            float[] light = probe.Read(0);
            float[] inputs = ReadInputs(probe, light);
            string diagnosticFolder = "current-measured";
            if (draw == 0)
                diagnosticFolder = "preflight";
            if (draw == 64)
                diagnosticFolder = "first-measured";
            string diagnosticPath = Path.Combine(path, diagnosticFolder);
            Directory.CreateDirectory(diagnosticPath);
            float[] reconstructed = sharing ? CropAttachment(probe.Read(8), 2) : [];
            float[] hitSample = sharing ? CropAttachment(renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0)), 4) : [];
            float[] distanceGuide = sharing ? CropAttachment(renderer.ReadImage(renderer.ScreenTraceSignals.Specular.ReflectionGuide(0)), 4) : [];
            if (sharing)
            {
                WriteFloats(Path.Combine(diagnosticPath, "reconstructed-output.f32"), reconstructed);
                WriteFloats(Path.Combine(diagnosticPath, "reflection-hit-sample.f32"),
                    hitSample);
                WriteFloats(Path.Combine(diagnosticPath, "reflection-distance-guide.f32"), distanceGuide);
            }
            File.WriteAllText(Path.Combine(diagnosticPath, "renderer-state.json"), JsonSerializer.Serialize(new
            {
                Draw = draw, scene.Tick, scene.Time, renderer.DrawnFrames, renderer.LastDraw.DrawIndex, renderer.LastDraw.Reset,
                Internal = new { probe.Size.Width, probe.Size.Height }, Output = new { renderer.OutputSize.Width, renderer.OutputSize.Height },
                ExposureStops = stops, PreviousExposureStops = previousStops,
            }, JsonOptions));
            var frame = new ComparisonFrame(
                Draw: draw, Inputs: inputs, Distances: CropAttachment(renderer.ReadDistance(), 1),
                History: CropAttachment(renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0)), 4)
            ) { ActualExposureStops = stops, PreviousExposureStops = previousStops, RasterMetadata = probe.NumericMetadata };
            ComparisonMeasurement measurement = completedAudit is { } geometry
                ? ReferenceComparison.RetainAuditedMeasurement(path: diagnosticPath, fixture: fixture, frame: frame, audit: geometry, measured: draw >= 64)
                : ReferenceComparison.RetainAndMeasure(path: diagnosticPath, fixture: fixture, frame: frame, measured: draw >= 64);
            double unfilteredSum = measurement.RawSpecular.Sum();
            if (draw >= 64)
            {
                foreach (double value in measurement.RawSpecular)
                    unfilteredFrames.Write((float)value);
                foreach (float value in distanceGuide)
                    guideFrames?.Write(value);
            }
            if (sharing)
                measurement = ReferenceComparison.RetainReconstruction(path: diagnosticPath, unfiltered: measurement, output: reconstructed, exposureScale: scale);
            if (!expectedDraw)
                throw new InvalidOperationException($"Unexpected renderer advance/reset; complete data retained in {diagnosticPath}.");
            if (probe.Size != (1280, 720) || renderer.OutputSize != probe.Size)
                throw new InvalidOperationException($"Unexpected native dimensions; complete data retained in {diagnosticPath}.");
            if (draw == 0)
            {
                preflight = measurement;
                File.WriteAllBytes(Path.Combine(path, "surface-classification.u8"), measurement.Surfaces);
                string assessment = geometryAudit ? "AUDITED geometry preflight passed; original analytic-position diagnostics remain separate" : "analytic geometry preflight passed";
                Console.WriteLine($"{roughnessName}/{mode}: {assessment}, plate={measurement.Surfaces.Count(surface => surface == (byte)ReferenceSurface.Plate)}, emitter={measurement.Surfaces.Count(surface => surface == (byte)ReferenceSurface.Emitter)}");
                continue;
            }
            if (preflight is null || !preflight.Surfaces.AsSpan().SequenceEqual(measurement.Surfaces))
                throw new InvalidOperationException($"Primary visibility changed after preflight; complete data retained in {diagnosticPath}.");
            for (int index = 0; index < light.Length; index += 2)
                if (light[index + 1] > 0)
                {
                    emission = Math.Max(emission, light[index + 1] / scale);
                    leastEmission = Math.Min(leastEmission, light[index + 1] / scale);
                    mostEmitterSpecular = Math.Max(mostEmitterSpecular, light[index] / scale);
                }
            for (int pixel = 0; pixel < measurement.RawSpecular.Length; pixel++)
            {
                frames.Write((float)measurement.RawSpecular[pixel]);
                plateFrames.Write((float)measurement.PlateSpecular[pixel]);
                emitterFrames.Write((float)measurement.EmitterSpecular[pixel]);
                hitFrames?.Write(hitSample[4 * pixel + 3]);
            }
            double[] pixels = measurement.PlateSpecular;
            measured.Add(pixels);
            maximumFactorRelativeError = Math.Max(maximumFactorRelativeError, measurement.MaximumFactorRelativeError);
            if (firstInputs is null)
            {
                firstInputs = inputs;
                WriteFloats(Path.Combine(path, "decoded-inputs.f32"), firstInputs);
            }
            ReferenceComparison.RequireSameReceivers(firstInputs, inputs, ComparisonCrop);
            draws.Add(new { Draw = draw, scene.Tick, scene.Time, renderer.DrawnFrames,
                renderer.LastDraw.DrawIndex, renderer.LastDraw.Reset, ExposureStops = stops, PreviousExposureStops = previousStops,
                PreExposureScale = scale, RadianceSum = pixels.Sum(), Peak = pixels.Max(),
                RawRadianceSum = measurement.RawSpecular.Sum(), EmitterRadianceSum = measurement.EmitterSpecular.Sum(), UnfilteredRadianceSum = unfilteredSum });
            Console.WriteLine($"{roughnessName}/{mode}: draw {draw}, sum {pixels.Sum():G9}, peak {pixels.Max():G9}");
        }
        if (diagnosticOnly)
        {
            File.WriteAllText(Path.Combine(path, "diagnostic-manifest.json"), JsonSerializer.Serialize(new
            {
                OriginalRendererFrames = renderer.DrawnFrames, WarmUpFrames = 0, MeasuredLightingFrames = 0,
                InternalWidth = probe.Size.Width, InternalHeight = probe.Size.Height,
                OutputWidth = renderer.OutputSize.Width, OutputHeight = renderer.OutputSize.Height,
                NativeJitterPixels = new { X = 0, Y = 0 }, Crop = ComparisonCrop, ExposureStops = ComparisonExposure,
                Mode = mode, Roughness = roughness, ProbeDraws = ReflectionComparisonProbe.OutputGroups.Count,
                GeometryAudit = geometryAudit, SnapshotClockSeconds = scene.Time, scene.Tick,
                Scope = "One diagnostic frame only. Geometry alignment is not a lighting comparison or independent certification of engine geometry.",
            }, JsonOptions));
            return 0;
        }
        if (emission <= 0 || emission != leastEmission)
            throw new InvalidOperationException("The visible emitter must have one recorded emissive radiance.");
        WriteLiveMean(path, measured);
        File.WriteAllText(Path.Combine(path, "draws.json"), JsonSerializer.Serialize(draws, JsonOptions));
        File.WriteAllText(Path.Combine(path, "manifest.json"), JsonSerializer.Serialize(new
        {
            Case = roughnessName, Mode = mode, AuthoredRoughness = roughness,
            PrototypeControls = new { Enabled = mode != "off", SingleDiffuseHistory = mode != "off", NeighborCap = mode == "on", ReflectionSharing = sharing },
            Reconstruction = sharing ? "Temporal distance history: symmetric outgoing-light exchange within the existing5x5 search. alpha=flooredRoughness^2; coneSlope=2alpha/max(1-alpha^2,1e-6); projectedRadiusPixels=coneSlope*(hitDistance/(hitDistance+viewDepth))/nativePixelAngle. RadiusPixels is capped by3*min(alpha/0.1^2,1), then converted to receiver-world meters. Both endpoint radii must cover their world-space separation. This is a planar normal-incidence GGX-cone proxy, not an exact oblique BRDF support or a truncation of material/light integration. Maximum1/48 per neighbor, total at most one half, multiplied by minimum endpoint supporting-hit weight. Existing material, normal, plane, intervening-cell checks and direction weights unchanged. Only new guide histories are written; existing light RGB/history/source are unchanged." : "None",
            ReflectionHitGuide = sharing ? "Existing specularSample alpha remains the latest sampled ray-origin-to-hit distance in meters, float16 promoted to float32, retained independently. It is not an independent ray at every receiver under2x2 sharing. Raw sample RGBA is retained per diagnostic phase; measured alpha is frame-major in measured-reflection-hit-distance.f32. At high roughness existing diffuse-ray handover can supply this guide; these two cases are below that handover." : "Unused",
            ReflectionDistanceHistory = sharing ? "Guide RGBA16F promoted to float32: R=distance first moment in meters over accepted light samples (miss contributes zero); G=valid-hit support mass, dimensionless0..1; B/A=0. Same accepted light reprojection, temporal weights and short-history spatial repair. Positive finite unsaturated hits only. Representative distance=R/G when G>0; effective supporting-hit weight=clamp(G*current slow-history length,0,1). This is evidence weight, not probability or an error bound. Light anti-lag changes history length; guide remains an estimate, not object identity. Never brightness/exposure weighted. Per-phase crop retained before validation; all32 measured RGBA crops in measured-reflection-distance-guide.f32, frame-major80x160x4 little-endian float32." : "Unused",
            UnfilteredFile = "measured-unfiltered-specular-r.f32", UnfilteredProbeFieldsRemainUnchanged = true,
            InternalWidth = probe.Size.Width, InternalHeight = probe.Size.Height, OutputWidth = renderer.OutputSize.Width, OutputHeight = renderer.OutputSize.Height,
            Crop = ComparisonCrop, WarmUpDraws = 64, MeasuredDraws = 32, FirstMeasuredDraw = 64, LastMeasuredDraw = 95,
            JitterPixels = new { X = 0, Y = 0 }, Upscaling = "NoUpscaling", ExposureStops = ComparisonExposure,
            PreExposureScale = Math.Pow(2, ComparisonExposure), EmissionRadiance = emission,
            MostEmitterOutgoingSpecular = mostEmitterSpecular, EmitterAdditionalSpecularShare = mostEmitterSpecular / emission,
            Eye = ComparisonEye, PlateBounds = new[] { -50.0, -0.1, -50, 50, 0, 50 },
            EmitterAuthoredBounds = new[] { -1.1, 0, -2.1, -0.9, 2, -1.9 },
            Stage = "Tool-only read-only Composite pass, after accumulation and material response, before display/FSR.",
            Quantity = "Outgoing indirect SPECULAR red-channel radiance, W/(m^2 sr); achromatic fixture. Existing material factor applied once in shader, recorded pre-exposure undone on CPU.",
            FrameFormat = "32 consecutive draws, each 80x160 row-major float32 scalar red radiance, little endian.",
            InputFormat = "16 float32 per crop pixel: preexposed specular R, preexposed emission R, roughness, reflectance, baseColor R, metallic, normal XYZ, distance, camera-relative position XYZ, composite view XYZ.",
            SourceLimit = "Reference is one emitter reflection. Live source includes existing screen-hit fast-history feedback; recorded emitter specular is slow-history observation, not a bound on every hit source.",
            ReadbackInputsUnchanged = true, Fixture = "One direct light, the emitter's tube light of face radiance 6800 (tiunr-5jqs), so the emitter's glow reaches no ray and the reference of emitter reflections stands for the light's direct specular; no ambient, environment, atmosphere, bloom, normal map or impostors; emitter black, metallic 0, reflectance 0, roughness 1, authored emission 6800.",
            ProbeDrawsPerRendererFrame = ReflectionComparisonProbe.OutputGroups.Count + (sharing ? 1 : 0),
            ProbeAllEightOutputsValidated = true, MaximumFactorRelativeError = maximumFactorRelativeError,
            AuditedGeometry = geometryAudit, OriginalAnalyticPositionFailuresAreDiagnosticsNotPasses = geometryAudit,
            GeometryAuditFrames = auditDraws.Count,
            NonModificationEvidence = "Genuine whole-image distance/history/composition surrounding GPU copies in each audited renderer frame, retained as raw SHA256 pairs in audit-draws.json; owned attachments and readonly audit descriptors. Host readback-after-readback is not used as proof.",
            PrimaryVisibilityRule = "Original analytic emitter-before-plate rule at native pixel center. Plate contribution is zero behind the emitter; raw outgoing values remain unchanged.",
            RawFile = "measured-specular-r.f32", PlateContributionFile = "measured-plate-specular-r.f32", EmitterFile = "measured-emitter-specular-r.f32",
            MeanQuantity = "VISIBLE PLATE outgoing specular contribution, not whichever surface occupies the pixel.",
            PlatePixels = preflight?.Surfaces.Count(surface => surface == (byte)ReferenceSurface.Plate),
            EmitterPixels = preflight?.Surfaces.Count(surface => surface == (byte)ReferenceSurface.Emitter),
            RetainedDiagnostics = "preflight=first renderer draw; first-measured=draw64; current-measured=latest checked draw, updated only within this new run. Each retains all16 fields, independent distance/history crop and metadata before validation.",
        }, JsonOptions));
        return 0;
    }

    private static RenderSnapshot ComparisonScene(VulkanRenderer renderer, float roughness, MeshData boxGeometry)
    {
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 6);
        var scene = new RenderSnapshot
        {
            Camera = new() { Position = new(0, 3, 6), PreviousPosition = new(0, 3, 6), Rotation = rotation,
                PreviousRotation = rotation, VerticalFov = MathF.PI / 3 }, CameraCuts = 1,
        };
        MeshId box = renderer.CreateMesh(boxGeometry);
        MaterialId plate = renderer.CreateMaterial(new(
            BaseColor: new(new Vector3(0.06f), 1), Roughness: roughness, Reflectance: 0.5f, Model: new StandardShading(Metallic: 0)
        ));
        MaterialId emitter = renderer.CreateMaterial(new(
            BaseColor: new(Vector3.Zero, 1), Roughness: 1, Reflectance: 0, Emissive: new(6800), Model: new StandardShading(Metallic: 0)
        ));
        scene.Instances.Add(RenderInstance.Create(
            mesh: box, material: plate, position: new(0, -0.05f, 0), rotation: Quaternion.Identity, scale: new(100, 0.1f, 100),
            boundingRadius: 71, hemisphere: HemisphereUp.Along(Vector3.UnitY), chainDepth: 0,
            flags: RenderFlags.Visible | RenderFlags.NoImpostor, entityIndex: 1, entityGeneration: 1
        ));
        scene.Instances.Add(RenderInstance.Create(
            mesh: box, material: emitter, position: new(-1, 1, -2), rotation: Quaternion.Identity, scale: new(0.2f, 2, 0.2f),
            boundingRadius: 2, hemisphere: HemisphereUp.Along(Vector3.UnitY), chainDepth: 0,
            flags: RenderFlags.Visible | RenderFlags.NoImpostor, entityIndex: 2, entityGeneration: 1
        ));
        scene.Lights.Add(EmitterLight(position: new(-1, 1, -2), previousPosition: new(-1, 1, -2), radiance: 6800));
        return scene;
    }

    /// <summary>The radius of the emitter's tube light: the capsule 2 m tall with the 0.2 x 2 x 0.2 m box's surface area.</summary>
    private const float EmitterTubeRadius = (4 * 0.2f * 2 + 2 * 0.2f * 0.2f) / (2 * MathF.PI * 2);

    /// <summary>
    /// The tube light that the emitter's entity holds (tiunr-5jqs): upright, as tall as the box, of its surface area, with
    /// the face radiance <paramref name="radiance"/>, which the box's emission equals. Its entity's glow then reaches no ray.
    /// </summary>
    private static TubeRenderLight EmitterLight(Vector3 position, Vector3 previousPosition, float radiance) => new(
        EntityIndex: 2, EntityGeneration: 1, Position: position, PreviousPosition: previousPosition, Axis: Vector3.UnitY,
        PreviousAxis: Vector3.UnitY, Color: Vector3.One, Power: radiance * MathF.PI * 2 * MathF.PI * EmitterTubeRadius * 2,
        Radius: EmitterTubeRadius, Length: 2 - 2 * EmitterTubeRadius, CastsShadow: false, ChainDepth: 0
    );

    private static float[] CropAttachment(float[] image, int channels = 2)
    {
        var values = new float[ComparisonCrop.Width * ComparisonCrop.Height * channels];
        for (int row = 0; row < ComparisonCrop.Height; row++)
            image.AsSpan(((row + ComparisonCrop.Top) * 1280 + ComparisonCrop.Left) * channels, ComparisonCrop.Width * channels)
                .CopyTo(values.AsSpan(row * ComparisonCrop.Width * channels));
        return values;
    }

    private static float[] ReadInputs(ReflectionComparisonProbe probe, float[] light)
    {
        float[][] fields = [CropAttachment(light), .. Enumerable.Range(1, 7).Select(index => CropAttachment(probe.Read(index)))];
        var values = new float[ComparisonCrop.Width * ComparisonCrop.Height * 16];
        for (int pixel = 0; pixel < ComparisonCrop.Width * ComparisonCrop.Height; pixel++)
            for (int field = 0; field < fields.Length; field++)
                for (int channel = 0; channel < 2; channel++)
                    values[pixel * 16 + field * 2 + channel] = fields[field][pixel * 2 + channel];
        return values;
    }

    private static ReferenceCamera ComparisonCamera => new(
        Eye: ComparisonEye, Target: new(0, 0, 6 - 3 * Math.Sqrt(3)), VerticalFov: MathF.PI / 3, Width: 1280, Height: 720
    );

    private static ReferenceEmitter ComparisonEmitter(double radiance) => new(
        Minimum: new(-1f - 0.1f, 0, -8f - 0.1f + 6.0), Maximum: new(-1f + 0.1f, 2, -8f + 0.1f + 6.0), Radiance: new(radiance, radiance, radiance)
    );

    private static void WriteLiveMean(string path, List<double[]> frames)
    {
        var pixels = new Double3[ComparisonCrop.Width * ComparisonCrop.Height];
        for (int pixel = 0; pixel < pixels.Length; pixel++)
        {
            double mean = frames.Average(frame => frame[pixel]);
            pixels[pixel] = new(mean, mean, mean);
        }
        var image = new ReferenceImage(ComparisonCrop.Width, ComparisonCrop.Height, pixels);
        using FileStream output = File.Create(Path.Combine(path, "mean.pfm"));
        image.WritePfm(output);
        image.WritePreview(Path.Combine(path, "mean-preview.png"), ComparisonExposure);
        WriteProfiles(Path.Combine(path, "mean-profiles.csv"), image, ComparisonCrop);
    }

    private static void WriteFloats(string path, float[] values) => File.WriteAllBytes(path, MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

    private static void CreateNewOutput(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
            throw new ArgumentException("Choose a new output path to preserve prior evidence.", nameof(path));
        Directory.CreateDirectory(path);
    }
}
