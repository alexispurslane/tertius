using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Tertius.Testing;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="FrameOutline"/> as a custom C# 15 union of five cases, one for each kind of frame that has an outline:
/// <see cref="SemanticOutline"/>, <see cref="FallingOutline"/>, <see cref="RestingOutline"/>, <see cref="NestingOutline"/>
/// and <see cref="FollowingOutline"/>. The default value holds no case: it is no outline. These tests pin what a union
/// promises. Each case goes in through its constructor and comes out through its <c>TryGetValue</c>. No other
/// <c>TryGetValue</c> answers true, so the four sphere cases, which have the same fields, never pass for each other, and the
/// rotation and the radii of a semantic outline never show up as fields of a sphere. Equality needs the same case and the
/// same fields. A <c>switch</c> over the outline takes the arm of the case that it holds. The render snapshot tests in the
/// simulation module pin which outline the builder gives each frame.
/// </summary>
public class FrameOutlineUnionTests
{
    private static readonly Vector3 Here = new(1, 2, 3);
    private static readonly Quaternion Turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
    private static readonly Vector3 Bubble = new(30, 40, 50);

    private static readonly SemanticOutline Ship = new(Here, Turned, Bubble);
    private static readonly FallingOutline Falling = new(Here, 7);
    private static readonly RestingOutline Resting = new(Here, 7);
    private static readonly NestingOutline Nesting = new(Here, 7);
    private static readonly FollowingOutline Following = new(Here, 7);

    /// <summary>One outline of each case. The four sphere cases share their center and radius, so only the case tells them apart.</summary>
    private static readonly FrameOutline[] EveryCase = [Ship, Falling, Resting, Nesting, Following];

    private static readonly string[] CaseNames =
    [
        nameof(SemanticOutline), nameof(FallingOutline), nameof(RestingOutline), nameof(NestingOutline),
        nameof(FollowingOutline)
    ];

    private static readonly object[] DefaultViews =
    [
        default(SemanticOutline), default(FallingOutline), default(RestingOutline), default(NestingOutline),
        default(FollowingOutline)
    ];

    /// <summary>The names of the cases whose <c>TryGetValue</c> answers true for the outline.</summary>
    private static string[] CasesHeld(FrameOutline outline)
    {
        var held = new List<string>();
        if (outline.TryGetValue(out SemanticOutline _))
            held.Add(nameof(SemanticOutline));
        if (outline.TryGetValue(out FallingOutline _))
            held.Add(nameof(FallingOutline));
        if (outline.TryGetValue(out RestingOutline _))
            held.Add(nameof(RestingOutline));
        if (outline.TryGetValue(out NestingOutline _))
            held.Add(nameof(NestingOutline));
        if (outline.TryGetValue(out FollowingOutline _))
            held.Add(nameof(FollowingOutline));
        return [.. held];
    }

    /// <summary>The value that each <c>TryGetValue</c> hands out, whatever it answers.</summary>
    private static object[] Views(FrameOutline outline)
    {
        outline.TryGetValue(out SemanticOutline semantic);
        outline.TryGetValue(out FallingOutline falling);
        outline.TryGetValue(out RestingOutline resting);
        outline.TryGetValue(out NestingOutline nesting);
        outline.TryGetValue(out FollowingOutline following);
        return [semantic, falling, resting, nesting, following];
    }

    private static string ArmOf(FrameOutline outline) => outline switch
    {
        SemanticOutline(_, _, var radii) => $"semantic {radii.Z}",
        FallingOutline(_, var radius) => $"falling {radius}",
        RestingOutline(_, var radius) => $"resting {radius}",
        NestingOutline(_, var radius) => $"nesting {radius}",
        FollowingOutline(_, var radius) => $"following {radius}",
        null => "none"
    };

    // ---- The default value ---------------------------------------------------------------------------------------

    [Fact]
    public void TheDefaultOutlineHoldsNoCase()
    {
        FrameOutline outline = default;

        outline.HasValue.Should().BeFalse();
        outline.Value.Should().BeNull();
        CasesHeld(outline).Should().BeEmpty();
        Views(outline).Should().Equal(DefaultViews);
    }

