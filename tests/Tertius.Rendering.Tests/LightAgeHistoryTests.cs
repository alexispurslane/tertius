using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The light-change age of the precise slow specular history, the renderer's behavior (tickets tiunr-py6q and
/// tiunr-7haj): each pixel's light-age record, the age that each ray brings back from its hit, and the receiver's cut of
/// its history to that age, on the real screen tracer and accumulator.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class LightAgeHistoryTests(ITestOutputHelper output)
{
    private const int Width = 128;
    private const int Height = 72;
    private const string Switch = "TERTIUS_REFLECTION_HISTORY_AGREEMENT";

    /// <summary>The draws in a row that a light must stay outside its range to count as a change (screen_trace.glsl).</summary>
    private const int LightChangeDraws = 4;

    /// <summary>A dark dielectric plate of roughness 0.2, the native captures' rough plate.</summary>
    private static readonly MaterialDesc Plate = new(
        BaseColor: new(new Vector3(0.06f), 1),
        Roughness: 0.2f,
        Reflectance: 0.5f,
        Model: new StandardShading(Metallic: 0)
    );

    private static readonly Vector3 SunDirection = new(0.4f, 1, 0.3f);

    [Fact]
    public void AStillJitteredSceneWithShadowEdgesResetsNoLightAge() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                AddSunAndOccluder(renderer, scene);
                Draw(renderer, scene, 16);
                int surface = renderer.ReadDistance().Count(distance => distance > 0);
                long resets = 0;
                long marks = 0;
                for (int frame = 0; frame < 64; frame++)
                {
                    Draw(renderer, scene, 1);
                    float[] record = renderer.ReadLightAge();
                    resets += Enumerable.Range(0, Width * Height).Count(pixel => AgeAt(record, pixel) == 0);
                    marks += MovedMarks(renderer).Count(mark => mark != 0);
                }

                output.WriteLine(
                    $"Still jittered lit scene: {resets} resets over 64 frames of {surface} surface pixels."
                );
                resets
                    .Should()
                    .Be(
                        0,
                        "the jitter and the temporal shadow filter move an edge pixel's light for fewer than four draws"
                    );
                marks.Should().Be(0, "the jitter moves no surface");
            },
            new(Upscaling: new Fsr3Upscaling(new FixedRenderScale(1), 0), CascadedLights: 1)
        );

    [Fact]
    public void AWalkOverTheSteepLightOfAStillLampCommitsNoLightChange() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // A lamp 0.6 m over the floor: its light on the floor falls steeply away from the spot below it, so a pixel
                // that the walk moves across the floor shows a light far from the last light of its nearest tap.
                var lamp = new Vector3(1, ReflectionTesting.FloorHeight + 0.6f, -4);
                scene.Lights.Add(
                    new PointRenderLight(
                        EntityIndex: Gpu.LampEntity,
                        EntityGeneration: 0,
                        Position: lamp,
                        PreviousPosition: lamp,
                        Color: Vector3.One,
                        Power: 4 * MathF.PI * 2,
                        Radius: 0.05f,
                        CastsShadow: false,
                        ChainDepth: TestChains.CamerasFrame
                    )
                );
                Draw(renderer, scene, 16);
                long commits = 0;
                long moves = 0;
                float[] last = renderer.ReadLightAge();

                for (int frame = 0; frame < 48; frame++)
                {
                    Step(scene);
                    Draw(renderer, scene, 1);
                    float[] record = renderer.ReadLightAge();
                    float[] distance = renderer.ReadDistance();
                    int[] away =
                    [
                        .. Enumerable
                            .Range(Width, Width * (Height - 2))
                            .Where(pixel => AwayFromARowDepthEdge(distance, pixel)),
                    ];
                    commits += away.Count(pixel => AgeAt(record, pixel) == 0);
                    moves += away.Count(pixel =>
                        ExactLightAt(last, pixel) > 0
                        && MathF.Abs(ExactLightAt(record, pixel) / ExactLightAt(last, pixel) - 1) > 0.02f
                    );
                    last = record;
                }

                output.WriteLine(
                    $"Walk toward a still lamp: {commits} committed light changes, {moves} pixel moves over 48 draws."
                );
                moves
                    .Should()
                    .BeGreaterThan(
                        1000,
                        "the walk must move pixels across the lamp's steep light by more than the share"
                    );
                commits.Should().Be(0, "no light changed: the walk only moves the pixels over the lamp's still light");
            }
        );

    [Fact]
    public void AWalkTowardADimmingEmitterStillCommitsItsChange() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 16);
                for (int frame = 0; frame < 8; frame++)
                {
                    Step(scene);
                    Draw(renderer, scene, 1);
                }
                SetGlow(renderer, scene, new(3.2f));

                for (int frame = 0; frame < LightChangeDraws + 2; frame++)
                {
                    Step(scene);
                    Draw(renderer, scene, 1);
                }

                float[] record = renderer.ReadLightAge();
                int[] interior = InteriorPixels(record, least: 2.5f);
                output.WriteLine(
                    "Ages of the interior after the walk: "
                        + string.Join(
                            ", ",
                            interior
                                .GroupBy(pixel => AgeAt(record, pixel))
                                .OrderBy(g => g.Key)
                                .Select(g => $"{g.Key}: {g.Count()}")
                        )
                );
                interior.Length.Should().BeGreaterThan(20, "the emitter must cover pixels whose neighbors are all lit");
                // At least 90%, not the 95% of a still camera: under motion, 7.5% of the interior misses the change, also
                // with the band around the nearest tap's light that the record took before tb-8zew (ticket tb-4g6h).
                interior
                    .Count(pixel => AgeAt(record, pixel) is >= 0 and <= 2)
                    .Should()
                    .BeGreaterThanOrEqualTo(
                        interior.Length * 90 / 100,
                        "the glow fell 20% while the camera walked, and the change counts on its fourth draw"
                    );
            }
        );

    [Fact]
    public void APixelWhoseJitterAlternatesAThinGlowingStripAndTheFloorResetsNoLightAge() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // A strip on the floor in front of the emitter, 0.4 pixels deep on screen and centered on row 60: the jitter
                // puts a pixel's sample on it in some draws and on the dark floor in others. After a miss, no neighbor
                // showed its light. Below the horizon at row 36, a floor point d m ahead lies k / d rows lower.
                const int row = 60;
                float k =
                    10
                    * (
                        Gpu.Project(point: new(0, ReflectionTesting.FloorHeight, -10), width: Width, height: Height).Y
                        - Height / 2f
                    );
                float distance = k / (row + 0.5f - Height / 2f);
                RenderInstance strip = ReflectionTesting.Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: ReflectionTesting.Glow(renderer, new(4)),
                    center: new(0, ReflectionTesting.FloorHeight + 0.002f, -distance),
                    size: new(2, 0.004f, 0.4f * distance * distance / k)
                );
                strip.EntityIndex = 3;
                scene.Instances.Add(strip);
                Draw(renderer, scene, 16);
                float[] least = new float[Width * Height];
                float[] most = new float[Width * Height];
                Array.Fill(least, float.MaxValue);
                long resets = 0;

                for (int frame = 0; frame < 64; frame++)
                {
                    Draw(renderer, scene, 1);
                    float[] record = renderer.ReadLightAge();
                    float[] hdr = renderer.ReadHdr();
                    for (int pixel = 0; pixel < Width * Height; pixel++)
                    {
                        least[pixel] = MathF.Min(least[pixel], Luminance(hdr, pixel));
                        most[pixel] = MathF.Max(most[pixel], Luminance(hdr, pixel));
                        resets += AgeAt(record, pixel) == 0 ? 1 : 0;
                    }
                }

                int flickering = Enumerable
                    .Range(row * Width, Width)
                    .Count(pixel => least[pixel] < 1 && most[pixel] >= 3);
                output.WriteLine($"Thin strip: {flickering} pixels flicker, {resets} resets over 64 draws.");
                flickering.Should().BeGreaterThan(10, "the jitter must move pixels between the strip and the floor");
                resets
                    .Should()
                    .Be(0, "the jitter's sample sequence keeps no pixel on the strip for four draws in a row");
            },
            new(Upscaling: new Fsr3Upscaling(new FixedRenderScale(1), 0))
        );

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ByDefaultALastingDimmingOfTheEmitterShortensTheReflectionHistoryToTheLightsAge(string? value) =>
        WithRenderer(
            value,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 64);
                float[] warm = Slow(renderer);
                SetGlow(renderer, scene, new(3.2f));

                Draw(renderer, scene, LightChangeDraws);

                float[] cut = Slow(renderer);
                Enumerable
                    .Range(Height / 2 * Width, Width * Height / 2)
                    .Count(pixel => LengthAt(warm, pixel) > 32 && LengthAt(cut, pixel) <= 1)
                    .Should()
                    .BeGreaterThan(30, "the receivers that reflect the emitter keep only the frame since the change");
            }
        );

    [Fact]
    public void TheCappedPreciseHistoryAlsoShortensToTheLightsAge()
    {
        string? mode = Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY");
        Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", "precise-capped");
        try
        {
            WithRenderer(
                null,
                (renderer, scene) =>
                {
                    Draw(renderer, scene, 64);
                    float[] warm = Slow(renderer);
                    SetGlow(renderer, scene, new(3.2f));

                    Draw(renderer, scene, LightChangeDraws);

                    float[] cut = Slow(renderer);
                    Enumerable
                        .Range(Height / 2 * Width, Width * Height / 2)
                        .Count(pixel => LengthAt(warm, pixel) > 8 && LengthAt(cut, pixel) <= 1)
                        .Should()
                        .BeGreaterThan(
                            30,
                            "every precise history keeps the light age, also below its cap of 32 frames"
                        );
                }
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", mode);
        }
    }

    [Fact]
    public void AStillLitSceneBringsNoAgeThatCouldCutAHistory() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // The first draw shows every instance as new, with no last place, which is motion evidence. So the count
                // starts at the second draw.
                AddSunAndOccluder(renderer, scene);
                Draw(renderer, scene, 1);
                long aged = 0;

                for (int frame = 1; frame < 32; frame++)
                {
                    Draw(renderer, scene, 1);
                    aged += SampleAges(renderer).Count(age => age != 0);
                    aged += renderer
                        .ReadImage(renderer.ScreenTraceSignals.Diffuse.SampleAge(0)!)
                        .Count(age => age != 0);
                }

                aged.Should()
                    .Be(0, "no light changes and nothing moves, so no ray brings evidence and no history is cut");
            },
            new(CascadedLights: 1)
        );

    [Fact]
    public void ATwentyPercentDimmingOfAnEmitterResetsTheAgeOfItsPixels() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);
                float[] before = renderer.ReadLightAge();
                int[] interior = InteriorPixels(before, least: 3);
                int[] floor =
                [
                    .. Enumerable.Range(60 * Width, 8 * Width).Where(pixel => ExactLightAt(before, pixel) == 0),
                ];
                interior.Length.Should().BeGreaterThan(20, "the emitter must cover pixels whose neighbors are all lit");
                SetGlow(renderer, scene, new(3.2f));

                Draw(renderer, scene, LightChangeDraws);

                float[] after = renderer.ReadLightAge();
                interior
                    .Count(pixel => AgeAt(after, pixel) == 0)
                    .Should()
                    .BeGreaterThanOrEqualTo(interior.Length * 95 / 100, "the glow fell 20%, ten times the tolerance");
                floor.Should().NotContain(pixel => AgeAt(after, pixel) == 0, "the floor's own light did not change");
            }
        );

    [Fact]
    public void TheAgeOfAChangedPixelCountsUpOneFramePerFrame() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);
                int[] interior = InteriorPixels(renderer.ReadLightAge(), least: 3);
                SetGlow(renderer, scene, new(3.2f));
                Draw(renderer, scene, LightChangeDraws);

                Draw(renderer, scene, 5);

                float[] record = renderer.ReadLightAge();
                interior.Length.Should().BeGreaterThan(20);
                interior
                    .Count(pixel => AgeAt(record, pixel) == 5)
                    .Should()
                    .BeGreaterThanOrEqualTo(interior.Length * 95 / 100, "five frames passed since the change");
            }
        );

    [Fact]
    public void AGlowThatDimsForFewerDrawsThanAChangeNeedsGivesNoEvidence() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);
                int[] interior = InteriorPixels(renderer.ReadLightAge(), least: 3);
                interior.Length.Should().BeGreaterThan(20);

                // Two flickers of three draws, one draw apart: the draw back inside the range starts the count again.
                SetGlow(renderer, scene, new(3.2f));
                Draw(renderer, scene, LightChangeDraws - 1);
                SetGlow(renderer, scene, new(4));
                Draw(renderer, scene, 1);
                SetGlow(renderer, scene, new(3.2f));
                Draw(renderer, scene, LightChangeDraws - 1);
                SetGlow(renderer, scene, new(4));
                Draw(renderer, scene, 4);

                float[] record = renderer.ReadLightAge();
                interior
                    .Should()
                    .OnlyContain(pixel => AgeAt(record, pixel) < 0, "no move lasted the draws that a change needs");
                interior
                    .Count(pixel => SeenFramesAt(record, pixel) == 31 + 2 * (LightChangeDraws - 1) + 5)
                    .Should()
                    .BeGreaterThanOrEqualTo(
                        interior.Length * 95 / 100,
                        "the record still counts the frames since the reset"
                    );
            }
        );

    [Fact]
    public void WhileADimmingIsPendingTheRecordKeepsTheOldLightAndCountsItsDraws() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);
                float[] warm = renderer.ReadLightAge();
                int[] interior = InteriorPixels(warm, least: 3);
                interior.Length.Should().BeGreaterThan(20);
                SetGlow(renderer, scene, new(3.2f));

                Draw(renderer, scene, LightChangeDraws - 1);

                float[] record = renderer.ReadLightAge();
                interior
                    .Count(pixel =>
                        ExactLightAt(record, pixel) == ExactLightAt(warm, pixel)
                        && PendingDrawsAt(record, pixel) == LightChangeDraws - 1
                        && SeenFramesAt(record, pixel) == 31 + LightChangeDraws - 1
                    )
                    .Should()
                    .BeGreaterThanOrEqualTo(
                        interior.Length * 95 / 100,
                        "the change has not lasted long enough to count"
                    );
            }
        );

    [Fact]
    public void ALastingDimmingCountsAsAChangeOnItsFourthDrawWithTheNewLight() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);
                float[] warm = renderer.ReadLightAge();
                int[] interior = InteriorPixels(warm, least: 3);
                interior.Length.Should().BeGreaterThan(20);
                SetGlow(renderer, scene, new(3.2f));

                Draw(renderer, scene, LightChangeDraws);

                float[] record = renderer.ReadLightAge();
                interior
                    .Count(pixel =>
                        AgeAt(record, pixel) == 0
                        && PendingDrawsAt(record, pixel) == 0
                        && MathF.Abs(ExactLightAt(record, pixel) - 0.8f * ExactLightAt(warm, pixel))
                            <= 0.005f * ExactLightAt(warm, pixel)
                    )
                    .Should()
                    .BeGreaterThanOrEqualTo(interior.Length * 95 / 100, "the glow stayed 20% lower for four draws");
            }
        );

    [Fact]
    public void AfterALastingDimmingTheReceiversBlendTakesNoOldLight() =>
        WithRenderer(
            "light-age-diagnostic",
            (renderer, scene) =>
            {
                Draw(renderer, scene, 64);
                float[] warm = Slow(renderer);
                SetGlow(renderer, scene, new(3.2f));

                Draw(renderer, scene, LightChangeDraws);

                float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
                float[] ages = SampleAges(renderer);
                float[] diagnostic = UncoveredDiagnostic(renderer);
                int[] bright =
                [
                    .. Enumerable
                        .Range(Height / 2 * Width, Width * Height / 2)
                        .Where(pixel =>
                            ages[pixel] == 1
                            && LengthAt(warm, pixel) > 8
                            && Luminance(sample, pixel) >= 0.3f * Luminance(warm, pixel)
                        ),
                ];
                bright.Length.Should().BeGreaterThan(30, "the plate reflects the emitter");
                bright
                    .Should()
                    .OnlyContain(
                        pixel => LengthBeforeBlendAt(diagnostic, pixel) == 0,
                        "the pending draws blended the new light into the old, so the commit keeps none of it"
                    );
            }
        );

    [Fact]
    public void AfterALastingDimmingTheDiffuseHistoryKeepsOnlyTheNewLight() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 64);
                TemporalSignal diffuse = renderer.ScreenTraceSignals.Diffuse;
                float[] warm = renderer.ReadImage(diffuse.Slow(0));
                SetGlow(renderer, scene, new(3.2f));

                Draw(renderer, scene, LightChangeDraws);

                float[] sample = renderer.ReadImage(diffuse.Sample(0));
                float[] ages = renderer.ReadImage(
                    diffuse.SampleAge(0)
                        ?? throw new InvalidOperationException("The precise history keeps the light age.")
                );
                float[] slow = renderer.ReadImage(diffuse.Slow(0));
                int[] cut =
                [
                    .. Enumerable
                        .Range(0, Width * Height)
                        .Where(pixel =>
                            ages[pixel] == 1
                            && LengthAt(warm, pixel) > 8
                            && Luminance(sample, pixel) >= 0.3f * Luminance(warm, pixel)
                        ),
                ];
                cut.Length.Should().BeGreaterThan(0, "the diffuse rays of some pixels hit the dimmed emitter");
                cut.Should()
                    .OnlyContain(
                        pixel => LengthAt(slow, pixel) == 1,
                        "the commit cuts the history to the change, so it holds this frame's sample alone"
                    );
            }
        );

    [Fact]
    public void AMoveBelowTheCutoffRadianceChangesNoLightAge() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                SetGlow(renderer, scene, new(4e-4f));
                Draw(renderer, scene, 32);
                int[] interior = InteriorPixels(renderer.ReadLightAge(), least: 3e-4f);
                interior.Length.Should().BeGreaterThan(20);
                // 25% darker, but by 1e-4 W/(m² sr), below the cutoff radiance of 2^−13 at exposure 0.
                SetGlow(renderer, scene, new(3e-4f));

                Draw(renderer, scene, 2 * LightChangeDraws);

                float[] record = renderer.ReadLightAge();
                interior.Should().OnlyContain(pixel => AgeAt(record, pixel) < 0, "such a move changes no pixel");
            }
        );

    [Fact]
    public void ABrighteningBelowTheCutoffRadianceChangesNoLightAge() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                SetGlow(renderer, scene, new(3e-4f));
                Draw(renderer, scene, 32);
                int[] interior = InteriorPixels(renderer.ReadLightAge(), least: 2.5e-4f);
                interior.Length.Should().BeGreaterThan(20);
                // 33% brighter, but by 1e-4 W/(m² sr), below the cutoff radiance of 2^−13 at exposure 0.
                SetGlow(renderer, scene, new(4e-4f));

                Draw(renderer, scene, 2 * LightChangeDraws);

                float[] record = renderer.ReadLightAge();
                interior.Should().OnlyContain(pixel => AgeAt(record, pixel) < 0, "the floor holds in both directions");
            }
        );

    [Fact]
    public void ALightThatDriftsByLessThanTheShareInEachDrawCountsAsNoChange() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);
                int[] interior = InteriorPixels(renderer.ReadLightAge(), least: 3);
                interior.Length.Should().BeGreaterThan(20);
                float glow = 4;

                // 1% darker in each of 12 draws, 11% in all: the record follows a light that stays near its own.
                for (int frame = 0; frame < 12; frame++)
                {
                    glow *= 0.99f;
                    SetGlow(renderer, scene, new(glow));
                    Draw(renderer, scene, 1);
                }

                float[] record = renderer.ReadLightAge();
                interior
                    .Should()
                    .OnlyContain(pixel => AgeAt(record, pixel) < 0, "no draw moved the light by more than the share");
            }
        );

    [Fact]
    public void AMoveAboveTheCutoffRadianceChangesTheLightAge() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                SetGlow(renderer, scene, new(4e-4f));
                Draw(renderer, scene, 32);
                int[] interior = InteriorPixels(renderer.ReadLightAge(), least: 3e-4f);
                interior.Length.Should().BeGreaterThan(20);
                // Darker by 1.5e-4 W/(m² sr), above the cutoff radiance of 2^−13 at exposure 0.
                SetGlow(renderer, scene, new(2.5e-4f));

                Draw(renderer, scene, LightChangeDraws);

                float[] record = renderer.ReadLightAge();
                interior
                    .Count(pixel => AgeAt(record, pixel) == 0)
                    .Should()
                    .BeGreaterThanOrEqualTo(interior.Length * 95 / 100, "the move exceeds the cutoff radiance");
            }
        );

    [Fact]
    public void UnderAHigherExposureASmallerMoveChangesTheLightAge() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                SetGlow(renderer, scene, new(4e-4f));
                Draw(renderer, scene, 32);
                int[] interior = InteriorPixels(renderer.ReadLightAge(), least: 3e-4f);
                interior.Length.Should().BeGreaterThan(20);
                // The move of 1e-4 W/(m² sr) that changes no age at exposure 0 shows 8 times brighter at 3 stops.
                SetGlow(renderer, scene, new(3e-4f));

                Draw(renderer, scene, LightChangeDraws);

                float[] record = renderer.ReadLightAge();
                interior
                    .Count(pixel => AgeAt(record, pixel) == 0)
                    .Should()
                    .BeGreaterThanOrEqualTo(
                        interior.Length * 95 / 100,
                        "the floor is the cutoff with the exposure undone"
                    );
            },
            new(Exposure: new FixedExposure(3))
        );

    [Fact]
    public void TheFloorHoldsForTheDemodulatedLightNotForTheLightThatADarkSurfaceShows() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                scene.Lights.Clear();
                scene.Lights.Add(Sun(irradiance: 0.02f));
                Draw(renderer, scene, 32);
                float[] before = renderer.ReadLightAge();
                int[] floor = [.. Enumerable.Range(60 * Width, 8 * Width)];
                scene.Lights[0] = Sun(irradiance: 0.017f);

                Draw(renderer, scene, LightChangeDraws);

                float[] after = renderer.ReadLightAge();
                float move = floor.Min(pixel => ExactLightAt(before, pixel) - ExactLightAt(after, pixel));
                float shown = floor.Max(pixel => ExactLightAt(before, pixel) - ExactLightAt(after, pixel)) * 0.06f;
                output.WriteLine($"Plate: demodulated move at least {move}, shown move at most {shown} W/(m² sr).");
                move.Should().BeGreaterThan(LightUnits.CutoffRadiance, "the demodulated move must exceed the floor");
                shown
                    .Should()
                    .BeLessThan(LightUnits.CutoffRadiance, "the move times the plate's albedo of 0.06 must not");
                floor
                    .Count(pixel => AgeAt(after, pixel) == 0)
                    .Should()
                    .BeGreaterThanOrEqualTo(
                        floor.Length * 95 / 100,
                        "the record cannot split glow from direct light, so it misses no move that a white surface shows"
                    );
            },
            new(CascadedLights: 1)
        );

    [Fact]
    public void AReceiverWhoseRayHitsTheDimmedEmitterKeepsOnlyTheFramesSinceTheChange() =>
        AReceiverWhoseRayHitsTheChangedEmitterKeepsOnlyTheFramesSinceTheChange(new(3.2f));

    [Fact]
    public void AReceiverWhoseRayHitsTheBrightenedEmitterKeepsOnlyTheFramesSinceTheChange() =>
        AReceiverWhoseRayHitsTheChangedEmitterKeepsOnlyTheFramesSinceTheChange(new(4.8f));

    [Fact]
    public void ACutHistoryGrowsBackWithTheAgeOfTheLight() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 64);
                float[] warm = Slow(renderer);
                SetGlow(renderer, scene, new(3.2f));
                Draw(renderer, scene, LightChangeDraws);
                float[] cut = Slow(renderer);
                int[] receivers =
                [
                    .. Enumerable
                        .Range(Height / 2 * Width, Width * Height / 2)
                        .Where(pixel => LengthAt(warm, pixel) > 32 && LengthAt(cut, pixel) <= 1),
                ];
                receivers.Length.Should().BeGreaterThan(30, "the dimming must cut receivers first");

                Draw(renderer, scene, 10);
                float[] early = Slow(renderer);
                Draw(renderer, scene, 30);
                float[] late = Slow(renderer);

                receivers
                    .Should()
                    .OnlyContain(
                        pixel => LengthAt(early, pixel) <= 12,
                        "the history holds no frame from before the change"
                    );
                receivers.Average(pixel => LengthAt(early, pixel)).Should().BeGreaterThan(4);
                receivers
                    .Average(pixel => LengthAt(late, pixel))
                    .Should()
                    .BeGreaterThan(20, "the age of the light grows, so the length grows back with it");
            }
        );

    /// <summary>The fast cap of the bounce light: the length to which light evidence cuts its histories.</summary>
    private const float DiffuseFastCap = 4;

    [Fact]
    public void ALampThatGoesOutCutsTheBounceHistoriesOfTheBlocksThatSeeItsLight() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // The emitter goes out, and 4 draws later the record counts the change. A block of 8 × 8 pixels whose
                // diffuse rays bring its light code has light evidence, which cuts every history of the block to the fast
                // cap, also the histories of the pixels whose own rays missed the emitter in this draw
                // (docs/plans/bounce-light-confidence.md, section 8.2).
                Draw(renderer, scene, 64);
                SetGlow(renderer, scene, Vector3.Zero);

                Draw(renderer, scene, LightChangeDraws);

                float[] codes = DiffuseLightCodes(renderer);
                float[] slow = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Slow(0));
                int[] seeing =
                [
                    .. Enumerable
                        .Range(0, Width * Height)
                        .Where(pixel => codes[pixel] is >= 1 and <= 7)
                        .Select(BlockOf)
                        .Distinct(),
                ];
                output.WriteLine($"Blocks whose diffuse rays saw the lamp go out: {seeing.Length}.");
                seeing.Length.Should().BeGreaterThan(10, "the floor's rays see the emitter");
                Enumerable
                    .Range(0, Width * Height)
                    .Where(pixel => seeing.Contains(BlockOf(pixel)))
                    .Select(pixel => LengthAt(slow, pixel))
                    .Should()
                    .OnlyContain(length => length <= DiffuseFastCap, "light evidence cuts every history of its block");
            }
        );

    [Fact]
    public void AfterALampGoesOutTheBounceLightSettlesWithinFivePercentInSixtyDraws() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // The settle tolerance of the epic: 60 draws after the emitter goes out, the floor's bounce light keeps at
                // most 5 % of the step to where it settles, the mean of draws 200 to 231 (docs/plans/bounce-light-confidence.md,
                // section 22, point 5). The floor's bounce light comes almost all from the emitter. The widened box alone
                // would drain the old light at 1 / 32 per draw.
                Draw(renderer, scene, 64);
                float before = FloorBounceLight(renderer);
                SetGlow(renderer, scene, Vector3.Zero);

                Draw(renderer, scene, 60);
                float early = FloorBounceLight(renderer);
                Draw(renderer, scene, 140);
                float settled = 0;
                for (int draw = 0; draw < 32; draw++)
                {
                    Draw(renderer, scene, 1);
                    settled += FloorBounceLight(renderer) / 32;
                }

                output.WriteLine(
                    $"Floor bounce light: {before:F4} lit, {early:F4} after 60 draws, {settled:F4} settled."
                );
                (before - settled).Should().BePositive("the emitter lit the floor");
                (early - settled)
                    .Should()
                    .BeLessThanOrEqualTo(0.05f * (before - settled), "the bounce light settles within 60 draws");
            }
        );

    [Fact]
    public void ADisoccludedSurfaceHasNoLightAgeEvidence() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                RenderInstance occluder = AddDarkSunAndOccluder(renderer, scene);
                Draw(renderer, scene, 4);
                scene.Lights[0] = Sun(irradiance: 3);
                Draw(renderer, scene, 28);
                float[] distanceBefore = renderer.ReadDistance();
                occluder.PreviousPosition = occluder.Position;
                occluder.Position.X += 1.5f;
                scene.Instances[^1] = occluder;

                Draw(renderer, scene, 1);

                float[] distanceAfter = renderer.ReadDistance();
                float[] record = renderer.ReadLightAge();
                int[] revealed =
                [
                    .. Enumerable
                        .Range(0, Width * Height)
                        .Where(pixel =>
                            distanceAfter[pixel] > distanceBefore[pixel] + 0.3f && distanceBefore[pixel] > 0
                        ),
                ];
                revealed.Length.Should().BeGreaterThan(10, "the occluder's move must reveal floor behind it");
                revealed
                    .Should()
                    .OnlyContain(
                        pixel => AgeAt(record, pixel) == ScreenTraceImages.NoLightAge,
                        "a surface without a history has no age"
                    );
                Enumerable
                    .Range(60 * Width, 8 * Width)
                    .Count(pixel => AgeAt(record, pixel) >= 20)
                    .Should()
                    .BeGreaterThan(4 * Width, "the floor saw the sun come on 29 frames ago");
            },
            new(CascadedLights: 1)
        );

    [Fact]
    public void ARayThatMissesBringsNoEvidence() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);
                SetGlow(renderer, scene, new(3.2f));

                Draw(renderer, scene, LightChangeDraws);

                float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
                float[] ages = SampleAges(renderer);
                int[] receivers = [.. Enumerable.Range(Height / 2 * Width, Width * Height / 2)];
                int[] misses = [.. receivers.Where(pixel => sample[4 * pixel + 3] == 0)];
                misses.Length.Should().BeGreaterThan(50, "the plate must reflect the sky somewhere");
                misses.Should().OnlyContain(pixel => ages[pixel] == 0, "the environment's light has no age");
                receivers
                    .Count(pixel => ages[pixel] == 1)
                    .Should()
                    .BeGreaterThan(30, "rays that hit the dimmed emitter bring its age 0");
            }
        );

    [Fact]
    public void ARoughReceiversDiffuseRayBringsTheAgeOfTheDimmedEmitter() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);
                SetGlow(renderer, scene, new(3.2f));

                Draw(renderer, scene, LightChangeDraws);

                float[] ages = SampleAges(renderer);
                Enumerable
                    .Range(Height / 2 * Width, Width * Height / 2)
                    .Count(pixel => ages[pixel] == 1)
                    .Should()
                    .BeGreaterThan(
                        30,
                        "above the tracer's roughest reflection, the specular sample takes the diffuse ray"
                    );
            },
            new(Roughness: 0.7f)
        );

    [Fact]
    public void ARayOfAChangedLightCutsTheReceiversHistoryHoweverDimTheRayIs() =>
        WithRenderer(
            "light-age-diagnostic",
            (renderer, scene) =>
            {
                SetGlow(renderer, scene, new(0.4f));
                Draw(renderer, scene, 64);
                float[] warm = Slow(renderer);
                // Half the glow: a shared ray weighs about as the receiver's own ray does (ter-xo83), so only a large step
                // gives more than 20 rays below a fifth of a history that the sky lights.
                SetGlow(renderer, scene, new(0.2f));

                Draw(renderer, scene, LightChangeDraws);

                float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
                float[] ages = SampleAges(renderer);
                float[] diagnostic = UncoveredDiagnostic(renderer);
                // The receivers whose history is still long just before the cut: the slow history's clamp after the
                // blend of the change draws shortens a few others to 4 to 6 frames first.
                int[] dim =
                [
                    .. Enumerable
                        .Range(Height / 2 * Width, Width * Height / 2)
                        .Where(pixel =>
                            ages[pixel] == 1
                            && LengthAt(warm, pixel) > 8
                            && LengthBeforeCutAt(diagnostic, pixel) > 8
                            && Luminance(sample, pixel) <= 0.2f * Luminance(warm, pixel)
                        ),
                ];
                output.WriteLine($"Receivers with a dim ray of age 0: {dim.Length}.");
                dim.Length.Should().BeGreaterThan(20, "the bright sky outweighs the dim emitter in these histories");
                // A cut that only bright rays may make restarts the history from its bright samples: after a lamp goes
                // out, the reflections jumped up by 72 % and took 128 draws to settle (ter-g3tk). The cut alone, from
                // the diagnostic: every such receiver keeps only the frames since the change.
                dim.Should()
                    .OnlyContain(
                        pixel => LengthBeforeBlendAt(diagnostic, pixel) == 0,
                        "the light that the ray sees is 0 frames old, however dim the ray is"
                    );
            },
            new(Gray: 4)
        );

    [Fact]
    public void ARayThatHitsAMovingObjectCarriesItsMovedMark() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // The emitter slides sideways by 2 cm per draw, about half a pixel at 5 m, and the camera stands still. So
                // the floor's pixels do not move, and a floor sample's motion bit comes only from a ray that hit the
                // emitter, whose pixels the plan marked as moved (docs/plans/bounce-light-confidence.md, section 8.4).
                Draw(renderer, scene, 32);

                for (int draw = 0; draw < 4; draw++)
                {
                    Slide(scene, instance: 1, new Vector3(0.02f, 0, 0));
                    Draw(renderer, scene, 1);
                }

                float[] ages = SampleAges(renderer);
                int moved = Enumerable
                    .Range(Height / 2 * Width, Width * Height / 2)
                    .Count(pixel => SawMotion(ages[pixel]));
                output.WriteLine($"Floor samples whose rays hit the moving emitter: {moved}.");
                moved.Should().BeGreaterThan(30, "the plate reflects the moving emitter");
            }
        );

    [Fact]
    public void ARayThatHitsAStillSurfaceWhileTheCameraMovesCarriesNoMovedMark() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // The camera walks toward the emitter and the scene stands still. The plan compares each pixel's motion
                // target with the motion of a still point at its surface point, by the motion target's own rule, so the two
                // agree up to the rounding of the target's half floats, and no surface carries a moved mark.
                Draw(renderer, scene, 16);
                long marked = 0;

                for (int draw = 0; draw < 16; draw++)
                {
                    Step(scene);
                    Draw(renderer, scene, 1);
                    marked += MovedMarks(renderer).Count(mark => mark != 0);
                }

                marked.Should().Be(0, "a still surface moves only with the camera");
            }
        );

    [Fact]
    public void UnderAStillCameraAStillSurfaceLiesExactlyWhereAStillPointWould() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // Both frames' matrices agree, so the motion target and the still motion take one rule to the same
                // places, and the moved test sees no distance between them (docs/plans/bounce-light-confidence.md,
                // section 8.4).
                Draw(renderer, scene, 16);
                ScreenTraceProbe probe = ScreenTraceProbe.On(renderer, "moved_surface_probe.comp");
                Draw(renderer, scene, 1);

                float[] apart = probe.Read();
                int[] hittable = HittablePixels(apart);
                hittable.Length.Should().BeGreaterThan(Width * Height / 2, "the floor and the emitter fill the view");
                int[] off = [.. hittable.Where(pixel => apart[4 * pixel] != 0)];
                output.WriteLine($"DEBUG nonzero {off.Length} of {hittable.Length}; largest {hittable.Max(pixel => apart[4 * pixel])}; moved {hittable.Count(pixel => apart[4 * pixel + 2] != 0)}");
                foreach (int pixel in off.Take(16))
                    output.WriteLine($"DEBUG {pixel % Width},{pixel / Width}: {apart[4 * pixel]} {apart[4 * pixel + 1]} {apart[4 * pixel + 2]} {apart[4 * pixel + 3]}");
                hittable.Should().OnlyContain(pixel => apart[4 * pixel] == 0, "both places are the same point's");
                // The depth ratios lie near 1, where a half float steps by 2^-11. The Vulkan spec lets the target's store
                // round toward 0 or to the nearest half, so the two can differ by one step, half the moved test's bound.
                hittable
                    .Should()
                    .OnlyContain(pixel => apart[4 * pixel + 1] <= 1f / 2048 && apart[4 * pixel + 2] == 0);
            }
        );

    [Fact]
    public void UnderASidewaysCameraAStillSurfaceLiesFarInsideTheLeastMotionOfAStillPoint() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // The camera moves sideways by 4 cm per draw, so every surface moves on screen by about 0.1 pixel or
                // more. A still surface's motion target and its still motion differ by the rounding alone: far below
                // LeastMotion, 1/32 pixel, at every pixel.
                Draw(renderer, scene, 16);
                ScreenTraceProbe probe = ScreenTraceProbe.On(renderer, "moved_surface_probe.comp");
                scene.Camera.PreviousPosition = scene.Camera.Position;
                scene.Camera.Position += new Vector3(0.04f, 0, 0);
                Draw(renderer, scene, 1);

                float[] apart = probe.Read();
                int[] hittable = HittablePixels(apart);
                hittable.Length.Should().BeGreaterThan(Width * Height / 2, "the floor and the emitter fill the view");
                float most = hittable.Max(pixel => apart[4 * pixel]);
                output.WriteLine($"The largest distance from a still point's place: {most} pixels.");
                hittable.Should().OnlyContain(pixel => apart[4 * pixel + 3] > 0.05f, "the camera moved every pixel");
                hittable.Should().OnlyContain(pixel => apart[4 * pixel] < 1f / 128 && apart[4 * pixel + 2] == 0);
            }
        );

    [Fact]
    public void TheMovedMarkFallsOnAMovingBlockAndNotOnAStillEmitterWhoseLightChanges() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // The emitter dims in place while a white block slides along the floor. The emitter's exact light changes,
                // so its record counts a change 4 draws later, while its surface stays where it was: a changed light is
                // light evidence, not motion (docs/plans/bounce-light-confidence.md, section 9, causes 1 and 3). Only the
                // block's own pixels carry the moved mark.
                scene.Instances.Add(Occluder(renderer));
                Draw(renderer, scene, 32);
                SetGlow(renderer, scene, new(3.2f));

                for (int draw = 0; draw < LightChangeDraws; draw++)
                {
                    Slide(scene, instance: scene.Instances.Count - 1, new Vector3(0.1f, 0, 0));
                    Draw(renderer, scene, 1);
                }

                float[] record = renderer.ReadLightAge();
                float[] marks = MovedMarks(renderer);
                int[] changed = [.. Enumerable.Range(0, Width * Height).Where(pixel => AgeAt(record, pixel) == 0)];
                output.WriteLine(
                    $"Pixels whose light changed: {changed.Length}; marked pixels: {marks.Count(mark => mark != 0)}."
                );
                changed.Length.Should().BeGreaterThan(30, "the emitter's light changed");
                changed.Should().OnlyContain(pixel => marks[pixel] == 0, "the emitter stands still");
                marks.Count(mark => mark != 0).Should().BeGreaterThan(10, "the block's own pixels moved");
            }
        );

    [Fact]
    public void ThePixelsOwnMotionSetsTheMotionBitOfItsSamples() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                // The camera moves sideways by 4 cm per draw, so every surface pixel moves on screen by more than LeastMotion,
                // 1/32 pixel per draw: the farthest floor, about 50 m away, moves by about 0.1 pixel. The camera's moves
                // change what the rays see, so every sample of both signals saw motion.
                Draw(renderer, scene, 16);

                for (int draw = 0; draw < 4; draw++)
                {
                    scene.Camera.PreviousPosition = scene.Camera.Position;
                    scene.Camera.Position += new Vector3(0.04f, 0, 0);
                    Draw(renderer, scene, 1);
                }

                float[] distance = renderer.ReadDistance();
                int[] surface = [.. Enumerable.Range(0, Width * Height).Where(pixel => distance[pixel] > 0)];
                surface.Length.Should().BeGreaterThan(Width * Height / 2);
                float[] specular = SampleAges(renderer);
                float[] diffuse = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.SampleAge(0)!);
                surface.Should().OnlyContain(pixel => SawMotion(specular[pixel]) && SawMotion(diffuse[pixel]));
            }
        );

    [Theory]
    [InlineData(null)]
    [InlineData("light-age-diagnostic")]
    public void TheMovedMarkLeavesTheLightCodeOfTheRecordAsItWas(string? value) =>
        WithRenderer(
            value,
            (renderer, scene) =>
            {
                // The emitter dims while it slides, so a ray that hits it brings both its light code, 1 four draws after
                // the dimming, and its moved mark. The mark sits in bit 15 of the record above the light code, which
                // narrowed to 14 bits, and under the diagnostic above its narrowed field (section 8.5).
                Draw(renderer, scene, 32);
                SetGlow(renderer, scene, new(3.2f));

                for (int draw = 0; draw < LightChangeDraws; draw++)
                {
                    Slide(scene, instance: 1, new Vector3(0.02f, 0, 0));
                    Draw(renderer, scene, 1);
                }

                SampleAges(renderer)
                    .Count(age => age == (TemporalSignal.SampleMotionBit | 1))
                    .Should()
                    .BeGreaterThan(30, "rays that hit the dimmed, moving emitter bring its age 0 and its mark");
            }
        );

    [Fact]
    public void AnExposureChangeAloneResetsNoLightAge() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                AddSunAndOccluder(renderer, scene);
                Draw(renderer, scene, 64);
                // The ambient light is not exact light, but the meter sees it, so the exposure adapts to it.
                scene.Ambient.Intensity = 8;
                float before = renderer.ReadExposureStops().Stops;
                int surface = renderer.ReadDistance().Count(distance => distance > 0);
                long resets = 0;
                for (int frame = 0; frame < 56; frame++)
                {
                    Draw(renderer, scene, 1);
                    float[] record = renderer.ReadLightAge();
                    resets += Enumerable.Range(0, Width * Height).Count(pixel => AgeAt(record, pixel) == 0);
                }

                float after = renderer.ReadExposureStops().Stops;
                MathF.Abs(after - before).Should().BeGreaterThan(0.1f, "the real exposure must adapt");
                output.WriteLine(
                    $"Exposure {before} -> {after} stops: {resets} resets of {56 * surface} pixel-frames."
                );
                (resets / (56.0 * surface)).Should().BeLessThan(0.001, "the exact light undoes the pre-exposure");
            },
            new(Exposure: AutoExposure.Default, CascadedLights: 1)
        );

    [Fact]
    public void AnUnknownAgreementSettingFails()
    {
        Action create = () => WithRenderer("light-ages", (_, _) => { });

        create.Should().Throw<ArgumentException>().WithMessage("*TERTIUS_REFLECTION_HISTORY_AGREEMENT*");
    }

    [Theory]
    [InlineData("off")]
    [InlineData("light-age")]
    public void TheRemovedSwitchValuesFailBecauseTheLightAgeIsAlwaysOn(string value)
    {
        Action create = () => WithRenderer(value, (_, _) => { });

        create.Should().Throw<ArgumentException>().WithMessage($"*'{value}'*always on*");
    }

    [Fact]
    public void TheOriginalSpecularHistoryKeepsNoLightAge()
    {
        string? mode = Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY");
        Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", "original");
        try
        {
            WithRenderer(
                null,
                (renderer, scene) =>
                {
                    Draw(renderer, scene, 4);

                    renderer.ScreenTraceSignals.Specular.SampleAge(0).Should().BeNull("its shaders are H13's");
                    renderer.ScreenTraceSignals.Diffuse.SampleAge(0).Should().BeNull("no record gives the rays an age");
                    renderer.ScreenTraceImages.LightAge(0).Should().BeNull();
                    Gpu.AssertClean(renderer);
                }
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", mode);
        }
    }

    [Theory]
    [InlineData("light-age-diagnostic")]
    [InlineData("control-trace")]
    public void TheInstrumentationNeedsAPreciseSpecularHistory(string value)
    {
        string? mode = Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY");
        Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", "original");
        try
        {
            Action create = () => WithRenderer(value, (_, _) => { });

            create.Should().Throw<ArgumentException>().WithMessage("*TERTIUS_SPECULAR_HISTORY=original*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", mode);
        }
    }

    [Fact]
    public void AResizeStartsEveryLightAgeWithoutEvidence() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 8);
                SetGlow(renderer, scene, new(3.2f));
                Draw(renderer, scene, 4);
                InteriorPixels(renderer.ReadLightAge(), least: 2).Should().NotBeEmpty();
                renderer.Resize(96, 64);

                Draw(renderer, scene, 1);

                float[] record = renderer.ReadLightAge();
                record.Length.Should().Be(2 * 96 * 64);
                Enumerable
                    .Range(0, 96 * 64)
                    .Should()
                    .OnlyContain(pixel => AgeAt(record, pixel) == ScreenTraceImages.NoLightAge);
                Gpu.AssertClean(renderer);
            }
        );

    [Fact]
    public void TheLightAgeRunsCleanlyWithTheH13Reconstruction()
    {
        string?[] previous = [.. SharingSwitches.Select(Environment.GetEnvironmentVariable)];
        foreach (string name in SharingSwitches)
            Environment.SetEnvironmentVariable(name, "1");
        try
        {
            WithRenderer(
                null,
                (renderer, scene) =>
                {
                    Draw(renderer, scene, 16);
                    SetGlow(renderer, scene, new(3.2f));

                    Draw(renderer, scene, LightChangeDraws + 3);

                    renderer.ShadingConstants.ReflectionSharingPrototype.Should().BeTrue();
                    Slow(renderer).Should().OnlyContain(value => float.IsFinite(value));
                    SampleAges(renderer).Should().Contain(age => age == 4, "rays that hit the emitter bring its age 3");
                    Gpu.AssertClean(renderer);
                }
            );
        }
        finally
        {
            for (int index = 0; index < SharingSwitches.Length; index++)
                Environment.SetEnvironmentVariable(SharingSwitches[index], previous[index]);
        }
    }

    [Fact]
    public void ASurfaceSeenSinceTheResetFrameCountsTheFramesSinceThen() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);

                float[] record = renderer.ReadLightAge();
                int[] surface =
                [
                    .. Enumerable.Range(0, Width * Height).Where(pixel => ExactLightAt(record, pixel) >= 0),
                ];
                surface.Length.Should().BeGreaterThan(Width * Height / 2);
                surface
                    .Count(pixel => SeenFramesAt(record, pixel) == 31)
                    .Should()
                    .BeGreaterThanOrEqualTo(
                        surface.Length * 99 / 100,
                        "the reset frame first showed them, 31 frames ago"
                    );
            }
        );

    [Fact]
    public void ANewlyUncoveredSurfaceIsSeenForZeroFramesThenOneMoreFramePerFrame() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                RenderInstance occluder = AddDarkSunAndOccluder(renderer, scene);
                Draw(renderer, scene, 32);
                float[] distanceBefore = renderer.ReadDistance();
                occluder.PreviousPosition = occluder.Position;
                occluder.Position.X += 1.5f;
                scene.Instances[^1] = occluder;
                Draw(renderer, scene, 1);
                float[] distanceAfter = renderer.ReadDistance();
                float[] uncovered = renderer.ReadLightAge();
                occluder.PreviousPosition = occluder.Position;
                scene.Instances[^1] = occluder;

                Draw(renderer, scene, 3);

                float[] later = renderer.ReadLightAge();
                int[] revealed =
                [
                    .. Enumerable
                        .Range(0, Width * Height)
                        .Where(pixel =>
                            distanceAfter[pixel] > distanceBefore[pixel] + 0.3f && distanceBefore[pixel] > 0
                        ),
                ];
                revealed.Length.Should().BeGreaterThan(10, "the occluder's move must reveal floor behind it");
                revealed.Should().OnlyContain(pixel => SeenFramesAt(uncovered, pixel) == 0, "the move first showed it");
                revealed.Should().OnlyContain(pixel => SeenFramesAt(later, pixel) == 3, "three frames showed it since");
            },
            new(CascadedLights: 1)
        );

    [Fact]
    public void AnUncoveredSurfaceBringsItsReceiversNoAge() =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                RenderInstance occluder = AddDarkSunAndOccluder(renderer, scene);
                Draw(renderer, scene, 16);
                occluder.PreviousPosition = occluder.Position;
                occluder.Position.X += 1.5f;
                scene.Instances[^1] = occluder;
                long aged = 0;

                // The occluder's move is motion evidence, the samples' motion bit, so the count reads the light codes alone.
                Draw(renderer, scene, 1);
                aged += SampleAges(renderer).Count(age => LightCodeOf(age) != 0);
                occluder.PreviousPosition = occluder.Position;
                scene.Instances[^1] = occluder;
                for (int frame = 0; frame < 8; frame++)
                {
                    Draw(renderer, scene, 1);
                    aged += SampleAges(renderer).Count(age => LightCodeOf(age) != 0);
                }

                aged.Should().Be(0, "a surface whose light was never seen to change gives a receiver no evidence");
            },
            new(CascadedLights: 1)
        );

    [Fact]
    public void TheDiagnosticRendersExactlyAsTheDefault()
    {
        float[][] expected = ImagesAfterDimmingAndUncovering(null);

        float[][] actual = ImagesAfterDimmingAndUncovering("light-age-diagnostic");

        for (int image = 0; image < expected.Length; image++)
            actual[image].Should().Equal(expected[image], "the diagnostic only observes");
    }

    [Fact]
    public void TheAccumulationTraceRendersExactlyAsTheDefaultAfterADimming()
    {
        float[][] expected = ImagesAfterDimmingAndUncovering(null);

        float[][] actual = ImagesAfterDimmingAndUncovering("control-trace");

        for (int image = 0; image < expected.Length; image++)
            actual[image]
                .Should()
                .Equal(expected[image], "the trace only observes, and the light age cuts as without it");
    }

    [Fact]
    public void TheDiagnosticGivesEachReceiverTheFramesSeenOfTheUnchangedSurfaceThatItsRaysHit() =>
        WithRenderer(
            "light-age-diagnostic",
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);

                float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
                float[] diagnostic = UncoveredDiagnostic(renderer);
                int[] receivers = [.. Enumerable.Range(Height / 2 * Width, Width * Height / 2)];
                receivers
                    .Count(pixel => SeenByRaysAt(diagnostic, pixel) == 31)
                    .Should()
                    .BeGreaterThan(30, "rays that hit the emitter see a surface that the reset frame first showed");
                receivers
                    .Where(pixel => sample[4 * pixel + 3] == 0)
                    .Should()
                    .OnlyContain(pixel => SeenByRaysAt(diagnostic, pixel) == -1, "a miss sees no surface");
                receivers
                    .Should()
                    .OnlyContain(pixel =>
                        SeenByRaysAt(diagnostic, pixel) == -1 || SeenByRaysAt(diagnostic, pixel) == 31
                    );
            }
        );

    [Fact]
    public void TheDiagnosticSeesTheRaysThatHitAFreshlyUncoveredSurface() =>
        WithRenderer(
            "light-age-diagnostic",
            (renderer, scene) =>
            {
                RenderInstance occluder = AddDarkSunAndOccluder(renderer, scene);
                Draw(renderer, scene, 32);
                occluder.PreviousPosition = occluder.Position;
                occluder.Position.X += 1.5f;
                scene.Instances[^1] = occluder;
                Draw(renderer, scene, 1);
                float[] moved = UncoveredDiagnostic(renderer);
                occluder.PreviousPosition = occluder.Position;
                scene.Instances[^1] = occluder;

                Draw(renderer, scene, 1);

                float[] next = UncoveredDiagnostic(renderer);
                int[] screen = [.. Enumerable.Range(0, Width * Height)];
                int fresh = screen.Count(pixel => SeenByRaysAt(moved, pixel) == 0);
                output.WriteLine($"Receivers whose rays hit a surface that the move uncovered: {fresh}.");
                fresh.Should().BeGreaterThan(5, "receivers near the block reflect its newly shown faces");
                screen.Count(pixel => SeenByRaysAt(next, pixel) == 0).Should().Be(0, "nothing moved in this frame");
                screen.Count(pixel => SeenByRaysAt(next, pixel) == 1).Should().BeGreaterThan(5);
            },
            new(CascadedLights: 1)
        );

    [Fact]
    public void TheDiagnosticGivesEachReceiversHistoryLengthBeforeItsBlend() =>
        WithRenderer(
            "light-age-diagnostic",
            (renderer, scene) =>
            {
                Draw(renderer, scene, 32);

                float[] slow = Slow(renderer);
                float[] diagnostic = UncoveredDiagnostic(renderer);
                int[] receivers =
                [
                    .. Enumerable
                        .Range(Height / 2 * Width, Width * Height / 2)
                        .Where(pixel => LengthAt(slow, pixel) > 0),
                ];
                receivers.Length.Should().BeGreaterThan(Width * Height / 4);
                receivers
                    .Should()
                    .OnlyContain(
                        pixel => LengthAt(slow, pixel) <= LengthBeforeBlendAt(diagnostic, pixel) + 1,
                        "the blend adds one frame, and the spatial dispatch can only shorten"
                    );
                // The slow history's clamp shortens the rest. The plate's pixels share their block's reflection ray
                // (ter-4oow), whose weighted samples move the clamp in about 6 % of the receivers; own rays moved it in
                // under 5 %.
                receivers
                    .Count(pixel => LengthAt(slow, pixel) == LengthBeforeBlendAt(diagnostic, pixel) + 1)
                    .Should()
                    .BeGreaterThanOrEqualTo(receivers.Length * 90 / 100);
            }
        );

    private static readonly string[] SharingSwitches =
    [
        "TERTIUS_REFLECTION_SHARING",
        "TERTIUS_INDIRECT_LIGHT_PROTOTYPE",
    ];

    /// <summary>
    /// The receivers whose ray hits the emitter in the draw that counts its change bring back age 0 and keep a history of
    /// one frame.
    /// </summary>
    private void AReceiverWhoseRayHitsTheChangedEmitterKeepsOnlyTheFramesSinceTheChange(Vector3 glow) =>
        WithRenderer(
            null,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 64);
                float[] warm = Slow(renderer);
                SetGlow(renderer, scene, glow);

                Draw(renderer, scene, LightChangeDraws);

                float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
                float[] ages = SampleAges(renderer);
                float[] after = Slow(renderer);
                int[] bright =
                [
                    .. Enumerable
                        .Range(Height / 2 * Width, Width * Height / 2)
                        .Where(pixel =>
                            ages[pixel] == 1
                            && LengthAt(warm, pixel) > 8
                            && Luminance(sample, pixel) >= 0.3f * Luminance(warm, pixel)
                        ),
                ];
                output.WriteLine(
                    $"Receivers with a bright ray of age 0 after the glow became {glow.X}: {bright.Length}."
                );
                bright.Length.Should().BeGreaterThan(30, "the plate reflects the emitter");
                bright
                    .Should()
                    .OnlyContain(pixel => LengthAt(after, pixel) <= 1, "the light the ray sees is 0 frames old");
            }
        );

    /// <summary>
    /// The specular slow history and sample, the diffuse slow history and the HDR image after a dimming of the emitter
    /// and a move of the occluder, with the agreement setting <paramref name="value"/>.
    /// </summary>
    private static float[][] ImagesAfterDimmingAndUncovering(string? value)
    {
        float[][] result = [];
        WithRenderer(
            value,
            (renderer, scene) =>
            {
                RenderInstance occluder = AddDarkSunAndOccluder(renderer, scene);
                scene.Lights[0] = Sun(irradiance: 3);
                Draw(renderer, scene, 16);
                SetGlow(renderer, scene, new(3.2f));
                occluder.PreviousPosition = occluder.Position;
                occluder.Position.X += 1.5f;
                scene.Instances[^1] = occluder;
                Draw(renderer, scene, 1);
                occluder.PreviousPosition = occluder.Position;
                scene.Instances[^1] = occluder;
                Draw(renderer, scene, 8);
                result =
                [
                    Slow(renderer),
                    renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0)),
                    renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Slow(0)),
                    renderer.ReadHdr(),
                ];
            },
            new(CascadedLights: 1)
        );
        return result;
    }

    /// <summary>The sun at <paramref name="irradiance"/>, which casts shadows.</summary>
    private static PointRenderLight Sun(float irradiance) =>
        Gpu.StarLight(
            direction: SunDirection,
            irradiance: irradiance,
            angularRadius: Gpu.StarAngularRadius,
            color: Gpu.StarColor,
            chainDepth: TestChains.Root
        );

    /// <summary>A sun of irradiance 3 and a white block on the floor, whose shadow gives the floor a shadow edge.</summary>
    private static void AddSunAndOccluder(VulkanRenderer renderer, RenderSnapshot scene)
    {
        scene.Lights.Clear();
        scene.Lights.Add(Sun(irradiance: 3));
        scene.Instances.Add(Occluder(renderer));
    }

    /// <summary>A sun of irradiance 0 and the white block on the floor, which the scene's last instance holds.</summary>
    private static RenderInstance AddDarkSunAndOccluder(VulkanRenderer renderer, RenderSnapshot scene)
    {
        scene.Lights.Clear();
        scene.Lights.Add(Sun(irradiance: 0));
        RenderInstance occluder = Occluder(renderer);
        scene.Instances.Add(occluder);
        return occluder;
    }

    private static RenderInstance Occluder(VulkanRenderer renderer)
    {
        RenderInstance occluder = ReflectionTesting.Block(
            box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
            material: renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.7f, Model: new StandardShading(Metallic: 0))
            ),
            center: new(-1.5f, ReflectionTesting.FloorHeight + 0.5f, -3.5f),
            size: new(1)
        );
        occluder.EntityIndex = 3;
        return occluder;
    }

    /// <summary>Gives the emitter, the scene's second instance, a glow of <paramref name="radiance"/>.</summary>
    private static void SetGlow(VulkanRenderer renderer, RenderSnapshot scene, Vector3 radiance)
    {
        RenderInstance emitter = scene.Instances[1];
        emitter.Material = ReflectionTesting.Glow(renderer, radiance);
        scene.Instances[1] = emitter;
    }

    /// <summary>One draw's step of the walks: the camera moves 4 cm toward the emitter.</summary>
    /// <summary>The pixels that a walk can hit in the output of moved_surface_probe.comp.</summary>
    private static int[] HittablePixels(float[] probe) =>
        [.. Enumerable.Range(0, Width * Height).Where(pixel => probe[4 * pixel] >= 0)];

    private static void Step(RenderSnapshot scene)
    {
        scene.Camera.PreviousPosition = scene.Camera.Position;
        scene.Camera.Position += new Vector3(0, 0, -0.04f);
    }

    /// <summary>
    /// A pixel whose rows above and below lie within a quarter of its distance: away from the horizontal depth edges, such
    /// as the emitter's lower edge over the floor, which the walk moves across the image. A pixel beside a vertical depth
    /// edge passes.
    /// </summary>
    private static bool AwayFromARowDepthEdge(float[] distance, int pixel) =>
        distance[pixel] > 0
        && MathF.Abs(distance[pixel - Width] / distance[pixel] - 1) < 0.25f
        && MathF.Abs(distance[pixel + Width] / distance[pixel] - 1) < 0.25f;

    /// <summary>The pixels whose exact light and whose eight neighbors' exact light are at least <paramref name="least"/>.</summary>
    private static int[] InteriorPixels(float[] record, float least) =>
        [
            .. Enumerable
                .Range(0, Width * Height)
                .Where(pixel =>
                    pixel % Width is > 0 and < Width - 1
                    && pixel / Width is > 0 and < Height - 1
                    && Enumerable
                        .Range(-1, 3)
                        .All(y =>
                            Enumerable.Range(-1, 3).All(x => ExactLightAt(record, pixel + y * Width + x) >= least)
                        )
                ),
        ];

    private static float ExactLightAt(float[] record, int pixel) => record[2 * pixel];

    /// <summary>The record's age: the integer part of its age channel, whose fraction holds the draws of a pending change.</summary>
    private static float AgeAt(float[] record, int pixel) => MathF.Truncate(record[2 * pixel + 1]);

    /// <summary>The draws that a pending change has stayed outside the range: the fraction of the age channel, in quarters.</summary>
    private static float PendingDrawsAt(float[] record, int pixel) =>
        MathF.Round(MathF.Abs(record[2 * pixel + 1] - AgeAt(record, pixel)) * LightChangeDraws);

    private static float LengthAt(float[] slow, int pixel) => slow[4 * pixel + 3];

    private static float Luminance(float[] rgba, int pixel) =>
        0.2126f * rgba[4 * pixel] + 0.7152f * rgba[4 * pixel + 1] + 0.0722f * rgba[4 * pixel + 2];

    private static float[] Slow(VulkanRenderer renderer) =>
        renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0));

    /// <summary>The specular sample's light age per pixel: 0 for no evidence, else the age plus 1.</summary>
    private static float[] SampleAges(VulkanRenderer renderer) =>
        renderer.ReadImage(
            renderer.ScreenTraceSignals.Specular.SampleAge(0)
                ?? throw new InvalidOperationException("The original specular history keeps no light age.")
        );

    /// <summary>The light code of the diffuse sample's age per pixel: 0 for no light age, else the age plus 1.</summary>
    private static float[] DiffuseLightCodes(VulkanRenderer renderer) =>
        [
            .. renderer
                .ReadImage(
                    renderer.ScreenTraceSignals.Diffuse.SampleAge(0)
                        ?? throw new InvalidOperationException("The original specular history keeps no light age.")
                )
                .Select(age => (float)((int)age & TemporalSignal.LightCodeMask)),
        ];

    /// <summary>The light code of a sample age, read as a float, without its motion bit (sample_age.glsl).</summary>
    private static int LightCodeOf(float age) => (int)age & TemporalSignal.LightCodeMask;

    /// <summary>Whether a sample age, read as a float, holds the motion bit (sample_age.glsl).</summary>
    private static bool SawMotion(float age) => ((int)age & TemporalSignal.SampleMotionBit) != 0;

    /// <summary>
    /// The moved mark of each pixel, the alpha of the hit light's first layer: 1 where the plan found that the pixel's
    /// surface moved since the last draw, else 0.
    /// </summary>
    private static float[] MovedMarks(VulkanRenderer renderer)
    {
        float[] light = renderer.ReadImageLayer(image: renderer.ScreenTraceImages.HitLight, layer: 0);
        return [.. Enumerable.Range(0, Width * Height).Select(pixel => light[4 * pixel + 3])];
    }

    /// <summary>Moves the scene's instance <paramref name="instance"/> by <paramref name="step"/> in this draw.</summary>
    private static void Slide(RenderSnapshot scene, int instance, Vector3 step)
    {
        RenderInstance moved = scene.Instances[instance];
        moved.PreviousPosition = moved.Position;
        moved.Position += step;
        scene.Instances[instance] = moved;
    }

    /// <summary>The block of 8 × 8 pixels, one workgroup of the accumulation, that holds the pixel.</summary>
    private static int BlockOf(int pixel) => pixel / Width / 8 * (Width / 8) + pixel % Width / 8;

    /// <summary>The mean luminance of the diffuse slow history over the floor, the image's lower half.</summary>
    private static float FloorBounceLight(VulkanRenderer renderer)
    {
        float[] slow = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Slow(0));
        return (float)Enumerable.Range(Height / 2 * Width, Width * Height / 2).Average(pixel => Luminance(slow, pixel));
    }

    /// <summary>
    /// The frames on screen of a pixel whose light the record never saw change, from its first frame at 0: the record
    /// holds −(frames + 1). A changed pixel's age is 0 or more, so it has none: −1.
    /// </summary>
    private static float SeenFramesAt(float[] record, int pixel) =>
        AgeAt(record, pixel) < 0 ? -AgeAt(record, pixel) - 1 : -1;

    /// <summary>The uncovered-surface diagnostic of the specular sample, four floats per pixel.</summary>
    private static float[] UncoveredDiagnostic(VulkanRenderer renderer) =>
        renderer.ReadImage(
            renderer.ScreenTraceSignals.Specular.UncoveredDiagnostic(0)
                ?? throw new InvalidOperationException("The diagnostic of uncovered surfaces is off.")
        );

    /// <summary>
    /// The fewest frames seen among the never-changed surfaces that the sample's rays hit, or −1 where they hit none.
    /// </summary>
    private static float SeenByRaysAt(float[] diagnostic, int pixel) => diagnostic[4 * pixel];

    /// <summary>The receiver's slow-history length just before its blend, after the light-age cut.</summary>
    private static float LengthBeforeBlendAt(float[] diagnostic, int pixel) => diagnostic[4 * pixel + 1];

    /// <summary>The receiver's length before the light-age cut, which equals its length before the blend where no cut acted.</summary>
    private static float LengthBeforeCutAt(float[] diagnostic, int pixel) => diagnostic[4 * pixel + 3];

    private static void Draw(VulkanRenderer renderer, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            scene.Tick++;
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1);
        }
    }

    /// <summary>
    /// The renderer's settings of a test, the gray of the ambient light, and the plate's roughness: null for the plate's
    /// own, because <c>new()</c> of this struct gives null, not the defaults.
    /// </summary>
    private readonly record struct Settings(
        ExposureSetting Exposure = default,
        UpscalingSetting Upscaling = default,
        int CascadedLights = 0,
        float Gray = 0.5f,
        float? Roughness = null
    );

    /// <summary>
    /// Runs <paramref name="action"/> on a renderer with the agreement setting <paramref name="value"/> and a scene of
    /// the ambient gray, the plate as the floor and a glowing block 5 m ahead, the scene's second instance.
    /// </summary>
    private static void WithRenderer(
        string? value,
        Action<VulkanRenderer, RenderSnapshot> action,
        Settings? chosen = null
    )
    {
        Settings settings = chosen ?? new();
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                framesInFlight: 1,
                cascadedLights: settings.CascadedLights,
                screenTrace: ScreenTrace.Default,
                warmUpFrames: 0,
                exposure: settings.Exposure,
                upscaling: settings.Upscaling
            );
            try
            {
                RenderSnapshot scene = ReflectionTesting.Gray(settings.Gray);
                scene.Instances.Add(
                    ReflectionTesting.Floor(renderer, Plate with { Roughness = settings.Roughness ?? Plate.Roughness })
                );
                RenderInstance emitter = ReflectionTesting.Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: ReflectionTesting.Glow(renderer, new(4)),
                    center: new(0, 0, -5),
                    size: new(4, 1.5f, 1)
                );
                emitter.EntityIndex = 2;
                scene.Instances.Add(emitter);
                action(renderer, scene);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }
}
