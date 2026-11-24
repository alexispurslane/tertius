using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

public sealed partial class AccumulatorRenderTests
{
    public sealed partial class RegionDrift
    {
        /// <summary>
        /// The change gate's value of an anchor that has not yet seen FullRateFrames valid draws in a row or a cause since
        /// its history started (region_drift.glsl, RegionGateUnestablished): its gate is closed.
        /// </summary>
        private const float GateUnestablished = -1;

        /// <summary>The closed gate's bar of a seed, in Welch's t (region_drift.glsl, WelchSeedBar).</summary>
        private const double SeedBar = 5.0;

        /// <summary>The closed gate's bar of a confirmed answer, in Welch's t (region_drift.glsl, WelchConfirmBar).</summary>
        private const double ConfirmBar = 2.6;

        /// <summary>
        /// The light of the designed regions, in the samples' units: far enough from 0 that no designed change takes a
        /// sample below 0.
        /// </summary>
        private const float DesignedBase = 2;

        /// <summary>The size of the designed noise, which alternates its sign from draw to draw.</summary>
        private const float DesignedSpread = 0.02f;

        /// <summary>A light code of a sample age that brings light evidence: a light that changed 4 draws ago.</summary>
        private const uint RecentLightCode = 5;

        /// <summary>
        /// The designed noise of <paramref name="draw"/>: +<see cref="DesignedSpread"/> in an even draw, −DesignedSpread in
        /// an odd one. Each window of 18 draws holds 9 of each, so the two windows' means are equal, and each window's
        /// spread is DesignedSpread · √(18 / 17).
        /// </summary>
        private static float AlternatingNoise(int draw) => draw % 2 == 0 ? DesignedSpread : -DesignedSpread;

        /// <summary>
        /// One draw of an anchor's ring (region_drift.glsl, regionRing): the region's light, the count of valid draws in a
        /// row, the signed answer and the change gate.
        /// </summary>
        private readonly record struct RingDraw(float Light, float ValidDraws, float Answer, float Gate);

        /// <summary>
        /// The ring of every anchor in the last draw, <paramref name="draw"/> draws after the last reset (a start or a
        /// camera cut), whose index chooses the ring's layer, row by row.
        /// </summary>
        private static RingDraw[] RingOf(VulkanRenderer renderer, SamplePass sample, int draw)
        {
            float[] layer = renderer.ReadImageLayer(
                sample.Signal.RegionRing(0)!,
                draw % TemporalSignal.RegionRingDraws
            );
            return
            [
                .. Enumerable
                    .Range(0, layer.Length / 4)
                    .Select(texel => new RingDraw(
                        Light: layer[4 * texel],
                        ValidDraws: layer[4 * texel + 1],
                        Answer: layer[4 * texel + 2],
                        Gate: layer[4 * texel + 3]
                    )),
            ];
        }

        /// <summary>What a run sets in one draw: its samples, its sample ages, a step of the camera and a camera cut.</summary>
        private sealed record DrawPlan(Func<int, int, Vector4> Samples)
        {
            /// <summary>The sample age of each pixel, or none: 0 everywhere.</summary>
            public Func<int, int, ushort>? Ages { get; init; }

            /// <summary>Whether the camera steps 0.1 to the side in the draw, which makes every tap move on screen.</summary>
            public bool Moves { get; init; }

            /// <summary>Whether the draw is a camera cut.</summary>
            public bool Cuts { get; init; }
        }

        /// <summary>
        /// Draws <paramref name="draws"/> frames of <paramref name="scene"/>, the first one after the reset, each as
        /// <paramref name="planOf"/> plans it, and returns the ring of every anchor in each draw.
        /// </summary>
        private static List<RingDraw[]> Run(
            VulkanRenderer renderer,
            SamplePass sample,
            RenderSnapshot scene,
            int draws,
            Func<int, DrawPlan> planOf
        )
        {
            List<RingDraw[]> rings = [];
            int sinceReset = 0;
            for (int draw = 0; draw < draws; draw++, sinceReset++)
            {
                DrawPlan plan = planOf(draw);
                sample.Samples = plan.Samples;
                sample.AgeCodes = plan.Ages ?? ((_, _) => 0);
                scene.Camera.PreviousPosition = scene.Camera.Position;
                if (plan.Moves)
                    scene.Camera.Position += new Vector3(0.1f, 0, 0);
                if (plan.Cuts)
                {
                    scene.CameraCuts++;
                    sinceReset = 0;
                }
                if (draw == 0)
                    renderer.Render(scene, 1f);
                else
                    Continue(renderer, scene, frames: 1);
                rings.Add(RingOf(renderer, sample, sinceReset));
            }
            return rings;
        }

