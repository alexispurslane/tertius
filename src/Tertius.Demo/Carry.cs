using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Lets a person carry one crate and throw it. (User) A <see cref="CrateOffer"/> that comes while the person's hands are
/// free is taken: Carry tells the crate that it is taken, so that the crate goes, and it spawns a drawn copy of the crate,
/// with no body, attached to the person. <see cref="HeldInView"/> keeps the copy in front of the eye, low and to the
/// right, turned with the view. While the hands are full, Carry ignores offers, and the crate that offered stays. Carry
/// also ignores an offer whose crate is gone. A crate that gets a <see cref="Use"/> and a <see cref="Destroy"/> in one
/// tick still sends its offer, and the offer arrives after the crate has died.
/// </summary>
/// <remarks>
/// <para>The throw is charged. (User) While the person carries a crate and its view is on, holding the left mouse button
/// charges the throw from 0 to <see cref="MaxThrowSpeed"/> in <see cref="ChargeSeconds"/>, and the charge stops there.
/// On the release, Carry spawns a new crate in the person's frame, with a new <see cref="Crate"/> component. It starts
/// on the view, as near to the eye as it can without its bounding sphere touching the person's capsule, and turned with
/// the view, as the drawn copy was. It moves at the person's velocity plus the view's direction times the charged speed.
/// The person feels the recoil: an impulse of minus the crate's mass times that throw velocity. Carry tells the drawn
/// copy to go, and the hands are free again. A click, with the press and the release in one tick, puts the crate down in
/// front of the person, at the person's own velocity.</para>
/// <para>Input counts only while the person's view is on, as for <see cref="PersonControl"/>. When the view goes off,
/// the charge is dropped, and the person keeps the crate. A press while the person carries nothing charges nothing.</para>
/// <para>Carry aims along the view offset of its entity's render row, which <see cref="PersonControl"/> sets in each
/// tick. Carry runs after PersonControl, so it aims along the view of this tick. The person's pose and velocity come
/// from the snapshot of the previous tick, and physics moves the person on in this tick's step, before the new crate
/// becomes live at the end of the tick. So the crate starts from where the person's velocity takes it in one tick.</para>
/// <para>Nothing checks the place where a thrown crate starts, because physics has no shape casts. A person that looks
/// at its feet, or stands against a wall, can start a crate inside the floor or the wall.</para>
/// </remarks>
/// <param name="body">
/// The person's capsule, about its entity's +Y and centered on its origin. A thrown crate starts clear of it. It must not
/// be null, or the constructor throws <see cref="ArgumentNullException"/>.
/// </param>
public sealed class Carry(CapsuleShapeDesc body) : IComponent
{
    /// <summary>The speed of a full charge, in meters per second, relative to the person.</summary>
    public const float MaxThrowSpeed = 12;

    /// <summary>How long the left mouse button must be held for a full charge, in seconds.</summary>
    public const float ChargeSeconds = 1;

    /// <summary>
    /// The gap between a thrown crate's bounding sphere and the person's capsule where the crate starts, in meters.
    /// </summary>
    public const float ThrowGap = 0.05f;

    /// <summary>
    /// Where the drawn copy of the carried crate sits, from the eye, in meters, in the view's axes: to the right, low and
    /// ahead. In a view 60 degrees high, a crate 0.4 m on a side stays in the bottom 30 percent of the image there, clear
    /// of the middle, where the view aims.
    /// </summary>
    public static readonly Vector3 Hold = new(0.35f, -0.5f, -0.9f);

    private readonly CapsuleShapeDesc body = body ?? throw new ArgumentNullException(nameof(body));

    // The crate that the person carries, and its drawn copy. The hands are free while the copy is null.
    private CrateSpec carried;

    private Entity copy;

    // Whether the left mouse button charges a throw, and the tick that handled its press.
    private bool charging;
    private long chargeStart;

    // After PersonControl, which sets the view offset that the throw aims along.
    public int Priority => 1;

