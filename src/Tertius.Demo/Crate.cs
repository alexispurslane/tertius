using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// What a crate is: its size, its mass, and the mesh and material that draw it. A crate offers it to the person that
/// uses it, so that the person can draw the crate in its hands and throw it again as a new crate. It is 24 bytes, so a
/// message can hold it.
/// </summary>
/// <param name="HalfExtents">Half the crate's size along each of its own axes, in meters.</param>
/// <param name="Mass">The crate's mass, in kilograms.</param>
/// <param name="Mesh">The mesh that draws the crate.</param>
/// <param name="Material">The material of the mesh.</param>
public readonly record struct CrateSpec(Vector3 HalfExtents, float Mass, MeshId Mesh, MaterialId Material)
{
    /// <summary>The radius of the crate's bounding sphere, in meters.</summary>
    public float BoundingRadius => HalfExtents.Length();

    /// <summary>The render row of the crate, and of a drawn copy of it.</summary>
    public RenderData Drawn() => new()
    {
        Mesh = Mesh,
        Material = Material,
        BoundingRadius = BoundingRadius,
        Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
    };

    /// <summary>
    /// A loose crate at <paramref name="pose"/> in <paramref name="frame"/>, which moves at <paramref name="velocity"/> in
    /// the frame's axes: a dynamic box with no damping, drawn by <see cref="Drawn"/>, with a <see cref="Crate"/> component.
    /// Throws <see cref="ArgumentOutOfRangeException"/> if the size or the mass is not positive and finite.
    /// </summary>
    public SpawnDesc Spawn(FrameHandle frame, DPose pose, Vector3 velocity) => new()
    {
        Frame = frame,
        Pose = pose,
        LinearVelocity = velocity,
        Body = new BodyDesc(new BoxShapeDesc(HalfExtents), Mass: Mass, LinearDamping: 0, AngularDamping: 0),
        Render = Drawn(),
        Components = [new Crate(this)],
    };
}

/// <summary>
/// A crate's answer to a <see cref="Use"/>: the crate offers itself to the user, with what the user needs to draw the
/// crate and to throw it. The sender is the crate. A <see cref="Carry"/> takes the offer with a <see cref="CrateTaken"/>.
/// </summary>
public struct CrateOffer : IEventPayload
{
    /// <summary>The crate that offers itself.</summary>
    public CrateSpec Crate;
}

/// <summary>Tells a <see cref="Crate"/> that a carrier took it, so that the crate goes: it destroys itself.</summary>
public struct CrateTaken : IEventPayload;

/// <summary>
/// A loose crate that a person can carry and throw. A <see cref="Use"/> makes the crate offer itself to the user with a
/// <see cref="CrateOffer"/>. A <see cref="CrateTaken"/> makes it go: it destroys itself. The crate is then dead, so it
/// ignores every later order. In a tick that brings both, the crate goes and offers itself to no one. Its entity needs
/// the body and the render row that <paramref name="spec"/> describes: <see cref="CrateSpec.Spawn"/> makes one.
/// </summary>
/// <param name="spec">
/// What the crate is. Its size and its mass must be positive and finite, or the constructor throws
/// <see cref="ArgumentOutOfRangeException"/>.
/// </param>
public sealed class Crate(CrateSpec spec) : IComponent
{
    private readonly CrateSpec spec = IsPositiveAndFinite(spec.HalfExtents.X) && IsPositiveAndFinite(spec.HalfExtents.Y) &&
        IsPositiveAndFinite(spec.HalfExtents.Z) && IsPositiveAndFinite(spec.Mass)
        ? spec
        : throw new ArgumentOutOfRangeException(nameof(spec), spec, "A crate's size and mass must be positive and finite.");

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Use>().And<CrateTaken>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (events.Contains<CrateTaken>())
        {
            self.Destroy();
            return;
        }
        foreach (ref Use use in events.Of<Use>())
            outbox.Send(use.User, new CrateOffer { Crate = spec });
    }

    private static bool IsPositiveAndFinite(float value) => value > 0 && float.IsFinite(value);
}
