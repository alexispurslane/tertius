using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Tertius.Testing;

namespace Tertius.Core.Tests;

/// <summary>
/// The values that the simulation and the renderer share for puffs: the shading union (<see cref="GlowPuffs"/>,
/// <see cref="LitPuffs"/> or neither), the look and its validity, the snapshot's puff, and the snapshot's members for
/// puffs. An emitter whose look is not valid makes no puffs, so the validity rules decide which emitters the simulation
/// reads at all.
/// </summary>
public class PuffSeamTests
{
    private static readonly Vector3 Orange = new(6, 3, 1);
    private static readonly Vector3 Pale = new(0.8f, 0.85f, 0.9f);

    /// <summary>A valid hot exhaust look: every number differs from the others, so a swap of two shows.</summary>
    private static readonly PuffLook Exhaust = new(Life: 2, StartRadius: 0.5f, EndRadius: 3, TemperatureRatio: 3,
        Shading: PuffShading.Glow(Orange));

    /// <summary>The names of the cases whose <c>TryGetValue</c> answers true for the shading.</summary>
    private static string[] CasesHeld(PuffShading shading)
    {
        var held = new List<string>();
        if (shading.TryGetValue(out GlowPuffs _))
            held.Add(nameof(GlowPuffs));
        if (shading.TryGetValue(out LitPuffs _))
            held.Add(nameof(LitPuffs));
        return [.. held];
    }

    /// <summary>The value that each <c>TryGetValue</c> hands out, whatever it answers.</summary>
    private static (GlowPuffs Glow, LitPuffs Lit) Views(PuffShading shading)
    {
        shading.TryGetValue(out GlowPuffs glow);
        shading.TryGetValue(out LitPuffs lit);
        return (glow, lit);
    }

    // ---- The shading union ---------------------------------------------------------------------------------------

    [Fact]
    public void ThePuffShadingUnionHoldsOneCaseAndMatchesWithoutBoxing()
    {
        PuffShading glow = PuffShading.Glow(Orange);
        PuffShading lit = PuffShading.Lit(Pale);

        CasesHeld(glow).Should().Equal(nameof(GlowPuffs));
        CasesHeld(lit).Should().Equal(nameof(LitPuffs));
        Views(glow).Should().Be((new(Orange), default), "the radiance does not leak out as an albedo");
        Views(lit).Should().Be((default, new(Pale)), "the albedo does not leak out as a radiance");

        // A pattern match reads the color of the case in place. Boxing the case, as Value does, would allocate.
        // The red channels are exact in binary, so the sum is exact.
        PuffShading[] shadings = [glow, PuffShading.Lit(new(0.5f, 0.85f, 0.9f)), default];
        float total = 0;
        foreach (PuffShading shading in shadings)
            total += RedOf(shading);
        long allocated = Allocations.Fewest(() =>
        {
            total = 0;
            for (int round = 0; round < 100; round++)
                foreach (PuffShading shading in shadings)
                    total += RedOf(shading);
        });

        allocated.Should().Be(0);
        total.Should().Be((6 + 0.5f + 100) * 100,
            "a glow of red 6, an albedo of red 0.5 and the empty shading's 100, over 100 rounds");
    }

    private static float RedOf(in PuffShading shading)
    {
        return shading switch
        {
            GlowPuffs(var radiance) => radiance.X,
            LitPuffs(var albedo) => albedo.X,
            _ => 100
        };
    }

    [Fact]
    public void TheDefaultShadingHoldsNoCase()
    {
        PuffShading shading = default;

        shading.HasValue.Should().BeFalse();
        shading.Value.Should().BeNull();
        CasesHeld(shading).Should().BeEmpty();
        Views(shading).Should().Be((default, default));
    }

    [Fact]
    public void AShadingBuiltFromACaseIsTheShadingThatTheFactoryBuilds()
    {
        PuffShading viaConstructor = new(new GlowPuffs(Orange));
        PuffShading viaConversion = new LitPuffs(Pale);

        (viaConstructor == PuffShading.Glow(Orange)).Should().BeTrue();
        (viaConversion == PuffShading.Lit(Pale)).Should().BeTrue();
        viaConstructor.HasValue.Should().BeTrue();
        viaConversion.HasValue.Should().BeTrue();
    }

    [Fact]
    public void ValueBoxesTheCaseForDebugging()
    {
        PuffShading.Glow(Orange).Value.Should().Be(new GlowPuffs(Orange));
        PuffShading.Lit(Pale).Value.Should().Be(new LitPuffs(Pale));
    }

