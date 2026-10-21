using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The pure rules of a puff, with no World (docs/plans/particles.md, "The rule of motion" and "Births"): how a row steps in
/// its frame's loose-body step, how many puffs an emitter makes in a step and at what age, the seed and the direction of a
/// puff, the newborn's state, and which emitters make puffs. The steps of the tests are built by hand. A turning step is
/// the step of a station that spins about its +Y at 0.31321 rad/s, the rate that gives 9.81 m/s² at 100 m.
/// </summary>
public class PuffRuleTests
{
    /// <summary>The World's step: the float 1/60 widened to a double, 0.01666666753590107 s.</summary>
    private const float Dt = 1f / 60;
    private const double StationRate = 0.31321;
    private static readonly Vector3 Down = new(0, -9.81f, 0);
    private static readonly PuffLook Hot = new(Life: 2, StartRadius: 0.2f, EndRadius: 1, TemperatureRatio: 3, PuffShading.Glow(new Vector3(6, 5.4f, 4.5f)));
    private static readonly PuffLook Cold = Hot with { TemperatureRatio = 0.75f, Shading = PuffShading.Lit(new Vector3(0.8f)) };
    private static readonly PuffLook Neutral = Hot with { TemperatureRatio = 1, Life = 100 };

    private static FrameTurns StationTurns()
    {
        var turn = new FrameTurn(DQuaternion.FromAxisAngle(Double3.UnitY, -StationRate * Dt), new Double3(0, StationRate, 0));
        return new FrameTurns(turn, turn);
    }

    private static LooseBodyStep StepOf(GravityField field = default, Double3 properAcceleration = default, MediumField medium = default,
        bool turning = false) => new()
    {
        Field = field,
        ProperAcceleration = properAcceleration,
        Turns = turning ? StationTurns() : new FrameTurns(FrameTurn.None, FrameTurn.None),
        TurnsStep = Dt,
        Medium = medium,
    };

    private static Puff At(Double3 place, Double3 chord = default, float age = 0, int look = 3) =>
        new() { Place = place, Chord = chord, Age = age, Look = look };

    // ---- The rule of motion ---------------------------------------------------------------------------------------

    [Fact]
    public void APuffInVacuumInAFrameThatDoesNotTurnGainsItsLinearGravityTimesTheStep()
    {
        LooseBodyStep step = StepOf(GravityField.Uniform(Down), properAcceleration: new Double3(1, 0, 0.5));
        Puff puff = At(new Double3(3, 40, -2), new Double3(4, 0.25, -9), age: 0.5f);
        Double3 linear = new Double3(0, Down.Y, 0) - new Double3(1, 0, 0.5);

        Puff next = puff.Stepped(in step, in Hot, Dt);

        next.Chord.Should().Be(puff.Chord + linear * Dt, "the chord gains the linear gravity times the step, to the bit");
        next.Place.Should().Be(puff.Place + next.Chord * Dt);
        next.Age.Should().Be(0.5f + Dt);
        next.Look.Should().Be(3);
    }

    [Fact]
    public void APuffInVacuumInATurningFrameTakesTheFreeBodyUpdateOfPhysics()
    {
        LooseBodyStep step = StepOf(GravityField.Uniform(new Vector3(0, -2, 0)), properAcceleration: new Double3(0.5, 0, 0), turning: true);
        Puff puff = At(new Double3(80, -3, 12), new Double3(2, 1, -30), age: 1.25f);
        Double3 linear = new Double3(0, -2, 0) - new Double3(0.5, 0, 0);
        FrameTurns turns = step.Turns;

        Puff next = puff.Stepped(in step, in Cold, Dt);

        // The expression of physics for a free dynamic body with no numeric home (JoltPhysicsBackend.Step.cs, ApplyTurningUpdate).
        Double3 ahead = puff.Chord + turns.FreeChange(puff.Place, puff.Chord, Dt) + turns.Step.Turn.Rotate(linear) * Dt;
        next.Chord.Should().Be(ahead);
        next.Place.Should().Be(puff.Place + ahead * Dt);
    }

