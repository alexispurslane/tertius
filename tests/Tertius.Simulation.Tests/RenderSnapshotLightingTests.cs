using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Everything in a snapshot that is not an instance: the camera, the lights that entities carry, the sun, the ambient
/// light and the spheres that cast eclipses. Positions are relative to the render origin and in the axes of the camera's
/// frame. A quarter turn about +Y maps (x, y, z) to (z, y, -x).
/// </summary>
public class RenderSnapshotLightingTests
{
    private static RenderData Lamp(LightKind kind = LightKind.Spot, bool enabled = true) => new()
    {
        Light = new LightData
        {
            Enabled = enabled,
            Kind = kind,
            Color = new Vector3(1, 0.5f, 0.25f),
            Intensity = 3,
            Range = 20,
            Offset = new Pose(new Vector3(0, 1, 0), Quaternion.Identity),
        },
    };

    // ---- The camera ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheViewOffsetOfTheCameraIsTurnedIntoItsRotation()
    {
        using var test = new TestWorld();
        test.World.Camera = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Render = new RenderData { ViewOffset = new Pose(new Vector3(0, 1.7f, 0), PoseChecks.QuarterTurnAboutY.ToQuaternion()) },
        });

        test.Run(1);

        PoseChecks.AssertSameRotation(test.World.Latest().Camera.Rotation, PoseChecks.QuarterTurnAboutY);
    }

    [Fact]
    public void TheRotationOfTheCameraIsTheRotationOfItsEntityComposedWithItsViewOffset()
    {
        using var test = new TestWorld();
        test.World.Camera = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(Double3.Zero, PoseChecks.QuarterTurnAboutY),
            Render = new RenderData { ViewOffset = new Pose(Vector3.Zero, PoseChecks.QuarterTurnAboutY.ToQuaternion()) },
        });

        test.Run(1);

        // Two quarter turns about +Y make a half turn.
        PoseChecks.AssertSameRotation(test.World.Latest().Camera.Rotation, DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI));
    }

    [Fact]
    public void TheVerticalFieldOfViewOfTheWorldIsTheOneOfTheCamera()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.VerticalFov = 1.1f;

        test.Run(1);

        test.World.Latest().Camera.VerticalFov.Should().Be(1.1f);
    }

    // ---- Lights ----------------------------------------------------------------------------------------------------

    [Fact]
    public void ALightIsAtThePoseOfItsEntityComposedWithTheLightOffsetRelativeToTheOrigin()
    {
        using var test = new TestWorld();
        // The camera's eye is 1.7 m above the origin of the root. The lamp stands at (5, 0, 0) and carries the light 1 m above itself.
        test.World.Camera = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Render = new RenderData { ViewOffset = new Pose(new Vector3(0, 1.7f, 0), Quaternion.Identity) },
        });
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = new DPose(new Double3(5, 0, 0), PoseChecks.QuarterTurnAboutY), Render = Lamp() });

        test.Run(1);

        RenderLight light = test.World.Latest().Lights.Should().ContainSingle().Subject;
        PoseChecks.AssertNear(light.Position, new Vector3(5, 1 - 1.7f, 0));
    }

    [Fact]
    public void ASpotLightPointsAlongTheNegativeZAxisOfItsEntityTurnedWithTheEntity()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = new DPose(new Double3(5, 0, 0), PoseChecks.QuarterTurnAboutY), Render = Lamp() });

        test.Run(1);

        // The quarter turn maps the local direction (0, 0, -1) to (-1, 0, 0).
        PoseChecks.AssertNear(test.World.Latest().Lights.Single().Direction, new Vector3(-1, 0, 0), 1e-6f);
    }

    [Fact]
    public void ALightKeepsItsKindColorIntensityAndRange()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Render = Lamp(LightKind.Point) });

        test.Run(1);

        RenderLight light = test.World.Latest().Lights.Single();
        light.Kind.Should().Be(LightKind.Point);
        light.Color.Should().Be(new Vector3(1, 0.5f, 0.25f));
        light.Intensity.Should().Be(3);
        light.Range.Should().Be(20);
    }

    [Fact]
    public void ALightIsInTheSnapshotEvenWhenItsEntityDrawsNothing()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Render = Lamp() });

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.Lights.Should().ContainSingle();
        snapshot.Instances.Should().BeEmpty();
    }

    [Fact]
    public void AnEntityThatDrawsAMeshAndCarriesALightIsBoth()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        RenderData lantern = Visible() with { Light = Lamp().Light };
        Entity entity = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(2), Render = lantern });

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.InstanceOf(entity).Position.Should().Be(new Vector3(2, 0, 0));
        snapshot.Lights.Should().ContainSingle();
    }

    [Fact]
    public void ADisabledLightIsNotInTheSnapshot()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Render = Lamp(enabled: false) });

        test.Run(1);

        test.World.Latest().Lights.Should().BeEmpty();
    }

    // ---- Ambient light ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheAmbientLightOfTheWorldIsTheOneOfTheSnapshot()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Ambient = new AmbientLight { SkyColor = new Vector3(0.2f), GroundColor = new Vector3(0.1f), Intensity = 0.5f };

        test.Run(1);

        AmbientLight ambient = test.World.Latest().Ambient;
        ambient.SkyColor.Should().Be(new Vector3(0.2f));
        ambient.GroundColor.Should().Be(new Vector3(0.1f));
        ambient.Intensity.Should().Be(0.5f);
    }

    // ---- The sun ---------------------------------------------------------------------------------------------------

    [Fact]
    public void TheSunIsInTheDirectionFromTheCameraToTheOriginOfTheSunFrame()
    {
        using var test = new TestWorld();
        // A 3-4-5 triangle: the sun is 5e11 m away, 3e11 m along +X and 4e11 m along +Y.
        test.World.SunFrame = test.SpawnStaticFrame(TestWorld.At(3e11, 4e11, 0), radius: 1e9f);
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        PoseChecks.AssertNear(test.World.Latest().Sun.Direction, new Vector3(0.6f, 0.8f, 0), 1e-6f);
    }

    [Fact]
    public void TheSunIsSeenFromTheEyeOfTheCameraAndNotFromItsFeet()
    {
        using var test = new TestWorld();
        test.World.SunFrame = test.SpawnStaticFrame(TestWorld.At(0, 100, 0), radius: 1e9f);
        test.World.Camera = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(100, 0, 0),
            Render = new RenderData { ViewOffset = new Pose(new Vector3(0, 100, 0), Quaternion.Identity) },
        });

        test.Run(1);

        // The eye is at (100, 100, 0) and the sun at (0, 100, 0), straight along -X.
        PoseChecks.AssertNear(test.World.Latest().Sun.Direction, new Vector3(-1, 0, 0), 1e-6f);
    }

    [Fact]
    public void TheSunHasTheColorAndTheIntensityOfTheWorld()
    {
        using var test = new TestWorld();
        test.World.SunFrame = test.SpawnStaticFrame(TestWorld.At(0, 0, 1e11), radius: 1e9f);
        test.World.SunColor = new Vector3(1, 0.9f, 0.8f);
        test.World.SunIntensity = 7;
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        RenderSun sun = test.World.Latest().Sun;
        sun.Color.Should().Be(new Vector3(1, 0.9f, 0.8f));
        sun.Intensity.Should().Be(7);
    }

    [Fact]
    public void WithoutASunFrameTheSunIsStraightAboveTheCamera()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Sun.Direction.Should().Be(Vector3.UnitY);
    }

    [Fact]
    public void ASunFrameAtTheEyeOfTheCameraKeepsTheDefaultDirectionInsteadOfBecomingNotANumber()
    {
        using var test = new TestWorld();
        test.World.SunFrame = test.SpawnStaticFrame(TestWorld.At(4, 5, 6), radius: 10);
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(4, 5, 6));

        test.Run(1);

        test.World.Latest().Sun.Direction.Should().Be(Vector3.UnitY);
    }

    [Fact]
    public void TheAngularRadiusOfTheSunComesFromTheRenderRadiusOfTheSunFramesOwner()
    {
        using var test = new TestWorld();
        // The sun's radius is 7e8 m and it is 1.5e11 m away: atan(7e8 / 1.5e11) = 0.0046666328 rad.
        test.World.SunFrame = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(1.5e11),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e9f)),
            Render = new RenderData { BoundingRadius = 7e8f },
        }));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Sun.AngularRadius.Should().BeApproximately(0.0046666328f, 1e-8f);
    }

    [Fact]
    public void ASunWhoseFrameOwnerHasNoRenderRadiusHasNoAngularRadius()
    {
        using var test = new TestWorld();
        test.World.SunFrame = test.SpawnStaticFrame(TestWorld.At(1.5e11), radius: 1e9f);
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Sun.AngularRadius.Should().Be(0);
    }
}