    // ---- One constructor and one TryGetValue per case ------------------------------------------------------------

    [Fact]
    public void EachCaseComesBackThroughItsOwnTryGetValueAndNoOtherCaseAnswers()
    {
        for (int i = 0; i < EveryCase.Length; i++)
        {
            FrameOutline outline = EveryCase[i];

            outline.HasValue.Should().BeTrue(CaseNames[i]);
            CasesHeld(outline).Should().Equal([CaseNames[i]], "only the case that the outline holds answers");
        }

        (new FrameOutline(Ship) is SemanticOutline).Should().BeTrue("a pattern match reads the same case");
        (new FrameOutline(Resting) is RestingOutline).Should().BeTrue("a pattern match reads the same case");
        (new FrameOutline(Resting) is FallingOutline).Should().BeFalse("a sphere case never passes for another one");
    }

    [Fact]
    public void ATryGetValueThatAnswersFalseHandsOutTheDefaultValueNotTheFieldsOfAnotherCase()
    {
        // Every case holds a center and a size, so a case that read a shared field without checking would show it.
        for (int i = 0; i < EveryCase.Length; i++)
        {
            object[] expected = [.. DefaultViews];
            expected[i] = EveryCase[i].Value!;

            Views(EveryCase[i]).Should().Equal(expected, CaseNames[i]);
        }
    }

    [Fact]
    public void EveryFieldOfEveryCaseSurvivesTheRoundTrip()
    {
        // Every field differs from every other field, so a swap of two fields shows.
        var rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        new FrameOutline(new SemanticOutline(new(1, 2, 3), rotation, new(4, 5, 6)))
            .TryGetValue(out SemanticOutline semantic).Should().BeTrue();
        semantic.Center.Should().Be(new Vector3(1, 2, 3));
        semantic.Rotation.Should().Be(rotation);
        semantic.Radii.Should().Be(new Vector3(4, 5, 6));

        new FrameOutline(new FallingOutline(new(1, 2, 3), 4)).TryGetValue(out FallingOutline falling).Should().BeTrue();
        falling.Should().Be(new FallingOutline(new(1, 2, 3), 4));
        new FrameOutline(new RestingOutline(new(5, 6, 7), 8)).TryGetValue(out RestingOutline resting).Should().BeTrue();
        resting.Should().Be(new RestingOutline(new(5, 6, 7), 8));
        new FrameOutline(new NestingOutline(new(9, 10, 11), 12)).TryGetValue(out NestingOutline nesting).Should()
            .BeTrue();
        nesting.Should().Be(new NestingOutline(new(9, 10, 11), 12));
        new FrameOutline(new FollowingOutline(new(13, 14, 15), 16)).TryGetValue(out FollowingOutline following).Should()
            .BeTrue();
        following.Should().Be(new FollowingOutline(new(13, 14, 15), 16));
    }

    [Fact]
    public void EveryCaseConvertsImplicitlyToTheOutlineThatItsConstructorBuilds()
    {
        FrameOutline[] converted = [Ship, Falling, Resting, Nesting, Following];
        FrameOutline[] constructed = [new(Ship), new(Falling), new(Resting), new(Nesting), new(Following)];

        converted.Should().Equal(constructed);
        converted.Select(outline => outline.Value).Should().Equal([Ship, Falling, Resting, Nesting, Following],
            "Value boxes the case that the outline holds");
    }

    // ---- Pattern matching ----------------------------------------------------------------------------------------

    [Fact]
    public void ASwitchOverAnOutlineTakesTheArmOfTheCaseItHoldsAndTheLastArmForTheDefault()
    {
        EveryCase.Select(ArmOf).Should().Equal("semantic 50", "falling 7", "resting 7", "nesting 7", "following 7");
        ArmOf(default).Should().Be("none");
    }