    [Fact]
    public void AHotPuffInAMediumRisesAndAColdPuffSinks()
    {
        LooseBodyStep step = StepOf(GravityField.Uniform(Down), medium: MediumField.Uniform(1.225f, 2));
        Puff atRest = At(new Double3(0, 10, 0));

        Puff hot = atRest.Stepped(in step, in Hot, Dt);
        Puff cold = atRest.Stepped(in step, in Cold, Dt);

        // Hot exhaust at the temperature ratio 3: a push of g (1 − 3) = 19.62 m/s² up, slowed by the drag 2 · 3.
        hot.Chord.Y.Should().BeApproximately(-(double)Down.Y * 2 * Dt / (1 + 6.0 * Dt), 1e-12);
        // Cold gas at 0.75: a push of 2.45 m/s² down, slowed by the drag 2 · 0.75.
        cold.Chord.Y.Should().BeApproximately((double)Down.Y * 0.25 * Dt / (1 + 1.5 * Dt), 1e-12);
        hot.Chord.X.Should().Be(0);
        cold.Chord.Z.Should().Be(0);
    }

    [Fact]
    public void APuffAtTheMediumsTemperatureFloatsAndComesToRest()
    {
        LooseBodyStep step = StepOf(GravityField.Uniform(Down), medium: MediumField.Uniform(1.225f, 2));
        Puff puff = At(new Double3(0, 10, 0), new Double3(5, 0, 0));

        for (int i = 0; i < 600; i++)
            puff = puff.Stepped(in step, in Neutral, Dt);

        puff.Chord.Y.Should().Be(0, "the medium lifts a puff at its own temperature by its whole weight");
        puff.Place.Y.Should().Be(10);
        puff.Chord.X.Should().BeApproximately(5 / Math.Pow(1 + 2.0 * Dt, 600), 1e-12, "the drag keeps 1 / (1 + 2 dt) of the chord in each step");
        puff.Chord.X.Should().BeLessThan(5e-5);
    }

    [Fact]
    public void APuffCoolsTowardTheMediumAndItsLiftShareMovesTowardOne()
    {
        // No drag, so that a puff at rest gains its push times the step.
        LooseBodyStep step = StepOf(GravityField.Uniform(Down), medium: MediumField.Uniform(1.225f, 0));
        Double3 place = new(0, 10, 0);

        double PushAt(float age) => At(place, age: age).Stepped(in step, in Hot, Dt).Chord.Y / Dt;

        // The temperature ratio of the mix is 1 + w (3 − 1), with the own share w = 1 − age / 2.
        PushAt(0).Should().BeApproximately(-Down.Y * 2, 1e-5, "a newborn holds only its own gas, at the ratio 3");
        PushAt(1).Should().BeApproximately(-Down.Y * 1, 1e-5, "at half its life it holds half, at the ratio 2");
        PushAt(1.9f).Should().BeApproximately(-Down.Y * 0.1, 1e-5, "near its end it is nearly the medium, at the ratio 1.1");
    }

    [Fact]
    public void APuffFeelsTheSameLiftAndDragAtEveryHeightOfAPlanetMedium()
    {
        // A uniform field, so that only the medium could tell the two heights apart.
        LooseBodyStep step = StepOf(GravityField.Uniform(Down), medium: MediumField.Planet(6.371e6, 1.225f, 8500, 2));
        Double3 velocity = new(3, -1, 2);

        Puff low = At(new Double3(0, 6.371e6 + 10, 0), velocity, age: 0.5f).Stepped(in step, in Hot, Dt);
        Puff high = At(new Double3(0, 6.371e6 + 40_000, 0), velocity, age: 0.5f).Stepped(in step, in Hot, Dt);

        high.Chord.Should().Be(low.Chord, "the lift share is the puff's temperature ratio, whatever the medium's density there");
        LooseBodyStep vacuumStep = StepOf(GravityField.Uniform(Down));
        Puff vacuum = At(new Double3(0, 6.371e6 + 10, 0), velocity, age: 0.5f).Stepped(in vacuumStep, in Hot, Dt);
        low.Chord.Should().NotBe(vacuum.Chord);
    }

