namespace Tertius.Simulation;

/// <summary>
/// The rules of a puff's birth (docs/plans/particles.md, "Births"): how many puffs an emitter makes in a step and when in
/// the step each one is born, the seed and the ejection direction of each, and the newborn's state at the end of the
/// step. They are pure functions. <see cref="PuffTable.AddNewborns"/> calls them for every emitter at step 7c of the sync
/// point.
/// </summary>
internal static class PuffBirths
{
    /// <summary>
    /// How many puffs an emitter makes in a step: floor(<paramref name="phase"/> + <paramref name="rate"/> ·
    /// <paramref name="dt"/>). <paramref name="next"/> is the birth phase where the next step starts, the rest of that sum,
    /// in [0, 1).
    /// </summary>
    /// <param name="phase">The emitter's birth phase where the step starts, in [0, 1).</param>
    /// <param name="rate">The emitter's rate, in puffs per s, above 0.</param>
    /// <param name="dt">The length of the step, in s.</param>
    /// <param name="next">The birth phase where the next step starts.</param>
    public static long Count(double phase, float rate, double dt, out double next)
    {
        double gathered = phase + rate * dt;
        double count = Math.Floor(gathered);
        next = gathered - count;
        // A rate so high that one step gathers more than int.MaxValue puffs makes no more than the capacity anyway.
        return count >= int.MaxValue ? int.MaxValue : (long)count;
    }

    /// <summary>
    /// The age at the end of the step of puff <paramref name="index"/> of the step, from 1 to the count, in s: (1 − s) ·
    /// <paramref name="dt"/>, with s = (<paramref name="index"/> − <paramref name="phase"/>) / (<paramref name="rate"/> ·
    /// <paramref name="dt"/>) the share of the step at which it is born, in (0, 1]. A share that rounds past 1 gives the age 0.
    /// </summary>
    public static double AgeAt(long index, double phase, float rate, double dt) =>
        Math.Max(0, (1 - (index - phase) / (rate * dt)) * dt);

    /// <summary>
    /// The index of the oldest of the <paramref name="count"/> newborns of a step that lives at the end of the step, from 1,
    /// or <paramref name="count"/> + 1 when none does. A newborn lives when its age (see <see cref="AgeAt"/>), as a float,
    /// is below <paramref name="life"/>. A look that lives less than a step makes newborns that would already be dead. The
    /// age falls as the index grows, so the newborns that live are the last ones of the step, and a search by halves
    /// finds the first of them.
    /// </summary>
    /// <param name="count">How many puffs the emitter makes in the step (see <see cref="Count"/>).</param>
    /// <param name="phase">The emitter's birth phase where the step starts, in [0, 1).</param>
    /// <param name="rate">The emitter's rate, in puffs per s, above 0.</param>
    /// <param name="dt">The length of the step, in s.</param>
    /// <param name="life">The life of the emitter's look, in s.</param>
    public static long FirstLiving(long count, double phase, float rate, double dt, float life)
    {
        long low = 1;
        long high = count + 1;
        while (low < high)
        {
            long middle = low + (high - low) / 2;
            if ((float)AgeAt(index: middle, phase: phase, rate: rate, dt: dt) < life)
                high = middle;
            else
                low = middle + 1;
        }

        return low;
    }

    /// <summary>
    /// The two random numbers of a puff, each in [0, 1): a counter-based hash of the emitter's entity handle, the tick number
    /// of the sync point and the puff's index in the step, folded by SplitMix64's finalizer. The high 32 bits give the first
    /// number and the low 32 bits the second. No generator state persists, so the same puff gets the same numbers in any
    /// order of work.
    /// </summary>
    public static (double First, double Second) Seed(Entity emitter, long tick, long index)
    {
        ulong hash = Finalize(Finalize(Finalize(emitter.Raw) ^ (ulong)tick) ^ (ulong)index);
        const double unit = 1.0 / (1UL << 32);
        return ((hash >> 32) * unit, (uint)hash * unit);
    }

    /// <summary>The finalizer of SplitMix64: a bijection of 64 bits that mixes every input bit into every output bit.</summary>
    private static ulong Finalize(ulong z)
    {
        z += 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// The ejection direction of a puff, a unit vector in the emitter's local axes, in the cone of half-angle
    /// <paramref name="spread"/> rad about -Z: cos θ = 1 − <paramref name="first"/> (1 − cos <paramref name="spread"/>) and
    /// φ = 2π <paramref name="second"/>, which spreads the directions evenly over the cone.
    /// </summary>
    public static Double3 Direction(float spread, double first, double second)
    {
        double cos = 1 - first * (1 - Math.Cos(spread));
        double sin = Math.Sqrt(Math.Max(0, 1 - cos * cos));
        double phi = 2 * Math.PI * second;
        return new(sin * Math.Cos(phi), sin * Math.Sin(phi), -cos);
    }

    /// <summary>
    /// The state of a newborn at the end of the step (docs/plans/particles.md, "The newborn's state"). It rode with its
    /// emitter until its birth, then flew free for <paramref name="age"/> seconds at the emitter's velocity plus the ejection
    /// velocity, slowed by one implicit step of the medium's drag over its age. Its chord is the chord behind its instant
    /// velocity: the instant velocity less half a step of its acceleration, turned back by the turn of the step in a turning
    /// frame.
    /// </summary>
    /// <param name="step">What the step of the newborn's frame does to a loose body in it.</param>
    /// <param name="emitterPose">The pose of the emitter's entity in the frame at the end of the step.</param>
    /// <param name="emitterVelocity">The velocity of the emitter's entity in the frame at the end of the step, in m/s.</param>
    /// <param name="emitter">The emitter.</param>
    /// <param name="direction">The ejection direction, a unit vector in the emitter's local axes (see <see cref="Direction"/>
    /// ).</param>
    /// <param name="age">The newborn's age at the end of the step, in s (see <see cref="AgeAt"/>).</param>
    /// <param name="look">The index of the newborn's look in the look table.</param>
    /// <param name="dt">The length of the step, in s.</param>
    public static Puff Newborn(in LooseBodyStep step, in DPose emitterPose, Double3 emitterVelocity,
        in PuffEmitter emitter,
        Double3 direction, double age, int look, double dt)
    {
        Double3 place = emitterPose.Position;
        Double3 ejection = emitterPose.Rotation.Rotate(direction) * emitter.Speed;
        // At its birth the puff holds only its own gas, so its lift share is its look's temperature ratio where the medium
        // fills its place.
        double lift = step.Medium.Fills(place) ? emitter.Look.TemperatureRatio : 0;
        double drag = step.Medium.Drag * lift;
        Double3 instant = (emitterVelocity + ejection) / (1 + drag * age);
        Double3 at = place - emitterVelocity * age + instant * age;
        Double3 linear = step.LinearGravity(at);
        Double3 push = lift == 0 ? linear : linear - step.EffectiveGravity(at, linear) * lift;
        Double3 behind = instant - (push - instant * drag) * (0.5 * dt);
        Double3 chord = step.Turns.Turns ? step.Turns.Step.ChordBehind(at, behind, dt) : behind;
        return new() { Place = at, Chord = chord, Age = (float)age, Look = look };
    }
}
