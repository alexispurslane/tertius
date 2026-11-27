using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>Impostor captures stay synchronized with deformation across frame slots, without diagnostic readback.</summary>
[Collection(GpuCollection.Name)]
public sealed class DeformationImpostorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CapturesAfterDeformationAcrossFrameSlotsKeepValidationSilent(int framesInFlight)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 320,
            height: 180,
            framesInFlight: framesInFlight,
            impostors: true,
            warmUpFrames: 0
        );
        try
        {
            MeshData cylinder = Primitives.BendingCylinder(radius: 0.2f, length: 2);
            RigPose pose = cylinder.Rigging!.Rig.Rest;
            MeshId posed = renderer.CreateMesh(cylinder);
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            for (uint frame = 0; frame < 8; frame++)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
                snapshot.Time = frame / 60d;
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: posed,
                        material: white,
                        position: new(0, -1, -3),
                        scale: Vector3.One,
                        entity: 1,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                snapshot.AddRigPose(
                    instance: 0,
                    joints: pose.Joints.AsSpan(),
                    previousJoints: pose.Joints.AsSpan(),
                    weights: pose.Weights.AsSpan(),
                    previousWeights: pose.Weights.AsSpan()
                );
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: white,
                        position: new(2, 0, -14),
                        scale: Vector3.One,
                        entity: frame + 2,
                        chainDepth: TestChains.CamerasFrame
                    )
                );

                renderer.Render(snapshot, 1);

                renderer.LastFrameStats.DeformedInstances.Should().Be(1);
                renderer.LastFrameStats.ImpostorCaptures.Should().Be(1, "each frame captures a new static object");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
