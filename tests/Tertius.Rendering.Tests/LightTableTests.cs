using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// ReSTIR's light table (docs/plans/temporal-renderer.md, section 7, "The candidates" and "The reservoirs"): which lights
/// are sampled and which are exact, each sampled light's source probability in the alias table, and the remap of the last
/// frame's light indices by the lights' keys. No GPU needed.
/// </summary>
public sealed class LightTableTests
{
    private static readonly Extent2D Output = new(1280, 720);

    /// <summary>A light of the main view, of entity <paramref name="slot"/> and <paramref name="generation"/>, at <paramref name="distance"/> m.</summary>
    private static PreparedLight Light(
        uint slot,
        float power = 100,
        double distance = 10,
        uint generation = 1,
        LightShadow shadow = default,
        MainViewTier tier = MainViewTier.InView
    ) =>
        new()
        {
            EntityIndex = slot,
            EntityGeneration = generation,
            Position = new(0, 0, -(float)distance),
            RadiantIntensity = new(power, power, power),
            Radius = 0.05f,
            Reach = 100,
            Distance = distance,
            RankDistance = distance,
            Tier = tier,
            Shadow = shadow,
        };

    private static LightShadow Cascades(int set) => new(new CascadeSet(Set: set, Occluders: default, Mapped: 0));

    private static RenderSnapshot Snapshot()
    {
        var snapshot = new RenderSnapshot { CameraCuts = 1 };
        snapshot.Chain.Add(new(FrameId: 7, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        return snapshot;
    }

    /// <summary>Fills a table of <paramref name="lights"/> in a draw of <paramref name="history"/>, and commits the draw.</summary>
    private static LightTable Draw(FrameHistory history, PreparedLight[] lights)
    {
        RenderSnapshot snapshot = Snapshot();
        FrameHistory.PendingFrame pending = history.Begin(
            snapshot: snapshot,
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            outputSize: Output,
            renderSize: Output
        );
        var table = new LightTable();
        table.Fill(lights, pending);
        pending.Write(
            new(
                EyeInFrame: default,
                FrameId: snapshot.OriginFrameId,
                Rotation: Quaternion.Identity,
                TanHalfX: 1,
                TanHalfY: 1,
                RootRotation: Quaternion.Identity
            )
        );
        history.Drew(pending);
        return table;
    }

    /// <summary>The probability with which the table's alias draw picks <paramref name="light"/>: the sum over its entries.</summary>
    private static double ProbabilityOf(LightTable table, int light)
    {
        ReadOnlySpan<LightSample> samples = table.Samples;
        double sum = 0;
        foreach (LightSample sample in samples)
        {
            if (sample.Light == light)
                sum += sample.Threshold;
            if (samples[sample.Alias].Light == light)
                sum += 1 - sample.Threshold;
        }

        return sum / samples.Length;
    }

    [Fact]
    public void EachSampledLightsProbabilityIsHalfItsShareOfTheRankIrradiancePlusHalfAUniformShare()
    {
        PreparedLight[] lights =
        [
            Light(slot: 1, power: 100, distance: 2),
            Light(slot: 2, power: 400, distance: 7),
            Light(slot: 3, power: 5, distance: 30),
            Light(slot: 4, power: 1000, distance: 3.5),
            Light(slot: 5, power: 0.25f, distance: 1),
        ];
        double[] ranks = [.. lights.Select(l => (double)LightUnits.RankIrradiance(l))];

        LightTable table = Draw(new FrameHistory(StillRule.Window), lights);

        for (int i = 0; i < lights.Length; i++)
        {
            double share = 0.5 * ranks[i] / ranks.Sum() + 0.5 / lights.Length;
            ProbabilityOf(table, i).Should().BeApproximately(share, 1e-12, "light {0} of the list", i);
            table.Samples.ToArray().Single(s => s.Light == i).Share.Should().BeApproximately(share, 1e-12);
        }
    }

    [Fact]
    public void ALightWithACascadeSetIsExactAndHasNoEntry()
    {
        PreparedLight[] lights =
        [
            Light(slot: 1),
            Light(slot: 2, power: 1e9f, distance: 1.5e11, shadow: Cascades(0)),
            Light(slot: 3),
        ];

        LightTable table = Draw(new FrameHistory(StillRule.Window), lights);

        table.Exact.ToArray().Should().Equal(1);
        table.Samples.ToArray().Select(s => s.Light).Should().BeEquivalentTo([0, 2]);
    }

    [Fact]
    public void AFarLightWithoutACascadeSetIsASampledLight()
    {
        PreparedLight[] lights =
        [
            Light(slot: 1, power: 1e9f, distance: 1.5e11, shadow: new(new FarShadowsOnly(default))),
            Light(slot: 2),
        ];

        LightTable table = Draw(new FrameHistory(StillRule.Window), lights);

        table.Exact.Length.Should().Be(0);
        table.Samples.ToArray().Select(s => s.Light).Should().BeEquivalentTo([0, 1]);
    }

    [Fact]
    public void ALightThatOnlyAFaceOfAMapKeepsIsNeitherSampledNorExact()
    {
        PreparedLight[] lights = [Light(slot: 1, tier: MainViewTier.FaceOnly), Light(slot: 2)];

        LightTable table = Draw(new FrameHistory(StillRule.Window), lights);

        table.Samples.ToArray().Select(s => s.Light).Should().Equal(1);
        table.Exact.Length.Should().Be(0);
    }

    [Fact]
    public void TheRemapGivesEachIndexOfTheLastFrameItsIndexInThisFrameOrNone()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history, [Light(slot: 1), Light(slot: 3), Light(slot: 5), Light(slot: 6)]);

        LightTable table = Draw(
            history,
            [Light(slot: 3), Light(slot: 4), Light(slot: 5, generation: 2), Light(slot: 6, shadow: Cascades(0))]
        );

        table
            .Remap.ToArray()
            .Should()
            .Equal(
                [LightTable.NoLight, 0, LightTable.NoLight, LightTable.NoLight],
                "slot 1 is gone, slot 3 moved to index 0, slot 5 holds another entity, and slot 6 became exact"
            );
    }