        /// <summary>Samples of the light <paramref name="light"/> at every pixel, with no noise.</summary>
        private static Func<int, int, Vector4> Uniform(float light) => (_, _) => new Vector4(new Vector3(light), 1);

        /// <summary>
        /// Welch's t of an anchor's ring in <paramref name="draw"/>, from its <paramref name="lights"/> of every draw since
        /// the reset: the mean of the newer window (the draw and the 17 before it) less the older window's (the 18 before
        /// those), over √((s_new² + s_old²) / 18). The tests' model of region_drift.glsl's statistic, for the draws whose
        /// windows both exist. 0 where both spreads and the difference are 0.
        /// </summary>
        private static double WelchOf(IReadOnlyList<float> lights, int draw)
        {
            const int Window = TemporalSignal.RegionRingDraws / 2;
            double origin = lights[draw - 2 * Window + 1];
            (double Mean, double Variance) MomentsOf(int last)
            {
                double[] values = [.. Enumerable.Range(last - Window + 1, Window).Select(at => lights[at] - origin)];
                double mean = values.Average();
                return (mean, values.Sum(value => (value - mean) * (value - mean)) / (Window - 1));
            }
            (double newerMean, double newerVariance) = MomentsOf(draw);
            (double olderMean, double olderVariance) = MomentsOf(draw - Window);
            double denominator = Math.Sqrt((newerVariance + olderVariance) / Window);
            double difference = newerMean - olderMean;
            if (denominator > 0)
                return difference / denominator;
            return Math.Sign(difference) * 1e9;
        }

