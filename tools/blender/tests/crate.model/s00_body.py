# The body of the crate: a beveled box with a seeded scatter of rivets on its front face.
# The front of the model faces -Y. The origin is the middle of the floor face.
from tertius_blender.geometry import MB

width, depth, height = model.params["size"]
mb = MB()
mb.box((0.0, 0.0, height / 2), (width, depth, height), bevel=0.03)
for _ in range(model.params["rivets"]):
    x = model.rng.uniform(-width / 2 + 0.08, width / 2 - 0.08)
    z = model.rng.uniform(0.08, height - 0.08)
    mb.cyl((x, -depth / 2 - 0.005, z), (0, -1, 0), 0.02, 0.02, segs=8)
crate = mb.finish("Crate", None, "Crate")
result = {"vertices": len(crate.data.vertices)}
