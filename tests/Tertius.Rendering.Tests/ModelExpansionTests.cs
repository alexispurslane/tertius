using System.Collections.Immutable;
using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The expansion of model instances (ARCHITECTURE.md, "Models"): before every collector of a frame, each instance of a
/// model becomes one instance per part, with the part's index, mesh, material and bounds, and the entity's current and
/// previous transforms composed with the part's offset and scale. Every other instance passes unchanged. No GPU needed.
/// </summary>
public sealed class ModelExpansionTests
{
    private static readonly MeshId Hull = new(0),
        Mast = new(1);

    private static readonly MaterialId Paint = new(2),
        Steel = new(5);

    private static readonly Quaternion Quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

    /// <summary>A hull at the entity's origin, and a mast 3 m up the entity's +X, turned a quarter about Y and stretched.</summary>
    private static readonly ModelDesc Ship = new(
        [
            new(Mesh: Hull, Material: Paint, Offset: Pose.Identity, Scale: Vector3.One, BoundingRadius: 2),
            new(
                Mesh: Mast,
                Material: Steel,
                Offset: new(new(3, 0, 0), Quarter),
                Scale: new(1, 4, 1),
                BoundingRadius: 4.5f
            ),
        ]
    );

    private static RenderInstance ShipAt(Vector3 position, Quaternion rotation, float scale)
    {
        RenderInstance instance = Gpu.Instance(
            mesh: MeshId.None,
            material: MaterialId.None,
            position: position,
            scale: new(scale),
            chainDepth: TestChains.CamerasFrame,
            rotation: rotation,
            flags: RenderFlags.CastsShadow,
            radius: 99,
            entity: 7
        );
        instance.Model = new(0);
        return instance;
    }

    private static RenderInstance BoxAt(Vector3 position, uint entity) =>
        Gpu.Instance(
            mesh: Hull,
            material: Paint,
            position: position,
            scale: Vector3.One,
            chainDepth: TestChains.CamerasFrame,
            entity: entity
        );

    private static RenderInstance[] Expanded(RenderSnapshot snapshot, params ModelDesc[] models)
    {
        var expansion = new ModelExpansion();
        expansion.Expand(snapshot, models);
        return expansion.Instances.ToArray();
    }