    [Fact]
    public void ReadingAnOutlineByPatternAllocatesNothing()
    {
        // A pattern match reads the fields of the case in place. Boxing the case, as Value does, would allocate.
        FrameOutline[] outlines = [.. EveryCase, default];
        float total = 0;
        foreach (FrameOutline outline in outlines)
            total += Reach(outline);
        long allocated = Allocations.Fewest(() =>
        {
            total = 0;
            for (int round = 0; round < 100; round++)
                foreach (FrameOutline outline in outlines)
                    total += Reach(outline);
        });

        allocated.Should().Be(0);
        total.Should().Be((50 + 4 * 7) * 100,
            "a bubble of largest radius 50 and four spheres of radius 7, over 100 rounds");
    }

    private static float Reach(in FrameOutline outline)
    {
        return outline switch
        {
            SemanticOutline(_, _, var radii) => MathF.Max(radii.X, MathF.Max(radii.Y, radii.Z)),
            FallingOutline(_, var radius) => radius,
            RestingOutline(_, var radius) => radius,
            NestingOutline(_, var radius) => radius,
            FollowingOutline(_, var radius) => radius,
            null => 0
        };
    }

    // ---- Equality ------------------------------------------------------------------------------------------------

    [Fact]
    public void TwoOutlinesOfTheSameCaseWithTheSameFieldsAreEqualAndHashAlike()
    {
        FrameOutline[] first = [.. EveryCase, default];
        FrameOutline[] second =
        [
            new SemanticOutline(Here, Turned, Bubble), new FallingOutline(Here, 7), new RestingOutline(Here, 7),
            new NestingOutline(Here, 7),
            new FollowingOutline(Here, 7), default
        ];

        for (int i = 0; i < first.Length; i++)
        {
            first[i].Equals(second[i]).Should().BeTrue($"case {i}");
            first[i].Equals((object)second[i]).Should().BeTrue($"case {i} as an object");
            (first[i] == second[i]).Should().BeTrue($"case {i} by ==");
            (first[i] != second[i]).Should().BeFalse($"case {i} by !=");
            first[i].GetHashCode().Should().Be(second[i].GetHashCode(), $"case {i}");
        }
    }

    [Fact]
    public void TwoSemanticOutlinesThatDifferInOneFieldAreNotEqual()
    {
        FrameOutline outline = Ship;

        outline.Should().NotBe(new FrameOutline(Ship with { Center = new(1, 2, 4) }), "another center");
        outline.Should().NotBe(new FrameOutline(Ship with { Rotation = Quaternion.Identity }), "another rotation");
        outline.Should().NotBe(new FrameOutline(Ship with { Radii = new(30, 40, 51) }), "other radii");
    }

    [Fact]
    public void TwoSphereOutlinesOfOneCaseThatDifferInOneFieldAreNotEqual()
    {
        FrameOutline outline = Falling;

        outline.Should().NotBe(new FrameOutline(Falling with { Center = new(1, 2, 4) }), "another center");
        outline.Should().NotBe(new FrameOutline(Falling with { Radius = 8 }), "another radius");
    }

    [Fact]
    public void OutlinesOfDifferentCasesAreNotEqualEvenWhenTheirSharedFieldsMatch()
    {
        // The four sphere cases have the same center and radius, and the semantic one has the same center, so only the case
        // tells them apart.
        for (int i = 0; i < EveryCase.Length; i++)
        for (int j = 0; j < EveryCase.Length; j++)
            if (i != j)
            {
                EveryCase[i].Equals(EveryCase[j]).Should().BeFalse($"{CaseNames[i]} against {CaseNames[j]}");
                (EveryCase[i] == EveryCase[j]).Should().BeFalse($"{CaseNames[i]} against {CaseNames[j]} by ==");
            }

        new FrameOutline(new SemanticOutline(Here, default, new(7, 0, 0))).Should().NotBe(new FrameOutline(Falling),
            "a semantic outline whose first radius is the sphere's radius is still another case");
    }