    [Fact]
    public void APuffOutsideTheRegionOfItsFramesMediumFeelsNoLiftAndNoDrag()
    {
        var cabin = new BoundingBox(new Vector3(-5), new Vector3(5));
        LooseBodyStep withAir = StepOf(GravityField.Uniform(Down), medium: MediumField.Uniform(1.225f, 2, cabin));
        LooseBodyStep vacuum = StepOf(GravityField.Uniform(Down));
        Double3 chord = new(1, 2, 3);

        Puff outside = At(new Double3(0, 20, 0), chord).Stepped(in withAir, in Hot, Dt);
        Puff inside = At(new Double3(0, 2, 0), chord).Stepped(in withAir, in Hot, Dt);

        outside.Chord.Should().Be(At(new Double3(0, 20, 0), chord).Stepped(in vacuum, in Hot, Dt).Chord, "outside its box the medium is vacuum");
        inside.Chord.Y.Should().BeGreaterThan(chord.Y, "inside the box the hot puff rises");
        inside.Chord.X.Should().BeLessThan(chord.X, "inside the box the drag slows it");
    }

    // ---- Births ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AnEmitterAtSixtyPuffsPerSecondMakesOnePuffPerStepAtTheEndOfTheStep()
    {
        double phase = 0;
        for (int step = 0; step < 60; step++)
        {
            double start = phase;
            PuffBirths.Count(start, 60, Dt, out phase).Should().Be(1, "step {0}", step);
            // The float step widened is a little more than 1/60, so the births run ahead of the steps by 5.2e-8 of a step each.
            PuffBirths.AgeAt(1, start, 60, Dt).Should().BeInRange(0, 4e-6 * Dt, "step {0}", step);
        }
    }

    [Fact]
    public void TheBirthPhaseCarriesTheShareOfAPuffIntoTheNextStep()
    {
        // At 45 puffs per second a step gathers 0.75 of a puff, so the births come every 4/3 of a step.
        const float rate = 45;
        double phase = 0;
        var births = new List<double>();
        for (int step = 1; step <= 8; step++)
        {
            double start = phase;
            long count = PuffBirths.Count(start, rate, Dt, out phase);
            for (long j = 1; j <= count; j++)
                births.Add(step * (double)Dt - PuffBirths.AgeAt(j, start, rate, Dt));
            phase.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(1);
        }

        births.Should().HaveCount(6, "8 steps of 0.75 make 6 puffs");
        for (int i = 1; i < births.Count; i++)
            (births[i] - births[i - 1]).Should().BeApproximately(1.0 / rate, 1e-12, "the births lie evenly in time at the rate");
        births[0].Should().BeApproximately(1.0 / rate, 1e-12, "the first puff is born when the first whole puff has gathered");
    }

    [Fact]
    public void AStepMakesAtMostTheCapacityOfPuffsAndThePhaseGathersTheWholeRate()
    {
        // An entity table and a frame tree, with no World. An emitter at 615 puffs per second gathers 10.25 puffs per step.
        EntityTable table = TableSupport.NewTable();
        var frames = new FrameTree();
        Entity owner = table.SpawnLive();
        FrameHandle space = frames.Create(owner, FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f));
        Entity emitter = table.Reserve();
        var render = new RenderData { Emitter = new PuffEmitter(615, 10, 0, Hot) };
        table.Activate(emitter, new TransformRow { Frame = space, PhysicsHome = space, Pose = DPose.Identity }, render, [], [], null, FrameHandle.Null);
        table.CopyDirtyRows();
        var puffs = new PuffTable(table, frames, capacity: 4, reportError: _ => { });

        puffs.Step(tick: 0, Dt);
        puffs.AddNewborns(tick: 0, Dt);

        puffs.Count.Should().Be(4);
        puffs.BirthPhase(emitter).Should().BeApproximately(615 * (double)Dt - 10, 1e-12, "the phase gathers the whole rate, also the puffs that the cap skips");
        // The last newborns up to the capacity are made: puffs 7 to 10 of the step, the youngest.
        float[] ages = puffs.View.Rows.ToArray().Select(row => row.Age).ToArray();
        ages.Should().Equal(Enumerable.Range(7, 4).Select(j => (float)PuffBirths.AgeAt(j, 0, 615, Dt)));
        puffs.Looks.UsersOf(puffs.View.Rows[0].Look).Should().Be(4);
        puffs.AssertInvariants();

        // In the next step the newborns alone pass the capacity, so every survivor dies and the last 4 of 10 newborns live.
        double phase = puffs.BirthPhase(emitter);
        puffs.Step(tick: 1, Dt);
        puffs.AddNewborns(tick: 1, Dt);