    [Fact]
    public void TheSameColorAsAGlowAndAsAnAlbedoIsTwoDifferentShadings()
    {
        PuffShading glow = PuffShading.Glow(Pale);
        PuffShading lit = PuffShading.Lit(Pale);

        (glow == lit).Should().BeFalse();
        (glow != lit).Should().BeTrue();
        glow.Equals(lit).Should().BeFalse();
        glow.Equals((object)lit).Should().BeFalse();
        PuffShading.Glow(Vector3.Zero).Should().NotBe(default(PuffShading), "a glow of black still holds a case");
        PuffShading.Lit(Vector3.Zero).Should().NotBe(default(PuffShading), "a black albedo still holds a case");
    }

    [Fact]
    public void ShadingsOfTheSameCaseAndColorAreEqualAndHashTheSame()
    {
        PuffShading[] first = [PuffShading.Glow(Orange), PuffShading.Lit(Pale), default];
        PuffShading[] second = [PuffShading.Glow(Orange), PuffShading.Lit(Pale), default];

        for (int i = 0; i < first.Length; i++)
        {
            first[i].Equals(second[i]).Should().BeTrue($"case {i}");
            first[i].Equals((object)second[i]).Should().BeTrue($"case {i} as an object");
            (first[i] == second[i]).Should().BeTrue($"case {i} by ==");
            (first[i] != second[i]).Should().BeFalse($"case {i} by !=");
            first[i].GetHashCode().Should().Be(second[i].GetHashCode(), $"case {i}");
        }

        PuffShading.Glow(Orange).Should().NotBe(PuffShading.Glow(Orange with { Z = 2 }), "another radiance");
        PuffShading.Lit(Pale).Should().NotBe(PuffShading.Lit(Pale with { Y = 0.5f }), "another albedo");
        PuffShading.Glow(Orange).Equals(null).Should().BeFalse();
        PuffShading.Glow(Orange).Equals((object)new GlowPuffs(Orange)).Should().BeFalse("a case is not the union");
    }

    [Fact]
    public void AShadingThatHoldsANaNEqualsItselfAndAnyCopyAndHashesTheSame()
    {
        // Equals must be reflexive, even for a color that is not a number: a value that is not equal to itself breaks
        // every set and every dictionary that holds it, such as the look table. So a NaN equals a NaN here.
        var notANumber = new Vector3(float.NaN, 0, 1);
        PuffShading[] shadings = [PuffShading.Glow(notANumber), PuffShading.Lit(notANumber)];

        foreach (PuffShading shading in shadings)
        {
            PuffShading copy = shading.TryGetValue(out GlowPuffs glow)
                ? PuffShading.Glow(glow.Radiance)
                : PuffShading.Lit(((LitPuffs)shading.Value!).Albedo);

            shading.Equals(shading).Should().BeTrue($"{shading} equals itself");
            (shading == copy).Should().BeTrue($"{shading} equals a copy of itself");
            shading.GetHashCode().Should()
                .Be(copy.GetHashCode(), $"{shading} and its copy are equal, so they hash the same");
        }
    }

    [Fact]
    public void ToStringNamesTheCaseAndItsColorAndSaysNoneForTheDefault()
    {
        default(PuffShading).ToString().Should().Be("PuffShading(none)");
        PuffShading.Glow(Orange).ToString().Should().StartWith("PuffShading(").And.Contain(nameof(GlowPuffs)).And
            .Contain("Radiance");
        PuffShading.Lit(Pale).ToString().Should().StartWith("PuffShading(").And.Contain(nameof(LitPuffs)).And
            .Contain("Albedo");
    }

    [Fact]
    public void AShadingTakesSixteenBytesAndALookThirtyTwo()
    {
        // One color and a tag. A look is its four numbers and its shading. An emitter carries a look in every render row,
        // and the entity table holds each render row twice.
        Unsafe.SizeOf<PuffShading>().Should().Be(16);
        Unsafe.SizeOf<PuffLook>().Should().Be(32);
    }

