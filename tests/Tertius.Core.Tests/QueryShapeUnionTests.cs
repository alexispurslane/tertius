using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="QueryShape"/> as a custom C# 15 union of three cases: <see cref="QuerySphere"/>, <see cref="QueryBox"/>
/// and <see cref="QueryCapsule"/>. The default value holds no case, and every query refuses it. These tests pin what a
/// union promises: each case goes in through its constructor and comes out through its <c>TryGetValue</c>, no other
/// <c>TryGetValue</c> answers true, equality needs the same case and the same fields, and a <c>switch</c> over the shape
/// takes the arm of the case that it holds. <see cref="QueryShapeTests"/> pins what the shape does with each case: its
/// bounding radius and the reasons why a query refuses it.
/// </summary>
public class QueryShapeUnionTests
{
    // Every field of every case differs from the fields of the other cases, so that a case that reads a field of another
    // case, or a swapped pair of fields, shows.
    private static readonly Vector3 Half = new(1, 2, 3);

    /// <summary>The names of the cases whose <c>TryGetValue</c> answers true for the shape.</summary>
    private static string[] CasesHeld(QueryShape shape)
    {
        var held = new List<string>();
        if (shape.TryGetValue(out QuerySphere _))
            held.Add(nameof(QuerySphere));
        if (shape.TryGetValue(out QueryBox _))
            held.Add(nameof(QueryBox));
        if (shape.TryGetValue(out QueryCapsule _))
            held.Add(nameof(QueryCapsule));
        return [.. held];
    }

    /// <summary>The value that each <c>TryGetValue</c> hands out, whatever it answers.</summary>
    private static (QuerySphere Sphere, QueryBox Box, QueryCapsule Capsule) Views(QueryShape shape)
    {
        shape.TryGetValue(out QuerySphere sphere);
        shape.TryGetValue(out QueryBox box);
        shape.TryGetValue(out QueryCapsule capsule);
        return (sphere, box, capsule);
    }

    private static string ArmOf(QueryShape shape) => shape switch
    {
        QuerySphere(var radius) => $"sphere {radius}",
        QueryBox(var halfExtents) => $"box {halfExtents.X} {halfExtents.Y} {halfExtents.Z}",
        QueryCapsule(var halfHeight, var radius) => $"capsule {halfHeight} {radius}",
        _ => "none"
    };

    /// <summary>One shape of each case.</summary>
    private static QueryShape[] OneOfEachCase() =>
    [
        new(new QuerySphere(5)),
        new(new QueryBox(Half)),
        new(new QueryCapsule(3, 0.5f))
    ];

    // ---- The default value ---------------------------------------------------------------------------------------

    [Fact]
    public void TheDefaultShapeHoldsNoCase()
    {
        QueryShape shape = default;

        shape.HasValue.Should().BeFalse();
        shape.Value.Should().BeNull();
        CasesHeld(shape).Should().BeEmpty();
    }

    [Fact]
    public void TheDefaultShapeHasNoReach()
    {
        default(QueryShape).BoundingRadius.Should().Be(0);
    }

    [Fact]
    public void TheDefaultShapeIsRefusedWithAProblemThatSaysItHoldsNoShape()
    {
        string? problem = default(QueryShape).Problem();

        problem.Should().NotBeNull();
        problem.Should().Contain("default shape").And.Contain("no sphere, box or capsule");
    }

    // ---- One constructor and one TryGetValue per case ------------------------------------------------------------

    [Fact]
    public void ASphereComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var shape = new QueryShape(new QuerySphere(5));

