"""Research tool (issue #42): applies the probe of #22 (its scenes, scripts and volume readback) and adds the two lamp
scenes of #42 to the Demo, plus their scripts in captures/issue42/. Run it from the checkout's root. The callers
(render-variant.sh) restore the checkout on exit.

The two lamp scenes keep the layout of the #22 probe: a gray receiver panel (reflectance 0.65) 3 m ahead, and a wall
0.48 m wide and 1.45 m tall 1.9 m ahead, where the red and the white slab stand in the probe. One star lights the wall's
face that looks at the receiver. The star's light does not reach the receiver's front, so the receiver gets only the
wall's bounce light.
- restraint-orange-lamp: an orange star of color (1, 0.4, 0) on a neutral wall of reflectance (0.8, 0.8, 0.8).
- restraint-warm-white: a warm-white star of color (1, 0.9, 0.8) on a beige wall of reflectance (0.7, 0.6, 0.5).
- restraint-faint-beige: the warm-white star on a fainter beige wall of reflectance (0.62, 0.6, 0.56), closer to hull paint.
- restraint-two-lamps: a white star (1, 1, 1) and the orange star (1, 0.4, 0) at the same place on the gray wall
  (0.8, 0.8, 0.8). Each star has the strength of the orange scene's star, so restraint-orange-lamp is the orange lamp alone.
- restraint-second-bounce: the crossing scene for the second bounce. The gray receiver of the probe; a red floor slab
  (0.9, 0.05, 0.05) at y = -1.2 m, from x = 1 to 3 m and z = -2.8 to -1.9 m, lit from above by the white star; and a
  gray ceiling slab (0.65) at y = 0.4 m, from x = 1 to 3 m and z = -2.6 to -1.2 m, that faces down. The star does not
  reach the ceiling's underside or the receiver's front, and the ceiling does not shade the red floor. So the ceiling
  is lit only by the red floor's wash, and its own bounce, the second bounce, reaches the receiver. Both slabs span
  x = 2 to 2.25 m, where the 0.25 m grid's edge moves when the camera crosses x = 0.125 m. The camera path is the
  probe's sweep.
The lamp scenes also have the camera "wall-back", which looks straight at the wall's face that the star does not light.
"""
import json
import subprocess
import sys
from pathlib import Path

tools = Path(__file__).resolve().parent
subprocess.run([sys.executable, "-I", str(tools.parent.parent / "fine-grid-outgoing-light/tools/prepare-render.py")],
               check=True)
folder = Path("captures/issue42")
folder.mkdir(parents=True, exist_ok=True)
prefix = Path("src/Tertius.Demo")
(prefix / "DemoScenes.Issue42Probe.cs").write_text("""using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    private static SceneInstance RestraintOrangeLamp(World world, IRenderResources resources) =>
        RestraintLampWall(world: world, resources: resources, lamp: new(1, 0.4f, 0), wall: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1));

    private static SceneInstance RestraintWarmWhite(World world, IRenderResources resources) =>
        RestraintLampWall(world: world, resources: resources, lamp: new(1, 0.9f, 0.8f), wall: new(x: 0.7f, y: 0.6f, z: 0.5f, w: 1));

    private static SceneInstance RestraintFaintBeige(World world, IRenderResources resources) =>
        RestraintLampWall(world: world, resources: resources, lamp: new(1, 0.9f, 0.8f), wall: new(x: 0.62f, y: 0.6f, z: 0.56f, w: 1));

    private static SceneInstance RestraintTwoLamps(World world, IRenderResources resources) =>
        RestraintLampWall(
            world: world, resources: resources, lamp: new(1, 0.4f, 0), wall: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1),
            secondLamp: Vector3.One
        );

    private static SceneInstance RestraintSecondBounce(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(100)));
        scene.Add("room", room.Entity);
        BounceSlab(
            world: world, resources: resources, scene: scene, frame: room, name: "receiver",
            center: new(0.65, 0, -3), size: new(4.3f, 3.2f, 0.08f), color: new(x: 0.65f, y: 0.65f, z: 0.65f, w: 1)
        );
        BounceSlab(
            world: world, resources: resources, scene: scene, frame: room, name: "red-floor",
            center: new(2, -1.2, -2.35), size: new(2, 0.04f, 0.9f), color: new(x: 0.9f, y: 0.05f, z: 0.05f, w: 1)
        );
        BounceSlab(
            world: world, resources: resources, scene: scene, frame: room, name: "gray-ceiling",
            center: new(2, 0.4, -1.9), size: new(2, 0.04f, 1.4f), color: new(x: 0.65f, y: 0.65f, z: 0.65f, w: 1)
        );
        Star(world: world, scene: scene, frame: room, center: Double3.Zero, direction: new(0, 1, -0.5));
        world.Ambient = new() { Intensity = 0 };
        AddCamera(world: world, scene: scene, frame: room, pose: LookingAt(new(0.05, 0, 0), new(0.8, -0.25, -3)));
        for (int i = 0; i <= 96; i++)
            scene.Add($"sweep-{i}", world.Spawn(ViewIn(room, LookingAt(new(0.0025 * i, 0, 0), new(0.8, -0.25, -3)))));
        return scene;
    }

    private static SceneInstance RestraintLampWall(
        World world, IRenderResources resources, Vector3 lamp, Vector4 wall, Vector3 secondLamp = default
    )
    {
        var scene = new SceneInstance();
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(100)));
        scene.Add("room", room.Entity);
        BounceSlab(
            world: world, resources: resources, scene: scene, frame: room, name: "receiver",
            center: new(0.65, 0, -3), size: new(4.3f, 3.2f, 0.08f), color: new(x: 0.65f, y: 0.65f, z: 0.65f, w: 1)
        );
        BounceSlab(
            world: world, resources: resources, scene: scene, frame: room, name: "wall",
            center: new(2.25, -0.85, -1.1), size: new(0.48f, 1.45f, 0.04f), color: wall
        );
        scene.Add(
            "sun",
            world.Spawn(StarAt(frame: room, place: new Double3(0, 1, -0.5).Normalized() * AstronomicalUnit, color: lamp))
        );
        if (secondLamp != Vector3.Zero)
            scene.Add(
                "second-sun",
                world.Spawn(
                    StarAt(frame: room, place: new Double3(0, 1, -0.5).Normalized() * AstronomicalUnit, color: secondLamp)
                )
            );
        world.Ambient = new() { Intensity = 0 };
        AddCamera(world: world, scene: scene, frame: room, pose: LookingAt(new(0.05, 0, 0), new(0.8, -0.25, -3)));
        scene.Add("across", world.Spawn(ViewIn(room, LookingAt(new(0.2, 0, 0), new(0.8, -0.25, -3)))));
        scene.AddView("across");
        // Looks straight at the wall's face that the star does not light, from 1.5 m on the camera's side. Light of
        // the lamp on that face crossed the wall (issue #47).
        scene.Add("wall-back", world.Spawn(ViewIn(room, LookingAt(new(2.25, -0.85, 0.4), new(2.25, -0.85, -1.1)))));
        scene.AddView("wall-back");
        return scene;
    }
}
""")
table = prefix / "DemoScenes.cs"
text = table.read_text()
line = '            new("volume-boundary-red", VolumeBoundaryRed),'
assert text.count(line) == 1
table.write_text(text.replace(line, line + '\n            new("restraint-orange-lamp", RestraintOrangeLamp),'
                                       '\n            new("restraint-warm-white", RestraintWarmWhite),'
                                       '\n            new("restraint-faint-beige", RestraintFaintBeige),'
                                       '\n            new("restraint-two-lamps", RestraintTwoLamps),'
                                       '\n            new("restraint-second-bounce", RestraintSecondBounce),'))

