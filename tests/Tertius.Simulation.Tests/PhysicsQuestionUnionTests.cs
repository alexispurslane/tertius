using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// <see cref="PhysicsQuestion"/> as a custom C# 15 union of three cases: <see cref="RayQuestion"/>,
/// <see cref="ShapeCastQuestion"/> and <see cref="OverlapQuestion"/>. A <see cref="PhysicsRequest"/> holds one of them
/// beside the fields that every request has. The default value holds no case. These tests pin what a union promises: each
/// case goes in through its constructor and comes out through its <c>TryGetValue</c>, no other <c>TryGetValue</c> answers
/// true, equality needs the same case and the same fields, and a <c>switch</c> over the question takes the arm of the case
/// that it holds. They also pin the sweep view that the runner reads, which is the same for every case: where the question
/// starts, along which line, how far, and how far from that line it reaches. The tests of the real queries, on the real
/// World and Jolt, are in <c>WorldRayCastTests</c> and <c>WorldShapeQueryTests</c> of the Physics tests.
/// </summary>
public class PhysicsQuestionUnionTests
{
    private static readonly Double3 Start = new(1, 2, 3);
    private static readonly Double3 Along = new(0, 0.6, 0.8);
    private static readonly DQuaternion Turned = DQuaternion.FromAxisAngle(Double3.UnitY, 0.5);
    private static readonly QueryShape Ball = QueryShape.Sphere(2);
    private static readonly QueryShape Crate = QueryShape.Box(new(3, 4, 12));
    private static readonly DPose StartPose = new(Start, Turned);

    /// <summary>The names of the cases whose <c>TryGetValue</c> answers true for the question.</summary>
    private static string[] CasesHeld(PhysicsQuestion question)
    {
        var held = new List<string>();
        if (question.TryGetValue(out RayQuestion _))
            held.Add(nameof(RayQuestion));
        if (question.TryGetValue(out ShapeCastQuestion _))
            held.Add(nameof(ShapeCastQuestion));
        if (question.TryGetValue(out OverlapQuestion _))
            held.Add(nameof(OverlapQuestion));
        return [.. held];
    }

    /// <summary>The value that each <c>TryGetValue</c> hands out, whatever it answers.</summary>
    private static (RayQuestion Ray, ShapeCastQuestion ShapeCast, OverlapQuestion Overlap) Views(
        PhysicsQuestion question)
    {
        question.TryGetValue(out RayQuestion ray);
        question.TryGetValue(out ShapeCastQuestion shapeCast);
        question.TryGetValue(out OverlapQuestion overlap);
        return (ray, shapeCast, overlap);
    }

    private static string ArmOf(PhysicsQuestion question) => question switch
    {
        RayQuestion(_, _, var length, var kind) => $"ray {kind} {length}",
        ShapeCastQuestion(var shape, _, _, var length) => $"shape cast {shape.BoundingRadius} {length}",
        OverlapQuestion(var shape, var pose) => $"overlap {shape.BoundingRadius} {pose.Position.X}",
        null => "none"
    };

    private static RayQuestion Ray(RayCastKind kind = RayCastKind.Nearest) => new(Start, Along, 50, kind);

    private static ShapeCastQuestion Cast() => new(Ball, StartPose, Along, 50);

    private static OverlapQuestion Overlap() => new(Ball, StartPose);

    /// <summary>One question of each case, with fields that share their values, so that only the case tells them apart.</summary>
    private static PhysicsQuestion[] OneOfEachCase() => [Ray(), Cast(), Overlap()];

    // ---- The default value ---------------------------------------------------------------------------------------

    [Fact]
    public void TheDefaultQuestionHoldsNoCase()
    {
        PhysicsQuestion question = default;

        question.HasValue.Should().BeFalse();
        question.Value.Should().BeNull();
        CasesHeld(question).Should().BeEmpty();
    }

    [Fact]
    public void TheDefaultQuestionHasAnEmptySweep()
    {
        PhysicsQuestion question = default;

        question.Origin.Should().Be(Double3.Zero);
        question.Direction.Should().Be(Double3.Zero);
        question.Length.Should().Be(0);
        question.Reach.Should().Be(0);
        question.FirstHitOnly.Should().BeFalse();
    }

