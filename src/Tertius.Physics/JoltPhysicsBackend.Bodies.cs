using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>A primary body's state as Jolt has it, in its frame. Velocities are at the center of mass.</summary>
    private readonly record struct BodySnapshot(DPose Pose, Double3 CenterOfMass, Vector3 Linear, Vector3 Angular);

    private static BodySnapshot ReadPrimary(BodyRecord record)
    {
        if (record.Character is { } character)
        {
            Double3 position = character.RPosition.ToDouble3();
            return new BodySnapshot(new DPose(position, character.Rotation.ToDouble()), position, character.LinearVelocity, Vector3.Zero);
        }
        BodyInterface bodies = record.World!.Bodies;
        bodies.GetRPosition(record.Body, out RVector3 origin);
        bodies.GetRCenterOfMassPosition(record.Body, out RVector3 centerOfMass);
        bodies.GetLinearAndAngularVelocity(record.Body, out Vector3 linear, out Vector3 angular);
        Quaternion rotation = bodies.GetRotation(record.Body);
        return new BodySnapshot(new DPose(origin.ToDouble3(), rotation.ToDouble()), centerOfMass.ToDouble3(), linear, angular);
    }

    /// <summary>The body origin of the primary body, in its frame.</summary>
    private static Double3 PrimaryPosition(BodyRecord record)
    {
        if (record.Character is { } character)
            return character.RPosition.ToDouble3();
        record.World!.Bodies.GetRPosition(record.Body, out RVector3 origin);
        return origin.ToDouble3();
    }

    private static MotionType ToMotionType(BodyMotion motion) => motion switch
    {
        BodyMotion.Static => MotionType.Static,
        BodyMotion.Kinematic => MotionType.Kinematic,
        _ => MotionType.Dynamic,
    };

    /// <summary>
    /// The object layer of an ordinary body. A static body on the Moving layer (the <see cref="BodyDesc"/> default)
    /// goes on the Static layer instead: it collides with the same layers that matter, and Jolt keeps static bodies
    /// out of the moving broad-phase tree.
    /// </summary>
    private static ObjectLayer LayerFor(BodyDesc desc) =>
        desc.Motion == BodyMotion.Static && desc.Layer == BodyLayer.Moving ? Layers.Static : (uint)desc.Layer;

    // ---- Creation ----

    private void CreateRecord(in BodyCreate create)
    {
        Entity entity = create.Entity;
        if (entity.IsNull)
        {
            log("Physics: ignored a BodyCreate for the null entity.");
            return;
        }
        BodyDesc desc = create.Desc ?? throw new ArgumentException($"The BodyCreate for {entity} has no body description.");
        if (desc.Shape is null)
            throw new ArgumentException($"The body description of {entity} has no shape.");

        var record = new BodyRecord { Entity = entity, Desc = desc };
        bool ownsFrame = frames.TryGetFrame(entity, out FrameHandle owned);
        if (ownsFrame)
        {
            if (desc.Motion == BodyMotion.Character)
                throw new ArgumentException($"{entity} owns {owned}, so its body cannot be a character.");
            record.OwnedFrame = owned;
            record.Motion = frames.Driver(owned) switch
            {
                MotionDriver.Physics => BodyMotion.Dynamic,
                MotionDriver.Kepler => BodyMotion.Kinematic,
                _ => BodyMotion.Static,
            };
            record.Layer = Layers.FrameHull;
        }
        else
        {
            if (create.Frame.IsNull || !frames.IsAlive(create.Frame))
            {
                log($"Physics: {entity} was not created because its frame {create.Frame} is not alive.");
                return;
            }
            if (desc.Motion == BodyMotion.Character && desc.Shape is not CapsuleShapeDesc)
                throw new ArgumentException($"{entity} is a character, so its shape must be a CapsuleShapeDesc, not {desc.Shape.GetType().Name}.");
            record.Motion = desc.Motion;
            record.Layer = LayerFor(desc);
        }

        // Validate and build every native shape before any state changes, so that a bad description throws cleanly.
        record.HasMesh = ShapeCache.ContainsMesh(desc.Shape);
        if (record.Motion != BodyMotion.Static && record.HasMesh)
            throw new ArgumentException(
                $"{entity} uses a mesh shape on a {(ownsFrame ? "frame hull that moves" : record.Motion.ToString().ToLowerInvariant() + " body")}. " +
                "Mesh shapes are only allowed on static bodies and as frame interiors.");
        if (record.Motion == BodyMotion.Dynamic && !(desc.Mass > 0))
            throw new ArgumentException($"{entity} is dynamic, so its mass must be positive, not {desc.Mass}.");
        shapes.Get(desc.Shape);
        if (ownsFrame)
            shapes.Get(desc.InteriorShape ?? desc.Shape);
        record.Radius = BoundingRadius(desc.Shape);

        if (Find(entity) is not null)
        {
            log($"Physics: {entity} already had a body. The old body is replaced.");
            DestroyRecord(entity);
        }
        else if (entity.Index < (uint)records.Length && records[entity.Index] is { } stale)
        {
            log($"Physics: {stale.Entity} still had a body when its slot was reused by {entity}. The old body is removed.");
            DestroyRecord(stale.Entity);
        }
        Store(record);

        if (ownsFrame)
        {
            frameOwners.Add(record);
            // The tree is the authority on a frame's pose. A root frame has no parent, so it has no hull.
            FrameHandle parent = ParentOf(owned);
            if (!parent.IsNull)
                CreateHullFromTree(record, parent);
            CreateInterior(record);
        }
        else
        {
            record.Up = Vector3.Transform(Vector3.UnitY, create.Pose.Rotation.ToJolt());
            CreatePrimary(record, create.Frame, create.Pose, create.LinearVelocity, create.AngularVelocity);
        }
    }

    private void Store(BodyRecord record)
    {
        uint index = record.Entity.Index;
        if (index >= (uint)records.Length)
            Array.Resize(ref records, (int)Math.Max(index + 1, (uint)records.Length * 2));
        records[index] = record;
    }

    /// <summary>Creates the primary body or character of a record in the system of <paramref name="frame"/>.</summary>
    private void CreatePrimary(BodyRecord record, FrameHandle frame, in DPose pose, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        FrameWorld world = WorldFor(frame);
        if (record.Motion == BodyMotion.Character)
        {
            record.Character = CreateCharacter(record, world, pose, linearVelocity);
        }
        else
        {
            record.Body = CreateRigid(world, record.Desc.Shape, ToMotionType(record.Motion), record.Layer, pose,
                linearVelocity, angularVelocity, BodyUserData.Encode(BodyKind.Primary, record.Entity), record.Desc);
        }
        record.Frame = frame;
        record.World = world;
        world.AddMember(record);
    }

    /// <summary>
    /// Creates a frame's hull at the frame's pose and velocity in the tree. The tree holds the velocity of the frame
    /// origin; Jolt wants the velocity of the center of mass, which differs for a spinning hull whose center of
    /// mass is off the origin.
    /// </summary>
    private void CreateHullFromTree(BodyRecord record, FrameHandle parent)
    {
        FrameHandle frame = record.OwnedFrame;
        DPose pose = frames.LocalPose(frame);
        Vector3 angular = frames.AngularVelocity(frame);
        Double3 centerOffset = pose.TransformDirection(shapes.Get(record.Desc.Shape).CenterOfMass);
        Double3 linear = frames.LinearVelocity(frame) + Double3.Cross(angular, centerOffset);
        CreatePrimary(record, parent, pose, linear.ToVector3(), angular);
    }

    private void CreateInterior(BodyRecord record)
    {
        FrameWorld world = WorldFor(record.OwnedFrame);
        record.InteriorBody = CreateRigid(world, record.Desc.InteriorShape ?? record.Desc.Shape, MotionType.Static, Layers.Static,
            DPose.Identity, Vector3.Zero, Vector3.Zero, BodyUserData.Encode(BodyKind.Interior, record.Entity), record.Desc);
        record.InteriorWorld = world;
    }

    private BodyID CreateRigid(
        FrameWorld world,
        ShapeDesc shapeDesc,
        MotionType motion,
        ObjectLayer layer,
        in DPose pose,
        Vector3 linearVelocity,
        Vector3 angularVelocity,
        ulong userData,
        BodyDesc material)
    {
        Shape shape = shapes.Get(shapeDesc);
        using var settings = new BodyCreationSettings(shape, pose.Position.ToJolt(), pose.Rotation.ToJolt(), motion, layer);
        settings.UserData = userData;
        settings.Friction = material.Friction;
        settings.Restitution = material.Restitution;
        settings.LinearDamping = material.LinearDamping;
        settings.AngularDamping = material.AngularDamping;
        settings.MaxLinearVelocity = options.MaxLinearVelocity;
        if (motion == MotionType.Dynamic)
        {
            settings.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
            settings.MassPropertiesOverride = new MassProperties { Mass = material.Mass };
            // Continuous collision only costs anything when the body moves far in one step.
            settings.MotionQuality = MotionQuality.LinearCast;
        }
        if (motion != MotionType.Static)
        {
            settings.LinearVelocity = linearVelocity;
            settings.AngularVelocity = angularVelocity;
        }
        BodyID id = world.Bodies.CreateAndAddBody(settings, motion == MotionType.Static ? Activation.DontActivate : Activation.Activate);
        if (id.IsInvalid)
            throw new InvalidOperationException(
                $"Jolt could not create a body in {world.Frame}: the system already holds its maximum of {options.MaxBodiesPerFrame} bodies.");
        return id;
    }

    private CharacterVirtual CreateCharacter(BodyRecord record, FrameWorld world, in DPose pose, Vector3 linearVelocity)
    {
        var capsule = (CapsuleShapeDesc)record.Desc.Shape;
        Shape shape = shapes.Get(capsule);

        Vector3 gravity = frames.EffectiveGravity(world.Frame, pose.Position);
        float strength = gravity.Length();
        Vector3 up = strength >= options.ZeroGravityThreshold ? -gravity / strength : Vector3.Normalize(record.Up);
        record.Up = up;

        var settings = new CharacterVirtualSettings
        {
            Shape = shape,
            // The inner body lets rigid bodies collide with the character and lets it report contacts.
            InnerBodyShape = shape,
            InnerBodyLayer = Layers.Moving,
            Up = up,
            MaxSlopeAngle = options.CharacterMaxSlopeDegrees * (MathF.PI / 180f),
            Mass = record.Desc.Mass,
            MaxStrength = options.CharacterMaxStrength,
            // Contacts below the center of the lower hemisphere support the character.
            SupportingVolume = new Plane(Vector3.UnitY, -capsule.Radius),
        };
        Quaternion rotation = Rotations.AlignUp(pose.Rotation.ToJolt(), up);
        var character = new CharacterVirtual(settings, pose.Position.ToJolt(), rotation,
            BodyUserData.Encode(BodyKind.CharacterInner, record.Entity), world.System);
        character.LinearVelocity = linearVelocity;
        world.CharacterCount++;
        return character;
    }

    // ---- Removal ----

    private void DestroyRecord(Entity entity)
    {
        if (Find(entity) is not { } record)
            return;
        RemoveGhosts(record);
        RemovePrimary(record);
        RemoveInterior(record);
        if (record.IsHull)
        {
            frameOwners.Remove(record);
            // Without its hull, only the tree knows where the frame is, so a pending hand-off of it no longer holds.
            if (parentOverrides.TryGetValue(record.OwnedFrame.Index, out var link) && link.Frame == record.OwnedFrame)
                parentOverrides.Remove(record.OwnedFrame.Index);
        }
        records[entity.Index] = null;
    }

    private static void RemovePrimary(BodyRecord record)
    {
        if (record.World is not { } world)
            return;
        if (record.Character is { } character)
        {
            world.ForgetContacts(character.InnerBodyID);
            character.Dispose();
            record.Character = null;
            world.CharacterCount--;
        }
        else if (record.Body.IsValid)
        {
            world.ForgetContacts(record.Body);
            world.Bodies.RemoveAndDestroyBody(record.Body);
        }
        record.Body = BodyID.Invalid;
        world.RemoveMember(record);
        record.World = null;
        record.Frame = FrameHandle.Null;
    }

    private static void RemoveInterior(BodyRecord record)
    {
        if (record.InteriorWorld is not { } world)
            return;
        world.ForgetContacts(record.InteriorBody);
        world.Bodies.RemoveAndDestroyBody(record.InteriorBody);
        record.InteriorWorld = null;
        record.InteriorBody = BodyID.Invalid;
    }

    private static void RemoveGhosts(BodyRecord record)
    {
        if (record.Ghosts is not { } ghosts)
            return;
        foreach (GhostBody ghost in ghosts)
            ghost.World.Bodies.RemoveAndDestroyBody(ghost.Body);
        ghosts.Clear();
    }

    private static void RemoveGhostIn(BodyRecord record, FrameWorld world)
    {
        if (record.Ghosts is not { } ghosts)
            return;
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            if (ghosts[i].World != world)
                continue;
            world.Bodies.RemoveAndDestroyBody(ghosts[i].Body);
            ghosts.RemoveAt(i);
        }
    }

    // ---- Moving frame hulls after the tree changed ----

    /// <summary>
    /// Puts a frame hull where the tree says, after the tree moved the frame on its own: the simulation reparented
    /// it, or the frame's parent was destroyed. The hull takes the tree's pose and velocity. A frame that became a
    /// root loses its hull.
    /// </summary>
    private void PlaceHullFromTree(BodyRecord owner, FrameHandle parent)
    {
        if (!parent.IsNull && ExistingWorld(parent) is { } target)
            RemoveGhostIn(owner, target);
        RemovePrimary(owner);
        if (!parent.IsNull)
            CreateHullFromTree(owner, parent);
    }
}
