using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;

namespace Tertius.Host.Tests;

/// <summary>
/// The drawn person of the demo's scenes <c>demo-surface</c> and <c>demo-exit</c> (docs/plans/blue-danube-demo.md, "The
/// person's walk"): the person's own row draws nothing, and an attached body with a visor on its front carries the look.
/// The person's view turns toward its walk, and the body follows the view and bobs with each step. The body lies inside
/// the box that the cameras and the film checks take for the person, <see cref="DemoSurface.PersonHalf"/>, at every
/// heading and every lift.
/// </summary>
public sealed class DrawnPersonTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();

    public void Dispose() => world.Dispose();

    private SceneInstance Build(string name) => DemoScenes.All.Single(s => s.Name == name).Build(world, RenderResources.None);

    /// <summary>
    /// In each scene the person's row has no mesh and is not visible, and the body and its visor are visible and cast
    /// shadows. After a tick, the body stands at the person's center, 2 cm lower, and the visor at the eye's height on the
    /// body's front.
    /// </summary>
    [Theory]
    [InlineData(BlueDanube.SceneName)]
    [InlineData(BlueDanube.ExitSceneName)]
    public void ThePersonIsDrawnByAnAttachedBodyWithAVisorOnItsFront(string name)
    {
        SceneInstance scene = Build(name);
        Entity person = scene["person"], body = scene["person-body"], visor = scene["person-visor"];

        world.Tick();

        RenderData row = world.GetRender(person);
        row.Mesh.Should().Be(MeshId.None);
        (row.Flags & RenderFlags.Visible).Should().Be(RenderFlags.None);
        world.GetRender(body).Flags.Should().Be(RenderFlags.Visible | RenderFlags.CastsShadow);
        world.GetRender(visor).Flags.Should().Be(RenderFlags.Visible | RenderFlags.CastsShadow);
        FrameHandle frame = world.Snapshot.FrameOf(person);
        DPose of = world.PoseIn(person, frame);
        DPose drawn = world.PoseIn(body, frame);
        Double3 bodyAt = of.Rotation.Conjugate().Rotate(drawn.Position - of.Position);
        (bodyAt - (Double3)DemoSurface.BodyCenter).Length.Should().BeLessThan(1e-3);
        Double3 visorAt = drawn.Rotation.Conjugate().Rotate(world.PoseIn(visor, frame).Position - drawn.Position);
        (visorAt - (Double3)DemoSurface.VisorCenter).Length.Should().BeLessThan(1e-3);
        (DemoSurface.BodyCenter + DemoSurface.VisorCenter).Y.Should().BeApproximately(DemoSurface.Eye.Y, 0.01f, "the visor is at the eye's height");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The body has a front that a viewer sees: it is wider than it is deep, so a quarter turn changes its outline, and the
    /// visor stands out of its front face, -Z.
    /// </summary>
    [Fact]
    public void TheBodyIsWiderThanDeepAndTheVisorStandsOutOfItsFront()
    {
        DemoSurface.BodyHalf.X.Should().BeGreaterThan(1.5f * DemoSurface.BodyHalf.Z);
        (DemoSurface.VisorCenter.Z + DemoSurface.VisorHalf.Z).Should().BeApproximately(-DemoSurface.BodyHalf.Z, 1e-6f, "the visor's back lies on the body's front");
    }

    /// <summary>
    /// Every corner of the body and of the visor, turned by any heading about the person's up and lifted by
    /// <see cref="Gait.StepRise"/> or not at all, lies inside <see cref="DemoSurface.PersonHalf"/>. So the cameras that keep
    /// that box in the picture keep the drawn person in it.
    /// </summary>
    [Fact]
    public void TheDrawnBodyLiesInsideThePersonsBoxAtEveryHeadingAndLift()
    {
        Vector3 half = DemoSurface.PersonHalf;
        var corners = new List<Vector3>();
        for (int corner = 0; corner < 8; corner++)
        {
            var sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
            corners.Add(DemoSurface.BodyCenter + sign * DemoSurface.BodyHalf);
            corners.Add(DemoSurface.BodyCenter + DemoSurface.VisorCenter + sign * DemoSurface.VisorHalf);
        }
        for (int degrees = 0; degrees < 360; degrees += 5)
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180);
            foreach (float lift in new[] { 0f, (float)Gait.StepRise })
            {
                foreach (Vector3 corner in corners)
                {
                    Vector3 at = Vector3.Transform(corner, turn) + new Vector3(0, lift, 0);
                    MathF.Sqrt(at.X * at.X + at.Z * at.Z).Should().BeLessThanOrEqualTo(half.X, $"at {degrees} degrees");
                    at.Y.Should().BeInRange(-half.Y - 1e-6f, half.Y + 1e-6f, $"at {degrees} degrees and a lift of {lift} m");
                }
            }
        }
    }

    /// <summary>
    /// In the scene <c>demo-surface</c> the person starts facing its first waypoint, along -X, toward the terminal's
    /// entrance, so the walk starts without a turn.
    /// </summary>
    [Fact]
    public void ThePersonStartsFacingTheTerminal()
    {
        SceneInstance scene = Build(BlueDanube.SceneName);

        Vector3 facing = Vector3.Transform(-Vector3.UnitZ, world.GetRender(scene["person"]).ViewOffset.Rotation);

        Vector3.Distance(facing, -Vector3.UnitX).Should().BeLessThan(1e-5f);
    }
}
