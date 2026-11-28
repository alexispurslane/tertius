import json
from pathlib import Path

folder = Path("captures/issue22")
folder.mkdir(parents=True, exist_ok=True)
prefix = Path("src/Tertius.Demo")
(prefix / "DemoScenes.Issue22Probe.cs").write_text("""using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    private static SceneInstance VolumeBoundary(World world, IRenderResources resources) =>
        VolumeBoundary(world: world, resources: resources, withRed: true, withWhite: true);

    private static SceneInstance VolumeBoundaryShell(World world, IRenderResources resources) =>
        VolumeBoundary(world: world, resources: resources, withRed: false, withWhite: true);

    private static SceneInstance VolumeBoundaryRed(World world, IRenderResources resources) =>
        VolumeBoundary(world: world, resources: resources, withRed: true, withWhite: false);

    private static SceneInstance VolumeBoundary(World world, IRenderResources resources, bool withRed, bool withWhite)
    {
        var scene = new SceneInstance();
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(100)));
        scene.Add("room", room.Entity);
        BounceSlab(
            world: world, resources: resources, scene: scene, frame: room, name: "receiver",
            center: new(0.65, 0, -3), size: new(4.3f, 3.2f, 0.08f), color: new(x: 0.65f, y: 0.65f, z: 0.65f, w: 1)
        );
        if (withRed)
            BounceSlab(
                world: world, resources: resources, scene: scene, frame: room, name: "red-reflector",
                center: new(2.125, -0.85, -1.1), size: new(0.23f, 1.45f, 0.04f),
                color: new(x: 1, y: 0, z: 0, w: 1)
            );
        if (withWhite)
            BounceSlab(
            world: world, resources: resources, scene: scene, frame: room, name: "white-reflector",
            center: new(2.375, -0.85, -1.1), size: new(0.23f, 1.45f, 0.04f), color: Vector4.One
        );
        Star(world: world, scene: scene, frame: room, center: Double3.Zero, direction: new(0, 1, -0.5));
        world.Ambient = new() { Intensity = 0 };
        AddCamera(world: world, scene: scene, frame: room, pose: LookingAt(new(0.05, 0, 0), new(0.8, -0.25, -3)));
        scene.Add("across", world.Spawn(ViewIn(room, LookingAt(new(0.2, 0, 0), new(0.8, -0.25, -3)))));
        scene.AddView("across");
        // The crossing film: 97 fixed cameras from x = 0 to 0.24 m. Only the 0.25 m grid shifts on the way, at x = 0.125 m.
        for (int i = 0; i <= 96; i++)
            scene.Add($"sweep-{i}", world.Spawn(ViewIn(room, LookingAt(new(0.0025 * i, 0, 0), new(0.8, -0.25, -3)))));
        return scene;
    }
}
""")
table = prefix / "DemoScenes.cs"
text = table.read_text()
line = "            .. BounceLightScenes,"
assert text.count(line) == 1
table.write_text(text.replace(line, line + '\n            new("volume-boundary", VolumeBoundary),\n            new("volume-boundary-shell", VolumeBoundaryShell),\n            new("volume-boundary-red", VolumeBoundaryRed),'))
renderer = Path("src/Tertius.Rendering/VulkanRenderer.cs")
text = renderer.read_text()
before = """            if (slotCaptures[frame.Index] != null)
                FinishSlot(frame);
        }
    }

    public void Render"""
after = """            if (slotCaptures[frame.Index] != null)
                FinishSlot(frame);
        }
        if (
            Environment.GetEnvironmentVariable("TERTIUS_ISSUE22_VOLUME_DUMP") is { Length: > 0 } dump
            && Graph.Passes.OfType<LightVolumePass>().SingleOrDefault() is { } volume
        )
        {
            File.WriteAllBytes(dump, MemoryMarshal.AsBytes(ReadBufferWords(volume.Buffer).AsSpan()));
            if (Environment.GetEnvironmentVariable("TERTIUS_ISSUE22_PIXEL_DUMP") == "1")
            {
                File.WriteAllBytes(dump + ".hdr", MemoryMarshal.AsBytes(ReadHdr().AsSpan()));
                if (screenTrace != null)
                    File.WriteAllBytes(dump + ".depth", MemoryMarshal.AsBytes(ReadDepthFinest().AsSpan()));
                CameraView view = LastScene.Camera;
                System.Numerics.Vector3 right = view.Right * view.TanHalfX;
                System.Numerics.Vector3 up = view.Up * view.TanHalfY;
                float[] camera =
                [
                    view.Width, view.Height, view.Forward.X, view.Forward.Y, view.Forward.Z,
                    right.X, right.Y, right.Z, up.X, up.Y, up.Z,
                    LastScene.Constants.RenderParams.Y, LastScene.Constants.RenderParams.Z
                ];
                File.WriteAllBytes(dump + ".camera", MemoryMarshal.AsBytes(camera.AsSpan()));
            }
        }
    }

    public void Render"""
assert text.count(before) == 1
renderer.write_text(text.replace(before, after))

for scene, camera, name in (("volume-boundary", "camera", "outside"), ("volume-boundary", "across", "across"),
                            ("volume-boundary-shell", "across", "shell-only"),
                            ("volume-boundary-red", "camera", "red-outside"), ("volume-boundary-red", "across", "red-across")):
    script = {"scene":scene, "window":{"width":640, "height":360},
              "steps":[{"do":"camera", "entity":camera}, {"do":"capture", "name":name}]}
    (folder / (name+".json")).write_text(json.dumps(script, indent=2)+"\n")

# Out from x = 0 to 0.24 m and back, two frames per camera: 386 frames, 6.4 s at 60 frames per second.
order = list(range(97)) + list(range(96, -1, -1))
steps = []
for n, i in enumerate(order):
    steps += [{"do":"camera", "entity":f"sweep-{i}"}, {"do":"film", "name":f"step-{n}", "frames":2, "every":1}]
(folder / "crossing.json").write_text(json.dumps({"scene":"volume-boundary", "window":{"width":640, "height":360},
                                                  "steps":steps}, indent=1)+"\n")
