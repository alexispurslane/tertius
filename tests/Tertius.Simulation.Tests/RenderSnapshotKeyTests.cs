using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// What the renderer needs to follow an object from frame to frame (docs/plans/temporal-renderer.md, section 3): each
/// instance carries the generation of its entity, so that a slot that a new entity reuses gives a new key, and its scale at
/// the previous build by the rule of the previous transforms. Each light carries the generation of its entity too, because
/// ReSTIR keys its lights.
/// </summary>
public sealed class RenderSnapshotKeyTests
{
    [Fact]
    public void AnInstanceCarriesTheGenerationOfItsEntity()
    {
        using var test = new TestWorld();
        Entity box = test.Show(test.Root, TestWorld.At(0, 0, -5));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().InstanceOf(box).EntityGeneration.Should().Be(box.Generation);
    }

    [Fact]
    public void ANewEntityInTheSlotOfADestroyedOneCarriesItsOwnGeneration()
    {
        using var test = new TestWorld();
        Entity first = test.Show(test.Root, TestWorld.At(0, 0, -5));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Run(1);
        test.World.Latest();
        test.World.Destroy(first);
        test.Run(1);
        test.World.Latest();

        Entity second = test.Show(test.Root, TestWorld.At(0, 0, -5));
        test.Run(1);

        second.Index.Should().Be(first.Index, "the table reuses the freed slot, which the test needs");
        test.World.Latest()
            .InstanceOf(second)
            .EntityGeneration.Should()
            .Be(second.Generation)
            .And.NotBe(first.Generation);
    }

    [Fact]
    public void APointLampInTheSlotOfADestroyedOneCarriesItsOwnGeneration()
    {
        (Entity first, Entity lamp, RenderLight light) = LampInAReusedSlot(
            new PointLightData(Offset: Pose.Identity, Color: Vector3.One, Power: 40, Radius: 0.05f, CastsShadow: false)
        );

        uint generation = light switch
        {
            PointRenderLight { EntityGeneration: var g } => g,
            SpotRenderLight => throw new InvalidOperationException("The lamp is a point light."),
            null => throw new InvalidOperationException("The lamp has no light."),
        };
        generation.Should().Be(lamp.Generation).And.NotBe(first.Generation);
    }

    [Fact]
    public void ASpotLampInTheSlotOfADestroyedOneCarriesItsOwnGeneration()
    {
        (Entity first, Entity lamp, RenderLight light) = LampInAReusedSlot(
            new SpotLightData(
                Offset: Pose.Identity,
                Color: Vector3.One,
                Power: 500,
                Radius: 0.1f,
                CastsShadow: false,
                InnerConeAngle: 0.5f,
                OuterConeAngle: 0.8f
            )
        );

        uint generation = light switch
        {
            SpotRenderLight { EntityGeneration: var g } => g,
            PointRenderLight => throw new InvalidOperationException("The lamp is a spot light."),
            null => throw new InvalidOperationException("The lamp has no light."),
        };
        generation.Should().Be(lamp.Generation).And.NotBe(first.Generation);
    }

    [Fact]
    public void ThePreviousScaleOfAGrowingObjectIsItsScaleAtThePreviousTick()
    {
        using var test = new TestWorld();
        Entity growing = test.Show(
            test.Root,
            TestWorld.At(0, 0, -10),
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Render.Scale = new(1 + self.Tick))
        );
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        // Tick 0 set the scale to 1 and tick 1 to 2.
        RenderInstance instance = test.World.Latest().InstanceOf(growing);
        instance.Scale.Should().Be(new Vector3(2));
        instance.PreviousScale.Should().Be(new Vector3(1));
    }

    [Fact]
    public void AnObjectThatAppearsInTheWorldHasItsOwnScaleAsItsPreviousScale()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Run(2);
        test.World.Latest();
        Entity newcomer = test.Show(test.Root, TestWorld.At(0, 0, -10));

        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(newcomer);
        instance.PreviousScale.Should().Be(instance.Scale);
    }

    [Fact]
    public void AFrameOwnerThatGrowsCarriesItsScaleAtThePreviousTick()
    {
        using var test = new TestWorld();
        Entity planet = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(0, 0, -1e4),
                AsFrame = new(FrameBounds.Sphere(1e5f)),
                Render = Visible(radius: 100),
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) => self.Render.Scale = new(3 + self.Tick)
                    ),
                ],
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        test.World.Latest().InstanceOf(planet).PreviousScale.Should().Be(new Vector3(3));
    }

    [Fact]
    public void WhenAnotherEntityBecomesTheCameraThePreviousScaleIsTheCurrentScale()
    {
        using var test = new TestWorld();
        Entity growing = test.Show(
            test.Root,
            TestWorld.At(0, 0, -10),
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Render.Scale = new(1 + self.Tick))
        );
        Entity first = test.SpawnCamera(test.Root, DPose.Identity);
        Entity second = test.SpawnCamera(test.Root, TestWorld.At(0, 5, 0));
        test.World.Camera = first;
        test.Run(2);
        test.World.Latest();

        test.World.Camera = second;
        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(growing);
        instance.PreviousScale.Should().Be(instance.Scale);
    }

    /// <summary>
    /// A lamp of <paramref name="data"/> that takes the slot of a destroyed entity, <c>first</c>, and the one light of the
    /// snapshot after it.
    /// </summary>
    private static (Entity First, Entity Lamp, RenderLight Light) LampInAReusedSlot(LightData data)
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        Entity first = test.Show(test.Root, TestWorld.At(0, 0, -5));
        test.Run(1);
        test.World.Latest();
        test.World.Destroy(first);
        test.Run(1);
        test.World.Latest();

        Entity lamp = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(0, 2, -5),
                Render = new() { Light = data },
            }
        );
        test.Run(1);

        lamp.Index.Should().Be(first.Index, "the table reuses the freed slot, which the test needs");
        return (first, lamp, test.World.Latest().Lights.Should().ContainSingle().Subject);
    }
}
