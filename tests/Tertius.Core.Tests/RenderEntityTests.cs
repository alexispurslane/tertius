using System.Numerics;
using AwesomeAssertions;
using Tertius.Testing;

namespace Tertius.Core.Tests;

/// <summary>
/// The entity of a render instance and of a render light: its slot with its generation, as one <see cref="Entity"/>. A new
/// entity can take the slot of a destroyed one, so the renderer follows an object from frame to frame by this entity, not
/// by the slot alone. The rendering tests pin what the renderer keys by it.
/// </summary>
public sealed class RenderEntityTests
{
    [Fact]
    public void AnInstanceNamesItsEntityBySlotAndGeneration()
    {
        var instance = RenderInstance.Create(
            mesh: new(0),
            material: new(0),
            position: Vector3.Zero,
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            boundingRadius: 0,
            hemisphere: HemisphereUp.Even,
            chainDepth: TestChains.CamerasFrame,
            entityIndex: 7,
            entityGeneration: 4
        );

        instance.Entity.Should().Be(new Entity(Index: 7, Generation: 4));
    }

    [Fact]
    public void APointLightNamesItsEntityBySlotAndGeneration()
    {
        var lamp = new PointRenderLight(
            EntityIndex: 7,
            EntityGeneration: 4,
            Position: Vector3.Zero,
            PreviousPosition: Vector3.Zero,
            Color: Vector3.One,
            Power: 4,
            Radius: 0.1f,
            CastsShadow: false,
            ChainDepth: TestChains.CamerasFrame
        );

        lamp.Entity.Should().Be(new Entity(Index: 7, Generation: 4));
    }

    [Fact]
    public void ASpotLightNamesItsEntityBySlotAndGeneration()
    {
        var beam = new SpotRenderLight(
            EntityIndex: 7,
            EntityGeneration: 4,
            Position: Vector3.Zero,
            PreviousPosition: Vector3.Zero,
            Direction: -Vector3.UnitY,
            PreviousDirection: -Vector3.UnitY,
            Color: Vector3.One,
            Power: 4,
            Radius: 0.1f,
            CastsShadow: false,
            InnerConeAngle: 0.3f,
            OuterConeAngle: 0.5f,
            ChainDepth: TestChains.CamerasFrame
        );

        beam.Entity.Should().Be(new Entity(Index: 7, Generation: 4));
    }
}
