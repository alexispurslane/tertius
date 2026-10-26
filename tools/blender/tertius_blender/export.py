"""The outputs of a build: the .blend file and the .glb file, with the export conventions of the asset pipeline
(docs/plans/asset-pipeline.md, "The build tool"). The converter of the importer exports through write_glb too, so a
converted file and a scripted model leave Blender with one set of options."""

import bpy


def write_glb(path, refuse_cameras=False):
    """Exports every object of the scene into the .glb file `path`. The exporter leaves cameras out. With
    `refuse_cameras`, which the build tool turns on, a camera fails the export instead, because a camera in a scripted
    model means that the stage of the views leaked into the model. A third-party file often holds a camera, so the
    converter leaves the check off.

    Run the export in a Blender started with --threads 1. With more threads, Blender computes the tangents in an order
    that changes between runs, and their last bits differ."""
    cameras = sorted(ob.name for ob in bpy.context.scene.objects if ob.type == "CAMERA")
    if refuse_cameras and cameras:
        raise ValueError("the scene holds cameras, which a model never exports: {:s}".format(", ".join(cameras)))
    # The exporter computes tangents only for triangles and quads, so it exports through a Triangulate modifier
    # at the end of each modifier stack. glTF holds triangles in any case. The scene keeps its polygons.
    added = []
    for ob in bpy.context.scene.objects:
        if ob.type == "MESH":
            added.append((ob, ob.modifiers.new(_TRIANGULATE, "TRIANGULATE")))
    try:
        _export(path)
    finally:
        for ob, modifier in added:
            ob.modifiers.remove(modifier)


_TRIANGULATE = "TERTIUS_EXPORT_TRIANGULATE"


def _export(path):
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=False,
        export_yup=True,
        export_apply=True,
        # Custom properties such as tertius_piece reach the importer as glTF extras.
        export_extras=True,
        export_texcoords=True,
        export_normals=True,
        export_tangents=True,
        export_lights=True,
        export_cameras=False,
        export_materials="EXPORT",
        export_image_format="AUTO",
        export_import_convert_lighting_mode="SPEC",
        export_draco_mesh_compression_enable=False,
    )


def save_blend(path):
    """Saves the open scene as the .blend file `path`, with image paths relative to it (//textures/...)."""
    bpy.ops.wm.save_as_mainfile(filepath=path, relative_remap=True, compress=True, check_existing=False)


def roots():
    """The names of the root objects, in name order."""
    return sorted(ob.name for ob in bpy.context.scene.objects if ob.parent is None)


def marked():
    """The names of the objects marked with the custom property tertius_piece = 1, in name order.

    The importer decides which nodes are pieces ("Models: nodes and pieces"). It also reads the PIECE_ prefix, the
    pieces of the sidecar and the collision markers, which the build tool does not see. So the report of the build
    names the roots and the marked objects, not the pieces."""
    return sorted(ob.name for ob in bpy.context.scene.objects if ob.get("tertius_piece") == 1)


def triangles():
    """The triangle count of every mesh of the scene, after modifiers."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    count = 0
    for ob in bpy.context.scene.objects:
        if ob.type == "MESH":
            mesh = ob.evaluated_get(depsgraph).data
            mesh.calc_loop_triangles()
            count += len(mesh.loop_triangles)
    return count
