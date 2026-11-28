"""Research tool (issue #42): applies the probe of #22 (its scenes, scripts and volume readback) and adds the two lamp
scenes of #42 to the Demo, plus their scripts in captures/issue42/. Run it from the checkout's root. The callers
(render-variant.sh) restore the checkout on exit.

The two lamp scenes keep the layout of the #22 probe: a gray receiver panel (reflectance 0.65) 3 m ahead, and a wall
0.48 m wide and 1.45 m tall 1.9 m ahead, where the red and the white slab stand in the probe. One star lights the wall's
face that looks at the receiver. The star's light does not reach the receiver's front, so the receiver gets only the
wall's bounce light.
- restraint-orange-lamp: an orange star of color (1, 0.4, 0) on a neutral wall of reflectance (0.8, 0.8, 0.8).
- restraint-warm-white: a warm-white star of color (1, 0.9, 0.8) on a beige wall of reflectance (0.7, 0.6, 0.5).
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

    private static SceneInstance RestraintLampWall(World world, IRenderResources resources, Vector3 lamp, Vector4 wall)
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
        world.Ambient = new() { Intensity = 0 };
        AddCamera(world: world, scene: scene, frame: room, pose: LookingAt(new(0.05, 0, 0), new(0.8, -0.25, -3)));
        scene.Add("across", world.Spawn(ViewIn(room, LookingAt(new(0.2, 0, 0), new(0.8, -0.25, -3)))));
        scene.AddView("across");
        return scene;
    }
}
""")
table = prefix / "DemoScenes.cs"
text = table.read_text()
line = '            new("volume-boundary-red", VolumeBoundaryRed),'
assert text.count(line) == 1
table.write_text(text.replace(line, line + '\n            new("restraint-orange-lamp", RestraintOrangeLamp),'
                                       '\n            new("restraint-warm-white", RestraintWarmWhite),'))

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

for scene, name in (("restraint-orange-lamp", "orange"), ("restraint-warm-white", "warm")):
    for camera, pose in (("camera", "outside"), ("across", "across")):
        script = {"scene": scene, "window": {"width": 640, "height": 360},
                  "steps": [{"do": "camera", "entity": camera}, {"do": "capture", "name": f"{name}-{pose}"}]}
        (folder / f"{name}-{pose}.json").write_text(json.dumps(script, indent=2) + "\n")
