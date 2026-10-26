"""The outputs of a build: the .blend file and the .glb file, with the export conventions of the asset pipeline
(docs/plans/asset-pipeline.md, "The build tool"). The converter of the importer exports through write_glb too, so a
converted file and a scripted model leave Blender with one set of options."""

import bpy


def write_glb(path):
    """Exports every object of the scene into the .glb file `path`. Fails when the scene holds a camera, because a
    camera there means that the stage of the views leaked into the model."""
    cameras = sorted(ob.name for ob in bpy.context.scene.objects if ob.type == "CAMERA")
    if cameras:
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


def pieces():
    """The names of the piece nodes: every root object, and every object marked with tertius_piece = 1
    ("Models: nodes and pieces"), in name order."""
    objects = bpy.context.scene.objects
    return sorted(ob.name for ob in objects if ob.parent is None or ob.get("tertius_piece") == 1)


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
