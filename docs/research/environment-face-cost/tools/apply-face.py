# Tool code of ter-yglw (the environment face cost). Applies the scratch switches of FaceProbe.cs and FrameTimeProbe.cs
# to a plain build copy of main: the face size, the size cull, faces in every second drawn frame, and the per-face and
# per-frame logs. The edits never enter src. Usage: python3 -I apply-face.py <build copy root>
import os, shutil, sys

root = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))


def edit(rel, old, new):
    path = os.path.join(root, rel)
    s = open(path).read()
    assert s.count(old) == 1, (rel, old[:70])
    open(path, 'w').write(s.replace(old, new))


edit('src/Tertius.Rendering/EnvironmentMapSetting.cs',
     'public static SceneEnvironmentMap Default => new(FaceSize: 128, FacesPerFrame: 1);',
     'public static SceneEnvironmentMap Default => new(FaceSize: FaceProbe.Size, FacesPerFrame: FaceProbe.PerFrame); // PROBE (ter-yglw)')
edit('src/Tertius.Rendering/Frame/EnvironmentMap.cs',
     """        EnvironmentFaces.InTurn(frame, Setting.FacesPerFrame, faces);
""",
     """        // PROBE (ter-yglw): faces in every second frame, and the per-face log.
        FaceProbe.Begin(frame);
        long inTurn = FaceProbe.InTurnFrame(frame);
        if (inTurn >= 0)
            EnvironmentFaces.InTurn(inTurn, Setting.FacesPerFrame, faces);
        else
            faces.Clear();
""")
edit('src/Tertius.Rendering/Scene/SceneBatcher.cs',
     """        int opaqueCount = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            ref readonly PreparedInstance instance = ref instances[i];
            if (
                !DrawsInMaps(instance)
                || !EnvironmentChain.Draws(map: map, objectMap: instance.Map)
                || CubeFaces.Culls(basis, instance.Position, instance.Radius)
            )
                continue;
            int mask = DepthRanges.OpaqueMask(Vector3.Dot(instance.Position, basis.Forward), instance.Radius);
""",
     """        int opaqueCount = 0;
        int probeKept = 0, probeCulled = 0; // PROBE (ter-yglw)
        long probeTriangles = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            ref readonly PreparedInstance instance = ref instances[i];
            if (
                !DrawsInMaps(instance)
                || !EnvironmentChain.Draws(map: map, objectMap: instance.Map)
                || CubeFaces.Culls(basis, instance.Position, instance.Radius)
            )
                continue;
            if (FaceProbe.CullsSmall(instance.Position, instance.Radius))
            {
                probeCulled++;
                continue;
            }
            if (FaceProbe.LeavesOut(instance.Mesh, instance.Map, map))
                continue;
            int mask = FaceProbe.Reach(
                DepthRanges.OpaqueMask(Vector3.Dot(instance.Position, basis.Forward), instance.Radius),
                instance.Position,
                instance.Radius
            );
            probeKept++;
            probeTriangles += meshes[instance.Mesh].IndexCount / 3 * BitOperations.PopCount((uint)mask);
""")
edit('src/Tertius.Rendering/Scene/SceneBatcher.cs',
     """                Near: faceRanges[DepthRanges.Near].Sided
            )
        );
    }
""",
     """                Near: faceRanges[DepthRanges.Near].Sided
            )
        );
        FaceProbe.Face(
            map,
            (int)face,
            faceRanges[DepthRanges.Far].Count + faceRanges[DepthRanges.Middle].Count + faceRanges[DepthRanges.Near].Count,
            probeTriangles,
            probeKept,
            probeCulled
        );
    }
""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """        disposed = true;
        if (window != null && onFramebufferResize != null)""",
     """        disposed = true;
        if (environmentMap is not null) // PROBE (ter-yglw): the face dump
            FaceProbe.Dump(this, environmentMap.DrawnFor.Length);
        if (window != null && onFramebufferResize != null)""")
edit('src/Tertius.Host/ScriptCommand.cs',
     """                    readings.Add(renderer.LastFrameStats);
""",
     """                    readings.Add(renderer.LastFrameStats);
                    FrameTimeProbe.Write(renderer.LastFrameStats); // PROBE (ter-yglw)
""")
# t1-6tn2: the parts of a face's work (FaceProbe.cs, TERTIUS_PROBE_FACE_PARTS).
edit('src/Tertius.Rendering/Passes/EnvironmentPrefilterPass.cs',
     """        for (int dispatch = 0; dispatch < PrefilterLevels.Dispatches(map.Levels); dispatch++)
            DispatchLevels(""",
     """        for (int dispatch = 0; dispatch < (FaceProbe.SkipsSpecular(count) ? 0 : PrefilterLevels.Dispatches(map.Levels)); dispatch++) // PROBE (t1-6tn2)
            DispatchLevels(""")
edit('src/Tertius.Rendering/Scene/SceneBatcher.cs',
     """        ReadOnlySpan<PreparedInstance> instances = scene.PlacedSpan;
        for (int map = 0; map < faces.Length; map++)
""",
     """        ReadOnlySpan<PreparedInstance> instances = FaceProbe.ForFaces(scene.PlacedSpan); // PROBE (t1-6tn2)
        for (int map = 0; map < faces.Length; map++)
""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """        return uploads.AddMesh(mesh);
""",
     """        MeshId id = uploads.AddMesh(mesh);
        FaceProbe.MeshCreated(id, mesh, uploads.AddMesh); // PROBE (t1-6tn2)
        return id;
""")
edit('src/Tertius.Rendering/Passes/EnvironmentPrefilterPass.cs',
     """        if (anyDrawn)
        {
            RecordMips(""",
     """        if (anyDrawn && !FaceProbe.SkipsPrefilter(scene.EnvironmentFaces.Count, scene.MapCount)) // PROBE (t1-6tn2)
        {
            RecordMips(""")
edit('src/Tertius.Rendering/Passes/EnvironmentPrefilterPass.cs',
     """        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: count * IrradianceCube.WorkgroupsPerFace,
""",
     """        if (!FaceProbe.SkipsIrradiance(count)) // PROBE (t1-6tn2)
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: count * IrradianceCube.WorkgroupsPerFace,
""")
shutil.copy(os.path.join(here, 'FaceProbe.cs'), os.path.join(root, 'src/Tertius.Rendering/Scene/FaceProbe.cs'))
shutil.copy(os.path.join(here, 'FrameTimeProbe.cs'), os.path.join(root, 'src/Tertius.Host/FrameTimeProbe.cs'))
print('face edits applied to', root)