# The deck cameras of set 1b: three views beside the warm gray glow of the station's floor, which lies from about 5 m
# behind the deck camera and curves up. The offsets are from the deck camera's eye in the station's frame (+X up).
exits = Path("src/Tertius.Demo/DemoScenes.StationExit.cs")
text = exits.read_text()
line = '            scene.Add("deck-camera", world.Spawn(ViewIn(station, DeckCamera)));\n'
assert text.count(line) == 1
exits.write_text(text.replace(line, line + """            foreach (
                (string id, Double3 eye, Double3 target) in new[]
                {
                    ("glow-back", new Double3(-1.0, 0, 3.0), new Double3(-2.0, 0, 9.0)),
                    ("glow-up", new Double3(-1.6, 0, 5.5), new Double3(3.5, 0, 3.0)),
                    ("glow-along", new Double3(-1.0, 0, 5.5), new Double3(-1.5, 8, 6.0)),
                }
            )
            {
                Double3 at = DeckCamera.Position;
                scene.Add(id, world.Spawn(ViewIn(station, Camera(at + eye, at + target, Double3.UnitX))));
                scene.AddView(id);
            }
"""))
deck = json.loads((tools / "deck-still.json").read_text())
for view in ("glow-back", "glow-up", "glow-along"):
    steps = [dict(step, entity=view) if step.get("entity") == "deck-camera" else step for step in deck["steps"]]
    (folder / f"{view}.json").write_text(json.dumps(dict(deck, steps=steps), indent=1) + "\n")

crossing = json.loads(Path("captures/issue22/crossing.json").read_text())
(folder / "crossing-second.json").write_text(json.dumps(dict(crossing, scene="restraint-second-bounce"), indent=1) + "\n")
(folder / "second-outside.json").write_text(json.dumps(
    {"scene": "restraint-second-bounce", "window": {"width": 640, "height": 360},
     "steps": [{"do": "camera", "entity": "camera"}, {"do": "capture", "name": "second-outside"}]}, indent=2) + "\n")

for scene, name in (("restraint-orange-lamp", "orange"), ("restraint-warm-white", "warm"),
                    ("restraint-faint-beige", "faint"), ("restraint-two-lamps", "two")):
    for camera, pose in (("camera", "outside"), ("across", "across"), ("wall-back", "wallback")):
        script = {"scene": scene, "window": {"width": 640, "height": 360},
                  "steps": [{"do": "camera", "entity": camera}, {"do": "capture", "name": f"{name}-{pose}"}]}
        (folder / f"{name}-{pose}.json").write_text(json.dumps(script, indent=2) + "\n")
