using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The shader's search pinned to its C# twin (docs/plans/environment-map-distances.md, "The corrected read, on the GPU",
/// test 20): a test compute shader, Shaders/map_hit.comp, runs mapHit for a grid of rays over a drawn map, and
/// <see cref="MapSearch.Hit"/> searches a read-back copy of the same distance cube, which <see cref="CubeField"/> filters
/// as the sampler does.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class MapSearchShaderTests
{
    private const int FaceSize = 32;

    /// <summary>
    /// How far the shader's direction and reach may lie from the twin's: the design's 10⁻³ for a lobe whose reads stay at
    /// levels of 4 × 4 texels or finer. The roughest lobe reads the levels of 2 × 2 and 1 × 1 texels, where nearly every
    /// read takes texels across a face's edge or corner, which the device decides and <see cref="CubeField"/> approximates.
    /// There the two differ by up to 4.4 · 10⁻³.
    /// </summary>
    private static double Tolerance(float alpha) => alpha < 0.5f ? 1e-3 : 5e-3;

    /// <summary>The roughnesses squared of the rays: the floor's, a gloss, the demo's paint, and a rough lobe.</summary>
    private static readonly float[] Alphas = [0.002f, 0.04f, 0.1444f, 0.5f];

    [Fact]
    public void TheShadersSearchMatchesItsCSharpTwin()
    {
        MapRay[] floorRays = FloorRays(),
            wallBaseRays = WallBaseRays(),
            intoOwnSurfaceRays = IntoOwnSurfaceRays(),
            insideSlabRays = SlabEdgeRays(behind: InsideSlab),
            beyondSlabRays = SlabEdgeRays(behind: BeyondSlab);
        MapRay[][] groups = [floorRays, wallBaseRays, intoOwnSurfaceRays, insideSlabRays, beyondSlabRays];
        MapRay[] rays = [.. groups.SelectMany(group => group)];

        (MapRay[] shown, MapHit[] twins) = SearchedByBoth(rays);

        // The rays of a group whose twin's hit is not as expected, by their index in the buffer.
        string[] Unexpected(MapRay[] group, Func<MapRay, MapHit, bool> expected)
        {
            int start = groups.TakeWhile(other => !ReferenceEquals(other, group)).Sum(other => other.Length);
            return
            [
                .. group
                    .Select((ray, at) => (Ray: ray, Index: start + at))
                    .Where(entry => !expected(entry.Ray, twins[entry.Index]))
                    .Select(entry =>
                        $"ray {entry.Index} from {entry.Ray.PositionMap} along {entry.Ray.DirectionAlpha}"
                    ),
            ];
        }
        Unexpected(wallBaseRays, AtTheNearSample)
            .Should()
            .BeEmpty("decision 20's near entry finds every ray from a wall's base at the near sample's place");
        Unexpected(intoOwnSurfaceRays, (_, twin) => !twin.Found)
            .Should()
            .BeEmpty("a ray that points into its own surface takes no search, as SSR traces no such ray");
        Unexpected(insideSlabRays, AtTheNearSample)
            .Should()
            .BeEmpty(
                "a near sample within κ behind its surface, with the first even sample behind too, is a near entry"
            );
        Unexpected(beyondSlabRays, (_, twin) => !twin.Found)
            .Should()
            .BeEmpty("a near sample more than κ behind its surface is no near entry");
        Mismatches(shown, twins)
            .Should()
            .BeEmpty("the same found, and the direction and the reach within each lobe's tolerance");
        twins
            .Count(twin => twin.Found)
            .Should()
            .BeGreaterThan(rays.Length / 2, "most rays meet the room's walls, its box or its floor");
    }

    /// <summary>
    /// <paramref name="rays"/> as map_hit.comp searched them over the drawn map of <see cref="Room"/>, and each one's hit
    /// from <see cref="MapSearch.Hit"/> on a read-back copy of the same cube.
    /// </summary>
    private static (MapRay[] Shown, MapHit[] Twins) SearchedByBoth(MapRay[] rays)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 90,
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = FaceSize,
            }
        );
        try
        {
            var pass = new MapHitPass(renderer.Allocator, rays);
            renderer.Graph.Add(FrameGraph.Stage.DirectLight, pass);
            RenderSnapshot snapshot = Room(renderer);
            snapshot.Time += 1.0 / 60;
            renderer.Render(snapshot, 1f);
            snapshot.Time += 1.0 / 60;
            renderer.Render(snapshot, 1f);
            var field = CubeField.Read(renderer, map: 0);
            MapRay[] shown = pass.Rays();
            var twins = new MapHit[shown.Length];
            for (int index = 0; index < shown.Length; index++)
                twins[index] = MapSearch.Hit(
                    field: ref field,
                    position: shown[index].Position,
                    direction: shown[index].Direction,
                    geometricNormal: new(
                        shown[index].GeometricNormal.X,
                        shown[index].GeometricNormal.Y,
                        shown[index].GeometricNormal.Z
                    ),
                    alpha: shown[index].DirectionAlpha.W,
                    faceSize: FaceSize
                );
            return (Shown: shown, Twins: twins);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The rays whose shader hit differs from the twin's: in found, or in the direction or the reach by the lobe's
    /// <see cref="Tolerance"/> or more.
    /// </summary>
    private static string[] Mismatches(MapRay[] shown, MapHit[] twins) =>
        [
            .. shown
                .Select((ray, index) => (Ray: ray, Index: index, Twin: twins[index]))
                .Select(entry =>
                {
                    MapRay ray = entry.Ray;
                    var direction = new Double3(ray.HitReach.X, ray.HitReach.Y, ray.HitReach.Z);
                    double turn = (direction - entry.Twin.Direction).Length,
                        reach = Math.Abs(ray.HitReach.W - entry.Twin.Reach);
                    double tolerance = Tolerance(ray.DirectionAlpha.W);
                    bool differs = ray.Found.X == 1 != entry.Twin.Found || turn >= tolerance || reach >= tolerance;
                    return differs
                        ? $"ray {entry.Index} from {ray.PositionMap} along {ray.DirectionAlpha}: found {ray.Found.X} and {entry.Twin.Found}, direction off by {turn:G3}, reach {ray.HitReach.W:G5} and {entry.Twin.Reach:G5}"
                        : null;
                })
                .OfType<string>(),
        ];

    /// <summary>
    /// A room around the camera, whose map the rays search: a floor 1 m below the eye, a back wall 4 m ahead, a left wall
    /// 3 m aside, and a box of 1 m on the floor ahead and to the right. The right and the open top let rays reach the sky.
    /// </summary>
    private static RenderSnapshot Room(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        MaterialId black = BrdfTesting.Glow(renderer, Vector3.Zero);
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        (Vector3 Position, Quaternion Rotation, Vector3 Scale)[] walls =
        [
            (new(0, -1, 0), Quaternion.Identity, new(20, 1, 20)),
            (new(0, 1.5f, -4), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), new(6, 1, 2.5f)),
            (new(-3, 1.5f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2), new(2.5f, 1, 6)),
        ];
        foreach ((Vector3 position, Quaternion rotation, Vector3 scale) in walls)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: plane,
                    material: black,
                    position: position,
                    scale: scale,
                    rotation: rotation,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                material: black,
                position: new(1, -0.5f, -2),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        return snapshot;
    }

    /// <summary>
    /// Rays from a grid of floor points, two of them on the box's base edges: the mirror ray of the eye's view, and that
    /// ray turned toward the left wall, each at every α of <see cref="Alphas"/>.
    /// </summary>
    private static MapRay[] FloorRays()
    {
        var rays = new List<MapRay>();
        for (int x = -2; x <= 2; x++)
            foreach (float z in (float[])[-0.5f, -1, -1.5f, -2.5f, -3, -3.5f])
            {
                var point = new Vector3(x, -1, z);
                Vector3 mirror = Mirrored(point);
                foreach (Vector3 direction in (Vector3[])[mirror, Vector3.Normalize(mirror + new Vector3(-0.5f, 0, 0))])
                    rays.AddRange(RaysAtEveryAlpha(point: point, direction: direction, geometricNormal: Vector3.UnitY));
            }
        return [.. rays];
    }

    /// <summary>
    /// The mirror rays of floor points 2 cm from the base of the back wall or the left wall. Each meets its wall nearer than
    /// the near sample, so the near sample and the first even sample both lie behind the wall, and only decision 20's near
    /// entry finds a hit: every even sample lies behind the wall too.
    /// </summary>
    private static MapRay[] WallBaseRays()
    {
        Vector3[] points =
        [
            new(-2, -1, -3.98f),
            new(0, -1, -3.98f),
            new(-1, -1, -3.98f),
            new(-2.98f, -1, -1),
            new(-2.98f, -1, -2),
            new(-2.98f, -1, -3),
        ];
        return
        [
            .. points.SelectMany(point =>
                RaysAtEveryAlpha(point: point, direction: Mirrored(point), geometricNormal: Vector3.UnitY)
            ),
        ];
    }

    /// <summary>
    /// The rays of floor points whose shading normal a normal map tilts, so that the lobe's direction d points 3° below the
    /// floor: each points into its own geometric surface, whose normal is up.
    /// </summary>
    private static MapRay[] IntoOwnSurfaceRays()
    {
        Vector3[] points = [new(-1, -1, -1), new(0, -1, -2), new(-2, -1, -3), new(2, -1, -0.5f)];
        float below = MathF.Sin(3 * MathF.PI / 180);
        return
        [
            .. points.SelectMany(point =>
            {
                Vector3 mirror = Mirrored(point);
                var across = Vector3.Normalize(mirror with { Y = 0 });
                Vector3 down = MathF.Sqrt(1 - below * below) * across - below * Vector3.UnitY;
                return RaysAtEveryAlpha(point: point, direction: down, geometricNormal: Vector3.UnitY);
            }),
        ];
    }

    /// <summary>
    /// How far behind the back wall <see cref="SlabEdgeRays"/> start, as a share of the wall's distance: inside κ = 0.1 but
    /// beyond κ / 2, and beyond κ but inside 2 κ, so that a κ halved or doubled in only one language changes their result.
    /// </summary>
    private const float InsideSlab = 0.075f,
        BeyondSlab = 0.15f;

    /// <summary>
    /// Rays from a point straight ahead, the share <paramref name="behind"/> of the back wall's 4 m behind it, along the
    /// wall to the left and on behind the left wall. Every sample lies behind a wall, so only the near entry, which takes
    /// a near sample within κ behind its surface, gives them a hit.
    /// </summary>
    private static MapRay[] SlabEdgeRays(float behind)
    {
        var point = new Vector3(0, 0, -4 * (1 + behind));
        // The normal of a mirror there that reflects the eye's view ray into the ray: halfway between the ray and the eye.
        var facing = Vector3.Normalize(-Vector3.UnitX - Vector3.Normalize(point));
        return RaysAtEveryAlpha(point: point, direction: -Vector3.UnitX, geometricNormal: facing);
    }

    /// <summary>The mirror ray of the eye's view of a floor point.</summary>
    private static Vector3 Mirrored(Vector3 point)
    {
        var view = Vector3.Normalize(point);
        return view with { Y = -view.Y };
    }

    /// <summary>The ray from <paramref name="point"/> along <paramref name="direction"/>, in map 0, at each α of <see cref="Alphas"/>.</summary>
    private static MapRay[] RaysAtEveryAlpha(Vector3 point, Vector3 direction, Vector3 geometricNormal) =>
        [
            .. Alphas.Select(alpha => new MapRay(
                PositionMap: new(point, 0),
                DirectionAlpha: new(direction, alpha),
                GeometricNormal: new(geometricNormal, 0),
                HitReach: default,
                Found: default
            )),
        ];

    /// <summary>Whether <paramref name="twin"/> is the near sample's place on <paramref name="ray"/>, which only the near entry returns.</summary>
    private static bool AtTheNearSample(MapRay ray, MapHit twin)
    {
        Double3 toPoint = ray.Position / ray.Position.Length;
        Double3 nearLookup = (1 - MapSearch.NearSample) * toPoint + MapSearch.NearSample * ray.Direction;
        return twin.Found && (twin.Direction - nearLookup).Length < 1e-12;
    }

    /// <summary>
    /// One ray of map_hit.comp's buffer: its shaded point and map, its direction and α, its surface's geometric normal, and
    /// what mapHit found.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MapRay(
        Vector4 PositionMap,
        Vector4 DirectionAlpha,
        Vector4 GeometricNormal,
        Vector4 HitReach,
        Vector4 Found
    )
    {
        /// <summary>The shaded point x relative to the eye, in m.</summary>
        public Double3 Position => new(PositionMap.X, PositionMap.Y, PositionMap.Z);

        /// <summary>The unit direction d.</summary>
        public Double3 Direction => new(DirectionAlpha.X, DirectionAlpha.Y, DirectionAlpha.Z);
    }

    /// <summary>
    /// A read-back distance cube as the readers' sampler filters it: trilinear between levels, bilinear in each, with the
    /// texels beyond a face's edge taken from the face beside it, as a cube read takes them.
    /// </summary>
    /// <param name="levels">Each level's six faces, each row by row from the top.</param>
    private readonly struct CubeField(float[][][] levels, int faceSize) : IInverseDistanceField
    {
        public static CubeField Read(VulkanRenderer renderer, int map)
        {
            int count = (int)Math.Log2(FaceSize) + 1;
            float[][][] levels = new float[count][][];
            for (int level = 0; level < count; level++)
                levels[level] =
                [
                    .. Enum.GetValues<CubeFace>()
                        .Select(face =>
                            renderer
                                .ReadEnvironmentFace(
                                    map: map,
                                    face: face,
                                    level: level,
                                    image: EnvironmentImage.Distance
                                )
                                .Select(value => (float)value)
                                .ToArray()
                        ),
                ];
            return new(levels, FaceSize);
        }

        public double At(Double3 direction, double level)
        {
            var toward = new Vector3((float)direction.X, (float)direction.Y, (float)direction.Z);
            int lower = (int)Math.Floor(level);
            double share = level - lower;
            double near = Bilinear(lower, toward);
            return share == 0 ? near : near + share * (Bilinear(lower + 1, toward) - near);
        }

        private double Bilinear(int level, Vector3 direction)
        {
            int size = Math.Max(faceSize >> level, 1);
            (CubeFace face, float s, float t) = CubeFaces.Locate(direction);
            double u = s * size - 0.5,
                v = t * size - 0.5;
            int i = (int)Math.Floor(u),
                j = (int)Math.Floor(v);
            double a = u - i,
                b = v - j;
            float[][] faces = levels[level];
            return (1 - a) * (1 - b) * Texel(faces, face, (i, j))
                + a * (1 - b) * Texel(faces, face, (i + 1, j))
                + (1 - a) * b * Texel(faces, face, (i, j + 1))
                + a * b * Texel(faces, face, (i + 1, j + 1));
        }

        /// <summary>
        /// A texel of a face of one level's <paramref name="faces"/>, or for one beyond the face's edge, the texel of the
        /// face beside it at that edge.
        /// </summary>
        private static float Texel(float[][] faces, CubeFace face, (int I, int J) texel)
        {
            int size = (int)Math.Sqrt(faces[0].Length);
            if ((uint)texel.I < (uint)size && (uint)texel.J < (uint)size)
                return faces[(int)face][texel.J * size + texel.I];
            (CubeFace next, float nextS, float nextT) = CubeFaces.Locate(
                CubeFaces.Direction(face, Across(texel.I, size), Across(texel.J, size))
            );
            int column = Math.Clamp((int)(nextS * size), 0, size - 1),
                row = Math.Clamp((int)(nextT * size), 0, size - 1);
            return faces[(int)next][row * size + column];
        }

        /// <summary>The face coordinate, from 0 to 1, of a texel's center, or just beyond the edge for a texel beyond it.</summary>
        private static float Across(int texel, int size)
        {
            const float beyond = 1e-4f;
            if (texel < 0)
                return -beyond;
            if (texel >= size)
                return 1 + beyond;
            return (texel + 0.5f) / size;
        }
    }

    /// <summary>
    /// A compute pass after the prefilter that runs map_hit.comp over <paramref name="rays"/> in a buffer that the CPU
    /// reads, with the scene's sets 0 to 2 and the buffer in set 3.
    /// </summary>
    private sealed unsafe class MapHitPass(DeviceAllocator allocator, MapRay[] rays) : RenderPass
    {
        private VulkanDevice? device;
        private ShaderLibrary? shaders;
        private GpuBuffer? buffer;
        private DescriptorSetLayout setLayout;
        private PipelineLayout layout;
        private DescriptorPool pool;
        private DescriptorSet set;
        private Pipeline pipeline;

        public override string Name => "map hit test";

        /// <summary>The rays with what the shader found, once the device is idle.</summary>
        public MapRay[] Rays() => buffer!.AsSpan<MapRay>(rays.Length).ToArray();

        public override void Prepare(FrameContext context)
        {
            if (device is not null)
                return;
            device = context.Device;
            buffer = GpuBuffer.Create(
                device: device,
                allocator: allocator,
                size: (ulong)(rays.Length * sizeof(MapRay)),
                usage: BufferUsageFlags.StorageBufferBit,
                memoryUsage: MemoryUsage.Readback,
                name: "map hit rays"
            );
            buffer.Write(rays);
            CreateLayouts(device.Vk, context.Layouts);
            CreateSet(device.Vk, context.Layouts);
            shaders = new ShaderLibrary(device, typeof(MapHitPass).Assembly);
            pipeline = new ComputePipelineDesc
            {
                Name = Name,
                Shader = "map_hit.comp",
                Layout = layout,
            }.Create(device, shaders);
        }

        /// <summary>The set layout of the rays' buffer, and the pipeline layout of the scene's sets 0 to 2 and it.</summary>
        private void CreateLayouts(Vk vk, DescriptorLayouts layouts)
        {
            var binding = new DescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
            };
            var setInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = &binding,
            };
            vk.CreateDescriptorSetLayout(
                    device: device!.Device,
                    pCreateInfo: &setInfo,
                    pAllocator: null,
                    pSetLayout: out setLayout
                )
                .Check("vkCreateDescriptorSetLayout(map hit)");
            DescriptorSetLayout* sets = stackalloc[] {
                layouts.FrameSet,
                layouts.TextureSet,
                layouts.GBufferSet,
                setLayout,
            };
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 4,
                PSetLayouts = sets,
            };
            vk.CreatePipelineLayout(
                    device: device.Device,
                    pCreateInfo: &layoutInfo,
                    pAllocator: null,
                    pPipelineLayout: out layout
                )
                .Check("vkCreatePipelineLayout(map hit)");
        }

        /// <summary>The pool and the set that hold the rays' buffer.</summary>
        private void CreateSet(Vk vk, DescriptorLayouts layouts)
        {
            var size = new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = 1 };
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = 1,
                PoolSizeCount = 1,
                PPoolSizes = &size,
            };
            vk.CreateDescriptorPool(
                    device: device!.Device,
                    pCreateInfo: &poolInfo,
                    pAllocator: null,
                    pDescriptorPool: out pool
                )
                .Check("vkCreateDescriptorPool(map hit)");
            DescriptorSetLayout one = setLayout;
            var allocateInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = 1,
                PSetLayouts = &one,
            };
            vk.AllocateDescriptorSets(device.Device, &allocateInfo, out set).Check("vkAllocateDescriptorSets(map hit)");
            layouts.WriteBuffer(set: set, binding: 0, type: DescriptorType.StorageBuffer, buffer: buffer!);
        }

        public override void Record(FrameContext context)
        {
            context.Barriers.Buffer(buffer!, BufferUse.ComputeWrite);
            context.BeginComputePass(pipeline: pipeline, layout: layout, passSet: set);
            context.Vk.CmdDispatch(
                commandBuffer: context.CommandBuffer,
                groupCountX: (uint)(rays.Length + 63) / 64,
                groupCountY: 1,
                groupCountZ: 1
            );
            context.Barriers.Buffer(buffer!, BufferUse.HostRead);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
        }

        public override void Dispose()
        {
            if (device is not null)
            {
                Vk vk = device.Vk;
                vk.DestroyPipeline(device.Device, pipeline, null);
                vk.DestroyDescriptorPool(device.Device, pool, null);
                vk.DestroyPipelineLayout(device.Device, layout, null);
                vk.DestroyDescriptorSetLayout(device.Device, setLayout, null);
            }
            shaders?.Dispose();
            buffer?.Dispose();
            base.Dispose();
        }
    }
}
