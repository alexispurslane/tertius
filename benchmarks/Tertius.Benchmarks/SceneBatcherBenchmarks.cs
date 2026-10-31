using System.Collections.Immutable;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tertius.Rendering;

namespace Tertius.Benchmarks;

/// <summary>
/// The CPU cost of the environment maps' culled views (docs/plans/environment-map.md, "Cost", and
/// docs/plans/environment-maps-per-frame.md, "Memory and cost"): one <see cref="SceneBatcher.Prepare"/> of a snapshot of
/// 20,000 instances, the size that <see cref="RenderSnapshotBenchmarks"/> builds, on one thread. With one map, the camera
/// in the root frame: the map off, one face and six faces. With four maps, the chain of the cabin of demo-surface: one face
/// and a refresh of all 24 faces. Beside the grid, 0, 1 or 100 posed instances of a tube whose rig is a chain of 53 joints,
/// the joints of MPFB's game-engine rig, measure the cost of posing (docs/plans/skinning-and-morphs.md, "Memory and cost").
/// <list type="bullet">
/// <item>The instances are unit cubes in a grid of 100 by 100 by 2, 1 m apart, 100 m in front of the camera, which sits
/// at the render origin and looks down -Z at 1920x1080. So the main view sees each of them.</item>
/// <item>With four maps, every instance is in the root frame, at chain depth 3, so it takes map 0 and every map draws it:
/// the dearest place for it. Every frame of the chain lies in the root's axes.</item>
/// <item>The one face is -Z of the camera's frame's map, the face along the camera's view, which also keeps each
/// instance: the dearest single face. The other faces keep none.</item>
/// <item>The posed instances stand in a row 20 m in front of the camera. Every joint but the root turns about +Z between the
/// two ends of the interpolation, so posing slerps every joint.</item>
/// </list>
/// The median time is the figure to read. The allocations must be 0, because the batcher's arrays have grown to the
/// scene in the setup.
/// </summary>
[MemoryDiagnoser]
[MedianColumn]
public class SceneBatcherBenchmarks
{
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>The default material's slot and seven more, all opaque.</summary>
    private static readonly MaterialInfo[] Materials = new MaterialInfo[8];

    /// <summary>The joints of the posed tube's rig: as many as MPFB's game-engine rig has.</summary>
    private const int ChainJoints = 53;

    /// <summary>The length of the posed tube, in meters, and the rest distance between two neighbor joints of its chain.</summary>
    private const float ChainLength = 2;

    private const float ChainStep = ChainLength / (ChainJoints - 1);

    /// <summary>The posed tube: 8 segments round, one ring of vertices per joint, each ring bound to its joint alone.</summary>
    private static readonly MeshData Chain = ChainTube();