        shape.HasValue.Should().BeTrue();
        shape.TryGetValue(out QuerySphere held).Should().BeTrue();
        held.Should().Be(new QuerySphere(5));
        CasesHeld(shape).Should().Equal(nameof(QuerySphere));
    }

    [Fact]
    public void ABoxComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var shape = new QueryShape(new QueryBox(Half));

        shape.HasValue.Should().BeTrue();
        shape.TryGetValue(out QueryBox held).Should().BeTrue();
        held.Should().Be(new QueryBox(Half));
        CasesHeld(shape).Should().Equal(nameof(QueryBox));
    }

    [Fact]
    public void ACapsuleComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var shape = new QueryShape(new QueryCapsule(3, 0.5f));

        shape.HasValue.Should().BeTrue();
        shape.TryGetValue(out QueryCapsule held).Should().BeTrue();
        held.Should().Be(new QueryCapsule(3, 0.5f));
        held.HalfHeight.Should().Be(3);
        held.Radius.Should().Be(0.5f);
        CasesHeld(shape).Should().Equal(nameof(QueryCapsule));
    }

    [Fact]
    public void ATryGetValueThatAnswersFalseHandsOutTheDefaultValueNotTheFieldsOfAnotherCase()
    {
        // A sphere and a capsule share the radius, so a case that read it without checking the kind would show it.
        Views(new(new QuerySphere(5))).Should().Be((new(5), default, default));
        Views(new(new QueryBox(Half))).Should().Be((default, new(Half), default));
        Views(new(new QueryCapsule(3, 0.5f))).Should().Be((default, default, new(3, 0.5f)));
        Views(default).Should().Be((default, default, default));
    }

    [Fact]
    public void ValueBoxesTheCaseThatTheShapeHolds()
    {
        new QueryShape(new QuerySphere(5)).Value.Should().Be(new QuerySphere(5));
        new QueryShape(new QueryBox(Half)).Value.Should().Be(new QueryBox(Half));
        new QueryShape(new QueryCapsule(3, 0.5f)).Value.Should().Be(new QueryCapsule(3, 0.5f));
    }

    [Fact]
    public void EveryCaseConvertsImplicitlyToTheShapeThatItsConstructorBuilds()
    {
        QueryShape fromSphere = new QuerySphere(5);
        QueryShape fromBox = new QueryBox(Half);
        QueryShape fromCapsule = new QueryCapsule(3, 0.5f);

        fromSphere.Value.Should().Be(new QuerySphere(5));
        fromBox.Value.Should().Be(new QueryBox(Half));
        fromCapsule.Value.Should().Be(new QueryCapsule(3, 0.5f));
        fromSphere.Should().Be(new QueryShape(new QuerySphere(5)));
        fromBox.Should().Be(new QueryShape(new QueryBox(Half)));
        fromCapsule.Should().Be(new QueryShape(new QueryCapsule(3, 0.5f)));
    }

    [Fact]
    public void TheStaticFactoriesBuildTheCaseThatTheirNameSaysWithTheArgumentsInTheirOrder()
    {
        QueryShape.Sphere(5).Value.Should().Be(new QuerySphere(5));
        QueryShape.Box(Half).Value.Should().Be(new QueryBox(Half));
        // The factory takes the half height first and the radius second, as the case does.
        QueryShape.Capsule(3, 0.5f).Value.Should().Be(new QueryCapsule(3, 0.5f));
    }

    [Fact]
    public void TheConstructorsAcceptASizeThatNoQueryCanUseSoThatTheShapeCanSayWhyItIsRefused()
    {
        // The case records and the constructors do not check sizes. Problem does, and the World calls it on every query.
        QueryShape zero = new QuerySphere(0);
        QueryShape notANumber = new QueryBox(new(1, float.NaN, 1));
        QueryShape negative = new QueryCapsule(-1, 1);

        zero.HasValue.Should().BeTrue();
        zero.Problem().Should().NotBeNull();
        notANumber.HasValue.Should().BeTrue();
        notANumber.Problem().Should().NotBeNull();
        negative.HasValue.Should().BeTrue();
        negative.Problem().Should().NotBeNull();
    }

    // ---- Pattern matching ----------------------------------------------------------------------------------------

    [Fact]
    public void ASwitchOverAShapeTakesTheArmOfTheCaseItHoldsAndTheLastArmForTheDefault()
    {
        ArmOf(new(new QuerySphere(5))).Should().Be("sphere 5");
        ArmOf(new(new QueryBox(Half))).Should().Be("box 1 2 3");
        ArmOf(new(new QueryCapsule(3, 0.5f))).Should().Be("capsule 3 0.5");
        ArmOf(default).Should().Be("none");
    }

    // ---- Equality ------------------------------------------------------------------------------------------------

    [Fact]
    public void TwoShapesOfTheSameCaseWithTheSameFieldsAreEqualAndHashAlike()
    {
        QueryShape[] first = OneOfEachCase();
        QueryShape[] second = OneOfEachCase();

        for (int i = 0; i < first.Length; i++)
        {
            first[i].Equals(second[i]).Should().BeTrue($"case {i}");
            first[i].Equals((object)second[i]).Should().BeTrue($"case {i} as an object");
            (first[i] == second[i]).Should().BeTrue($"case {i} by ==");
            (first[i] != second[i]).Should().BeFalse($"case {i} by !=");
            first[i].GetHashCode().Should().Be(second[i].GetHashCode(), $"case {i}");
        }

        default(QueryShape).Should().Be(default(QueryShape));
        default(QueryShape).GetHashCode().Should().Be(default(QueryShape).GetHashCode());
    }

    [Fact]
    public void TwoShapesOfTheSameCaseThatDifferInOneFieldAreNotEqual()
    {
        var sphere = new QueryShape(new QuerySphere(5));
        var box = new QueryShape(new QueryBox(Half));
        var capsule = new QueryShape(new QueryCapsule(3, 0.5f));

        sphere.Should().NotBe(new QueryShape(new QuerySphere(5.5f)));
        box.Should().NotBe(new QueryShape(new QueryBox(new(1.5f, 2, 3))), "another X");
        box.Should().NotBe(new QueryShape(new QueryBox(new(1, 2.5f, 3))), "another Y");
        box.Should().NotBe(new QueryShape(new QueryBox(new(1, 2, 3.5f))), "another Z");
        capsule.Should().NotBe(new QueryShape(new QueryCapsule(3.5f, 0.5f)), "another half height");
        capsule.Should().NotBe(new QueryShape(new QueryCapsule(3, 0.75f)), "another radius");
        (sphere != new QueryShape(new QuerySphere(5.5f))).Should().BeTrue();
    }

    [Fact]
    public void ShapesOfDifferentCasesAreNotEqualEvenWhenTheirFieldsMatch()
    {
        // A sphere and a capsule of half height 0 have the same radius and the same zero half height, so only the case
        // can tell them apart. They are two cases even though a query makes one native shape of both.
        new QueryShape(new QuerySphere(1)).Should().NotBe(new QueryShape(new QueryCapsule(0, 1)));
        new QueryShape(new QueryCapsule(0, 1)).Should().NotBe(new QueryShape(new QuerySphere(1)));
        (new QueryShape(new QuerySphere(1)) == new QueryShape(new QueryCapsule(0, 1))).Should().BeFalse();
    }

    [Fact]
    public void AShapeWithACaseIsNeverEqualToTheDefaultShapeEvenWhenEveryFieldIsZero()
    {
        // The default used to be a sphere of radius 0. Now it holds no case, and a sphere of radius 0 is a case that
        // holds a size no query can use.
        QueryShape empty = default;

        new QueryShape(new QuerySphere(0)).Should().NotBe(empty);
        new QueryShape(new QueryBox(Vector3.Zero)).Should().NotBe(empty);
        new QueryShape(new QueryCapsule(0, 0)).Should().NotBe(empty);
        (empty != new QueryShape(new QuerySphere(0))).Should().BeTrue();
    }

    [Fact]
    public void AShapeIsNotEqualToAnObjectThatIsNotAShape()
    {
        new QueryShape(new QuerySphere(5)).Equals((object)new QuerySphere(5)).Should().BeFalse();
        new QueryShape(new QuerySphere(5)).Equals(null).Should().BeFalse();
    }

    // ---- ToString ------------------------------------------------------------------------------------------------

    [Fact]
    public void ToStringNamesTheCaseAndItsFieldsAndSaysNoneForTheDefault()
    {
        default(QueryShape).ToString().Should().Be("QueryShape(none)");
        new QueryShape(new QuerySphere(5)).ToString().Should().StartWith("QueryShape(").And.Contain(nameof(QuerySphere))
            .And.Contain("5");
        new QueryShape(new QueryBox(Half)).ToString().Should().Contain(nameof(QueryBox)).And.Contain("2");
        new QueryShape(new QueryCapsule(3, 0.5f)).ToString().Should().Contain(nameof(QueryCapsule)).And.Contain("0.5");
    }
}