    // Tick runs it on every tick, so that it drops the charge as soon as the view goes off.
    public EventMask Wants => EventMask.Of<CrateOffer>().And<InputEvent>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        bool viewOn = self.Snapshot.Camera == self.Entity;
        if (!viewOn)
            charging = false;
        for (int i = 0; i < events.Count; i++)
        {
            ref Event e = ref events[i];
            if (e.Is<CrateOffer>())
            {
                // The crate can be gone by now. Then it cannot be taken, and a copy of it would be a second crate.
                if (copy.IsNull && self.Snapshot.IsAlive(e.Sender))
                    Take(self: self, outbox: outbox, crate: e.Sender, spec: e.As<CrateOffer>().Crate);
            }
            else if (viewOn && e.Is<InputEvent>())
            {
                // The left button charges a throw when it goes down with a crate in hand, and throws when it goes up.
                switch (e.As<InputEvent>())
                {
                    case MouseButtonChanged { Code: var button, IsDown: true }
                        when (MouseButton)button == MouseButton.Left && !copy.IsNull:
                        charging = true;
                        chargeStart = self.Tick;
                        break;
                    case MouseButtonChanged { Code: var button, IsDown: false }
                        when (MouseButton)button == MouseButton.Left && charging:
                        charging = false;
                        Throw(self, outbox);
                        break;
                    case MouseButtonChanged or KeyChanged or MouseMoved or Scrolled or null:
                        // Every other input does nothing.
                        break;
                    default:
                        throw new UnreachableException();
                }
            }
        }
    }

    private void Take(Self self, Outbox outbox, Entity crate, CrateSpec spec)
    {
        carried = spec;
        outbox.Send(crate, new CrateTaken());
        copy = self.Spawn(
            spec.HeldCopy(
                frame: self.Frame,
                holder: self.Entity,
                view: self.Render.ViewOffset,
                grip: new(Hold, Quaternion.Identity)
            )
        );
    }

    private void Throw(Self self, Outbox outbox)
    {
        // Stryker disable once Arithmetic : while ChargeSeconds is 1, dividing by it and multiplying by it give the same speed.
        float speed = MathF.Min(
            MaxThrowSpeed,
            MaxThrowSpeed * (self.Tick - chargeStart) * (float)self.DeltaTime / ChargeSeconds
        );
        // The view and its direction, in the person's axes.
        Pose view = self.Render.ViewOffset;
        Release(
            self: self,
            outbox: outbox,
            carried: carried,
            copy: copy,
            body: body,
            eye: view.Position,
            aim: Vector3.Transform(-Vector3.UnitZ, view.Rotation),
            turn: view.Rotation,
            speed: speed
        );
        copy = Entity.Null;
    }

    /// <summary>
    /// Where a thrown crate of <paramref name="carried"/> starts, in the thrower's axes: on the line from
    /// <paramref name="eye"/> along the unit vector <paramref name="aim"/>, as near to the eye as it can without its
    /// bounding sphere touching <paramref name="body"/>, the thrower's capsule, with a gap of <see cref="ThrowGap"/>.
    /// </summary>
    public static Vector3 ThrowStart(CapsuleShapeDesc body, CrateSpec carried, Vector3 eye, Vector3 aim) =>
        eye
        + aim
            * Clearance(
                eye: eye,
                aim: aim,
                halfHeight: body.HalfHeight,
                radius: body.Radius + carried.BoundingRadius + ThrowGap
            );

    /// <summary>
    /// Throws <paramref name="carried"/> from the hands of the thrower of <paramref name="self"/>, whose capsule is
    /// <paramref name="body"/>, along the unit vector <paramref name="aim"/> from <paramref name="eye"/>, at
    /// <paramref name="speed"/> m/s relative to the thrower, turned by <paramref name="turn"/>: all in the thrower's axes.
    /// It spawns a new crate in the thrower's frame at <see cref="ThrowStart"/>, with the components
    /// <paramref name="more"/>, which moves at the thrower's velocity plus the throw. The thrower feels the recoil, an
    /// impulse of minus the crate's mass times the throw's velocity, and the drawn copy <paramref name="copy"/> goes. The
    /// thrower's pose and velocity come from the snapshot of the previous tick, and physics moves the thrower on in this
    /// tick's step, before the new crate becomes live at the end of the tick. So the crate starts from where the
    /// thrower's velocity takes it in one tick. Returns the new crate.
    /// </summary>
    public static Entity Release(
        Self self,
        Outbox outbox,
        CrateSpec carried,
        Entity copy,
        CapsuleShapeDesc body,
        Vector3 eye,
        Vector3 aim,
        Quaternion turn,
        float speed,
        params IComponent[] more
    )
    {
        // The thrower's pose at the end of this tick's step, as far as its velocity tells.
        DPose pose = self.PoseIn(self.Frame);
        Double3 velocity = self.VelocityIn(self.Frame);
        DPose thrower = pose with { Position = pose.Position + velocity * self.DeltaTime };
        Vector3 throwVelocity = thrower.Rotation.Rotate(aim) * speed;
        var start = DPose.Compose(
            thrower,
            new Pose(ThrowStart(body: body, carried: carried, eye: eye, aim: aim), turn).ToDPose()
        );
        Entity crate = self.Spawn(carried.Spawn(self.Frame, start, velocity + throwVelocity, more));
        self.Body.AddImpulse(-carried.Mass * throwVelocity);
        outbox.Send(copy, new Destroy());
        return crate;
    }

    /// <summary>
    /// How far a point must go from <paramref name="eye"/> along the unit vector <paramref name="aim"/> to be
    /// <paramref name="radius"/> from the segment of the Y axis from -<paramref name="halfHeight"/> to
    /// <paramref name="halfHeight"/>, or 0 if the eye is that far already. The points within the radius of the segment
    /// form a capsule, which is convex, so every point farther along the aim is farther from the segment too.
    /// </summary>
    private static float Clearance(Vector3 eye, Vector3 aim, float halfHeight, float radius)
    {
        // Stryker disable once Equality : only an eye at exactly the clearance from the capsule reaches the boundary, and a view away from the capsule gets 0 there too.
        if (Vector3.Distance(eye, new(0, Math.Clamp(eye.Y, -halfHeight, halfHeight), 0)) >= radius)
            return 0;
        // The aim leaves the capsule through its round side, the cylinder about Y, if the point where it leaves the
        // cylinder lies between the ends of the segment.
        float across = aim.X * aim.X + aim.Z * aim.Z,
            end = MathF.Sign(aim.Y) * halfHeight;
        if (across > 0)
        {
            float b = eye.X * aim.X + eye.Z * aim.Z,
                c = eye.X * eye.X + eye.Z * eye.Z - radius * radius;
            float side = (-b + MathF.Sqrt(MathF.Max(0, b * b - across * c))) / across;
            float y = eye.Y + aim.Y * side;
            // Stryker disable once Equality : where the exit point lies exactly at the end of the cylinder, the round side and the rounded end give the same point.
            if (MathF.Abs(y) <= halfHeight)
                return side;
            end = MathF.Sign(y) * halfHeight;
        }

        // Otherwise it leaves through the rounded end on that side, a sphere about that end of the segment.
        Vector3 fromEnd = eye - new Vector3(0, end, 0);
        float along = Vector3.Dot(aim, fromEnd);
        return -along + MathF.Sqrt(MathF.Max(0, along * along - fromEnd.LengthSquared() + radius * radius));
    }
}