    /// <summary>The unit cube of the grid, then the posed tube.</summary>
    internal static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f, Deform: null),
        new(
            FirstIndex: 36,
            IndexCount: (uint)Chain.Indices.Length,
            VertexOffset: 24,
            Radius: GeometryStore.Radius(Chain.Vertices),
            Deform: MeshDeform.Pack(StagedMesh.Of(Chain))!.Deform
        ),
    ];

    /// <summary>A region with room for every copy, pose row and job of the posed instances.</summary>
    private static readonly DeformRegion Region = new(
        Base: 1 << 20,
        Capacity: 1 << 20,
        PoseRowCapacity: 1 << 20,
        JobCapacity: 1 << 10
    );

    private readonly SceneBatcher batcher = new(
        exposure: AutoExposure.Default,
        cascadedLights: 1,
        shadowMapSize: ShadowMap.DefaultSize,
        depthRangeScale: 16f
    );
    private readonly PreparedScene scene = new();
    private RenderSnapshot snapshot = new();
    private EnvironmentDraw environment;

    [Params("off", "one-face", "six-faces", "four-maps-one-face", "four-maps-24-faces")]
    public string Map = "off";

    [Params(0, 1, 100)]
    public int Posed;

    [GlobalSetup]
    public void Setup()
    {
        FaceMask[] faces = Map switch
        {
            "off" => [],
            "one-face" => [FaceMask.NegativeZ],
            "six-faces" => [FaceMask.All],
            "four-maps-one-face" => [FaceMask.None, FaceMask.None, FaceMask.None, FaceMask.NegativeZ],
            "four-maps-24-faces" => [FaceMask.All, FaceMask.All, FaceMask.All, FaceMask.All],
            _ => throw new ArgumentException($"No map setting {Map}."),
        };
        snapshot = Grid(chainLength: Math.Max(faces.Length, 1));
        AddPosed(snapshot, Posed);
        environment =
            faces.Length == 0
                ? new(new WithoutMap())
                : new(new WithMap(Faces: faces, Irradiance: new IrradianceReading?[faces.Length]));
        // Grows the batcher's arrays and the scene's to the snapshot, so the measured calls allocate nothing.
        Prepare();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Prepare();
        Console.WriteLine(
            $"SceneBatcher map={Map} posed={Posed}: {snapshot.Instances.Count} instances, {scene.VisibleCount} visible, "
                + $"{scene.InstanceCount} drawn instances, {scene.CommandCount} commands, "
                + $"{scene.EnvironmentFaces.Count} faces, {scene.DeformedInstances} deformed"
        );
    }

    [Benchmark]
    public void Prepare() =>
        batcher.Prepare(
            snapshot: snapshot,
            alpha: 0.5f,
            width: Width,
            height: Height,
            meshes: Meshes,
            materials: Materials,
            environment: environment,
            region: Region,
            scene: scene
        );

    /// <summary>
    /// The grid, under a chain of <paramref name="chainLength"/> frames in the root's axes, every instance in the root.
    /// </summary>
    internal static RenderSnapshot Grid(int chainLength)
    {
        var grid = new RenderSnapshot
        {
            Camera = new()
            {
                Rotation = Quaternion.Identity,
                PreviousRotation = Quaternion.Identity,
                VerticalFov = MathF.PI / 3,
            },
            Lights =
            {
                BenchmarkLights.Star(
                    direction: new(0.3f, 1, 0.2f),
                    irradiance: 2.5f,
                    chainDepth: (ushort)(chainLength - 1)
                ),
            },
        };
        for (int depth = 0; depth < chainLength; depth++)
            grid.Chain.Add(
                new(FrameId: chainLength - depth, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity)
            );
        for (int i = 0; i < 20_000; i++)
            grid.Instances.Add(
                RenderInstance.Create(
                    mesh: new(0),
                    material: new(1),
                    position: new(x: i % 100 - 50, y: i / 100 % 100 - 50, z: -100 - i / 10_000),
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    boundingRadius: 0.866f,
                    hemisphere: HemisphereUp.Along(Vector3.UnitY),
                    chainDepth: (ushort)(chainLength - 1),
                    flags: RenderFlags.Visible | RenderFlags.CastsShadow
                )
            );
        return grid;
    }

    /// <summary>
    /// Appends <paramref name="count"/> posed instances of the tube to <paramref name="grid"/>, in a row 20 m in front of the
    /// camera, after every instance of the grid, as the snapshot's order of rig poses needs.
    /// </summary>
    private static void AddPosed(RenderSnapshot grid, int count)
    {
        var current = new Pose[ChainJoints];
        var previous = new Pose[ChainJoints];
        current[0] = previous[0] = Pose.Identity;
        for (int joint = 1; joint < ChainJoints; joint++)
        {
            var offset = new Vector3(0, ChainStep, 0);
            current[joint] = new(offset, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.03f));
            previous[joint] = new(offset, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.02f));
        }

        ushort chainDepth = (ushort)(grid.Chain.Count - 1);
        for (int i = 0; i < count; i++)
        {
            grid.Instances.Add(
                RenderInstance.Create(
                    mesh: new(1),
                    material: new(1),
                    position: new(x: i % 20 - 10, y: i / 20 * 3 - 6, z: -20),
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    boundingRadius: 0,
                    hemisphere: HemisphereUp.Along(Vector3.UnitY),
                    chainDepth: chainDepth,
                    flags: RenderFlags.Visible | RenderFlags.CastsShadow
                )
            );
            grid.AddRigPose(
                instance: grid.Instances.Count - 1,
                joints: current,
                previousJoints: previous,
                weights: [],
                previousWeights: []
            );
        }
    }

    /// <summary>
    /// A tube along +Y of <see cref="ChainLength"/> m whose rig is a chain of <see cref="ChainJoints"/> joints,
    /// <see cref="ChainStep"/> apart, with ring r of the tube bound to joint r alone and each cap to its end ring's joint.
    /// </summary>
    private static MeshData ChainTube()
    {
        const int segments = 8;
        MeshData tube = Primitives.BendingCylinder(
            radius: 0.15f,
            length: ChainLength,
            segments: segments,
            rings: ChainJoints - 1
        );
        ImmutableArray<RigJoint> joints =
        [
            .. Enumerable
                .Range(0, ChainJoints)
                .Select(joint => new RigJoint(
                    Name: $"joint-{joint}",
                    Parent: joint == 0 ? null : joint - 1,
                    Rest: joint == 0 ? Pose.Identity : new(new(0, ChainStep, 0), Quaternion.Identity)
                )),
        ];
        var skin = new MeshSkin(
            Vertices:
            [
                .. tube.Vertices.Select(vertex =>
                {
                    // By height, so that each cap follows its end ring's joint.
                    ushort ring = (ushort)MathF.Round(vertex.Position.Y / ChainStep);
                    return new VertexSkin(Joint0: ring, Joint1: 0, Joint2: 0, Joint3: 0, Weights: Vector4.UnitX);
                }),
            ],
            InverseBinds:
            [
                .. Enumerable
                    .Range(0, ChainJoints)
                    .Select(joint => new Pose(new(0, -joint * ChainStep, 0), Quaternion.Identity)),
            ]
        );
        return tube with { Rigging = new(new Rig(joints, []), skin, []) };
    }
}
