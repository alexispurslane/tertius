using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Benchmarks;

/// <summary>
/// The scene that the render benchmarks share: a headless renderer at 1280 × 720 with one frame in flight, without
/// validation and impostors, so that every frame draws the same, and a gray ground and 100 boxes from 10 m to 200 m
/// ahead, lit by the sun.
/// </summary>
internal static class GroundAndBoxesScene
{
    /// <summary>The headless renderer of the render benchmarks.</summary>
    public static VulkanRenderer Renderer() =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = false,
                Width = 1280,
                Height = 720,
                FramesInFlight = 1,
                Impostors = false,
            }
        );

    /// <summary>
    /// The snapshot of the ground and the boxes, with the sun and the ambient light. The boxes take their places and
    /// turns from <paramref name="random"/>, which the benchmark goes on drawing from for what it adds.
    /// </summary>
    public static RenderSnapshot Snapshot(VulkanRenderer renderer, Random random)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId gray = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.55f, y: 0.55f, z: 0.55f, w: 1), roughness: 0.55f)
        );
        var snapshot = new RenderSnapshot
        {
            Camera = new()
            {
                Rotation = Quaternion.Identity,
                PreviousRotation = Quaternion.Identity,
                VerticalFov = MathF.PI / 3,
            },
            Lights = { BenchmarkLights.Star(direction: new(0.4f, 0.8f, 0.5f), irradiance: 3, chainDepth: 0) },
            Ambient = new()
            {
                SkyColor = new(0.09f, 0.11f, 0.15f),
                GroundColor = new(0.05f),
                Intensity = 1,
            },
        };
        var up = HemisphereUp.Along(Vector3.UnitY);
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: plane,
                material: gray,
                position: new(0, -2, -200),
                rotation: Quaternion.Identity,
                scale: new(250, 1, 250),
                boundingRadius: 0,
                hemisphere: up,
                chainDepth: 0,
                flags: RenderFlags.Visible | RenderFlags.CastsShadow
            )
        );
        AddBoxes(snapshot: snapshot, box: box, gray: gray, up: up, random: random);
        return snapshot;
    }

    /// <summary>Renders the first frames, which upload the meshes and the materials and grow the buffers.</summary>
    public static void WarmUp(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        for (int i = 0; i < 4; i++)
            renderer.Render(snapshot, 1f);
    }

    /// <summary>Adds the 100 boxes, from 10 m to 200 m ahead, each turned about its vertical axis.</summary>
    private static void AddBoxes(RenderSnapshot snapshot, MeshId box, MaterialId gray, HemisphereUp up, Random random)
    {
        for (int i = 0; i < 100; i++)
        {
            float depth = 10 + 190 * random.NextSingle();
            var position = new Vector3((random.NextSingle() * 2 - 1) * depth, -1, -depth);
            snapshot.Instances.Add(
                RenderInstance.Create(
                    mesh: box,
                    material: gray,
                    position: position,
                    rotation: Quaternion.CreateFromYawPitchRoll(random.NextSingle() * 3, 0, 0),
                    scale: new(2),
                    boundingRadius: 0,
                    hemisphere: up,
                    chainDepth: 0,
                    flags: RenderFlags.Visible | RenderFlags.CastsShadow,
                    entityIndex: (uint)i + 1
                )
            );
        }
    }
}
