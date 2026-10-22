using System.Numerics;

namespace Tertius.Host.Windowing;

/// <summary>A key that the debug fly camera reads.</summary>
public enum FlyKey
{
    Forward,
    Back,
    Left,
    Right,
    Down,
    Up,
    Fast
}

/// <summary>
/// The debug fly camera of the window. It starts at the world's camera and moves relative to it, in the axes of that
/// camera's frame. The render thread puts it into the render snapshot, so it changes nothing in the world.
/// </summary>
public sealed class FlyCamera
{
    /// <summary>Meters per second without <see cref="FlyKey.Fast"/>.</summary>
    public const float Speed = 5;

    /// <summary>How much faster <see cref="FlyKey.Fast"/> moves.</summary>
    public const float FastFactor = 10;

    /// <summary>Radians of turn per pixel of mouse movement.</summary>
    public const float TurnPerPixel = 0.0025f;

    // The view never reaches straight up or down, where the yaw axis and the view would line up.
    private const float MaxPitch = MathF.PI / 2 - 0.01f;

    private readonly bool[] held = new bool[Enum.GetValues<FlyKey>().Length];
    private Quaternion start = Quaternion.Identity;
    private float yaw, pitch;

    /// <summary>The position relative to the world's camera, in meters, in the axes of the camera's frame.</summary>
    public Vector3 Position { get; private set; }

    /// <summary>
    /// The rotation in the axes of the world camera's frame: the start rotation, pitched about its own +X, then turned by
    /// the yaw about the frame's +Y.
    /// </summary>
    public Quaternion Rotation =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * start *
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);

    /// <summary>The id of the world camera's frame when the camera started, as <see cref="RenderSnapshot.OriginFrameId"/>
    /// gives it.</summary>
    public long Frame { get; private set; }

    /// <summary>
    /// Restarts the camera at the world's camera if the world camera is in another frame than at the start. The camera's
    /// numbers are in the axes of the old frame, so keeping them would turn the view when the camera entity changes frame.
    /// </summary>
    public void Follow(Quaternion worldCameraRotation, long frame)
    {
        if (frame != Frame)
            Start(worldCameraRotation, frame);
    }

    /// <summary>Puts the camera at the world's camera in <paramref name="frame"/>, with its rotation, and lets go of every key.</summary>
    public void Start(Quaternion worldCameraRotation, long frame = 0)
    {
        start = worldCameraRotation;
        Frame = frame;
        yaw = pitch = 0;
        Position = Vector3.Zero;
        Array.Clear(held);
    }

    public void SetKey(FlyKey key, bool down) => held[(int)key] = down;

    /// <summary>Turns by a mouse movement in pixels: right turns right, down looks down.</summary>
    public void Turn(Vector2 mouseDelta)
    {
        yaw -= mouseDelta.X * TurnPerPixel;
        pitch = Math.Clamp(pitch - mouseDelta.Y * TurnPerPixel, -MaxPitch, MaxPitch);
    }

    /// <summary>Moves for <paramref name="seconds"/> by the keys that are down: along the view, to its side, or along the frame's +Y.</summary>
    public void Advance(float seconds)
    {
        Quaternion rotation = Rotation;
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, rotation);
        Vector3 move = Vector3.Zero;
        if (held[(int)FlyKey.Forward]) move += forward;
        if (held[(int)FlyKey.Back]) move -= forward;
        if (held[(int)FlyKey.Right]) move += right;
        if (held[(int)FlyKey.Left]) move -= right;
        if (held[(int)FlyKey.Up]) move += Vector3.UnitY;
        if (held[(int)FlyKey.Down]) move -= Vector3.UnitY;
        float speed = held[(int)FlyKey.Fast] ? Speed * FastFactor : Speed;
        Position += move * speed * seconds;
    }

    /// <summary>Puts the camera into a render camera, as both its present and its previous pose, so that nothing interpolates.</summary>
    public void Apply(ref RenderCamera camera)
    {
        camera.Position = camera.PreviousPosition = Position;
        camera.Rotation = camera.PreviousRotation = Rotation;
    }
}
