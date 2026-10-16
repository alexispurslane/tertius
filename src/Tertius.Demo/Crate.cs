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
    public SpawnDesc Spawn(FrameHandle frame, DPose pose, Double3 velocity) => new()
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
/// <remarks>
/// A crate holds one offer at a time, so that two persons cannot take it. After it sends an offer, it ignores every
/// <see cref="Use"/> until <see cref="AnswerTicks"/> ticks have passed. A taker answers in that time with a
/// <see cref="CrateTaken"/>. If no answer has come by then, the person declined, and the next Use gets an offer. When one
/// tick brings several Uses, the first of them gets the offer.
/// </remarks>
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

    /// <summary>
    /// How many ticks after an offer its answer reaches the crate, if there is one. A message arrives one tick after the
    /// tick that sends it. So the offer reaches the user one tick after the crate sends it, and the user's
    /// <see cref="CrateTaken"/> reaches the crate one tick after that.
    /// </summary>
    public const int AnswerTicks = 2;

    // The number of the tick on which the crate sent its last offer, or null if it has sent none.
    private long? offeredOn;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Use>().And<CrateTaken>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (events.Of<CrateTaken>().Any())
        {
            self.Destroy();
            return;
        }
        // An offer is out while its answer can still come.
        if (offeredOn is long sent && self.Tick - sent < AnswerTicks)
            return;
        // Nothing reaches this return. The crate wants only Use and CrateTaken, and a CrateTaken returned above, so the
        // events always hold a Use. The guard stays so that a change of what the crate wants cannot offer to nobody.
        // Stryker disable once Statement : the crate wants only Use and CrateTaken, so no call without a Use gets here.
        if (!events.Of<Use>().TryFirst(out Use firstUse))
            return;
        outbox.Send(firstUse.User, new CrateOffer { Crate = spec });
        offeredOn = self.Tick;
    }

    private static bool IsPositiveAndFinite(float value) => value > 0 && float.IsFinite(value);
}