    [Fact]
    public void NoPublicMemberOfTheUnionExposesTheColorOfOneCase()
    {
        // The colors are reachable only through TryGetValue and a pattern match, which name the case. A public property
        // or field of the union could be read whichever case the shading holds.
        const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
        Type union = typeof(PuffShading);

        union.GetFields(publicInstance).Should().BeEmpty();
        union.GetProperties(publicInstance).Select(property => property.PropertyType).Should()
            .OnlyContain(type => type == typeof(bool) || type == typeof(object));
        union.GetConstructors().Select(constructor => constructor.GetParameters().Single().ParameterType)
            .Should().BeEquivalentTo([typeof(GlowPuffs), typeof(LitPuffs)],
                "a shading is made from one case and nothing else");
    }

    // ---- Validity ------------------------------------------------------------------------------------------------

    [Fact]
    public void AShadingIsValidOnlyWithACaseAndAFiniteColorOfNoNegativeChannel()
    {
        PuffShading.Glow(Orange).IsValid.Should().BeTrue();
        PuffShading.Lit(Pale).IsValid.Should().BeTrue();
        PuffShading.Glow(Vector3.Zero).IsValid.Should().BeTrue("a glow of black is dark, not wrong");
        PuffShading.Lit(Vector3.Zero).IsValid.Should().BeTrue("a black albedo scatters nothing, but it is a color");
        PuffShading.Glow(new(-0f, 0, 0)).IsValid.Should().BeTrue("negative zero is zero");
        default(PuffShading).IsValid.Should().BeFalse("the empty shading says nothing about how a puff shows");

        foreach (int channel in new[] { 0, 1, 2 })
        foreach (float bad in new[] { -0.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Vector3 color = Pale;
            color[channel] = bad;
            PuffShading.Glow(color).IsValid.Should().BeFalse($"a radiance of {bad} in channel {channel}");
            PuffShading.Lit(color).IsValid.Should().BeFalse($"an albedo of {bad} in channel {channel}");
        }
    }

    [Fact]
    public void ALookIsValidOnlyWithAPositiveLifeAndTemperatureRatioFiniteRadiiAndAShading()
    {
        Exhaust.IsValid.Should().BeTrue();
        (Exhaust with { StartRadius = 0, EndRadius = 0 }).IsValid.Should()
            .BeTrue("a puff of no size is invisible, not wrong");
        (Exhaust with { Shading = PuffShading.Lit(Pale), TemperatureRatio = 0.75f }).IsValid.Should()
            .BeTrue("cold gas");
        (Exhaust with { Life = float.Epsilon, TemperatureRatio = float.Epsilon }).IsValid.Should()
            .BeTrue("the least numbers above 0");

        float[] notFinite = [float.NaN, float.PositiveInfinity, float.NegativeInfinity];
        foreach (float bad in notFinite.Concat([0f, -0f, -1f]))
        {
            (Exhaust with { Life = bad }).IsValid.Should().BeFalse($"a life of {bad}");
            (Exhaust with { TemperatureRatio = bad }).IsValid.Should().BeFalse($"a temperature ratio of {bad}");
        }

        foreach (float bad in notFinite.Concat([-0.01f]))
        {
            (Exhaust with { StartRadius = bad }).IsValid.Should().BeFalse($"a start radius of {bad}");
            (Exhaust with { EndRadius = bad }).IsValid.Should().BeFalse($"an end radius of {bad}");
        }

        (Exhaust with { Shading = default }).IsValid.Should().BeFalse("a look without a shading");
        (Exhaust with { Shading = PuffShading.Glow(new(1, float.NaN, 1)) }).IsValid.Should()
            .BeFalse("a shading that is not valid");
        default(PuffLook).IsValid.Should().BeFalse("the default look, of the default emitter, makes no puffs");
    }

    // ---- The snapshot --------------------------------------------------------------------------------------------

    [Fact]
    public void ClearEmptiesThePuffsAndTheirLooksAndTheAgeStep()
    {
        var snapshot = new RenderSnapshot();
        snapshot.Puffs.Add(new() { Position = Vector3.One, PreviousPosition = Vector3.Zero, Age = 0.5f, Look = 0 });
        snapshot.PuffLooks.Add(Exhaust);
        snapshot.PuffAgeStep = 1 / 60f;

        snapshot.Clear();

        snapshot.Puffs.Should().BeEmpty();
        snapshot.PuffLooks.Should().BeEmpty();
        snapshot.PuffAgeStep.Should().Be(0);
    }

    [Fact]
    public void ARenderPuffIs32Bytes()
    {
        // Two positions, an age and a look index. The snapshot carries one per live puff, up to the capacity.
        Unsafe.SizeOf<RenderPuff>().Should().Be(32);
    }
}