    [Fact]
    public void AnOutlineWithACaseIsNeverEqualToTheEmptyOutlineEvenWhenEveryFieldIsZero()
    {
        FrameOutline empty = default;

        new FrameOutline(new SemanticOutline(default, default, default)).Should().NotBe(empty);
        new FrameOutline(new FallingOutline(default, 0)).Should().NotBe(empty);
        new FrameOutline(new FollowingOutline(default, 0)).Should().NotBe(empty);
        (empty != new FrameOutline(new NestingOutline(default, 0))).Should().BeTrue();
    }

    [Fact]
    public void AnOutlineIsNotEqualToAnObjectThatIsNotAnOutline()
    {
        new FrameOutline(Falling).Equals((object)Falling).Should().BeFalse();
        new FrameOutline(Falling).Equals(null).Should().BeFalse();
    }

    [Fact]
    public void AnOutlineThatHoldsANaNEqualsItselfAndAnyCopyAndHashesTheSame()
    {
        // Equals must be reflexive, even for a field that is not a number: a value that is not equal to itself breaks
        // every set and every dictionary that holds it. So a NaN equals a NaN here, as it does in Vector3.Equals.
        FrameOutline[] outlines =
        [
            Ship with { Radii = new(float.NaN, 1, 1) }, Ship with { Rotation = new(float.NaN, 0, 0, 1) },
            Falling with { Radius = float.NaN }, Following with { Center = new(0, float.NaN, 0) }
        ];

        foreach (FrameOutline outline in outlines)
        {
            FrameOutline copy = outline.Value switch
            {
                SemanticOutline semantic => semantic,
                FallingOutline falling => falling,
                FollowingOutline following => following,
                _ => default
            };

            outline.Equals(outline).Should().BeTrue($"{outline} equals itself");
            (outline == copy).Should().BeTrue($"{outline} equals a copy of itself");
            outline.GetHashCode().Should()
                .Be(copy.GetHashCode(), $"{outline} and its copy are equal, so they hash the same");
        }
    }

    // ---- ToString ------------------------------------------------------------------------------------------------

    [Fact]
    public void ToStringNamesTheCaseAndItsFieldsAndSaysNoneForTheDefault()
    {
        default(FrameOutline).ToString().Should().Be("FrameOutline(none)");
        for (int i = 0; i < EveryCase.Length; i++)
            EveryCase[i].ToString().Should().StartWith("FrameOutline(").And.Contain(CaseNames[i]);
        new FrameOutline(Ship).ToString().Should().Contain("Radii");
        new FrameOutline(Nesting).ToString().Should().Contain("Radius = 7");
    }

    // ---- Shape ---------------------------------------------------------------------------------------------------

    [Fact]
    public void NoPublicMemberOfTheUnionExposesTheFieldsOfOneCase()
    {
        // The fields are reachable only through TryGetValue and a pattern match, which name the case. A public property
        // or field of the union could be read whichever case the outline holds.
        const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
        Type union = typeof(FrameOutline);

        union.GetFields(publicInstance).Should().BeEmpty();
        union.GetProperties(publicInstance).Select(property => property.PropertyType).Should()
            .OnlyContain(type => type == typeof(bool) || type == typeof(object));
        union.GetConstructors().Select(constructor => constructor.GetParameters().Single().ParameterType).Should()
            .BeEquivalentTo(
                [
                    typeof(SemanticOutline), typeof(FallingOutline), typeof(RestingOutline), typeof(NestingOutline),
                    typeof(FollowingOutline)
                ],
                "an outline is made from one case and nothing else");
    }

    [Fact]
    public void AnOutlineTakesFortyEightBytesOneSemanticOutlineASphereRadiusAndItsKindByte()
    {
        // The cases share the storage of the center, and the four sphere cases share one radius, so the union holds one
        // semantic outline's 40 bytes, one radius and a kind byte padded to 4. Storing each case whole would take 104 bytes.
        Unsafe.SizeOf<SemanticOutline>().Should().Be(40);
        Unsafe.SizeOf<FrameOutline>().Should().Be(48);
    }
}
