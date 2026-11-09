using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Everything in a snapshot that is not an instance: the camera, the lights that entities carry, and the ambient light. Positions are relative to the render origin and in the axes of the camera's
/// frame. A quarter turn about +Y maps (x, y, z) to (z, y, -x).
/// </summary>
public sealed class RenderSnapshotLightingTests
{
    private static readonly Pose LampOffset = new(new(0, 1, 0), Quaternion.Identity);
    private static readonly Vector3 LampColor = new(1, 0.5f, 0.25f);

    /// <summary>A render row that carries a spot light, one meter above the entity, that draws nothing.</summary>
    private static RenderData Lamp() =>
        new()
        {
            Light = new SpotLightData(
                Offset: LampOffset,
                Color: LampColor,
                Power: 3,
                Radius: 0.2f,
                CastsShadow: true,
                InnerConeAngle: 0.3f,
                OuterConeAngle: 0.5f
            ),
        };

    /// <summary>The Sun's light at the origin of its entity: 3.828 × 10²⁶ W from a sphere of 6.957 × 10⁸ m.</summary>
    private static readonly PointLightData Star = new(
        Offset: default,
        Color: Vector3.One,
        Power: 3.828e26f,
        Radius: 6.957e8f,
        CastsShadow: true
    );

    /// <summary>The same as <see cref="Lamp"/>, but a point light.</summary>
    private static RenderData PointLamp() =>
        new()
        {
            Light = new PointLightData(Offset: LampOffset, Color: LampColor, Power: 3, Radius: 0.2f, CastsShadow: true),
        };

    /// <summary>The same as <see cref="Lamp"/>, but a tube light, a bar of 1.7 m.</summary>
    private static RenderData TubeLamp() =>
        new()
        {
            Light = new TubeLightData(
                Offset: LampOffset,
                Color: LampColor,
                Power: 3,
                Radius: 0.2f,
                Length: 1.7f,
                CastsShadow: true
            ),
        };

    /// <summary>The lamp of each kind: "point", "spot" or "tube".</summary>
    private static RenderData LampOfKind(string kind) =>
        kind switch
        {
            "point" => PointLamp(),
            "spot" => Lamp(),
            "tube" => TubeLamp(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A lamp is a point, a spot or a tube."),
        };

    // ---- The camera ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheViewOffsetOfTheCameraIsTurnedIntoItsRotation()
    {
        using var test = new TestWorld();
        test.World.Camera = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Render = new RenderData
                {
                    ViewOffset = new(new(0, 1.7f, 0), PoseChecks.QuarterTurnAboutY.ToQuaternion()),
                },
            }
        );

        test.Run(1);