        puffs.View.Rows.ToArray().Select(row => row.Age).Should().Equal(Enumerable.Range(7, 4).Select(j => (float)PuffBirths.AgeAt(j, phase, 615, Dt)));
        puffs.Looks.UsersOf(puffs.View.Rows[0].Look).Should().Be(4);
        puffs.AssertInvariants();
    }

    [Fact]
    public void ANewbornThatWouldOutliveItsLookInsideItsStepIsNotMade()
    {
        // 10 puffs per step, of a look that lives 4 ms, a quarter of a step: only the newborns born in its last 4 ms live.
        EntityTable table = TableSupport.NewTable();
        var frames = new FrameTree();
        Entity owner = table.SpawnLive();
        FrameHandle space = frames.Create(owner, FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f));
        Entity emitter = table.Reserve();
        var render = new RenderData { Emitter = new PuffEmitter(600, 10, 0, Hot with { Life = 0.004f }) };
        table.Activate(emitter, new TransformRow { Frame = space, PhysicsHome = space, Pose = DPose.Identity }, render, [], [], null, FrameHandle.Null);
        table.CopyDirtyRows();
        var puffs = new PuffTable(table, frames, capacity: 100, reportError: _ => { });

        puffs.Step(tick: 0, Dt);
        puffs.AddNewborns(tick: 0, Dt);

        puffs.View.Rows.ToArray().Select(row => row.Age).Should().HaveCount(3).And.AllSatisfy(age => age.Should().BeLessThan(0.004f));
        puffs.Looks.UsersOf(puffs.View.Rows[0].Look).Should().Be(3);
        puffs.AssertInvariants();
    }

    [Fact]
    public void TheFirstNewbornThatLivesIsTheOldestOneYoungerThanItsLook()
    {
        // 10 puffs per step from the phase 0: puff j of the step is (10 - j) tenths of a step old at its end.
        PuffBirths.FirstLiving(10, 0, 600, Dt, life: 2).Should().Be(1, "a look that lives longer than the step keeps every newborn");
        PuffBirths.FirstLiving(10, 0, 600, Dt, life: 0.004f).Should().Be(8, "a look of a quarter of a step keeps the newborns of its last quarter");
        PuffBirths.FirstLiving(10, 0, 600, Dt, life: (float)PuffBirths.AgeAt(8, 0, 600, Dt)).Should().Be(9, "a newborn exactly as old as its look's life does not live");
        PuffBirths.FirstLiving(0, 0, 600, Dt, life: 2).Should().Be(1, "a step without newborns has none that live");
    }

    [Fact]
    public void TheCapSkipsOnlyNewbornsThatWouldLive()
    {
        // Capacity 4. Emitter b, first in slot order, makes 2 newborns of a long look. Emitter a, second, makes 10 newborns
        // of a look that lives a quarter of a step, so only its last 3 live. Of the 5 newborns that live, the cap keeps the
        // last 4: one of b's and the 3 of a's.
        EntityTable table = TableSupport.NewTable();
        var frames = new FrameTree();
        Entity owner = table.SpawnLive();
        FrameHandle space = frames.Create(owner, FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f));
        PuffLook brief = Hot with { Life = 0.004f };
        Entity b = table.Reserve();
        table.Activate(b, new TransformRow { Frame = space, PhysicsHome = space, Pose = DPose.Identity },
            new RenderData { Emitter = new PuffEmitter(120, 10, 0, Hot) }, [], [], null, FrameHandle.Null);
        Entity a = table.Reserve();
        table.Activate(a, new TransformRow { Frame = space, PhysicsHome = space, Pose = DPose.Identity },
            new RenderData { Emitter = new PuffEmitter(600, 10, 0, brief) }, [], [], null, FrameHandle.Null);
        table.CopyDirtyRows();
        var puffs = new PuffTable(table, frames, capacity: 4, reportError: _ => { });

        puffs.Step(tick: 0, Dt);
        puffs.AddNewborns(tick: 0, Dt);

        puffs.Count.Should().Be(4, "the cap skips only newborns that would live, so it fills the capacity");
        float[] ages = puffs.View.Rows.ToArray().Select(row => row.Age).ToArray();
        ages.Should().Equal(new[] { PuffBirths.AgeAt(2, 0, 120, Dt), PuffBirths.AgeAt(8, 0, 600, Dt), PuffBirths.AgeAt(9, 0, 600, Dt),
            PuffBirths.AgeAt(10, 0, 600, Dt) }.Select(age => (float)age));
        puffs.AssertInvariants();
    }

    [Fact]
    public void ANewbornInVacuumLiesAheadOfItsEmitterByItsEjectionVelocityTimesItsAge()
    {
        LooseBodyStep step = StepOf(GravityField.Uniform(Down));
        // The emitter points its -Z along +X.
        var pose = new DPose(new Double3(100, 20, -5), DQuaternion.FromAxisAngle(Double3.UnitY, -Math.PI / 2));
        Double3 velocity = new(7, 0, 3);
        var emitter = new PuffEmitter(60, 30, 0, Hot);
        const double age = 0.004;

        Puff newborn = PuffBirths.Newborn(in step, in pose, velocity, in emitter, new Double3(0, 0, -1), age, look: 2, Dt);

        Double3 ejection = pose.Rotation.Rotate(new Double3(0, 0, -30));
        ejection.X.Should().BeApproximately(30, 1e-12);
        (newborn.Place - (pose.Position + ejection * age)).Length.Should().BeLessThan(1e-12);
        newborn.Age.Should().Be((float)age);
        newborn.Look.Should().Be(2);
    }

    [Fact]
    public void NewbornsInAMediumLieAlongTheEmittersPathInsteadOfInBeads()
    {
        // A drag of 2 · 3 per second. An emitter that ejects nothing: its newborns would all pile at its place at the end of
        // the step if they rode with it to the end, and would form a bead per step.
        LooseBodyStep step = StepOf(medium: MediumField.Uniform(1.225f, 2));
        var pose = new DPose(new Double3(0, 0, 0), DQuaternion.Identity);
        Double3 velocity = new(60, 0, 0);
        var emitter = new PuffEmitter(600, 0, 0, Hot);

        double[] ages = [0.1 * Dt, 0.4 * Dt, 0.7 * Dt, Dt];
        Double3[] places = ages.Select(age => PuffBirths.Newborn(in step, in pose, velocity, in emitter, new Double3(0, 0, -1), age, 0, Dt).Place).ToArray();

        for (int i = 0; i < ages.Length; i++)
        {
            places[i].Y.Should().Be(0);
            places[i].Z.Should().Be(0);
            // Each lies between the emitter's place at its birth and its place now, held back by the drag over its age.
            places[i].X.Should().BeInRange(-velocity.X * ages[i], 0);
            places[i].X.Should().BeApproximately(-velocity.X * ages[i] + ages[i] * velocity.X / (1 + 6 * ages[i]), 1e-12);
        }
        for (int i = 1; i < ages.Length; i++)
            places[i].X.Should().BeLessThan(places[i - 1].X, "an older newborn lies further back along the path");
    }

    [Fact]
    public void ANewbornsChordIsTheChordBehindItsInstantVelocity()
    {
        var medium = MediumField.Uniform(1.225f, 2);
        LooseBodyStep turning = StepOf(GravityField.Uniform(new Vector3(0, -2, 0)), medium: medium, turning: true);
        LooseBodyStep still = StepOf(GravityField.Uniform(Down), medium: medium);
        var pose = new DPose(new Double3(60, 3, -8), DQuaternion.Identity);
        Double3 velocity = new(1, 2, -3);
        var emitter = new PuffEmitter(60, 20, 0, Hot);
        const double age = 0.01;

        foreach (LooseBodyStep step in new[] { still, turning })
        {
            Puff newborn = PuffBirths.Newborn(in step, in pose, velocity, in emitter, new Double3(0, 0, -1), age, 0, Dt);

            // Its instant velocity at the end of the step, after one implicit step of the drag 2 · 3 over its age.
            Double3 instant = (velocity + new Double3(0, 0, -20)) / (1 + 6 * age);
            Double3 linear = step.LinearGravity(newborn.Place);
            Double3 acceleration = linear - step.EffectiveGravity(newborn.Place, linear) * 3 - instant * 6;
            // The inverse of the chord rule gives the instant velocity less half a step of the puff's acceleration.
            Double3 back = step.Turns.Turns ? step.Turns.Step.InstantAtEnd(newborn.Place, newborn.Chord, Dt) : newborn.Chord;
            (back - (instant - acceleration * (0.5 * Dt))).Length.Should().BeLessThan(1e-9, "turning: {0}", step.Turns.Turns);
        }
    }

    [Fact]
    public void TheSeedOfAPuffDependsOnlyOnItsEmitterItsTickAndItsIndex()
    {
        var emitter = new Entity(17, 3);
        (double First, double Second) seed = PuffBirths.Seed(emitter, 1200, 4);

        PuffBirths.Seed(new Entity(17, 3), 1200, 4).Should().Be(seed, "the same three integers give the same numbers");
        var others = new[]
        {
            PuffBirths.Seed(new Entity(18, 3), 1200, 4),
            PuffBirths.Seed(new Entity(17, 4), 1200, 4),
            PuffBirths.Seed(emitter, 1201, 4),
            PuffBirths.Seed(emitter, 1200, 5),
        };
        foreach ((double First, double Second) other in others)
        {
            other.First.Should().NotBe(seed.First);
            other.Second.Should().NotBe(seed.Second);
        }
        for (int j = 1; j <= 1000; j++)
        {
            (double first, double second) = PuffBirths.Seed(emitter, j * 7, j);
            first.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(1);
            second.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(1);
        }
    }

    [Fact]
    public void EjectionDirectionsStayInsideTheConeOfTheSpread()
    {
        const float spread = 0.3f;
        var minusZ = new Double3(0, 0, -1);
        double widest = 0;
        for (int j = 1; j <= 2000; j++)
        {
            (double first, double second) = PuffBirths.Seed(new Entity(5, 1), 0, j);
            Double3 direction = PuffBirths.Direction(spread, first, second);
            direction.Length.Should().BeApproximately(1, 1e-12);
            double angle = Math.Acos(Math.Clamp(Double3.Dot(direction, minusZ), -1, 1));
            angle.Should().BeLessThanOrEqualTo(spread + 1e-6);
            widest = Math.Max(widest, angle);
        }
        widest.Should().BeGreaterThan(0.29, "the directions fill the cone out to its edge");
        PuffBirths.Direction(spread, 0, 0.3).Should().Be(minusZ, "the first number 0 gives the cone's axis");
        Math.Acos(Double3.Dot(PuffBirths.Direction(spread, 1, 0.3), minusZ)).Should().BeApproximately(spread, 1e-6, "the first number 1 gives its edge");
        PuffBirths.Direction(0, 0.7, 0.3).Should().Be(minusZ, "no spread ejects along -Z");
        PuffBirths.Direction(MathF.PI, 1, 0.3).Z.Should().BeApproximately(1, 1e-6, "a spread of π reaches +Z");
    }

    [Fact]
    public void AnEmitterWithNumbersThatAreNotValidMakesNoPuffs()
    {
        var valid = new PuffEmitter(60, 30, 0.2f, Hot);
        valid.CanMakePuffs.Should().BeTrue();
        (valid with { Speed = 0 }).CanMakePuffs.Should().BeTrue("a puff can leave at rest relative to its emitter");
        (valid with { Spread = MathF.PI }).CanMakePuffs.Should().BeTrue();
        (valid with { Spread = 0 }).CanMakePuffs.Should().BeTrue();

        PuffEmitter[] invalid =
        [
            valid with { Rate = 0 }, valid with { Rate = -1 }, valid with { Rate = float.NaN }, valid with { Rate = float.PositiveInfinity },
            valid with { Speed = -0.5f }, valid with { Speed = float.NaN }, valid with { Speed = float.PositiveInfinity },
            valid with { Spread = -0.1f }, valid with { Spread = MathF.PI + 1e-6f }, valid with { Spread = float.NaN },
            valid with { Look = Hot with { Life = 0 } }, valid with { Look = Hot with { TemperatureRatio = float.NaN } },
        ];
        foreach (PuffEmitter emitter in invalid)
            emitter.CanMakePuffs.Should().BeFalse("{0}", emitter);
    }

    [Fact]
    public void AnEmitterWithAnEmptyLookMakesNoPuffs()
    {
        new PuffEmitter(60, 30, 0.2f, default).CanMakePuffs.Should().BeFalse();
        new PuffEmitter(60, 30, 0.2f, Hot with { Shading = default }).CanMakePuffs.Should().BeFalse();
        default(PuffEmitter).CanMakePuffs.Should().BeFalse("the default emitter makes no puffs");
        new RenderData().Emitter.CanMakePuffs.Should().BeFalse();
    }
}