    [Fact]
    public void TheDefaultQuestionEqualsOnlyTheDefaultQuestion()
    {
        PhysicsQuestion none = default;

        (none == default).Should().BeTrue();
        none.GetHashCode().Should().Be(default(PhysicsQuestion).GetHashCode());
        foreach (PhysicsQuestion held in OneOfEachCase())
            (none == held).Should().BeFalse();
    }

    [Fact]
    public void AQuestionOfAnyCaseHoldsAValue()
    {
        foreach (PhysicsQuestion held in OneOfEachCase())
            held.HasValue.Should().BeTrue();
    }

    // ---- One constructor and one TryGetValue per case ------------------------------------------------------------

    [Theory]
    [InlineData(RayCastKind.Nearest)]
    [InlineData(RayCastKind.All)]
    public void ARayComesBackThroughItsTryGetValueAndNoOtherCaseAnswers(RayCastKind kind)
    {
        var question = new PhysicsQuestion(Ray(kind));

        question.TryGetValue(out RayQuestion held).Should().BeTrue();
        held.Should().Be(Ray(kind));
        CasesHeld(question).Should().Equal(nameof(RayQuestion));
    }

    [Fact]
    public void AShapeCastComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var question = new PhysicsQuestion(Cast());

        question.TryGetValue(out ShapeCastQuestion held).Should().BeTrue();
        held.Should().Be(Cast());
        CasesHeld(question).Should().Equal(nameof(ShapeCastQuestion));
    }

    [Fact]
    public void AnOverlapComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var question = new PhysicsQuestion(Overlap());

        question.TryGetValue(out OverlapQuestion held).Should().BeTrue();
        held.Should().Be(Overlap());
        CasesHeld(question).Should().Equal(nameof(OverlapQuestion));
    }

    [Fact]
    public void ATryGetValueThatAnswersFalseHandsOutTheDefaultNotTheFieldsOfAnotherCase()
    {
        (RayQuestion Ray, ShapeCastQuestion ShapeCast, OverlapQuestion Overlap) ray = Views(new(Ray()));
        ray.ShapeCast.Should().Be(default(ShapeCastQuestion));
        ray.Overlap.Should().Be(default(OverlapQuestion));

        (RayQuestion Ray, ShapeCastQuestion ShapeCast, OverlapQuestion Overlap) cast = Views(new(Cast()));
        cast.Ray.Should().Be(default(RayQuestion));
        cast.Overlap.Should().Be(default(OverlapQuestion));

        (RayQuestion Ray, ShapeCastQuestion ShapeCast, OverlapQuestion Overlap) overlap = Views(new(Overlap()));
        overlap.Ray.Should().Be(default(RayQuestion));
        overlap.ShapeCast.Should().Be(default(ShapeCastQuestion));

        (RayQuestion Ray, ShapeCastQuestion ShapeCast, OverlapQuestion Overlap) none = Views(default);
        none.Should().Be((default, default, default));
    }

    [Fact]
    public void ARayKeepsADirectionThatIsNotUnitLengthAsItWasGiven()
    {
        // The union stores what it gets. The World checks and normalizes a request before it builds the question.
        var question = new PhysicsQuestion(new RayQuestion(Start, new(0, 0, 7), 5, RayCastKind.All));

        question.TryGetValue(out RayQuestion held);
        held.Direction.Should().Be(new Double3(0, 0, 7));
    }

    [Fact]
    public void AShapeCastAndAnOverlapKeepTheirPoseRotation()
    {
        new PhysicsQuestion(Cast()).TryGetValue(out ShapeCastQuestion cast);
        new PhysicsQuestion(Overlap()).TryGetValue(out OverlapQuestion overlap);

        cast.Start.Rotation.Should().Be(Turned);
        overlap.Pose.Rotation.Should().Be(Turned);
    }

    [Fact]
    public void ABoxShapeSurvivesAShapeCastAndAnOverlap()
    {
        new PhysicsQuestion(new ShapeCastQuestion(Crate, StartPose, Along, 9)).TryGetValue(out ShapeCastQuestion cast);
        new PhysicsQuestion(new OverlapQuestion(Crate, StartPose)).TryGetValue(out OverlapQuestion overlap);

        cast.Shape.Should().Be(Crate);
        overlap.Shape.Should().Be(Crate);
    }

    [Fact]
    public void EachCaseConvertsImplicitlyAndTheValueBoxesTheSameCase()
    {
        PhysicsQuestion ray = Ray();
        PhysicsQuestion cast = Cast();
        PhysicsQuestion overlap = Overlap();

        ray.Should().Be(new PhysicsQuestion(Ray()));
        cast.Should().Be(new PhysicsQuestion(Cast()));
        overlap.Should().Be(new PhysicsQuestion(Overlap()));
        ray.Value.Should().Be(Ray());
        cast.Value.Should().Be(Cast());
        overlap.Value.Should().Be(Overlap());
    }

    // ---- Equality: the same case, the same fields ----------------------------------------------------------------

    [Fact]
    public void TwoQuestionsOfTheSameCaseWithTheSameFieldsAreEqualAndHashAlike()
    {
        foreach ((PhysicsQuestion a, PhysicsQuestion b) in new (PhysicsQuestion, PhysicsQuestion)[]
                 {
                     (Ray(), Ray()), (Ray(RayCastKind.All), Ray(RayCastKind.All)), (Cast(), Cast()),
                     (Overlap(), Overlap())
                 })
        {
            a.Equals(b).Should().BeTrue();
            (a == b).Should().BeTrue();
            (a != b).Should().BeFalse();
            a.Equals((object)b).Should().BeTrue();
            a.GetHashCode().Should().Be(b.GetHashCode());
        }
    }

    [Fact]
    public void QuestionsOfDifferentCasesAreNeverEqualEvenWhenTheirSharedFieldsMatch()
    {
        // The ray, the shape cast and the overlap start at the same point, and the ray and the shape cast share a
        // direction and a length, and the shape cast and the overlap share a shape and a rotation.
        PhysicsQuestion[] each = OneOfEachCase();
        for (int i = 0; i < each.Length; i++)
        for (int j = 0; j < each.Length; j++)
            if (i != j)
            {
                each[i].Equals(each[j]).Should().BeFalse($"case {i} and case {j} differ");
                (each[i] != each[j]).Should().BeTrue();
            }
    }

    [Fact]
    public void ARayDiffersWhenItsOriginItsDirectionItsLengthOrItsKindDiffers()
    {
        PhysicsQuestion baseline = Ray();

        (baseline == Ray() with { Origin = Start + Double3.UnitX }).Should().BeFalse();
        (baseline == Ray() with { Direction = Double3.UnitZ }).Should().BeFalse();
        (baseline == Ray() with { Length = 51 }).Should().BeFalse();
        (baseline == Ray(RayCastKind.All)).Should().BeFalse();
    }

    [Fact]
    public void AShapeCastDiffersWhenItsShapeItsStartItsRotationItsDirectionOrItsLengthDiffers()
    {
        PhysicsQuestion baseline = Cast();

        (baseline == Cast() with { Shape = Crate }).Should().BeFalse();
        (baseline == Cast() with { Start = new(Start + Double3.UnitX, Turned) }).Should().BeFalse();
        (baseline == Cast() with { Start = new(Start, DQuaternion.Identity) }).Should().BeFalse();
        (baseline == Cast() with { Direction = Double3.UnitZ }).Should().BeFalse();
        (baseline == Cast() with { Length = 51 }).Should().BeFalse();
    }

    [Fact]
    public void AnOverlapDiffersWhenItsShapeItsPositionOrItsRotationDiffers()
    {
        PhysicsQuestion baseline = Overlap();

        (baseline == Overlap() with { Shape = Crate }).Should().BeFalse();
        (baseline == Overlap() with { Pose = new(Start + Double3.UnitX, Turned) }).Should().BeFalse();
        (baseline == Overlap() with { Pose = new(Start, DQuaternion.Identity) }).Should().BeFalse();
    }

    [Fact]
    public void ARayAndAShapeCastAreNotMistakenForEachOtherInTheHashSet()
    {
        var set = new HashSet<PhysicsQuestion>(OneOfEachCase()) { Ray(), Cast(), Overlap() };

        set.Should().HaveCount(3);
    }

    // ---- The sweep view ------------------------------------------------------------------------------------------

    [Fact]
    public void ARaySweepsFromItsOriginAlongItsDirectionForItsLengthAndReachesNothingSideways()
    {
        PhysicsQuestion question = Ray();

        question.Origin.Should().Be(Start);
        question.Direction.Should().Be(Along);
        question.Length.Should().Be(50);
        question.Reach.Should().Be(0);
    }

    [Fact]
    public void AShapeCastSweepsFromItsStartAndReachesAsFarAsItsShapesBoundingRadius()
    {
        PhysicsQuestion question = new ShapeCastQuestion(Crate, StartPose, Along, 9);

        question.Origin.Should().Be(Start);
        question.Direction.Should().Be(Along);
        question.Length.Should().Be(9);
        question.Reach.Should().Be(Crate.BoundingRadius).And.Be(13);
    }

    [Fact]
    public void AnOverlapSweepsNothingFromItsCenterAndReachesAsFarAsItsShapesBoundingRadius()
    {
        PhysicsQuestion question = new OverlapQuestion(Crate, StartPose);

        question.Origin.Should().Be(Start);
        question.Direction.Should().Be(Double3.Zero);
        question.Length.Should().Be(0);
        question.Reach.Should().Be(13);
    }

    [Fact]
    public void OnlyTheNearestRayAndTheShapeCastKeepTheFirstHitAndTheOtherCasesKeepEveryHit()
    {
        ((PhysicsQuestion)Ray(RayCastKind.Nearest)).FirstHitOnly.Should().BeTrue();
        ((PhysicsQuestion)Ray(RayCastKind.All)).FirstHitOnly.Should().BeFalse();
        ((PhysicsQuestion)Cast()).FirstHitOnly.Should().BeTrue();
        ((PhysicsQuestion)Overlap()).FirstHitOnly.Should().BeFalse();
    }

    // ---- switch and ToString -------------------------------------------------------------------------------------

    [Fact]
    public void ASwitchTakesTheArmOfTheCaseThatTheQuestionHolds()
    {
        ArmOf(Ray(RayCastKind.All)).Should().Be("ray All 50");
        ArmOf(Cast()).Should().Be("shape cast 2 50");
        ArmOf(Overlap()).Should().Be("overlap 2 1");
        ArmOf(default).Should().Be("none");
    }

    [Fact]
    public void ToStringNamesTheCaseAndItsFieldsAndTheDefaultHasNone()
    {
        default(PhysicsQuestion).ToString().Should().Be("PhysicsQuestion(none)");
        ((PhysicsQuestion)Ray()).ToString().Should().Be($"PhysicsQuestion({Ray()})");
        ((PhysicsQuestion)Cast()).ToString().Should().Be($"PhysicsQuestion({Cast()})");
        ((PhysicsQuestion)Overlap()).ToString().Should().Be($"PhysicsQuestion({Overlap()})");
    }

    // ---- The request that holds a question ------------------------------------------------------------------------

    [Fact]
    public void TwoRequestsAreEqualOnlyWhenTheirQuestionsAre()
    {
        var asker = new Entity(4, 1);
        var frame = new FrameHandle(new(1, 1), 0, 1);

        new PhysicsRequest(asker, frame, Cast(), default, 17).Should()
            .Be(new PhysicsRequest(asker, frame, Cast(), default, 17));
        new PhysicsRequest(asker, frame, Cast(), default, 17).Should()
            .NotBe(new PhysicsRequest(asker, frame, Overlap(), default, 17));
    }

    /// <summary>
    /// The World builds every request from a checked case, so a request with no question comes only from a bug. A test
    /// stands in for that bug: it queues a request with the default question. A Debug build breaks a contract at the sync
    /// point. A Release build gives the request no answer and records the state in <see cref="World.Errors"/>.
    /// </summary>
    [Fact]
    public void ARequestWithNoQuestionIsABugAndAReleaseBuildGivesItNoAnswerAndRecordsAnError()
    {
        using var t = new TestWorld();
        Entity asker = t.SpawnAt(Double3.Zero);
        t.Run(1);
        t.World.Entities.Chunks[0].PhysicsRequests.Add(new(asker, t.Root, default, default, 1));

#if TERTIUS_CONTRACTS
        Action tick = () => t.Run(1);

        tick.Should().Throw<ContractException>().WithMessage($"*{asker}*holds no question*");
#else
        t.Run(1);

        t.World.Errors.Should().ContainSingle().Which.Should().Contain(asker.ToString()).And.Contain("holds no question").And.Contain("only a bug");
#endif
    }
}