        PoseChecks.AssertSameRotation(test.World.Latest().Camera.Rotation, PoseChecks.QuarterTurnAboutY);
    }

    [Fact]
    public void TheRotationOfTheCameraIsTheRotationOfItsEntityComposedWithItsViewOffset()
    {
        using var test = new TestWorld();
        test.World.Camera = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = new(Double3.Zero, PoseChecks.QuarterTurnAboutY),
                Render = new RenderData { ViewOffset = new(Vector3.Zero, PoseChecks.QuarterTurnAboutY.ToQuaternion()) },
            }
        );

        test.Run(1);

        // Two quarter turns about +Y make a half turn.
        PoseChecks.AssertSameRotation(
            test.World.Latest().Camera.Rotation,
            DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI)
        );
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
        test.World.Camera = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Render = new RenderData { ViewOffset = new(new(0, 1.7f, 0), Quaternion.Identity) },
            }
        );
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = new(new(5, 0, 0), PoseChecks.QuarterTurnAboutY),
                Render = Lamp(),
            }
        );

        test.Run(1);

        RenderLight light = test.World.Latest().Lights.Should().ContainSingle().Subject;
        light.TryGetValue(out SpotRenderLight spot).Should().BeTrue();
        PoseChecks.AssertNear(spot.Position, new(5, 1 - 1.7f, 0));
    }

    [Fact]
    public void APointLightIsAtThePoseOfItsEntityComposedWithTheLightOffsetRelativeToTheOrigin()
    {
        using var test = new TestWorld();
        // The same scene as for the spot light: the eye is 1.7 m above the origin, the lamp stands at (5, 0, 0) and carries the light 1 m above itself.
        test.World.Camera = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Render = new RenderData { ViewOffset = new(new(0, 1.7f, 0), Quaternion.Identity) },
            }
        );
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = new(new(5, 0, 0), PoseChecks.QuarterTurnAboutY),
                Render = PointLamp(),
            }
        );

        test.Run(1);

        RenderLight light = test.World.Latest().Lights.Should().ContainSingle().Subject;
        light.TryGetValue(out PointRenderLight point).Should().BeTrue();
        PoseChecks.AssertNear(point.Position, new(5, 1 - 1.7f, 0));
    }

    [Fact]
    public void TheOffsetOfALightIsTurnedWithTheEntityBeforeItIsAdded()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        // The entity is turned a quarter about +Y, so the offset (0, 0, -2) of the light becomes (-2, 0, 0) from the entity at (5, 0, 0).
        Pose offset = new(new(0, 0, -2), Quaternion.Identity);
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = new(new(5, 0, 0), PoseChecks.QuarterTurnAboutY),
                Render = new RenderData
                {
                    Light = new PointLightData(
                        Offset: offset,
                        Color: LampColor,
                        Power: 3,
                        Radius: 0.2f,
                        CastsShadow: true
                    ),
                },
            }
        );
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = new(new(5, 0, 0), PoseChecks.QuarterTurnAboutY),
                Render = new RenderData
                {
                    Light = new SpotLightData(
                        Offset: offset,
                        Color: LampColor,
                        Power: 3,
                        Radius: 0.2f,
                        CastsShadow: true,
                        InnerConeAngle: 0.3f,
                        OuterConeAngle: 0.5f
                    ),
                },
            }
        );

        test.Run(1);

        RenderLight[] lights = [.. test.World.Latest().Lights];
        lights.Should().HaveCount(2);
        lights[0].TryGetValue(out PointRenderLight point).Should().BeTrue();
        PoseChecks.AssertNear(point.Position, new(3, 0, 0));
        lights[1].TryGetValue(out SpotRenderLight spot).Should().BeTrue();
        PoseChecks.AssertNear(spot.Position, new(3, 0, 0));
    }

    [Fact]
    public void ASpotLightTurnsWithTheRotationOfItsOffset()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        Pose quarterTurnAboutY = new(Vector3.Zero, PoseChecks.QuarterTurnAboutY.ToQuaternion());
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Render = new RenderData
                {
                    Light = new SpotLightData(
                        Offset: quarterTurnAboutY,
                        Color: LampColor,
                        Power: 3,
                        Radius: 0.2f,
                        CastsShadow: true,
                        InnerConeAngle: 0.3f,
                        OuterConeAngle: 0.5f
                    ),
                },
            }
        );

        test.Run(1);

        // The quarter turn maps the local direction (0, 0, -1) to (-1, 0, 0), here by the offset and not by the entity.
        test.World.Latest().Lights.Single().TryGetValue(out SpotRenderLight spot).Should().BeTrue();
        PoseChecks.AssertNear(spot.Direction, new(-1, 0, 0), 1e-6f);
    }

    [Fact]
    public void AnOffsetThatIsTheDefaultPoseIsTheEntityItselfAndASpotLightPointsAlongItsNegativeZAxis()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        // The default pose has a zeroed rotation. The builder reads it as the identity instead of making a NaN.
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(5),
                Render = new RenderData
                {
                    Light = new PointLightData(
                        Offset: default,
                        Color: LampColor,
                        Power: 3,
                        Radius: 0.2f,
                        CastsShadow: true
                    ),
                },
            }
        );
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(6),
                Render = new RenderData
                {
                    Light = new SpotLightData(
                        Offset: default,
                        Color: LampColor,
                        Power: 3,
                        Radius: 0.2f,
                        CastsShadow: true,
                        InnerConeAngle: 0.3f,
                        OuterConeAngle: 0.5f
                    ),
                },
            }
        );

        test.Run(1);

        RenderLight[] lights = [.. test.World.Latest().Lights];
        lights.Should().HaveCount(2);
        lights[0].TryGetValue(out PointRenderLight point).Should().BeTrue();
        PoseChecks.AssertNear(point.Position, new(5, 0, 0));
        lights[1].TryGetValue(out SpotRenderLight spot).Should().BeTrue();
        PoseChecks.AssertNear(spot.Position, new(6, 0, 0));
        PoseChecks.AssertNear(spot.Direction, new(0, 0, -1), 1e-6f);
    }

    [Fact]
    public void ASpotLightPointsAlongTheNegativeZAxisOfItsEntityTurnedWithTheEntity()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = new(new(5, 0, 0), PoseChecks.QuarterTurnAboutY),
                Render = Lamp(),
            }
        );

        test.Run(1);

        // The quarter turn maps the local direction (0, 0, -1) to (-1, 0, 0).
        test.World.Latest().Lights.Single().TryGetValue(out SpotRenderLight spot).Should().BeTrue();
        PoseChecks.AssertNear(spot.Direction, new(-1, 0, 0), 1e-6f);
    }

    [Fact]
    public void ALightCarriesItsPowerRadiusAndShadowFlagIntoTheSnapshot()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Spawn(new() { Frame = test.Root, Render = PointLamp() });
        test.World.Spawn(new() { Frame = test.Root, Render = Lamp() });
        test.World.Spawn(new() { Frame = test.Root, Render = TubeLamp() });

        test.Run(1);

        RenderLight[] lights = [.. test.World.Latest().Lights];
        lights.Should().HaveCount(3);
        lights[0].TryGetValue(out PointRenderLight point).Should().BeTrue();
        point.Color.Should().Be(new Vector3(1, 0.5f, 0.25f));
        point.Power.Should().Be(3);
        point.Radius.Should().Be(0.2f);
        point.CastsShadow.Should().BeTrue();
        lights[1].TryGetValue(out SpotRenderLight spot).Should().BeTrue();
        spot.Color.Should().Be(new Vector3(1, 0.5f, 0.25f));
        spot.Power.Should().Be(3);
        spot.Radius.Should().Be(0.2f);
        spot.CastsShadow.Should().BeTrue();
        spot.InnerConeAngle.Should().Be(0.3f);
        spot.OuterConeAngle.Should().Be(0.5f);
        lights[2].TryGetValue(out TubeRenderLight tube).Should().BeTrue();
        tube.Color.Should().Be(new Vector3(1, 0.5f, 0.25f));
        tube.Power.Should().Be(3);
        tube.Radius.Should().Be(0.2f);
        tube.Length.Should().Be(1.7f);
        tube.CastsShadow.Should().BeTrue();
    }

    [Fact]
    public void ALightCarriesTheIndexOfItsEntity()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        // Two entities before the lamps, so that no lamp's index is 0 by chance.
        test.Show(test.Root, TestWorld.At(0, 0, -5));
        test.Show(test.Root, TestWorld.At(0, 0, -6));
        Entity point = test.World.Spawn(new() { Frame = test.Root, Render = PointLamp() });
        Entity spot = test.World.Spawn(new() { Frame = test.Root, Render = Lamp() });
        Entity tube = test.World.Spawn(new() { Frame = test.Root, Render = TubeLamp() });

        test.Run(1);

        RenderLight[] lights = [.. test.World.Latest().Lights];
        lights[0].TryGetValue(out PointRenderLight pointLight).Should().BeTrue();
        pointLight.EntityIndex.Should().Be(point.Index);
        lights[1].TryGetValue(out SpotRenderLight spotLight).Should().BeTrue();
        spotLight.EntityIndex.Should().Be(spot.Index);
        lights[2].TryGetValue(out TubeRenderLight tubeLight).Should().BeTrue();
        tubeLight.Entity.Should().Be(tube, "a tube light carries both the index and the generation of its entity");
    }

    [Theory]
    [InlineData("point")]
    [InlineData("spot")]
    [InlineData("tube")]
    public void ALightOnAMovingEntityCarriesItsPreviousPlace(string kind)
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new(0, 1, 0)));
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(10, 0, -5),
                Render = LampOfKind(kind),
                Components = [new Mover(new(1, 0, 0))],
            }
        );
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        // After tick 1 the lamp is at (12, 0, -5), its light 1 m above it, and the camera at (0, 2, 0). A tick earlier the
        // lamp was at (11, 0, -5). Both places are relative to the camera of this snapshot.
        (Vector3 position, Vector3 previous) = test.World.Latest().Lights.Single() switch
        {
            PointRenderLight point => (point.Position, point.PreviousPosition),
            SpotRenderLight spot => (spot.Position, spot.PreviousPosition),
            TubeRenderLight tube => (tube.Position, tube.PreviousPosition),
            null => throw new InvalidOperationException("The lamp's light holds a case."),
        };
        position.Should().Be(new Vector3(12, -1, -5));
        previous.Should().Be(new Vector3(11, -1, -5));
    }

    [Fact]
    public void ASpotLightCarriesItsPreviousAxis()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        // The lamp turns a quarter turn about +Y in each tick: by a quarter turn at the end of tick 0 and by a half turn at
        // the end of tick 1. They take the axis (0, 0, -1) to (-1, 0, 0) and to (0, 0, 1).
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Render = Lamp(),
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) =>
                            self.SetTransform(
                                self.PoseInOwnFrame() with
                                {
                                    Rotation = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2 * (self.Tick + 1)),
                                }
                            )
                    ),
                ],
            }
        );
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        test.World.Latest().Lights.Single().TryGetValue(out SpotRenderLight spot).Should().BeTrue();
        PoseChecks.AssertNear(spot.Direction, new(0, 0, 1), 1e-6f);
        PoseChecks.AssertNear(spot.PreviousDirection, new(-1, 0, 0), 1e-6f);
    }

    [Fact]
    public void ATubeLightFollowsItsEntityAndTurnsItsSegmentWithIt()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        // The bar moves 1 m along +X and turns a quarter turn about +Z in each tick: to x = 1 and a quarter turn at the end
        // of tick 0, and to x = 2 and a half turn at the end of tick 1. The turns take the segment's axis (0, 1, 0) to
        // (-1, 0, 0) and to (0, -1, 0).
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Render = new RenderData
                {
                    Light = new TubeLightData(
                        Offset: default,
                        Color: LampColor,
                        Power: 30,
                        Radius: 0.13f,
                        Length: 1.7f,
                        CastsShadow: false
                    ),
                },
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) =>
                            self.SetTransform(
                                new DPose(
                                    new Double3(self.Tick + 1, 0, -5),
                                    DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2 * (self.Tick + 1))
                                )
                            )
                    ),
                ],
            }
        );
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        test.World.Latest().Lights.Single().TryGetValue(out TubeRenderLight tube).Should().BeTrue();
        PoseChecks.AssertNear(tube.Position, new(2, 0, -5));
        PoseChecks.AssertNear(tube.PreviousPosition, new(1, 0, -5));
        PoseChecks.AssertNear(tube.Axis, new(0, -1, 0), 1e-6f);
        PoseChecks.AssertNear(tube.PreviousAxis, new(-1, 0, 0), 1e-6f);
        tube.Length.Should().Be(1.7f);
        tube.Radius.Should().Be(0.13f);
    }

    [Fact]
    public void AfterTheCameraChangesFrameALightsPreviousPlaceIsItsPlace()
    {
        using var scene = new RenderSnapshotInterpolationTests.HandOffScene();
        scene.Test.World.Spawn(
            new()
            {
                Frame = scene.Test.Root,
                Pose = new(scene.StartOfMoving, DQuaternion.Identity),
                Render = Lamp(),
                Components = [new Mover(new(1, 0, 0))],
            }
        );
        scene.Test.Run(2);
        scene.Test.World.Latest();

        scene.Test.Run(1);

        // The snapshot of the tick in which the camera flew into B does not interpolate, so the light was where it is.
        scene.Test.World.Latest().Lights.Single().TryGetValue(out SpotRenderLight spot).Should().BeTrue();
        spot.PreviousPosition.Should().Be(spot.Position);
        spot.PreviousDirection.Should().Be(spot.Direction);
        scene.Test.World.GetFrame(scene.Camera).Should().Be(scene.B);
    }

    [Fact]
    public void AStarAtTheRootIsALightOfTheRootsEntity()
    {
        using var test = new TestWorld();
        // A second root under the shared root, whose own entity carries the Sun's light at the root's origin.
        FrameHandle star = test.World.CreateRootFrame(new(FrameBounds.Sphere(1e9f)), new RenderData { Light = Star });
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Lights.Single().TryGetValue(out PointRenderLight light).Should().BeTrue();
        light.EntityIndex.Should().Be(star.Entity.Index);
        light.Power.Should().Be(3.828e26f);
    }

    [Fact]
    public void AWorldWithoutALightHasNoLight()
    {
        // No light from above or anywhere else stands in for a missing one: a world lit by nothing is dark.
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Show(test.Root, TestWorld.At(0, 0, -5));

        test.Run(1);

        test.World.Latest().Lights.Should().BeEmpty();
    }

    /// <summary>
    /// The camera sits at the origin of the test's root. A second root lies 1 km along +X under the shared virtual root,
    /// turned a quarter turn about Y, and a lamp stands 200 m along that root's -Z. The quarter turn takes that root's -Z to
    /// the first root's -X, so the lamp stands at (800, 0, 0) from the camera, and its light 1 m above it.
    /// </summary>
    [Fact]
    public void ALightInAnotherFrameTreeIsPlacedThroughTheSharedRoot()
    {
        using var test = new TestWorld();
        FrameHandle other = test.World.GetOwnedFrame(
            test.World.Spawn(
                new()
                {
                    Pose = new(new(1000, 0, 0), PoseChecks.QuarterTurnAboutY),
                    AsFrame = new(FrameBounds.Sphere(5e5f)),
                }
            )
        );
        test.World.Spawn(
            new()
            {
                Frame = other,
                Pose = TestWorld.At(0, 0, -200),
                Render = PointLamp(),
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Lights.Single().TryGetValue(out PointRenderLight light).Should().BeTrue();
        PoseChecks.AssertNear(light.Position, new(800, 1, 0), 1e-3f);
    }

    [Fact]
    public void ALightIsInTheSnapshotEvenWhenItsEntityDrawsNothing()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Spawn(new() { Frame = test.Root, Render = Lamp() });

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
        Entity entity = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(2),
                Render = lantern,
            }
        );

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.InstanceOf(entity).Position.Should().Be(new Vector3(2, 0, 0));
        snapshot.Lights.Should().ContainSingle();
    }

    [Fact]
    public void AnEntityWithoutALightPutsNoneInTheSnapshot()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        // The default light holds no case. The first entity draws nothing, so only its light could put it in the snapshot.
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Render = new RenderData { Light = default },
            }
        );
        test.World.Spawn(new() { Frame = test.Root, Render = Visible() });

        test.Run(1);

        test.World.Latest().Lights.Should().BeEmpty();
    }

    // ---- Ambient light ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheAmbientLightOfTheWorldIsTheOneOfTheSnapshot()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Ambient = new()
        {
            SkyColor = new(0.2f),
            GroundColor = new(0.1f),
            Intensity = 0.5f,
        };

        test.Run(1);

        AmbientLight ambient = test.World.Latest().Ambient;
        ambient.SkyColor.Should().Be(new Vector3(0.2f));
        ambient.GroundColor.Should().Be(new Vector3(0.1f));
        ambient.Intensity.Should().Be(0.5f);
    }
}