    [Fact]
    public void ALightWhoseIndexInTheListDoesNotFitAReservoirIsNotSampled()
    {
        // A reservoir holds a light's index in the list in 16 bits, and NoLight is the largest of them.
        PreparedLight[] lights = new PreparedLight[LightTable.NoLight + 2];
        for (int i = 0; i < lights.Length; i++)
            lights[i] = Light(slot: (uint)i + 1, tier: i is 0 or >= LightTable.NoLight ? MainViewTier.InView : MainViewTier.FaceOnly);

        LightTable table = Draw(new FrameHistory(StillRule.Window), lights);

        table.Samples.ToArray().Select(s => s.Light).Should().Equal(0);
    }

    [Fact]
    public void TheTableKeepsItsInvariantsThroughDrawsThatAddRemoveAndChangeLights()
    {
        var history = new FrameHistory(StillRule.Window);
        PreparedLight[][] draws =
        [
            [Light(slot: 1), Light(slot: 2, shadow: Cascades(0)), Light(slot: 4, power: 7, distance: 3)],
            [Light(slot: 2, shadow: Cascades(0)), Light(slot: 3), Light(slot: 4, generation: 2), Light(slot: 9)],
            [Light(slot: 3, tier: MainViewTier.FaceOnly), Light(slot: 9, power: 0)],
            [],
            [Light(slot: 5, power: 0), Light(slot: 6, power: 0)],
        ];

        foreach (PreparedLight[] lights in draws)
            Draw(history, lights).Invoking(table => table.AssertInvariants()).Should().NotThrow();
    }

    [Fact]
    public void AFirstDrawHasAnEmptyRemap() =>
        Draw(new FrameHistory(StillRule.Window), [Light(slot: 1)]).Remap.Length.Should().Be(0);
}
