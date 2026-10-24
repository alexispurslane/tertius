namespace Tertius.Simulation;

/// <summary>
/// The puffs that an entity leaves behind it (docs/plans/particles.md, "The emitter and the look"): a component writes it
/// into its own entity's render row, <see cref="RenderData.Emitter"/>, as it writes the entity's light. Puffs leave the
/// origin of the entity along its local -Z, in a cone of half-angle <see cref="Spread"/>, at <see cref="Speed"/> relative
/// to the entity, and <see cref="Rate"/> of them in each second. Each puff is born in the entity's frame and lives there
/// for its whole life. The emitter has no offset: a part places the nozzle. The default emitter makes no puffs.
/// </summary>
/// <remarks>
/// An emitter makes puffs only when it can (see <see cref="CanMakePuffs"/>). One that cannot is left out without an
/// error, as a light whose range is zero is. To switch a jet off and on again, keep the emitter value and write the
/// default over it. A component that follows a throttle changes <see cref="Rate"/> and <see cref="Speed"/>, which are not
/// in the look, so its puffs share one look however its throttle moves.
/// </remarks>
/// <param name="Rate">How many puffs the emitter makes in each second, above 0 to make any.</param>
/// <param name="Speed">The ejection speed: a newborn's velocity relative to the entity, in m/s, 0 or more.</param>
/// <param name="Spread">The half-angle of the cone of ejection directions about the entity's local -Z, in rad, from 0 to π.</param>
/// <param name="Look">What the emitter's puffs share: their life, their radii, their temperature ratio and their shading.</param>
public readonly record struct PuffEmitter(float Rate, float Speed, float Spread, PuffLook Look)
{
    /// <summary>
    /// True when the emitter makes puffs: <see cref="Rate"/> is finite and above 0, <see cref="Speed"/> is finite and 0 or
    /// more, <see cref="Spread"/> lies from 0 to π, and the look is valid (see <see cref="PuffLook.IsValid"/>). False for
    /// the default emitter.
    /// </summary>
    public bool CanMakePuffs =>
        float.IsFinite(Rate)
        && Rate > 0
        && float.IsFinite(Speed)
        && Speed >= 0
        && Spread >= 0
        && Spread <= MathF.PI
        && Look.IsValid;
}