        /// <summary>
        /// The tests of the change gate of an anchor (region_drift.glsl, regionGateAfter) in the scene of one anchor, and of
        /// the test that each gate state runs (tests/STYLE.md, "Long tests").
        /// </summary>
        public sealed class ChangeGate
        {
            /// <summary>
            /// The scene of one anchor and a run of <paramref name="draws"/> draws planned by <paramref name="planOf"/>:
            /// the one anchor's ring in each draw.
            /// </summary>
            private static List<RingDraw> RunOneAnchor(int draws, Func<int, DrawPlan> planOf)
            {
                (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: SignalParameters.Diffuse
                );
                try
                {
                    return
                    [
                        .. Run(renderer: renderer, sample: sample, scene: scene, draws: draws, planOf: planOf)
                            .Select(ring => ring[0]),
                    ];
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }

            /// <summary>Noisy samples of the light 1 in <paramref name="draw"/>, as the still regions of RegionDrift.</summary>
            private static DrawPlan Still(int draw) =>
                new((column, row) =>
                    NoisySample(light: 1, variation: Variation, column: column, row: row, frame: draw)
                );

            [Fact]
            public void AColdStartLeavesTheGateUnestablishedForItsFirstValidDraws()
            {
                List<RingDraw> ring = RunOneAnchor(draws: 7, planOf: Still);

                ring.Select(draw => draw.Gate)
                    .Should()
                    .OnlyContain(gate => gate == GateUnestablished, "no anchor has a history to recover at a start");
            }

            [Fact]
            public void AfterFullRateFramesValidDrawsInARowTheGateIsEstablishedAndClosed()
            {
                List<RingDraw> ring = RunOneAnchor(draws: 60, planOf: Still);

                int established = ring.FindIndex(draw => draw.ValidDraws == FullRateFrames);
                established.Should().BePositive("the still draws are valid");
                ring.Take(established).Should().OnlyContain(draw => draw.Gate == GateUnestablished);
                ring.Skip(established)
                    .Select(draw => draw.Gate)
                    .Should()
                    .OnlyContain(
                        gate => gate == TemporalSignal.RegionCauseWindowDraws,
                        "a still region without a cause keeps its gate closed"
                    );
            }

            [Fact]
            public void DrawsThatAreNotValidAtTheStartNeverOpenTheGate()
            {
                // The camera moves in the draws 1 to 20, so no tap counts there. The anchor's view was never established,
                // so nothing holds light that a recovery would replace.
                List<RingDraw> ring = RunOneAnchor(
                    draws: 60,
                    planOf: draw => Still(draw) with { Moves = draw is >= 1 and <= 20 }
                );

                ring.Skip(1).Take(20).Should().OnlyContain(draw => draw.ValidDraws == 0, "the camera moved");
                ring.Select(draw => draw.Gate)
                    .Should()
                    .OnlyContain(
                        gate => gate == GateUnestablished || gate == TemporalSignal.RegionCauseWindowDraws,
                        "missing draws at the start are no movement recovery"
                    );
                ring[^1].Gate.Should().Be(TemporalSignal.RegionCauseWindowDraws, "the still draws establish the view");
            }

            [Fact]
            public void AnObservedCauseOpensTheGateForRegionCauseWindowDraws()
            {
                // One draw at draw 40 brings a recent light code at every pixel. The gate counts the draws since the
                // cause and closes when they reach RegionCauseWindowDraws.
                const int Cause = 40;
                List<RingDraw> ring = RunOneAnchor(
                    draws: Cause + TemporalSignal.RegionCauseWindowDraws + 2,
                    planOf: draw => Still(draw) with { Ages = (_, _) => (ushort)(draw == Cause ? RecentLightCode : 0) }
                );

                ring.Skip(Cause)
                    .Select(draw => draw.Gate)
                    .Should()
                    .Equal(
                        Enumerable
                            .Range(0, TemporalSignal.RegionCauseWindowDraws + 2)
                            .Select(since => (float)Math.Min(since, TemporalSignal.RegionCauseWindowDraws)),
                        "the gate is open from the cause's draw on and closed RegionCauseWindowDraws draws later"
                    );
            }

            [Fact]
            public void ASamplesMotionBitIsAnObservedCauseToo()
            {
                // The motion bit says that a ray of the sample saw a surface that moved: a cause of a change of the bounce
                // light, as a recent light code is (evidenceOfSample).
                List<RingDraw> ring = RunOneAnchor(
                    draws: 42,
                    planOf: draw =>
                        Still(draw) with
                        {
                            Ages = (_, _) => (ushort)(draw == 40 ? TemporalSignal.SampleMotionBit : 0),
                        }
                );

                ring[39].Gate.Should().Be(TemporalSignal.RegionCauseWindowDraws);
                ring.Skip(40).Select(draw => draw.Gate).Should().Equal([0f, 1f], "the motion bit opened the gate");
            }

            /// <summary>
            /// A still established view (draws 0 to 39), then a camera move in the draws 40 to 59, then a still view again.
            /// </summary>
            private static DrawPlan MoveAfterAnEstablishedView(int draw) =>
                Still(draw) with
                {
                    Moves = draw is >= 40 and < 60,
                };

            [Fact]
            public void AMoveThatInvalidatesAnEstablishedViewHoldsItsGateOpen()
            {
                List<RingDraw> ring = RunOneAnchor(draws: 60, planOf: MoveAfterAnEstablishedView);

                ring[39].Gate.Should().Be(TemporalSignal.RegionCauseWindowDraws, "the view was established and closed");
                ring.Skip(40)
                    .Should()
                    .OnlyContain(draw => draw.ValidDraws == 0 && draw.Gate == 0, "the history of the view went stale");
            }

            [Fact]
            public void AfterAMoveStopsTheGateStaysOpenForRegionCauseWindowDraws()
            {
                List<RingDraw> ring = RunOneAnchor(
                    draws: 60 + TemporalSignal.RegionCauseWindowDraws + 2,
                    planOf: MoveAfterAnEstablishedView
                );

                ring.Skip(60)
                    .Select(draw => draw.Gate)
                    .Should()
                    .Equal(
                        Enumerable
                            .Range(1, TemporalSignal.RegionCauseWindowDraws + 2)
                            .Select(still => (float)Math.Min(still, TemporalSignal.RegionCauseWindowDraws)),
                        "the gate counts the still draws after the move and closes at RegionCauseWindowDraws"
                    );
            }

            [Fact]
            public void ACameraCutRestartsAnOpenGateUnestablished()
            {
                // A cause opens the gate at draw 40, and the camera cuts at draw 50 with no cause in its draw.
                List<RingDraw> ring = RunOneAnchor(
                    draws: 51,
                    planOf: draw =>
                        Still(draw) with
                        {
                            Ages = (_, _) => (ushort)(draw == 40 ? RecentLightCode : 0),
                            Cuts = draw == 50,
                        }
                );

                ring[49].Gate.Should().Be(9, "the cause opened the gate 9 draws before the cut");
                ring[50]
                    .Should()
                    .Be(
                        new RingDraw(Light: ring[50].Light, ValidDraws: 1, Answer: 0, Gate: GateUnestablished),
                        "a cut discards the decisions and the draws before it"
                    );
            }

            [Fact]
            public void ACameraCutLeavesNoDrawFromBeforeItInAnyBounceHistory()
            {
                // The light is 1 before the cut at draw 50 and 0.5 from it on, without noise.
                (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: SignalParameters.Diffuse
                );
                try
                {
                    Run(
                        renderer: renderer,
                        sample: sample,
                        scene: scene,
                        draws: 51,
                        planOf: draw => new(Uniform(draw < 50 ? 1 : 0.5f)) { Cuts = draw == 50 }
                    );

                    LengthsOf(renderer, sample).Should().OnlyContain(length => length == 1, "every history restarts");
                    InteriorLightOf(renderer, sample)
                        .Should()
                        .OnlyContain(light => light == 0.5f, "no light from before the cut remains");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }

            /// <summary>
            /// Designed samples of the one anchor's region: the base light, the alternating noise, and from draw 50 on a
            /// step of <paramref name="size"/> times the noise's size. Every sample brings a recent light code where
            /// <paramref name="cause"/>, which holds the gate open.
            /// </summary>
            private static DrawPlan DesignedStep(int draw, float size, bool cause) =>
                new(Uniform(DesignedBase + AlternatingNoise(draw) + (draw >= 50 ? size * DesignedSpread : 0)))
                {
                    Ages = (_, _) => (ushort)(cause ? RecentLightCode : 0),
                };

            [Fact]
            public void WhileTheGateIsOpenAChangeBelowTheSeedBarGetsASignedAnswer()
            {
                // A step of 1.2 times the noise moves the newer window's mean past the older window's spread, the open
                // gate's test, while Welch's t stays below the seed bar.
                List<RingDraw> ring = RunOneAnchor(
                    draws: 90,
                    planOf: draw => DesignedStep(draw, size: -1.2f, cause: true)
                );

                Enumerable
                    .Range(35, 55)
                    .Max(draw => Math.Abs(WelchOf([.. ring.Select(at => at.Light)], draw)))
                    .Should()
                    .BeLessThan(SeedBar);
                ring.Select(draw => draw.Answer).Should().Contain(-1, "the open gate's test answers with the sign");
                ring.Select(draw => draw.Answer).Should().OnlyContain(answer => answer == -1 || answer == 0);
            }

            [Fact]
            public void WhileTheGateIsClosedAChangeBelowTheSeedBarWithNoAnsweringNeighborGetsNoAnswer()
            {
                List<RingDraw> ring = RunOneAnchor(
                    draws: 90,
                    planOf: draw => DesignedStep(draw, size: -1.2f, cause: false)
                );

                Enumerable
                    .Range(35, 55)
                    .Max(draw => Math.Abs(WelchOf([.. ring.Select(at => at.Light)], draw)))
                    .Should()
                    .BeGreaterThanOrEqualTo(ConfirmBar, "the change lies between the two bars");
                ring.Should().OnlyContain(draw => draw.Answer == 0, "the closed gate asks for a seed");
            }

            [Fact]
            public void UnderTheClosedGateAStepOfLightWithoutNoiseAnswers()
            {
                // Both windows have no spread once the newer window holds only the step's draws: Welch's t then has no
                // denominator, and the change counts as a seed.
                List<RingDraw> ring = RunOneAnchor(
                    draws: 90,
                    planOf: draw => new(Uniform(draw < 50 ? DesignedBase : DesignedBase + 0.05f))
                );

                ring[49].Gate.Should().Be(TemporalSignal.RegionCauseWindowDraws);
                ring[50 + 17].Answer.Should().Be(1, "the windows hold the light before and after the step");
            }

            [Fact]
            public void UnderTheClosedGateAConstantLightWithoutNoiseNeverAnswers()
            {
                List<RingDraw> ring = RunOneAnchor(draws: 90, planOf: _ => new(Uniform(DesignedBase)));

                ring.Should().OnlyContain(draw => draw.Answer == 0, "nothing changed");
            }
        }

        /// <summary>
        /// The closed gate's signed neighbor rule (region_drift.glsl, regionClosedDecisionOf) in a scene of 3 × 3 anchors,
        /// where each anchor's region light follows its own designed change (tests/STYLE.md, "Long tests").
        /// </summary>
        public sealed class SignedNeighbors
        {
            /// <summary>The side of the scene of 3 × 3 anchors, in pixels: 24 blocks, whose anchors are the blocks 0, 8 and 16.</summary>
            private const int GridSide = 192;

            /// <summary>The anchors of the scene in x and in y.</summary>
            private const int GridAnchors = 3;

            /// <summary>The draw from which the designed changes of most tests start.</summary>
            private const int Start = 50;

            /// <summary>
            /// A designed change: the region light of anchor (<paramref name="X"/>, <paramref name="Y"/>) steps by
            /// <paramref name="Size"/> times the noise's size from draw <paramref name="From"/> on, and the other anchors'
            /// light stays.
            /// </summary>
            private readonly record struct Step(int X, int Y, float Size, int From = Start);

            /// <summary>
            /// The light of the blocks at <paramref name="block"/> in one direction that moves the mean of the regions of
            /// the 3 anchors in that direction by <paramref name="moves"/> and no other light: the blocks 0 to 7 lie in the
            /// regions of the anchors 0 and 1, block 8 in all three, the blocks 9 to 16 in those of 1 and 2, and the blocks
            /// 17 to 23 in that of 2 alone. Block 8 keeps its light, so each next part solves for one more anchor.
            /// </summary>
            private static float LightOfBlock(int block, float[] moves)
            {
                float first = 9f / 8 * moves[0];
                float middle = 17f / 8 * moves[1] - first;
                return block switch
                {
                    < 8 => first,
                    8 => 0,
                    <= 16 => middle,
                    _ => 16f / 7 * (moves[2] - middle / 2),
                };
            }

            /// <summary>The samples of <paramref name="draw"/>: the base light, the alternating noise and the steps.</summary>
            private static Func<int, int, Vector4> SamplesOf(int draw, Step[] steps)
            {
                float[,] moves = new float[GridAnchors, GridAnchors];
                foreach (Step step in steps.Where(step => draw >= step.From))
                    moves[step.X, step.Y] += step.Size * DesignedSpread;
                int blocks = GridSide / 8;
                float[,] light = new float[blocks, blocks];
                for (int x = 0; x < blocks; x++)
                for (int y = 0; y < blocks; y++)
                {
                    float[] alongY = [.. Enumerable.Range(0, GridAnchors).Select(anchor => LightOfBlock(y, [moves[anchor, 0], moves[anchor, 1], moves[anchor, 2]]))];
                    light[x, y] = LightOfBlock(x, alongY);
                }
                float noisy = DesignedBase + AlternatingNoise(draw);
                return (column, row) => new Vector4(new Vector3(noisy + light[column / 8, row / 8]), 1);
            }

            /// <summary>
            /// Runs <paramref name="draws"/> draws of the scene of 3 × 3 anchors with <paramref name="steps"/>, and the
            /// sample ages <paramref name="ages"/> in every draw. Returns the ring of every anchor in each draw.
            /// </summary>
            private static List<RingDraw[]> RunGrid(Step[] steps, int draws = 110, Func<int, int, ushort>? ages = null)
            {
                (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: SignalParameters.Diffuse,
                    width: GridSide,
                    height: GridSide
                );
                try
                {
                    return Run(
                        renderer: renderer,
                        sample: sample,
                        scene: scene,
                        draws: draws,
                        planOf: draw => new(SamplesOf(draw, steps)) { Ages = ages }
                    );
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }

            /// <summary>The ring of anchor (<paramref name="x"/>, <paramref name="y"/>) in every draw of a run.</summary>
            private static RingDraw[] AnchorOf(List<RingDraw[]> rings, int x, int y) =>
                [.. rings.Select(ring => ring[y * GridAnchors + x])];

            /// <summary>The draws of a run in which anchor (<paramref name="x"/>, <paramref name="y"/>) answered.</summary>
            private static int[] AnsweredDraws(List<RingDraw[]> rings, int x, int y) =>
                [.. AnchorOf(rings, x, y).Select((draw, at) => (draw.Answer, at)).Where(draw => draw.Answer != 0).Select(draw => draw.at)];

            /// <summary>Welch's t of anchor (<paramref name="x"/>, <paramref name="y"/>) in each tested draw of a run, 0 before.</summary>
            private static double[] WelchOfAnchor(List<RingDraw[]> rings, int x, int y)
            {
                float[] lights = [.. AnchorOf(rings, x, y).Select(draw => draw.Light)];
                return [.. Enumerable.Range(0, lights.Length).Select(draw => draw < TemporalSignal.RegionRingDraws - 1 ? 0 : WelchOf(lights, draw))];
            }

            [Fact]
            public void AnIsolatedChangeAboveTheSeedBarAnswersWithItsSignFromItsFirstDrawAboveTheBar()
            {
                List<RingDraw[]> rings = RunGrid([new(X: 1, Y: 1, Size: -2.5f)]);

                int firstAbove = Array.FindIndex(WelchOfAnchor(rings, 1, 1), t => Math.Abs(t) >= SeedBar);
                firstAbove.Should().BePositive("the change passes the seed bar");
                AnswersOf(rings, 1, 1).Take(firstAbove).Should().OnlyContain(answer => answer == 0);
                AnswersOf(rings, 1, 1)[firstAbove].Should().Be(-1, "the answer has the change's sign");
            }

            /// <summary>The answers of anchor (<paramref name="x"/>, <paramref name="y"/>) in every draw of a run.</summary>
            private static float[] AnswersOf(List<RingDraw[]> rings, int x, int y) =>
                [.. AnchorOf(rings, x, y).Select(draw => draw.Answer)];

            [Fact]
            public void AnIsolatedChangeGivesNoOtherAnchorAnAnswer()
            {
                List<RingDraw[]> rings = RunGrid([new(X: 1, Y: 1, Size: -2.5f)]);

                rings.Should()
                    .OnlyContain(
                        ring => ring.Where((_, texel) => texel != 1 * GridAnchors + 1).All(draw => draw.Answer == 0),
                        "the other anchors' light stays"
                    );
            }

            [Fact]
            public void AnAnchorThatAnsweredKeepsAnsweringWhileItsChangeStaysAboveTheConfirmBar()
            {
                List<RingDraw[]> rings = RunGrid([new(X: 1, Y: 1, Size: 2.5f)]);

                double[] t = WelchOfAnchor(rings, 1, 1);
                int first = Array.FindIndex(t, value => Math.Abs(value) >= SeedBar);
                int last = Array.FindLastIndex(t, value => Math.Abs(value) >= ConfirmBar);
                t.Skip(first).Take(last - first + 1).Should().Contain(value => value < SeedBar, "the change falls below the seed bar");
                AnsweredDraws(rings, 1, 1)
                    .Should()
                    .Equal(Enumerable.Range(first, last - first + 1), "each answer confirms the next draw's weak change");
            }

            [Fact]
            public void AWeakChangeAnswersOneDrawAfterANeighborOfTheSameSignAnswered()
            {
                List<RingDraw[]> rings = RunGrid([new(X: 0, Y: 0, Size: 2.5f), new(X: 1, Y: 0, Size: 1.5f)]);

                WelchOfAnchor(rings, 1, 0).Max(Math.Abs).Should().BeLessThan(SeedBar, "anchor (1, 0)'s change is weak");
                AnsweredDraws(rings, 1, 0).First().Should().Be(AnsweredDraws(rings, 0, 0).First() + 1);
            }

            [Fact]
            public void AWeakAnswerPassesOnToTheNextWeakNeighbor()
            {
                // Anchor (2, 0) is no neighbor of (0, 0): it takes the answer of (1, 0), one draw after (1, 0) took (0, 0)'s.
                List<RingDraw[]> rings = RunGrid(
                    [new(X: 0, Y: 0, Size: 2.5f), new(X: 1, Y: 0, Size: 1.5f), new(X: 2, Y: 0, Size: 1.5f)]
                );

                AnsweredDraws(rings, 2, 0).First().Should().Be(AnsweredDraws(rings, 0, 0).First() + 2);
            }

            [Fact]
            public void AWeakChangeWithoutAnAnsweringNeighborGetsNoAnswer()
            {
                List<RingDraw[]> rings = RunGrid([new(X: 1, Y: 0, Size: 1.5f), new(X: 2, Y: 0, Size: 1.5f)]);

                WelchOfAnchor(rings, 1, 0).Max(Math.Abs).Should().BeGreaterThanOrEqualTo(ConfirmBar);
                rings.Should().OnlyContain(ring => ring.All(draw => draw.Answer == 0), "no change passes the seed bar");
            }

            [Fact]
            public void AWeakChangeDoesNotTakeTheAnswerOfANeighborOfTheOtherSign()
            {
                List<RingDraw[]> rings = RunGrid([new(X: 0, Y: 0, Size: 2.5f), new(X: 1, Y: 0, Size: -1.5f)]);

                AnsweredDraws(rings, 0, 0).Should().NotBeEmpty();
                AnswersOf(rings, 1, 0).Should().OnlyContain(answer => answer == 0, "the neighbor's change has the other sign");
            }

            [Fact]
            public void ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing()
            {
                // Anchor (1, 0)'s weak change passes the confirm bar two draws after (0, 0)'s last answer.
                List<RingDraw[]> rings = RunGrid(
                    [new(X: 0, Y: 0, Size: 2.5f), new(X: 1, Y: 0, Size: 1.5f, From: Start + 17)],
                    draws: 130
                );

                int lastAnswer = AnsweredDraws(rings, 0, 0).Last();
                double[] weak = WelchOfAnchor(rings, 1, 0);
                Math.Abs(weak[lastAnswer + 1]).Should().BeLessThan(ConfirmBar);
                Math.Abs(weak[lastAnswer + 2]).Should().BeGreaterThanOrEqualTo(ConfirmBar);
                AnswersOf(rings, 1, 0).Should().OnlyContain(answer => answer == 0, "only the draw before's answers confirm");
            }

            [Fact]
            public void AnOpenGatesSignedAnswerConfirmsTheWeakChangeOfAClosedNeighbor()
            {
                // A recent light code at the tap of block (20, 20) holds the gate of anchor (2, 2) open: no other anchor's
                // region holds that block. Its weak change gets the open gate's answer, which anchor (1, 1) then takes.
                List<RingDraw[]> rings = RunGrid(
                    [new(X: 2, Y: 2, Size: -1.2f), new(X: 1, Y: 1, Size: -1.5f)],
                    ages: (column, row) => (ushort)(column == 8 * 20 + 4 && row == 8 * 20 + 4 ? RecentLightCode : 0)
                );

                int opened = AnsweredDraws(rings, 2, 2).First();
                rings[opened][2 * GridAnchors + 2].Gate.Should().Be(0, "the cause holds the gate open");
                rings[opened][1 * GridAnchors + 1].Gate.Should().Be(TemporalSignal.RegionCauseWindowDraws);
                WelchOfAnchor(rings, 2, 2).Max(Math.Abs).Should().BeLessThan(SeedBar, "no seed answers (2, 2)'s change");
                AnswerOfAnchor(rings: rings, x: 2, y: 2, draw: opened).Should().Be(-1);
                AnsweredDraws(rings, 1, 1).First().Should().Be(opened + 1);
                AnswerOfAnchor(rings: rings, x: 1, y: 1, draw: opened + 1).Should().Be(-1);
            }

            [Fact]
            public void AfterTheRenderSizeFallsTheLastAnchorReadsNoNeighborBeyondIt()
            {
                // At the window's largest size, 6 × 2 anchors, the light falls everywhere, and every anchor answers in
                // each draw. At half the size the dispatch covers the anchors 0 to 2 of row 0, and the ring's other texels
                // keep the answers of the larger size. Then anchor 2 alone gets a weak change, which no anchor inside the
                // half size answered.
                (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingInAWindow();
                try
                {
                    sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
                    renderer.Render(scene, 1f);
                    Report(renderer, overBudget: false);
                    int frame = 0;
                    for (; frame < 100 + FallDraws; frame++)
                        DrawInTheWindow(
                            renderer: renderer,
                            sample: sample,
                            scene: scene,
                            light: frame < 100 ? 1 : 1 - 0.5f * (frame - 99) / FallDraws,
                            frame: frame,
                            overBudget: false
                        );
                    for (int draw = 0; draw < 40 && renderer.LastScene.Camera.Width > WindowWidth / 2; draw++, frame++)
                        DrawInTheWindow(renderer: renderer, sample: sample, scene: scene, light: 0.5f, frame: frame, overBudget: true);
                    List<RingDraw[]> rings = [];
                    for (int draw = 0; draw < 110; draw++, frame++)
                    {
                        float[] blockLight = [.. Enumerable.Range(0, WindowWidth / 8).Select(block => LightOfBlock(block, [0, 0, draw >= Start ? 1.5f * DesignedSpread : 0]))];
                        float light = DesignedBase + AlternatingNoise(frame);
                        sample.Samples = (column, _) => new Vector4(new Vector3(light + blockLight[column / 8]), 1);
                        Continue(renderer, scene, frames: 1);
                        Report(renderer, overBudget: true);
                        rings.Add(RingOf(renderer, sample, frame + 1));
                    }

                    renderer.LastScene.Camera.Width.Should().Be(WindowWidth / 2, "the render size stayed at half");
                    const int WindowAnchors = WindowWidth / 64;
                    float[] lights = [.. rings.Select(ring => ring[2].Light)];
                    int[] staleBefore = [.. Enumerable.Range(TemporalSignal.RegionRingDraws, rings.Count - TemporalSignal.RegionRingDraws).Where(draw => Math.Abs(WelchOf(lights, draw)) >= ConfirmBar && (rings[draw - 1][3].Answer != 0 || rings[draw - 1][WindowAnchors + 2].Answer != 0 || rings[draw - 1][WindowAnchors + 3].Answer != 0))];
                    staleBefore.Should().NotBeEmpty("anchor 2's weak change meets a stale answer beyond the last anchor");
                    rings.Should().OnlyContain(ring => ring.Take(3).All(draw => draw.Answer == 0), "no anchor inside the half size answered");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }

            /// <summary>The answer of anchor (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="draw"/>.</summary>
            private static float AnswerOfAnchor(List<RingDraw[]> rings, int x, int y, int draw) =>
                rings[draw][y * GridAnchors + x].Answer;

            [Fact]
            public void TheCornerAnchorsTakeTheAnswerOfTheirDiagonalNeighbor()
            {
                List<RingDraw[]> rings = RunGrid(
                    [new(X: 1, Y: 1, Size: 2.5f), new(X: 0, Y: 0, Size: 1.5f), new(X: 2, Y: 2, Size: 1.5f)]
                );

                int seeded = AnsweredDraws(rings, 1, 1).First();
                AnsweredDraws(rings, 0, 0).First().Should().Be(seeded + 1, "the first anchor reads only anchors inside");
                AnsweredDraws(rings, 2, 2).First().Should().Be(seeded + 1, "the last anchor reads only anchors inside");
            }
        }
    }
}
