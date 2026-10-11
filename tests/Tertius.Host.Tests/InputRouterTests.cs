using System.Numerics;
using Silk.NET.Input;
using Tertius.Host.Windowing;

namespace Tertius.Host.Tests;

/// <summary>InputRouter: where each input event of the window goes.</summary>
public class InputRouterTests
{
    /// <summary>Records what the router asks of the window, in order.</summary>
    private sealed class Window : IInputTarget
    {
        public List<InputEvent> Posted { get; } = new();
        public List<(FlyKey Key, bool Down)> FlyKeys { get; } = new();
        public List<Vector2> Turns { get; } = new();
        public List<bool> FlyChanges { get; } = new();
        public int Closes { get; private set; }

        public void Post(InputEvent e) => Posted.Add(e);
        public void Fly(FlyKey key, bool down) => FlyKeys.Add((key, down));
        public void Turn(Vector2 movement) => Turns.Add(movement);
        public void FlyChanged(bool flying) => FlyChanges.Add(flying);
        public void Close() => Closes++;
    }

    private static (InputKind Kind, int Code) Of(InputEvent e) => (e.Kind, e.Code);

    [Fact]
    public void EscapeClosesTheWindow()
    {
        var window = new Window();

        new InputRouter(freePlay: true, window).Key(Key.Escape, down: true);

        window.Closes.Should().Be(1);
        window.Posted.Should().BeEmpty();
    }

    [Fact]
    public void F1SwitchesTheFlyCameraOnAndOffAndNeverReachesTheGame()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: true, window);

        router.Key(Key.F1, down: true);
        router.Key(Key.F1, down: false);
        router.Key(Key.F1, down: true);
        router.Key(Key.F1, down: false);

        window.FlyChanges.Should().Equal(true, false);
        window.Posted.Should().BeEmpty();
    }

    [Fact]
    public void InFreePlayAKeyReachesTheGameAsADownAndAnUp()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: true, window);

        router.Key(Key.W, down: true);
        router.Key(Key.W, down: false);

        window.Posted.Select(Of).Should().Equal((InputKind.KeyDown, (int)Key.W), (InputKind.KeyUp, (int)Key.W));
    }

    [Fact]
    public void InFreePlayTheMouseReachesTheGameAsMovementsButtonsAndScrolls()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: true, window);

        router.MouseMove(new Vector2(10, 10));
        router.MouseMove(new Vector2(13, 6));
        router.MouseButton(MouseButton.Left, down: true);
        router.MouseButton(MouseButton.Left, down: false);
        router.Scroll(new Vector2(0, -2));

        window.Posted.Select(e => (e.Kind, e.Code, e.Delta)).Should().Equal(
            (InputKind.MouseMove, 0, Vector2.Zero),
            (InputKind.MouseMove, 0, new Vector2(3, -4)),
            (InputKind.MouseDown, (int)MouseButton.Left, Vector2.Zero),
            (InputKind.MouseUp, (int)MouseButton.Left, Vector2.Zero),
            (InputKind.Scroll, 0, new Vector2(0, -2)));
    }

    [Fact]
    public void DuringAScriptNoInputReachesTheGame()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: false, window);

        router.Key(Key.W, down: true);
        router.Key(Key.W, down: false);
        router.MouseMove(new Vector2(10, 10));
        router.MouseButton(MouseButton.Left, down: true);
        router.Scroll(new Vector2(0, 1));

        window.Posted.Should().BeEmpty();
    }

    [Fact]
    public void DuringAScriptTheFlyCameraStillWorks()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: false, window);

        router.Key(Key.F1, down: true);
        router.Key(Key.W, down: true);

        window.FlyChanges.Should().Equal(true);
        window.FlyKeys.Should().Equal((FlyKey.Forward, true));
    }

    [Fact]
    public void WhileFlyingTheMovementKeysAndTheMouseDriveTheFlyCameraAndNothingReachesTheGame()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: true, window);
        router.Key(Key.F1, down: true);

        router.Key(Key.W, down: true);
        router.Key(Key.ShiftLeft, down: true);
        router.MouseMove(new Vector2(100, 100));
        router.MouseMove(new Vector2(110, 95));
        router.Key(Key.X, down: true);
        router.MouseButton(MouseButton.Left, down: true);

        window.FlyKeys.Should().Equal((FlyKey.Forward, true), (FlyKey.Fast, true));
        window.Turns.Should().Equal(Vector2.Zero, new Vector2(10, -5));
        window.Posted.Should().BeEmpty();
    }

    [Fact]
    public void SwitchingToTheFlyCameraReleasesEveryKeyAndButtonThatTheGameHolds()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: true, window);
        router.Key(Key.W, down: true);
        router.MouseButton(MouseButton.Right, down: true);
        window.Posted.Clear();

        router.Key(Key.F1, down: true);

        window.Posted.Select(Of).Should().BeEquivalentTo([(InputKind.KeyUp, (int)Key.W), (InputKind.MouseUp, (int)MouseButton.Right)]);
    }

    [Fact]
    public void AReleaseWhoseDownTheGameNeverSawDoesNotReachTheGame()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: true, window);
        router.Key(Key.F1, down: true);
        // W goes down to the fly camera, then the fly camera switches off while W is held.
        router.Key(Key.W, down: true);
        router.Key(Key.F1, down: true);

        router.Key(Key.W, down: false);
        router.MouseButton(MouseButton.Left, down: false);

        window.Posted.Should().BeEmpty();
    }

    [Fact]
    public void TheFirstMouseMovementAfterTheFlyCameraSwitchesOffMovesNothing()
    {
        var window = new Window();
        var router = new InputRouter(freePlay: true, window);
        router.MouseMove(new Vector2(10, 10));
        router.Key(Key.F1, down: true);
        // While flying, the window hides the cursor and reports positions far from the last one.
        router.MouseMove(new Vector2(800, 10));
        router.Key(Key.F1, down: true);
        window.Posted.Clear();

        router.MouseMove(new Vector2(12, 10));

        window.Posted.Should().ContainSingle().Which.Delta.Should().Be(Vector2.Zero);
    }
}