    private static RenderSnapshot With(params RenderInstance[] instances)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.AddRange(instances);
        return snapshot;
    }

    private static void ShouldBeClose(Vector3 actual, Vector3 expected) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(1e-5f, $"{actual} should be {expected}");

    private static void ShouldBeClose(Quaternion actual, Quaternion expected) =>
        MathF.Abs(Quaternion.Dot(actual, expected)).Should().BeGreaterThan(1 - 1e-6f, $"{actual} should be {expected}");

    [Fact]
    public void AModelInstanceBecomesOneInstancePerPartInPartOrder()
    {
        RenderInstance[] parts = Expanded(With(ShipAt(Vector3.Zero, Quaternion.Identity, 1)), Ship);

        parts.Select(p => (p.Part, p.Mesh, p.Material)).Should().Equal((0, Hull, Paint), (1, Mast, Steel));
        parts.Should().OnlyContain(p => p.Model.IsNone, "a part instance is an ordinary instance");
    }

    [Fact]
    public void APartKeepsTheEntityFlagsAndKey()
    {
        RenderInstance ship = ShipAt(Vector3.Zero, Quaternion.Identity, 1);
        ship.EntityGeneration = 3;

        RenderInstance mast = Expanded(With(ship), Ship)[1];

        mast.Entity.Should().Be(ship.Entity);
        mast.Flags.Should().Be(ship.Flags);
        mast.ChainDepth.Should().Be(ship.ChainDepth);
        mast.Hemisphere.Should().Be(ship.Hemisphere);
    }

    [Fact]
    public void APartIsPlacedByItsOffsetInTheTurnedAndScaledAxesOfItsEntity()
    {
        RenderInstance mast = Expanded(With(ShipAt(new(10, 0, -20), Quarter, scale: 2)), Ship)[1];

        // A quarter turn about Y takes +X to -Z, and the scale doubles the 3 m.
        ShouldBeClose(mast.Position, new(10, 0, -26));
        ShouldBeClose(mast.Rotation, Quarter * Quarter);
        mast.Scale.Should().Be(new Vector3(2, 8, 2));
    }

    [Fact]
    public void ThePreviousTransformOfAPartFollowsThePreviousTransformOfItsEntity()
    {
        // The ship moved by 5 m along X and turned a quarter about Y since the previous tick.
        RenderInstance ship = ShipAt(new(5, 0, -20), Quarter, scale: 2);
        ship.PreviousPosition = new(0, 0, -20);
        ship.PreviousRotation = Quaternion.Identity;
        ship.PreviousScale = new(1);

        RenderInstance mast = Expanded(With(ship), Ship)[1];

        ShouldBeClose(mast.PreviousPosition, new(3, 0, -20));
        ShouldBeClose(mast.PreviousRotation, Quarter);
        mast.PreviousScale.Should().Be(new Vector3(1, 4, 1));
        ShouldBeClose(mast.Position, new(5, 0, -26));
    }

    [Fact]
    public void APreviousScaleWithoutVolumeCountsAsTheCurrentScale()
    {
        RenderInstance ship = ShipAt(new(0, 0, -20), Quaternion.Identity, scale: 2);
        ship.PreviousScale = Vector3.Zero;

        RenderInstance mast = Expanded(With(ship), Ship)[1];

        mast.PreviousScale.Should().Be(new Vector3(2, 8, 2));
        ShouldBeClose(mast.PreviousPosition, new(6, 0, -20));
    }

    [Fact]
    public void ThePartsBoundingRadiusIsItsOwnRadiusTimesTheEntitysScale()
    {
        RenderInstance[] parts = Expanded(With(ShipAt(Vector3.Zero, Quaternion.Identity, scale: 3)), Ship);

        parts.Select(p => p.BoundingRadius).Should().Equal(6f, 13.5f);
    }

    [Fact]
    public void APartWithoutABoundingRadiusLeavesItToTheMesh()
    {
        ModelDesc unbounded = new([Ship.Parts[0] with { BoundingRadius = 0 }]);

        RenderInstance hull = Expanded(With(ShipAt(Vector3.Zero, Quaternion.Identity, scale: 3)), unbounded)[0];

        hull.BoundingRadius.Should().Be(0, "a radius of 0 asks the batcher to take the scaled mesh's extent");
    }

    [Fact]
    public void TwoPlacementsOfOneModelDrawTheSameMeshesAndMaterials()
    {
        RenderInstance[] parts = Expanded(
            With(ShipAt(new(0, 0, -20), Quaternion.Identity, 1), ShipAt(new(9, 0, -20), Quarter, 1) with { EntityIndex = 8 }),
            Ship
        );

        parts.Select(p => (p.EntityIndex, p.Mesh, p.Material))
            .Should()
            .Equal((7u, Hull, Paint), (7u, Mast, Steel), (8u, Hull, Paint), (8u, Mast, Steel));
    }

    [Fact]
    public void InstancesWithoutAModelKeepTheirPlacesAroundTheParts()
    {
        RenderInstance before = BoxAt(new(1, 0, -5), entity: 2);
        RenderInstance after = BoxAt(new(2, 0, -5), entity: 9);

        RenderInstance[] instances = Expanded(With(before, ShipAt(Vector3.Zero, Quaternion.Identity, 1), after), Ship);

        instances.Select(i => i.EntityIndex).Should().Equal(2u, 7u, 7u, 9u);
        instances[0].Should().Be(before);
        instances[3].Should().Be(after);
    }

    [Fact]
    public void AModelThatTheRendererDoesNotHoldDrawsNothing()
    {
        RenderInstance stranger = ShipAt(Vector3.Zero, Quaternion.Identity, 1);
        stranger.Model = new(1);

        Expanded(With(stranger), Ship).Should().BeEmpty();
    }

    [Fact]
    public void AModelWithoutPartsDrawsNothing()
    {
        Expanded(With(ShipAt(Vector3.Zero, Quaternion.Identity, 1)), new ModelDesc([])).Should().BeEmpty();
    }

    public static TheoryData<float, float, float> StretchedScales => new() { { 2, 1, 1 }, { 1, 2, 1 }, { 1, 1, 2 } };

    [Theory]
    [MemberData(nameof(StretchedScales))]
    public void AModelAtAScaleThatDiffersAlongTheAxesDrawsNothing(float x, float y, float z)
    {
        // The turned mast would need a shear, which an instance cannot hold.
        RenderInstance stretched = ShipAt(Vector3.Zero, Quaternion.Identity, 1);
        stretched.Scale = new(x, y, z);

        Expanded(With(stretched), Ship).Should().BeEmpty();
    }

    [Fact]
    public void AnEntityRotationThatIsNotARotationCountsAsTheIdentity()
    {
        RenderInstance unset = ShipAt(new(0, 0, -20), Quaternion.Identity, 1);
        unset.Rotation = default;
        unset.PreviousRotation = default;

        RenderInstance mast = Expanded(With(unset), Ship)[1];

        ShouldBeClose(mast.Position, new(3, 0, -20));
        ShouldBeClose(mast.Rotation, Quarter);
        ShouldBeClose(mast.PreviousRotation, Quarter);
    }

    [Fact]
    public void AModelOfManyPartsDrawsEveryPart()
    {
        ModelDesc many = new([.. Enumerable.Repeat(Ship.Parts[0], 300)]);

        RenderInstance[] parts = Expanded(With(ShipAt(Vector3.Zero, Quaternion.Identity, 1)), many);

        parts.Select(p => (int)p.Part).Should().Equal(Enumerable.Range(0, 300));
    }

    [Fact]
    public void ManyPosedInstancesAfterAModelKeepTheirRigPoses()
    {
        RenderSnapshot snapshot = With(ShipAt(Vector3.Zero, Quaternion.Identity, 1));
        for (uint box = 0; box < 20; box++)
        {
            snapshot.Instances.Add(BoxAt(new(box, 0, -5), entity: 10 + box));
            snapshot.AddRigPose(
                instance: snapshot.Instances.Count - 1,
                joints: [Pose.Identity],
                previousJoints: [Pose.Identity],
                weights: [],
                previousWeights: []
            );
        }
        var expansion = new ModelExpansion();

        expansion.Expand(snapshot, [Ship]);

        expansion.RigPoses.ToArray().Select(p => p.Instance).Should().Equal(Enumerable.Range(2, 20));
    }

    [Fact]
    public void TheRigPoseOfAnInstanceAfterAModelNamesItsPlaceAmongTheParts()
    {
        RenderSnapshot snapshot = With(ShipAt(Vector3.Zero, Quaternion.Identity, 1), BoxAt(new(1, 0, -5), entity: 9));
        snapshot.AddRigPose(instance: 1, joints: [Pose.Identity], previousJoints: [Pose.Identity], weights: [], previousWeights: []);
        var expansion = new ModelExpansion();

        expansion.Expand(snapshot, [Ship]);

        RenderRigPose pose = expansion.RigPoses.ToArray().Should().ContainSingle().Subject;
        pose.Instance.Should().Be(2, "the ship's two parts come before the box");
        pose.JointStart.Should().Be(0, "the joints stay where the snapshot keeps them");
    }

    [Fact]
    public void TheRigPoseOfAModelInstanceIsDropped()
    {
        RenderSnapshot snapshot = With(ShipAt(Vector3.Zero, Quaternion.Identity, 1));
        snapshot.AddRigPose(instance: 0, joints: [Pose.Identity], previousJoints: [Pose.Identity], weights: [], previousWeights: []);
        var expansion = new ModelExpansion();

        expansion.Expand(snapshot, [Ship]);

        expansion.RigPoses.IsEmpty.Should().BeTrue("the parts of a model draw as uploaded");
    }

    [Fact]
    public void ASnapshotWithoutModelsPassesUnchanged()
    {
        RenderSnapshot snapshot = With(BoxAt(new(1, 0, -5), entity: 2), BoxAt(new(2, 0, -5), entity: 3));
        snapshot.AddRigPose(instance: 1, joints: [Pose.Identity], previousJoints: [Pose.Identity], weights: [], previousWeights: []);
        var expansion = new ModelExpansion();

        expansion.Expand(snapshot, [Ship]);

        expansion.Instances.ToArray().Should().Equal(snapshot.Instances);
        expansion.RigPoses.ToArray().Should().Equal(snapshot.RigPoses);
    }

    [Fact]
    public void ASecondExpansionForgetsTheFirst()
    {
        var expansion = new ModelExpansion();
        expansion.Expand(With(ShipAt(Vector3.Zero, Quaternion.Identity, 1)), [Ship]);

        expansion.Expand(With(BoxAt(new(1, 0, -5), entity: 2), ShipAt(Vector3.Zero, Quaternion.Identity, 1)), [Ship]);

        expansion.Instances.ToArray().Select(i => i.EntityIndex).Should().Equal(2u, 7u, 7u);
    }

    [Fact]
    public void AnExpansionWithoutAModelAfterOneWithAModelPassesTheSnapshotThrough()
    {
        var expansion = new ModelExpansion();
        expansion.Expand(With(ShipAt(Vector3.Zero, Quaternion.Identity, 1)), [Ship]);

        expansion.Expand(With(BoxAt(new(1, 0, -5), entity: 2)), [Ship]);

        expansion.Instances.ToArray().Select(i => i.EntityIndex).Should().Equal(2u);
    }

    [Fact]
    public void TheRigPoseOfAnInstanceAfterAPosedModelInstanceIsKept()
    {
        RenderSnapshot snapshot = With(ShipAt(Vector3.Zero, Quaternion.Identity, 1), BoxAt(new(1, 0, -5), entity: 9));
        snapshot.AddRigPose(instance: 0, joints: [Pose.Identity], previousJoints: [Pose.Identity], weights: [], previousWeights: []);
        snapshot.AddRigPose(instance: 1, joints: [Pose.Identity], previousJoints: [Pose.Identity], weights: [], previousWeights: []);
        var expansion = new ModelExpansion();

        expansion.Expand(snapshot, [Ship]);

        expansion.RigPoses.ToArray().Select(p => p.Instance).Should().Equal(2);
    }

    [Fact]
    public void APreviousScaleTooSmallToDrawCountsAsTheCurrentScale()
    {
        RenderInstance ship = ShipAt(new(0, 0, -20), Quaternion.Identity, scale: 2);
        ship.PreviousScale = new(1e-20f);

        RenderInstance mast = Expanded(With(ship), Ship)[1];

        mast.PreviousScale.Should().Be(new Vector3(2, 8, 2));
    }

    [Fact]
    public void APreviousScaleOfTheOtherSignCountsAsTheCurrentScale()
    {
        // A mirror between two ticks passes through a flat scale, which no view draws.
        RenderInstance ship = ShipAt(new(0, 0, -20), Quaternion.Identity, scale: 2);
        ship.PreviousScale = new(-1);

        RenderInstance mast = Expanded(With(ship), Ship)[1];

        mast.PreviousScale.Should().Be(new Vector3(2, 8, 2));
    }

    [Fact]
    public void AMirroredModelKeepsAPositiveBoundingRadius()
    {
        RenderInstance[] parts = Expanded(With(ShipAt(Vector3.Zero, Quaternion.Identity, scale: -3)), Ship);

        parts.Select(p => p.BoundingRadius).Should().Equal(6f, 13.5f);
    }
}
