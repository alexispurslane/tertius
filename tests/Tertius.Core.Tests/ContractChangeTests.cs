using System.Numerics;

namespace Tertius.Core.Tests;

public sealed class ContractChangeTests
{
    [Fact]
    public void ComputeRelativeToIsSafeFromManyThreads()
    {
        var tree = new PoseTree<DPose>();
        var nodes = new List<PoseNode> { tree.Add(PoseNode.Null, DPose.Identity) };
        for (int i = 1; i < 600; i++)
            nodes.Add(tree.Add(nodes[(i - 1) / 3],
                new(new(i, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, i * 0.01))));
        tree.UpdateWorld();

        var expected = new DPose[tree.Count];
        tree.ComputeRelativeTo(nodes[599], expected);
        Parallel.For(0, 64, _ =>
        {
            var result = new DPose[tree.Count];
            tree.ComputeRelativeTo(nodes[599], result);
            for (int i = 0; i < result.Length; i++)
                Assert.Equal(expected[i], result[i]);
        });
    }

    [Fact]
    public void ComputeRelativeToWithSuppliedLocalsUsesThem()
    {
        var tree = new PoseTree<DPose>();
        PoseNode root = tree.Add(PoseNode.Null, DPose.Identity);
        PoseNode a = tree.Add(root, new(new(10, 0, 0), DQuaternion.Identity));
        PoseNode b = tree.Add(root, new(new(20, 0, 0), DQuaternion.Identity));
        tree.UpdateWorld();

        DPose[] previous = tree.LocalPoses.ToArray();
        previous[tree.DenseIndex(b)] = new(new(25, 0, 0), DQuaternion.Identity);
        var result = new DPose[tree.Count];
        tree.ComputeRelativeTo(a, previous, result);
        Assert.Equal(15, result[tree.DenseIndex(b)].Position.X, 9);
        // The tree's own poses are untouched.
        Assert.Equal(20, tree.GetLocal(b).Position.X);
    }

    [Fact]
    public void DestroyingAFrameConvertsChildVelocities()
    {
        var frames = new FrameTree();
        FrameHandle root = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f));
        FrameHandle ship = frames.Create(frameOwner: new(2, 1), parent: root,
            localPose: new(new(1000, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)),
            frameBounds: FrameBounds.Sphere(500));
        FrameHandle shuttle = frames.Create(frameOwner: new(3, 1), parent: ship,
            localPose: new(new(10, 0, 0), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(20));
        frames.SetTreeLinearVelocity(ship, new(0, 0, 100));
        frames.SetAngularVelocity(ship, new(0, 0.1f, 0));
        frames.SetTreeLinearVelocity(shuttle, new(1, 0, 0));
        frames.Update();

        Double3 expected = frames.VelocityToParent(ship, new(10, 0, 0), new(1, 0, 0));
        frames.Destroy(ship);
        frames.Update();

        Assert.Equal(root, frames.Parent(shuttle));
        Assert.True((frames.LinearVelocity(shuttle) - expected).Length < 1e-9,
            frames.LinearVelocity(shuttle).ToString());
        Assert.Equal(0.1f, frames.AngularVelocity(shuttle).Y, 6);
    }

    [Fact]
    public void ChildOfADestroyedRootBecomesARootAndKeepsItsMotion()
    {
        var frames = new FrameTree();
        FrameHandle root = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null,
            localPose: new(new(5, 0, 0), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(1e6f));
        FrameHandle child = frames.Create(frameOwner: new(2, 1), parent: root,
            localPose: new(new(1, 0, 0), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(10));
        frames.SetTreeLinearVelocity(child, new(0, 3, 0));
        frames.Update();

        frames.Destroy(root);
        frames.Update();

        Assert.True(frames.Parent(child).IsNull);
        Assert.Equal(6, frames.LocalPose(child).Position.X, 9);
        Assert.Equal(3, frames.LinearVelocity(child).Y, 9);
    }

    [Fact]
    public void FrameTopicsCarryTheFrameGeneration()
    {
        var frame = new FrameHandle(new(9, 1), 4, 7);
        Topic topic = frame.Topic;
        Assert.Equal(7u, topic.Generation);
        Target target = topic;
        Assert.True(target.TryGetValue(out Topic back));
        Assert.Equal(topic, back);
        Assert.NotEqual(Topic.OfFrame(frame with { Generation = 8 }), topic);
        Assert.Equal(0u, Topics.Input.Generation);
    }

    [Fact]
    public void DefaultRenderIdsMeanNone()
    {
        Assert.True(default(MeshId).IsNone);
        Assert.True(default(MaterialId).IsNone);
        Assert.True(default(TextureId).IsNone);
        Assert.Equal(0, new MeshId(0).Value);
        Assert.False(new MeshId(0).IsNone);
        MaterialDesc material = MaterialDesc.Color(Vector4.One);
        Assert.True(material.Albedo.IsNone && material.Normal.IsNone && material.SpecularGloss.IsNone);
    }

    [Fact]
    public void RenderInstanceCreateIsDrawableAndStill()
    {
        var i = RenderInstance.Create(mesh: new(1), material: new(2), position: new(1, 2, 3),
            rotation: Quaternion.Identity, scale: Vector3.One, boundingRadius: 1.5f,
            hemisphere: HemisphereUp.Even, part: 2);
        Assert.Equal(i.Position, i.PreviousPosition);
        Assert.Equal(i.Rotation, i.PreviousRotation);
        Assert.Equal((ushort)2, i.Part);
        Assert.Equal(RenderFlags.Visible, i.Flags);
    }
}
