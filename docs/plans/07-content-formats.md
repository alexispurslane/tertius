# 07 Content formats: blueprints and scenes

Status: planned. It needs plan 06 for the host. Plan 08 builds packages on top of it. Plans 09 and 10 use both.

## Goal

Store blueprints and scenes in JSON files that people and agents can write by hand. A JSON Schema describes each file kind, so VS Code completes and marks errors while you type.

- A blueprint file describes one kind of entity: its components, body, render data and frame.
- A scene file describes a starting state of a world: a set of entities, their frames, their attachments and their per-instance values.
- The loader turns blueprint files into `Blueprint` records and scene files into spawned entities.

Today a blueprint exists only as a C# object (`src/Tertius.Simulation/Blueprints/Blueprints.cs`), and no scene format exists. This plan owns the file formats, the merge rule, the value grammar, the schemas and the scene spawner. Plan 08 owns packages, ids, mods and code loading. Plan 09 owns the assets that the files name.

Two terms of plan 08 appear in this plan. A content set is the result of one load: the packages, the frozen blueprint registry and the prepared scenes. A code set is the loaded package code under a content set: the component types and the caches over them. Several content sets can share one code set. Three terms of System.Text.Json appear too. A contract, `JsonTypeInfo`, describes how one C# type maps to JSON: its keys, member types and converters. A converter reads and writes the JSON form of one type. A modifier changes a contract after System.Text.Json builds it.

## Decisions

1. The files are JSON, and a JSON Schema describes each file kind. (User)
2. The files are plain JSON, and a comment or a trailing comma is a load error. (User) System.Text.Json cannot keep comments through a parse and a write, and the editor of plan 10 saves by itself. A silently lost comment is worse than none. Explanations go into the C# documentation comments of the members, and the schemas show them.
3. A file holds no name and no id. Its id comes from its path (plan 08). One file holds one blueprint or one scene.
4. The loader uses System.Text.Json, which ships with .NET 11, and no other JSON or schema package. (User) The user wanted metaprogramming through attributes and reflection, as Serde gives Rust, not a hand-written type model. Source generation cannot see mod types, so the loader uses the reflection resolver.
5. One contract per C# type drives the binder, the merge, the schema exporter and the inspector of plan 10. A modifier adds the engine rules to each contract, and converters give the engine types their forms. So the schema and the binder accept the same value forms.
6. One merge rule covers blueprint inheritance, scene overrides and the patches of plan 08. The rule is JSON Merge Patch (RFC 7386), directed by the C# type of each position. Only at a union position does an object with a `kind` member replace the target. One rule is easier to learn and test than three.
7. A collection that content can change is an object keyed by name: components, scene entities and topics. An array holds a value, and a merge replaces it whole. So a merge can add, change or remove one item by its name.
8. Numbers use the units of the code: SI units and radians. (User) Where degrees are easier to read, a file writes `{"deg": 30}`. A color can also be an sRGB hex string. See "Units".
9. Keys are the camelCase form of the C# member names, as `JsonNamingPolicy.CamelCase` makes them. Enum values are camelCase names too. Component names keep their C# form, because they are type names.
10. A scene is a flat set of entities keyed by scene id. A scene id is a name that is unique in one scene. Each entity names the frame that it is in and its attachment parent. In a nested file, every move of an entity to another frame is a large edit. The editor and the patches of plan 08 work on one entity at a time. (User)
11. In a scene file, a component field of type `Entity` holds a scene id. The spawner writes the handle after it spawns every entity of the scene. So the order of the entities in the file does not matter for references.
12. The loader resolves blueprint inheritance on the JSON tree, after the patches of plan 08. It gives the registry fully resolved blueprints with no parent. A patch to a parent then reaches its children, and the body, render and frame sections merge field by field.
13. File model types in `Tertius.Content` mirror the engine types `BodyDesc`, `RenderData` and `FrameSpec`. They add the forms that files need: asset references, degree literals and file defaults. A small hand-written mapping turns them into engine types, and the schema comes from the file model types.
14. A render section can define a primitive mesh (box, sphere or plane) and a material inline. Tests, prototypes and the demo then need no asset files for simple shapes.
15. An error names its place only as far as that is cheap. (User) System.Text.Json keeps no positions in a tree. So an error in a value from a parent, a scene override or a patch has no line and column. It names the file and the JSON Pointer. A value of the file being bound also gets its line and column. The loader reads no other file for positions.
16. Errors use the compiler format that the `$msCompile` problem matcher of VS Code reads. Agents get the same errors as JSON.
17. A value that a file sets is immutable. Every spawn of a blueprint shares the bound values. A mutable shared value lets one entity change every other entity of its blueprint, and the chunk workers then race.
18. Each code set has its own `JsonSerializerOptions` and drops them at unload. (User) No mod type reaches `JsonSerializerOptions.Default`. A research probe for the earlier design saw the caches of `JsonSerializer` keep a package context alive, so this is a real risk. Step 2 makes sure that a package context unloads after a bind. If it does not on .NET 11, the editor restarts its process to reload code, and players get the CT5010 path of plan 08.

## Design

### File kinds

| File | Folder in a package | Owner | Purpose |
|---|---|---|---|
| `manifest.json` | The package root | Plan 08 | Version, dependencies and authors of the package |
| `*.blueprint.json` | `blueprints/` | This plan | One entity template |
| `*.scene.json` | `scenes/` | This plan | A starting state of a world |
| `*.patch.json` | `patches/` | Plan 08 | A change to a blueprint or scene of another package |
| `*.import.json` | `assets/` | Plan 09 | Import configuration and provenance of source assets |

Plan 08 gives the full rules for ids and references. In short:

- `ships/corvette` is a path in the package of the file that writes it.
- `tertius.base:ships/corvette` names the package too.
- An asset reference keeps the file extension. It can name a sub-asset after `#`: `ships/corvette.blend#Hull`. A sub-asset is a named thing inside an asset file, such as a piece or a material. The text after `#` is the fragment. Plan 09 defines the fragments of each asset kind.
- A name or reference always resolves in the package of the file that wrote it. This rule also holds after a merge mixes values from several files.

### Options and contracts

`ContentContracts` holds the `JsonSerializerOptions` of one code set (see "Code"). Every bind, merge, schema and inspector of that code set uses them:

- `JsonNamingPolicy.CamelCase` for keys, and `JsonStringEnumConverter` with camelCase names and `allowIntegerValues: false`. `FlagsConverterFactory` comes before it, because `JsonStringEnumConverter` reads a flags value as one string, not an array.
- `JsonUnmappedMemberHandling.Disallow`, `AllowDuplicateProperties = false` and `JsonNumberHandling.Strict`. So an unknown key, a duplicate key and a number in a string are errors.
- `IncludeFields` and `AllowOutOfOrderMetadataProperties` are true. So public fields bind like properties, and `kind` can stand anywhere in a union object.
- The Tertius converters of "Value grammar", and a `DefaultJsonTypeInfoResolver` with the modifier.

The modifier changes each contract once, at the time that System.Text.Json builds it:

- It keeps only the members that `ComponentMembers.Settable` lists.
- It leaves out a member whose type a file cannot set or is not immutable. `T[]`, `List<T>` and a class with a `set` accessor are such types. A key that names the member gets CT3005 and the immutable type to use.
- It wraps the setter of a `[Range]` member with a range test.
- It gives each `TextureId` member a converter that knows its `[TextureSlot]`.
- It adds the union cases of other packages (see "Unions").
- It leaves out `Entity` and `FrameHandle` members of types that are not components. A key for such a member gets CT3005 too.

Each Tertius converter implements `IContentForm`: the JSON Schema of its forms, and the reference strings in a value. `ContentWalk` visits a `JsonNode` tree with the contract of each position, and gives the pointer, the contract and the member of each value. The merge, phase 6 of plan 08, the preparer and the editor use it. The inspector of plan 10 reads the contracts directly.

### Value grammar

The converter of the C# type of the target member reads the value. Each engine type of the table has a converter in `Tertius.Content`, such as `VectorConverter` or `PoseConverter`. System.Text.Json reads the other types itself.

| C# member type | JSON form | Example |
|---|---|---|
| `bool` | `true` or `false` | `true` |
| An integer type | A whole number in the range of the type | `12` |
| `float`, `double` | A number, or a degree literal | `0.25`, `{"deg": 30}` |
| `string` | A string | `"hello"` |
| An enum | The camelCase name of a value | `"dynamic"` |
| A flags enum | An array of camelCase names | `["visible", "castsShadow"]` |
| `Vector2`, `Vector3`, `Vector4` | An array of 2, 3 or 4 numbers. A `Vector3` or `Vector4` can also be an sRGB hex color. | `[0, 1, 0]`, `"#ff8a3d"` |
| `Double3` | An array of 3 numbers, read as doubles | `[1.495978707e11, 0, 0]` |
| `Quaternion`, `DQuaternion` | A rotation (see "Rotations and poses") | `{"axis": [0, 1, 0], "angle": {"deg": 90}}` |
| `Pose`, `DPose` | A pose, or a node pose (see "Rotations and poses") | `{"position": [0, 1, 0]}`, `{"node": "ships/corvette.blend#AirlockInner"}` |
| `FrameBounds` | A radius for a sphere, or 3 radii for an ellipsoid | `50`, `[30, 22, 60]` |
| `Entity` | A scene id. Only scene files can set it. | `"airlock-outer"` |
| `FrameHandle` | The scene id of a frame entity. Only scene files can set it. | `"corvette"` |
| `Topic` | A topic name | `"input"` |
| `BlueprintId` | A blueprint reference | `"weapons/bolt"` |
| `MeshId`, `ModelId`, `MaterialId`, `TextureId` | An asset reference. A render section can also hold an inline mesh or material. | `"ships/corvette.blend#Hull"` |
| `ShapeDesc` | A shape object, read through the `ShapeSection` union | `{"kind": "sphere", "radius": 1}` |
| `T?` | The form of `T`. A missing key leaves the initial value of the member, which can be null. | |
| `IReadOnlyList<T>`, `ImmutableArray<T>` | An array of values in the form of `T` | `[[0, 0, 1], [0, 0, -1]]` |
| A struct | An object. Its keys are the camelCase names of its settable members. | `{"min": 1, "max": 4}` |
| A class whose settable members are all `init` properties | An object, as for a struct. It binds through its parameterless constructor or its one public constructor. | `{"min": 1, "max": 4}` |
| A union | An object with `kind` (see "Unions") | `{"kind": "point", "mu": 3.986004418e14}` |

These rules apply to every value:

- A settable member is a public instance field that is not read-only or constant. A public property with a public `set` or `init` accessor and no index parameters is also settable. `ComponentMembers` holds this rule.
- A member of a type that the table does not list cannot be set from a file. The contract and the schema leave it out, and a file that sets it gets error CT3005.
- `FloatConverter` reads the number text with `Utf8JsonReader.GetSingle`, so that a `float` never rounds twice through a `double`. Step 4 tests this.
- An integer outside the range of the member type is error CT3003. A number with a fraction for an integer member is error CT3002.
- `[Range]` from `System.ComponentModel.DataAnnotations` sets a minimum and a maximum. A range also catches many degree values written into radian fields.
- A positional record sets its members through its constructor and calls no setter. Step 5 tests `[Range]` on such a member. If the range test does not run, the modifier rejects that record type with CT3005.
- Outside a load, an `Entity`, a `FrameHandle`, a `BlueprintId` or an asset id has no name. So its converter writes a debug form, such as `#index.generation`, that no file accepts. The inspector of plan 10 writes the reference or scene id that its widget holds.
- A `TextureId` member has a texture slot: the kind of texture that it needs. `[TextureSlot(...)]` on the member sets it, and the default is `TextureSlot.Any`. Plan 09 makes sure that the image fits the slot.
- Only a member of a component itself can have the type `Entity` or `FrameHandle`. A struct, a record or a list cannot hold one (see "Scene files").

The binder reads each value once per blueprint, and every spawn of the blueprint gets the same value object. So a file can set only immutable values:

- `string`, every scalar, and the engine types of the table. The engine never changes a `ShapeDesc` or another engine value after it makes it.
- `IReadOnlyList<T>` and `ImmutableArray<T>`, with an element type that is immutable by these rules. `ReadOnlyListConverterFactory` makes an `ImmutableArray<T>` for an `IReadOnlyList<T>`, so no component can cast the list back to an array.
- A struct whose settable members have immutable types. An assignment copies a struct, so its settable fields are safe.
- A class whose settable members are all `init` properties of immutable types, such as a record with `init` members.

### Unions

A union is a type whose value is one of several cases, such as a shape, a gravity field or an orbit. Its `kind` member names the case, and its other keys depend on the case.

- A union is an abstract class or record with `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` and one `[JsonDerivedType]` per case. No case has a member named `Kind`.
- A package adds a case to a union of a dependency with `[UnionCase("name")]` on the case class. The modifier adds it to the contract of the union. Two cases with one `kind` are error CT3013.
- The engine types `ShapeDesc`, `GravityField` and `KeplerOrbit` carry no attributes of System.Text.Json. The file model unions `ShapeSection`, `GravitySection` and `OrbitSection` map to them.

### How a JSON object becomes a component

This is the component `Thruster` of the package `tertius.demo`:

```csharp
namespace TertiusDemo;

/// <summary>Main engines. The ship pushes along -Z from its nozzles.</summary>
public sealed class Thruster : IComponent
{
    /// <summary>Total thrust at full throttle, in newtons.</summary>
    [JsonRequired, Range(0, 1e9)] public float MaxThrust;
    /// <summary>Nozzle positions in ship axes, in meters.</summary>
    public IReadOnlyList<Vector3> Nozzles { get; init; } = [];
    /// <summary>Specific impulse, in seconds.</summary>
    [JsonPropertyName("isp"), Range(1, 1e4)] public float SpecificImpulse { get; init; } = 300;
    private float throttle;

    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>();
    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { ... }
}
```

The corvette blueprint below sets it with this entry:

```json
"Thruster": { "maxThrust": 2.5e5, "nozzles": [[-3, 0, 30], [3, 0, 30]] }
```

The keys map to the members by these rules. Each rule names the error of a mistake.

1. Members: the settable members `MaxThrust`, `Nozzles` and `SpecificImpulse`. `Priority` and `Wants` have no setter, so the modifier removes them from the contract. `throttle` is private. So `"priority": 5` is CT3001.
2. Keys: the camelCase member name, so `MaxThrust` is `maxThrust`. `[JsonPropertyName]` sets another key, as `isp` shows. `[JsonIgnore]` leaves a member out.
3. Creation: the binder calls the public parameterless constructor, so each member starts with its initializer.
4. Missing keys keep the initial value. `[JsonRequired]` makes a key required in the resolved tree. A corvette with no `maxThrust` in it or its parents is CT3009. The schemas list no required keys, because a child file leaves out the keys of its parent.
5. Unknown keys are errors, from `JsonUnmappedMemberHandling.Disallow`. `"maxThrst"` is CT3001, with the hint `maxThrust`. A key twice in one object is CT2001.
6. `init` properties bind like fields.
7. Forms: the member type selects the form of "Value grammar". `[JsonConverter]` on a member selects another converter. `"maxThrust": "2.5e5"` and `"nozzles": [[0, 0]]` are CT3002.
8. Ranges: the modifier tests each value of a `[Range]` member, so `"maxThrust": 2e10` is CT3003. The schema states the same `minimum` and `maximum`.
9. Immutability: `IReadOnlyList<Vector3>` binds as an `ImmutableArray<Vector3>`. A key for a `Vector3[]` member is CT3005, with the hint `ImmutableArray<Vector3>`.
10. Descriptions: the XML documentation summary, or `[Description]`, is the description in the schema and the tooltip in the inspector.
11. The recipe: the bind gives one `Thruster`, the prototype. The recipe takes the value of each key of the resolved tree from it with `JsonPropertyInfo.Get`. `ComponentFactories` compiles the factory, so a spawn reads no JSON and runs no reflection.

### Units

| Quantity | Unit in files | Notes |
|---|---|---|
| Length, position, radius, light range | m | Positions in a frame are doubles. Offsets are floats. |
| Angle | rad, or a degree literal | This includes the field of view, cone half-angles and Kepler angles. |
| Angular rate | rad/s | A degree literal turns deg/s into rad/s. |
| Time, epoch, start time | s | An epoch is absolute simulation time. |
| Velocity | m/s | In the axes of the frame of the entity |
| Angular velocity | rad/s | In the axes of the frame of the entity |
| Mass | kg | |
| Force | N | For component fields |
| Acceleration, uniform gravity | m/s² | |
| Gravitational parameter μ | m³/s² | |
| Damping | 1/s | Jolt's unit |
| Color | Linear RGB, 0 to 1 per channel | Emissive values can be above 1. A hex string is sRGB, and the converter turns it into linear values. |
| Light intensity | 1 is 683 cd for point and spot lights | Irradiance at distance d is color × intensity / d². An intensity of 1 is a Blender point light of 4π W, about 12.6 W (plan 09). |
| Scale | A factor | It changes rendering only. Collision does not scale. |

The degree literal `{"deg": x}` is a number literal. `FloatConverter` and `DoubleConverter` read it and give x × π / 180 in the member type. It is valid where a `float` or `double` is expected, and nowhere else. One converter for all `float` and `double` values is the simplest choice, because no angle member needs an attribute. Files keep radians for three reasons:

- A plain number in a file always has the unit of the code. No unit bug can come from a guessed conversion.
- glTF also stores radians.
- Published data, such as orbital elements or the light cones of Blender, often uses degrees. The literal keeps such a value readable and exactly as published.

The sRGB literal is `"#rrggbb"` or `"#rrggbbaa"`. `VectorConverter` converts each color channel with the sRGB transfer function and keeps alpha linear. It is valid only for `Vector3` and `Vector4` members.

### Rotations and poses

A rotation has one of two forms:

- `[x, y, z, w]` is a quaternion. The converter normalizes it. If its length differs from 1 by more than 1e-3, the converter reports error CT3004.
- `{"axis": [x, y, z], "angle": a}` is a rotation by the angle a about the axis. The converter normalizes the axis. The angle is a number in radians, or a degree literal.

Euler angles are left out, because they need an order rule. The axis and angle form covers rotations that people write by hand.

A pose is `{"position": [x, y, z], "rotation": r}`, and both keys are optional. A missing position is zero, and a missing rotation is the identity. `default(Quaternion)` is all zeros and not a valid rotation, so the converter never uses it. A `DPose` reads its position as doubles.

A node pose is `{"node": <asset reference>}`. Its value is the pose of a node of a model, relative to the origin of the piece that holds the node (plan 09). A door then takes its place from the model, and no file copies the numbers. A node pose has no other keys.

The axes are the axes of the engine: right-handed, +Y up, and -Z forward for cameras, spot lights and models.

### Blueprint files

A blueprint file has these top-level keys.

| Key | Form | Inherited | Meaning |
|---|---|---|---|
| `$schema` | A string | No | The path of the schema. The loader ignores the value. |
| `parent` | A blueprint reference | No | The blueprint that this one starts from |
| `abstract` | A boolean, `false` by default | No | An abstract blueprint can only be a parent. The loader registers it without a bind, so it can leave out required keys. A spawn of it is error CT4007. |
| `components` | An object: component name to field values | Yes | The components of the entity |
| `body` | A body section, or `null` | Yes | The physics body. `null` removes an inherited body. |
| `render` | A render section, or `null` | Yes | Model, mesh, material, flags and light |
| `asFrame` | A frame section, or `null` | Yes | Makes the entity a frame |
| `topics` | An object: topic name to `true` or `false` | Yes | The topics that the entity subscribes to at spawn. `false` removes an inherited topic. |

Components run in `Priority` order. The order in the file only breaks ties of `Priority`. A merge keeps the order of the parent and appends new components at the end.

The first example is an abstract parent, `content/tertius.demo/blueprints/ships/ship-base.blueprint.json`. Its id is `tertius.demo:ships/ship-base`. Every ship is a Kepler frame with no gravity of its own, and a child blueprint adds the hull, the bubble and the model.

```json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "abstract": true,
  "components": { "Health": { "max": 1000 } },
  "asFrame": { "driver": "kepler", "gravity": { "kind": "none" } }
}
```

The child, `content/tertius.demo/blueprints/ships/corvette.blueprint.json`, has the id `tertius.demo:ships/corvette`.

```json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "parent": "ships/ship-base",
  "components": {
    "Health": { "max": 1200 },
    "Thruster": { "maxThrust": 2.5e5, "nozzles": [[-3, 0, 30], [3, 0, 30]] }
  },
  "body": {
    "shape": { "kind": "asset", "asset": "ships/corvette.blend#Hull" },
    "interiorShape": { "kind": "asset", "asset": "ships/corvette.blend#Hull" },
    "mass": 3.2e5
  },
  "render": { "model": "ships/corvette.blend#Hull" },
  "asFrame": { "bounds": [30, 22, 60] }
}
```

In this example:

- `Health` resolves to `tertius.base:Health`, because `tertius.demo` depends on `tertius.base`. `Thruster` comes from the DLL of `tertius.demo`.
- The resolved corvette has `Health` with `max` 1200 at the position of the parent's `Health`, then `Thruster`. `maxThrust` is in N, `mass` in kg, and `nozzles` and the ellipsoid radii `bounds` in m in ship axes.
- The frame section of the corvette merges with the parent's: driver `kepler`, no gravity, and the ellipsoid bubble. The orbit comes from the scene.
- `ships/corvette.blend#Hull` is the piece `Hull` of the Blender file `assets/ships/corvette.blend`. A piece is a node of a model that renders as one unit (plan 09). `render.model` draws every part and light of the piece.
- One piece gives two shapes. The body is a moving use, so it gets the convex collision markers of the piece. The interior is a static use, so it gets the triangle mesh markers. Plan 09 defines the markers.

Four smaller blueprints from the same package show topics, inline primitives, an inline material, a light and a door. The first is `blueprints/people/crew.blueprint.json`:

```json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "components": { "PlayerController": { "walkSpeed": 1.6, "jumpSpeed": 3.5 } },
  "topics": { "input": true },
  "body": { "shape": { "kind": "capsule", "halfHeight": 0.55, "radius": 0.3 }, "motion": "character", "mass": 75 },
  "render": {
    "mesh": "people/crew.glb#Body",
    "material": "people/crew.glb#Suit",
    "viewOffset": { "position": [0, 0.75, 0] }
  }
}
```

`blueprints/props/crate.blueprint.json`:

```json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "body": { "shape": { "kind": "box", "halfExtents": [0.4, 0.4, 0.4] }, "mass": 40 },
  "render": {
    "mesh": { "kind": "box", "halfExtents": [0.4, 0.4, 0.4] },
    "material": { "baseColor": "#8a6b3f", "gloss": 0.3 }
  }
}
```

`blueprints/props/warning-lamp.blueprint.json`:

```json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "render": {
    "mesh": { "kind": "sphere", "radius": 0.08 },
    "material": { "baseColor": "#ffb070", "emissive": [4, 2.4, 1.2] },
    "light": {
      "kind": "spot", "color": "#ffb070", "intensity": 30, "range": 8,
      "innerConeAngle": { "deg": 25 }, "outerConeAngle": { "deg": 35 },
      "offset": { "rotation": { "axis": [1, 0, 0], "angle": { "deg": -90 } } }
    }
  }
}
```

`blueprints/ship-parts/airlock-door.blueprint.json`:

```json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "components": { "Door": { "openSeconds": 2.0 } },
  "body": { "shape": { "kind": "asset", "asset": "ships/corvette.blend#AirlockDoor" }, "motion": "kinematic" },
  "render": { "model": "ships/corvette.blend#AirlockDoor" }
}
```

The crew mesh is a piece with one material, and `viewOffset` is the eye of the camera entity. The lamp light reaches 8 m, and its offset turns the spot light down.

### Sections

The body section maps to `BodyDesc`.

| Key | Form | Default | Notes |
|---|---|---|---|
| `shape` | A shape | None | The resolved blueprint must have one (CT3009). |
| `motion` | `"static"`, `"kinematic"`, `"dynamic"` or `"character"` | `"dynamic"` | A character needs a capsule. |
| `layer` | `"static"` or `"moving"` | `"moving"` | The engine sets the frame hull and ghost layers itself, so files cannot. |
| `mass` | kg | 1 | |
| `friction` | 0 to 1 | 0.5 | |
| `restitution` | 0 to 1 | 0.1 | |
| `linearDamping` | 1/s | 0.05 | |
| `angularDamping` | 1/s | 0.05 | |
| `interiorShape` | A shape | None | The static shape inside the frame of a frame owner |

A shape is a `ShapeSection` union.

| `kind` | Keys | Engine type |
|---|---|---|
| `box` | `halfExtents` (m) | `BoxShapeDesc` |
| `sphere` | `radius` (m) | `SphereShapeDesc` |
| `capsule` | `halfHeight`, `radius` (m), along +Y | `CapsuleShapeDesc` |
| `convexHull` | `points`: an array of positions, at least 4 | `ConvexHullShapeDesc` |
| `mesh` | `vertices`, `indices`. Only static bodies and frame interiors can use it. | `MeshShapeDesc` |
| `compound` | `children`: an array of `{"shape": ..., "pose": ...}` | `CompoundShapeDesc` |
| `asset` | `asset`: a reference to a piece of a model | The `ShapeDesc` that plan 09 builds from the piece for the use |

The use of an asset shape is static or moving. A static use is an `interiorShape`, or the shape of a body with motion `static`. Every other body is a moving use. The mapping passes the use to the resolver, so one piece can give convex hulls for a body and a triangle mesh for an interior.

The physics `ShapeCache` keys shapes on the object reference, not on the value. So the loader builds one `ShapeDesc` object per resolved blueprint, and every spawn of that blueprint shares it. The asset resolver returns one shared object per piece and use. A scene entity that changes its body section gets its own object.

The render section maps to `RenderData`. The mapping starts from `new RenderData()`, never from `default`, which has a zero scale.

| Key | Form | Default | Notes |
|---|---|---|---|
| `model` | A reference to a piece of a model, or to a whole model file | None | The entity draws every part and light of the piece. A section with `model` has no `mesh` and no `material` (CT3011). Plan 09 adds the binding, with `ModelId`. |
| `mesh` | An asset reference, or a primitive | None | Primitives: `{"kind": "box", "halfExtents"}`, `{"kind": "sphere", "radius", "segments", "rings"}`, `{"kind": "plane", "halfSize", "uvRepeat"}` |
| `material` | An asset reference, or an inline material | The default material of the renderer | A merge replaces an inline material whole. |
| `scale` | 3 factors | `[1, 1, 1]` | |
| `boundingRadius` | m | 0 | 0 means that the renderer derives it. |
| `flags` | An array of `"visible"`, `"castsShadow"`, `"transparent"`, `"noImpostor"`, `"celestial"` | `["visible", "castsShadow"]` | |
| `viewOffset` | A pose | Identity | The eye of the camera entity |
| `light` | A light section | No light | |

`MeshSourceConverter` and `MaterialSourceConverter` read `mesh` and `material`, which are a string or an object. The mapping resolves what they give.

The default flags of a file differ from `new RenderData()`, which has no flags. A file with a render section draws its model or mesh unless the file says otherwise.

An inline material maps to `MaterialDesc`. Each texture key passes its slot to the resolver.

| Key | Form | Default | Slot |
|---|---|---|---|
| `baseColor` | Linear RGBA | `[1, 1, 1, 1]` | |
| `specularIntensity` | 0 to 1 | 0.5 | |
| `gloss` | 0 to 1 | 0.5 | |
| `emissive` | Linear RGB | `[0, 0, 0]` | |
| `albedo` | A texture reference | None | `Albedo` |
| `normal` | A texture reference | None | `Normal` |
| `specularGloss` | A texture reference | None | `SpecularGloss` |
| `emissiveMap` | A texture reference | None | `Emissive`. Emitted radiance is `emissive` times the texture color. Plan 09 adds the binding, with `MaterialDesc.EmissiveMap`. |

The loader creates one material for each distinct inline value and one mesh for each distinct primitive.

The light section maps to `LightData` with `Enabled` set to true. A spot light points along -Z of its offset.

| Key | Form | Default |
|---|---|---|
| `kind` | `"point"` or `"spot"` | `"point"` |
| `color` | Linear RGB | `[1, 1, 1]` |
| `intensity` | 1 is 683 cd | 1 |
| `range` | m | 10 |
| `innerConeAngle` | rad | 0.3 |
| `outerConeAngle` | rad | 0.5 |
| `offset` | A pose | Identity |

The `kind` of a light section is an enum, not the case of a union. So a merge changes it like any other value (see "The merge rule").

The frame section `asFrame` maps to `FrameSpec`.

| Key | Form | Default | Notes |
|---|---|---|---|
| `bounds` | A radius, or 3 radii (m) | None | The bubble. The resolved frame section must have one (CT3009). |
| `driver` | `"static"`, `"kepler"` or `"physics"` | `"static"` | |
| `gravity` | `{"kind": "none"}`, `{"kind": "uniform", "acceleration": [x, y, z], "region": {"min": [...], "max": [...]}}` or `{"kind": "point", "mu": μ}` | None | `region` is optional. |
| `orbit` | An orbit | None | A Kepler frame must have one, and other frames must not (CT7007). |

An orbit has one of two kinds:

- `{"kind": "elements", ...}` holds the elements of `KeplerOrbit`: `semiMajorAxis` (m), `eccentricity`, `inclination`, `longitudeOfAscendingNode`, `argumentOfPeriapsis`, `meanAnomalyAtEpoch` (rad), `epoch` (s), `parentMu` (m³/s²), `axialTilt` (a rotation), `spinRate` (rad/s) and `spinPhaseAtEpoch` (rad).
- `{"kind": "circular", "radius": r, "phase": p, "spinRate": s, "parentMu": μ}` calls `KeplerOrbit.Circular`.

If `parentMu` is absent, the scene preparer takes μ from the point gravity of the parent frame. If the parent frame has no point gravity, that is error CT7006. So μ lives in one place. The orbit conventions are the engine's. The reference plane is the XZ plane of the parent, and +Y is the orbit normal at zero inclination. The rotation order is Ω about +Y, then i about +X, then ω about +Y.

A blueprint can leave out the orbit, as the corvette does, because the scene gives it. Three rules keep an incomplete orbit out of a running world:

- The scene preparer makes sure that every Kepler frame has an orbit with a semi-major axis and a parent μ above 0 (CT7007).
- A `BlueprintId` field that names a Kepler blueprint without a complete orbit is error CT3012. A component can then never spawn a frame that stays at the origin of its parent.
- `World.Spawn` rejects such a `FrameSpec` with an ordinary argument guard (see "Engine changes for this plan").

### Scene files

A scene file has these top-level keys.

| Key | Form | Default | Engine target |
|---|---|---|---|
| `$schema` | A string | None | Ignored |
| `startTime` | s | 0 | `WorldOptions.StartTime`. The host reads it before it creates the `World`. |
| `camera` | A scene id | None | `World.Camera` |
| `verticalFov` | rad | π/3 | `World.VerticalFov` |
| `sun` | `{"frame": id, "color": [r, g, b], "intensity": i}` | None | `SunFrame`, `SunColor`, `SunIntensity` |
| `ambient` | `{"skyColor", "groundColor", "intensity"}` | The engine default | `World.Ambient` |
| `entities` | An object: scene id to entity entry | None | The scene must have it (CT3009). |

An entity entry has these keys.

| Key | Form | Meaning |
|---|---|---|
| `blueprint` | A blueprint reference | The blueprint to start from. Without it, the entity starts empty. |
| `frame` | A scene id | The frame entity that this entity is in |
| `pose` | A pose with double positions, or a node pose | The pose in the frame. For a frame entity, the pose in its parent frame. |
| `velocity` | m/s | Initial linear velocity, in frame axes |
| `angularVelocity` | rad/s | Initial angular velocity, in frame axes |
| `attachTo` | A scene id | The attachment parent. The entity is in the frame of its parent. |
| `attachOffset` | A pose with float positions | The offset from the attachment parent |
| `components`, `body`, `render`, `asFrame`, `topics` | As in blueprint files | Merged over the values of the blueprint |

A scene id matches `^[a-z0-9][a-z0-9_.-]*$` and has at most 64 characters. The binder binds the own keys of an entry as `EntityEntry`. It binds each section and each component of the entry on its own.

Before it spawns anything, the preparer enforces these rules. Each broken rule is an error at the position of the entry.

- An entity with no `frame` and no `attachTo` must have a frame section, and it becomes a root frame. A root frame has no pose, no velocity and no body (CT7004, CT7005).
- An attached entity has no `frame`, `pose`, `velocity`, body or frame section (CT7004).
- A Kepler frame has no `pose` and no `velocity`, because its orbit sets both (CT7004).
- `frame` names a frame entity of the same scene (CT7002, CT7003).
- The `frame` and `attachTo` references form no cycle (CT7001).
- Every Kepler frame has a complete orbit, and no other frame has an orbit (CT7007).
- `camera` names an entity, and `sun.frame` names a frame entity (CT7008).

The spawner spawns every entity with `World.Spawn(SpawnDesc)`, root frames too. A root frame gets a `SpawnDesc` with `AsFrame` set and no `Frame`, so it keeps its components, subscriptions and render data. `World.CreateRootFrame` takes no components, so the spawner does not use it.

The spawner spawns a frame before its contents, and an attachment parent before its children. Among the entities that are ready at the same time, the order in the file decides. This is Kahn's algorithm with a queue ordered by file position. The World needs the handle of the frame and of the parent at spawn time. The tie-break by file order gives the same handles for the same file in a fresh world.

A component field of type `Entity` or `FrameHandle` holds a scene id in a scene file. In a blueprint file, such a field is error CT3008, because no entity exists yet. `SceneIdConverter` gives the default handle. The preparer finds each such field with the contract walk, and it keeps a list of the component, the member and the scene id. After the last spawn, the spawner writes each handle through a setter that it compiled at load time. No tick runs during this time, so no component ever sees a default handle.

This example scene is `content/tertius.demo/scenes/start.scene.json`. It has a star, a planet frame, a ship frame in orbit with entities inside, node poses, an attachment and `Entity` fields.

```json
{
  "$schema": "../.schemas/scene.schema.json",
  "startTime": 0,
  "camera": "pilot",
  "verticalFov": { "deg": 70 },
  "sun": { "frame": "sol", "color": [1.0, 0.96, 0.9], "intensity": 1 },
  "ambient": { "skyColor": [0.02, 0.02, 0.03], "groundColor": [0.01, 0.01, 0.01], "intensity": 1 },
  "entities": {
    "sol": { "blueprint": "stars/sol" },
    "terra": {
      "blueprint": "planets/terra", "frame": "sol",
      "asFrame": {
        "orbit": {
          "kind": "elements", "semiMajorAxis": 1.495978707e11, "eccentricity": 0.0167,
          "inclination": 0, "longitudeOfAscendingNode": 0,
          "argumentOfPeriapsis": { "deg": 102.9 }, "meanAnomalyAtEpoch": { "deg": 100.5 }, "epoch": 0,
          "axialTilt": { "axis": [1, 0, 0], "angle": { "deg": 23.44 } },
          "spinRate": 7.2921159e-5, "spinPhaseAtEpoch": 0
        }
      }
    },
    "corvette": {
      "blueprint": "ships/corvette", "frame": "terra",
      "asFrame": { "orbit": { "kind": "circular", "radius": 6.771e6, "phase": { "deg": 40 } } }
    },
    "pilot": {
      "blueprint": "people/crew", "frame": "corvette",
      "pose": { "position": [0, 0.9, -18], "rotation": { "axis": [0, 1, 0], "angle": { "deg": 180 } } }
    },
    "crate-1": { "blueprint": "props/crate", "frame": "corvette", "pose": { "position": [2.5, 0.4, -6] } },
    "airlock-inner": {
      "blueprint": "ship-parts/airlock-door", "frame": "corvette",
      "pose": { "node": "ships/corvette.blend#AirlockInner" }
    },
    "airlock-outer": {
      "blueprint": "ship-parts/airlock-door", "frame": "corvette",
      "pose": { "node": "ships/corvette.blend#AirlockDoor" },
      "components": { "Door": { "openSeconds": 3.0 } }
    },
    "airlock": {
      "blueprint": "ship-parts/airlock", "frame": "corvette", "pose": { "position": [0, 1.1, 14] },
      "components": { "Airlock": { "innerDoor": "airlock-inner", "outerDoor": "airlock-outer", "cycleSeconds": 8 } }
    },
    "outer-lamp": {
      "blueprint": "props/warning-lamp", "attachTo": "airlock-outer", "attachOffset": { "position": [0, 1.4, 0.1] },
      "render": { "light": { "color": "#ff8a3d" } }
    }
  }
}
```

In this example:

- `sol` is the root frame, with no `frame` and no pose. `stars/sol` has point gravity with μ 1.32712440018e20 m³/s² and a 1e13 m bubble.
- `planets/terra` has a Kepler driver, point gravity with μ 3.986004418e14 m³/s², a 9e8 m bubble and a 6.371e6 m sphere. Its orbit has no `parentMu`, so the preparer takes μ from `sol`.
- `corvette` orbits 400 km above the surface of `terra`. The entities in its frame have poses in ship axes.
- The doors take their poses from nodes of the ship model, and only `airlock-outer` overrides `openSeconds`. `innerDoor` and `outerDoor` are `Entity` fields with scene ids.
- The lamp follows the outer door, and its render section changes only the light color.

The spawn order of this scene is `sol`, `terra`, `corvette`, `pilot`, `crate-1`, `airlock-inner`, `airlock-outer`, `airlock`, `outer-lamp`. The lamp waits for its parent, and every other entity keeps its file position.

### The merge rule

`MergePatch.Apply(target, patch)` gives a new `JsonNode` tree and never changes its inputs. A `JsonNode` has at most one parent, so the merge clones each node that it takes from an input. It knows the contract of each position, and it applies the first rule that fits:

1. If the position is a union and the patch is an object with a `kind` member, the result is the patch.
2. If a Tertius converter reads the value at the position, the result is the patch. Rotations, poses, vectors, references, and the mesh and material of a render section are such values. So a node pose never mixes with `position`.
3. If the patch and the target are both objects, the result starts with the members of the target, in target order. A patch member with the value `null` removes the member. Any other patch member becomes `Merge(old value, patch value)`. New members come after the old ones, in patch order.
4. In every other case, the result is the patch. An array, a string, a number or a boolean replaces the target.

A union patch with no `kind` merges by rule 3, so it keeps the case of the target. Any other member named `kind` is an ordinary member, because its position is not a union. Three examples are the `kind` of a light section, a component field `Kind`, and a scene entity with the scene id `kind`.

The merge finds the contract of a position in two ways:

- The file model types give the contracts of the keys of blueprint, scene, patch and import files. The `components` key has the type `ComponentMap`, whose keys name component types. It has no converter, so rule 3 merges it member by member.
- Phase 6 of plan 08 qualifies every component name before any merge, so `ContentContracts.Component` gives the contract of each component.

At a position of unknown type, such as an unknown key or an unknown component, the merge uses plain RFC 7386. The binder then reports the unknown key or name.

| Use | Target | Patch |
|---|---|---|
| Inheritance | The resolved parent, or an empty object for a blueprint with no parent | The inherited keys of the child |
| Scene override | The inherited keys of the resolved blueprint, or an empty object for an entry with no blueprint | The inheritable keys of the entity entry |
| Patch file (plan 08) | The target file, before inheritance | The `merge` object of the patch |

Phase 6 also qualifies each reference in its own file, with the package of that file. It finds the references with the contract walk and `IContentForm`. So `Health` in one file and `tertius.base:Health` in another file are one key. A reference resolves in the package of the file that wrote it.

Every file merges onto a resolved tree or onto an empty object. So a `null` never reaches the binder: it removes a member, as in RFC 7386, or it removes nothing. A file cannot set a member to `null`. A removed member is absent from the resolved tree, so the component keeps the initial value of its C# member.

The merge fills a source map. It maps the JSON Pointer of each value that a patch sets to the file and pointer that wrote it. It also records each removal with its `null`. A replaced subtree drops the entries below it, and a lookup takes the longest recorded prefix. Plan 08 uses the map to blame the package of a patch for an error that the patch caused.

### Mapping to engine types

| Place in the file | File model type | Engine type |
|---|---|---|
| Blueprint file | `BlueprintFile` | `Blueprint`, registered with `Parent: null` |
| `body` | `BodySection` | `BodyDesc` |
| A shape | `ShapeSection` and its cases | `ShapeDesc` and its subtypes |
| `render` | `RenderSection` | `RenderData` |
| `render.light` | `LightSection` | `LightData` |
| An inline material | `MaterialSection` | `MaterialDesc` |
| `asFrame` | `FrameSection` | `FrameSpec` |
| An orbit | `OrbitSection` and its cases | `KeplerOrbit` |
| An entity entry | `EntityEntry` | `SpawnDesc` |

### Asset references and the resolver

The loader turns every asset reference into an engine resource through `IAssetResolver` (see "Code"). Plan 09 implements it. This plan defines what the loader passes:

| Field or key | Resolver member |
|---|---|
| `render.model`, `ModelId` fields | `Model` |
| `render.mesh` with a reference, `MeshId` fields | `Mesh` |
| `render.mesh` with a primitive | `Primitive` |
| `render.material` with a reference, `MaterialId` fields | `Material` |
| `render.material` with an inline material | `InlineMaterial`, after `Texture` for each texture key |
| Texture keys of an inline material | `Texture` with the slot of the key |
| `TextureId` fields | `Texture` with the slot of `[TextureSlot]`, or `Any` |
| `{"kind": "asset"}` shapes | `Collision` with the use of the shape |
| Node poses | `NodePose` |

The resolver calls come in a fixed order: package load order, then file path, then document order. A resolver hands out renderer ids in the order of the requests, so equal content gets equal ids in every run. Phases 9 and 10 of plan 08 bind on one thread in this order, and every resolver call happens during a bind:

- `AssetIdConverter` and `PoseConverter` call the resolver while they read a value. A converter lives as long as its code set, and the .NET documentation shows no way to give it the state of one call. So the binder puts the resolver of the load into a `BindContext` on its thread around each `Deserialize` call.
- A section value that needs another value of its section binds to a small reference value first, such as an `AssetRef`. The mapping resolves it right after its section binds. An asset shape needs `motion` for its use, and an inline material needs its texture ids.

This needs no second pass over the trees and no table of resolved ids. Phase 5 still parses in parallel.

### Schemas

- There is one schema per file kind: blueprint, scene, patch, import and manifest. Plan 08 owns the patch and manifest schemas, and plan 09 owns the import schema.
- The schema writer runs `JsonSchemaExporter` over the options of the code set, so the schema and the binder share one contract. Plan 08 writes the schemas per package into `<package>/.schemas/`, which git ignores.
- `TransformSchemaNode` adds what the exporter leaves out: descriptions, `minimum` and `maximum` from `[Range]`, the forms of each converter from its `IContentForm`, and the component map of the blueprint schema.
- Descriptions come from the XML documentation comments, or from `[Description]`. The mod SDK of plan 08 turns on the documentation file.
- In the blueprint schema, `components` has one property per visible component name, short and qualified. `propertyNames` lists both forms, and `additionalProperties` is false. VS Code then completes component names and field names.
- VS Code fully supports JSON Schema drafts 4 to 7 and only partly supports later drafts (VS Code documentation). The .NET documentation names no draft for the exporter, so step 12 tests the schemas in VS Code.
- Each file holds the relative path of its schema in `$schema`. `tertius content format` and `tertius content new` (plan 08) write that path, so people and agents do not count `../` by hand.

The blueprint, scene and patch schemas describe partial documents. A child blueprint leaves out the keys of its parent, and a scene entry or a patch changes only some keys. So the transform applies three rules to these schemas:

- It removes every `required` keyword. The binder reports a missing required key (CT3009) on the resolved tree only, after every merge.
- Every member of `components`, and every inheritable section, also accepts `null`, because a `null` removes the value.
- A union accepts an object without `kind`, because such a patch merges into the case of the target.

The loader does not use the schemas at load time. A conformance test runs `check-jsonschema`, a test tool of the dev shell, and the binder over the same good and bad files. The test compares the value-form errors of both tools by JSON Pointer. It leaves out CT3009 and the reference errors, which only the loader can find.

### Parsing, writing and errors

Phase 5 of plan 08 parses each file with `JsonSerializer.Deserialize<JsonObject>` and `ContentContracts.ParseOptions`. Comments and trailing commas are errors by default, and the default depth limit is 64. `AllowDuplicateProperties = false` makes a duplicate key an error, as the .NET 10 documentation shows for `JsonObject`. The loader skips a UTF-8 byte order mark, and it reports bytes that are not UTF-8 as CT2003.

A parse error is CT2001. Its `JsonException` gives the line and the byte in the line, both from 0. The loader turns them into a line and a UTF-16 column from 1, as VS Code counts. If the text at that place starts with `/`, the hint says that content files hold no comments.

The binder binds each section and each component on its own with `JsonSerializer.Deserialize`. System.Text.Json stops at the first error of a call. So one bad component gives one error, and the other components still bind. An unknown component name hides the errors of its own fields. Each child binds the values of its parents again, so `DiagnosticBag` keeps one copy of equal errors.

The binder finds the place of a bind error in three steps:

1. It turns the `Path` of the `JsonException` into a JSON Pointer of the resolved tree.
2. The source map gives the file and the pointer that wrote the value.
3. If the file being bound wrote the value, `JsonPositions.Find` reads that file up to the pointer with a `Utf8JsonReader`. It gives the line and column. A bind from a merged tree gives no line in the file, so this step runs only after an error.

The binder takes the error code from the exception and the contract, never from the message text. A Tertius converter throws `ContentValueException`, which carries its code. For other exceptions, the binder looks at the value at the pointer and the contract of its position. So it tells apart CT3001, CT3002, CT3003, CT3006, CT3009 and CT3010. System.Text.Json reports some errors as a `NotSupportedException`, which has the path only in its message text. So the binder finds a union object without `kind` with the contract walk before the bind, and reports CT3009.

`ContentFormat.Write` writes a file in one canonical layout with `Utf8JsonWriter`. `tertius content format` (plan 08) and the editor of plan 10 use it:

- Two spaces of indent, LF line ends and one final newline.
- Members in document order, so a save never moves a key.
- An array of numbers, strings and booleans on one line, with a space after each comma. An indented `Utf8JsonWriter` puts each item on its own line, so `ContentFormat` writes these arrays with `WriteRawValue`.
- `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, the encoder that escapes the least, so text such as `é`, `<` and `&` stays readable.
- A number from a parsed file keeps its text, and a second format gives the same bytes.
- A first save can change the layout of a hand-written file, but never its values.

An error reads like this:

```
content/tertius.demo/scenes/start.scene.json(41,23): error CT4001: Unknown blueprint 'props/crat'. Did you mean 'props/crate'?
```

The JSON form has the keys `code`, `severity`, `package`, `file`, `pointer`, `line`, `column`, `message` and `hint`. The file, line, column and pointer name the place where the failing value was written. For an unsaved editor document, that place is in the text that the editor will save.

This plan owns the codes CT2xxx (JSON syntax), CT3xxx (values) and CT7xxx (scenes). Plan 08 owns the other ranges. The loader of this plan also reports four codes that plan 08 defines: CT4001, CT4006, CT4007 and CT4008.

| Code | Meaning |
|---|---|
| CT2001 | The file is not plain JSON: a syntax error, a comment, a trailing comma, a duplicate key, or nesting deeper than 64. The message is the text of the `JsonException`. |
| CT2003 | The file is not valid UTF-8. |
| CT3001 | Unknown key |
| CT3002 | The JSON value has the wrong form for the member type. |
| CT3003 | The number is outside the range of the type or of `[Range]`. |
| CT3004 | The quaternion is not normalized. |
| CT3005 | The member type cannot be set from a file, or it is not immutable. |
| CT3006 | Unknown enum name |
| CT3007 | Two members of one C# type have the same key. |
| CT3008 | An `Entity` or `FrameHandle` value outside a scene file |
| CT3009 | A required key is missing from the resolved tree. |
| CT3010 | Unknown `kind` |
| CT3011 | A render section has `model` together with `mesh` or `material`. |
| CT3012 | A `BlueprintId` value names a Kepler frame blueprint without a complete orbit. |
| CT3013 | Two cases of one union have the same `kind`. |
| CT7001 | The frames or attachments of a scene form a cycle. |
| CT7002 | `frame` names an entity that is not a frame. |
| CT7003 | Unknown scene id |
| CT7004 | The entry has a key that this kind of entity cannot have. |
| CT7005 | The entry has no frame, no attachment parent and no frame section. |
| CT7006 | A Kepler orbit has no `parentMu`, and its parent frame has no point gravity. |
| CT7007 | A Kepler frame has no complete orbit, or a frame with another driver has an orbit. |
| CT7008 | `camera` or `sun.frame` names a wrong entity. |

### Code

The new module is `src/Tertius.Content`. It references Core and Simulation only. These are its public types for this plan:

```csharp
namespace Tertius.Content;

/// <summary>
/// The contracts of one code set: the options with the converters and the modifier, and the component contracts.
/// The code set of plan 08 owns it and drops it at unload. Until plan 08, the test loader owns one.
/// </summary>
public sealed class ContentContracts
{
    public ContentContracts(IReadOnlyList<ComponentInfo> components, IReadOnlyList<Type> unionCases);
    public JsonSerializerOptions Options { get; }             // read-only after the constructor
    public JsonTypeInfo Of(Type type);
    public JsonTypeInfo? Component(string qualifiedName);

    public static JsonSerializerOptions ParseOptions { get; }   // JsonObject only: no comments, trailing commas or duplicate keys
}

/// <summary>What a Tertius converter tells the schema exporter and the contract walk.</summary>
public interface IContentForm
{
    JsonObject Schema(Func<Type, JsonNode> schemaOf);                                      // its forms. schemaOf exports a nested type.
    void References(JsonNode value, ContentContracts contracts, List<JsonValue> found);    // the reference strings, nested ones too
}

/// <summary>A JsonException with an error code. System.Text.Json adds the path.</summary>
public sealed class ContentValueException(string code, string message, string? hint = null) : JsonException(message)
{ public string Code => code; public string? Hint => hint; }

/// <summary>Visits a tree together with the contract of each position.</summary>
public static class ContentWalk
{
    public static void Visit(JsonNode root, JsonTypeInfo type, ContentContracts contracts, Action<WalkStep> visit);
}
public readonly record struct WalkStep(string Pointer, JsonNode Value, JsonTypeInfo Type, JsonPropertyInfo? Member);

/// <summary>The file and the pointer that wrote each value of a merged tree, and each removal with its null.</summary>
public sealed class SourceMap
{
    public void Wrote(string pointer, string file, string filePointer);
    public void Removed(string pointer, string file, string filePointer);
    public (string File, string Pointer) WriterOf(string pointer);             // the entry of the longest prefix
    public (string File, string Pointer)? RemovalOf(string pointer);
}

public static class MergePatch
{
    /// <summary>RFC 7386, directed by the contract of each position (see "The merge rule"). Records each writer in sources.</summary>
    public static JsonNode Apply(JsonNode target, JsonNode patch, JsonTypeInfo type, ContentContracts contracts, string patchFile, SourceMap sources);
}

public static class JsonPositions
{
    /// <summary>The line and column (from 1, the column in UTF-16 code units) of the value at a JSON Pointer, or null.</summary>
    public static (int Line, int Column)? Find(ReadOnlySpan<byte> utf8, string pointer);
}

public static class ContentFormat
{
    public static void Write(JsonElement root, IBufferWriter<byte> output);   // the canonical layout
}

public enum Severity { Error, Warning, Info }

public sealed record ContentDiagnostic(
    string Code, Severity Severity, string Message,
    string? File, int? Line, int? Column, string? Pointer, string? Package, string? Hint = null)
{
    public override string ToString() => ...;   // file(line,column): error CODE: message, or file: error CODE: message with no line
}

/// <summary>Collects diagnostics from many threads. Equal diagnostics count once. Sorted gives a fixed order for the same inputs.</summary>
public sealed class DiagnosticBag
{
    public void Add(ContentDiagnostic diagnostic);
    public bool HasErrors { get; }

    /// <summary>
    /// Sorted by package load order, file path, line, column, JSON Pointer and code. A missing line or column counts
    /// as 0. Plans 08, 09 and 10 use this one order.
    /// </summary>
    public IReadOnlyList<ContentDiagnostic> Sorted();
}

/// <summary>A package id, such as tertius.base. Plan 08 defines the grammar, and step 1 of this plan adds the type.</summary>
public readonly record struct PackageId { ... }

/// <summary>A reference to an asset or to a sub-asset, such as tertius.demo:ships/corvette.blend#Hull.</summary>
public readonly record struct AssetRef(PackageId Package, string Path, string? Fragment);

/// <summary>A component type and its names. Plan 08 discovers them in package DLLs.</summary>
public sealed record ComponentInfo(string Name, PackageId Package, Type Type)
{
    public string QualifiedName => $"{Package}:{Name}";
}

/// <summary>What one content file can see: its own package and the packages it depends on. Plan 08 implements it.</summary>
public interface IContentScope
{
    PackageId Package { get; }
    bool TryComponent(string name, out ComponentInfo component, out string error);
    bool TryBlueprint(string reference, out string qualifiedId, out string error);
    bool TryAsset(string reference, out AssetRef asset, out string error);
}

/// <summary>A value or the reason that there is none.</summary>
public readonly record struct Resolved<T>(T? Value, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>Primitive meshes that a render section can define inline. Value equality makes them cache keys.</summary>
public abstract record PrimitiveMesh;
public sealed record BoxMesh(Vector3 HalfExtents) : PrimitiveMesh;
public sealed record SphereMesh(float Radius, int Segments = 48, int Rings = 24) : PrimitiveMesh;
public sealed record PlaneMesh(float HalfSize, float UvRepeat = 1) : PrimitiveMesh;

/// <summary>Moving: a body that moves, which needs convex shapes. Static: an interior or a static body.</summary>
public enum CollisionUse { Moving, Static }

/// <summary>
/// Turns asset references into engine resources. Plan 09 implements it for asset files. Each method returns the
/// same result for the same input, so equal references share one resource and one ShapeDesc object.
/// Until plan 09 lands, PrimitiveAssets implements it: it creates primitives and inline materials through an
/// IRenderer, or returns None ids without one, and it reports each asset file reference as error CT4008.
/// </summary>
public interface IAssetResolver
{
    /// <summary>ContentLoader calls it after phase 2 with the load set in load order, before any other member. Plan 08 adds it.</summary>
    void BeginLoad(IReadOnlyList<PackageInfo> packages, DiagnosticBag diagnostics);

    Resolved<MeshId> Mesh(AssetRef asset);
    Resolved<ModelId> Model(AssetRef asset);                          // plan 09 adds it, together with ModelId
    Resolved<MaterialId> Material(AssetRef asset);
    Resolved<TextureId> Texture(AssetRef asset, TextureSlot slot);
    Resolved<ShapeDesc> Collision(AssetRef asset, CollisionUse use);
    Resolved<Pose> NodePose(AssetRef node);
    MeshId Primitive(PrimitiveMesh mesh);
    MaterialId InlineMaterial(MaterialDesc material);
}

/// <summary>One entity of a prepared scene: its spawn data, its component recipes and its handle fix-ups.</summary>
public sealed class PreparedEntity
{
    public string Id { get; }
}

/// <summary>A scene with every reference resolved and every component recipe compiled.</summary>
public sealed class PreparedScene
{
    public string Id { get; }
    public double StartTime { get; }
    public IReadOnlyList<PreparedEntity> Entities { get; }   // in spawn order
}

/// <summary>The entities of one spawned scene, by scene id.</summary>
public sealed class SceneInstance
{
    public IReadOnlyDictionary<string, Entity> Entities { get; }
    public Entity this[string id] { get; }
}

public static class SceneSpawner
{
    /// <summary>
    /// Spawns the scene into an empty world that was created with the scene's start time, and sets the camera,
    /// sun and ambient light. Call it outside a tick. It runs no reflection and reads no JSON.
    /// </summary>
    public static SceneInstance Spawn(World world, PreparedScene scene);
}
```

`PackageInfo` comes from plan 08 with `BeginLoad`. `TextureSlot` is new in Core, because mods name it in an attribute:

```csharp
namespace Tertius;

/// <summary>What a texture is for. The resolver of plan 09 makes sure that the role of the image fits the slot.</summary>
public enum TextureSlot { Albedo, Normal, SpecularGloss, Emissive, Ui, Data, Any }

namespace Tertius.Simulation;

/// <summary>Sets the slot of a TextureId member. Without it, the slot is Any.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class TextureSlotAttribute(TextureSlot slot) : Attribute
{
    public TextureSlot Slot { get; } = slot;
}

/// <summary>Adds this class as a case, with this kind, to the union of a dependency that it derives from.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UnionCaseAttribute(string kind) : Attribute
{
    public string Kind { get; } = kind;
}
```

A probe compiled `[TextureSlot(TextureSlot.Ui)]` on .NET 11. C# picks the attribute class, because the enum does not derive from `Attribute`.

A host uses the module like this:

```csharp
PreparedScene scene = content.Scene("tertius.demo:start");
var world = new World(new WorldOptions { StartTime = scene.StartTime, Blueprints = content.Blueprints }, physics);
SceneInstance instance = SceneSpawner.Spawn(world, scene);
Entity pilot = instance["pilot"];   // the scripted runs of plan 06 find entities this way
```

where

- `content` = the loaded content set of plan 08.
- `content.Blueprints` = the frozen blueprint registry of that set, which every world of the set shares.
- `physics` = the physics backend, or null for a world without physics.
- `instance` = the map from scene ids to the handles of the spawned entities.
- `pilot` = the handle of the entity with the scene id `pilot`.

Plan 10 makes three additions to this module in its own steps. They are `ContentDocument` and `ContentEdit` (step B1), partial scenes (step B2) and the preparer of one entity entry (step B4).

### Engine changes for this plan

Each change goes into Simulation or Core test-first, and the existing tests stay green.

1. `ComponentMembers`: a public static class with the settable-member rule, moved out of `Blueprints`. The modifier, the factories and the registry use the same rule.
2. `ComponentFactories`: the factory cache becomes an instance, and `Blueprints` takes one in its constructor. One instance belongs to one code set, and every content set over that code shares it. A static cache keeps mod code loaded (research probe).
3. `ComponentRecipe`: a component type that passed the rules, its field values and its compiled factory, with `Create()`. The scene spawner uses recipes for entities with overrides.
4. `Blueprint.Topics` (a list of `Topic`) and `Blueprint.Abstract`. `CreateSpawnDesc` sets `Subscriptions` and rejects an abstract blueprint.
5. `BlueprintId`: an index into one registry plus the tag of that registry, with no public constructor. A registry tag is a number that is unique to one registry. `Blueprints.Declare(IReadOnlyList<string>)` gives every blueprint id its `BlueprintId` before any blueprint registers. The loader declares the ids in ordinal order, so a field can name a blueprint that registers later, or its own blueprint. `World.Spawn(BlueprintId, ...)` and `Self.Spawn(BlueprintId, ...)` use it. A component field of this type resolves at load time, so a typo fails at load and not in the middle of a game. A spawn with the tag of another registry throws, like a stale entity handle. `Blueprints.IdOf(string)` gives the id of a declared blueprint to engine code and tests.
6. `Blueprints.Freeze()`: after it, `Register` throws. A content set freezes its registry, so worlds can share it safely.
7. `WorldOptions.Blueprints`: the registry to share. Null keeps today's behavior, a fresh empty registry.
8. `World.Spawn` rejects a Kepler `FrameSpec` whose orbit has a `SemiMajorAxis` or a `ParentMu` that is not above 0. This is an ordinary argument guard in every build. `StateAt` of a default `KeplerOrbit` returns zero, so without the guard such a frame stays at the origin of its parent with no error.
9. `TextureSlot` in Core, and `TextureSlotAttribute` and `UnionCaseAttribute` in Simulation, as shown in "Code".

The string overloads `World.Spawn(string, ...)` and `Self.Spawn(string, ...)` stay for tests and tools. Plan 08 marks the `Self` overload `[EngineOnly]`, so mod code spawns only through `BlueprintId` fields that the loader checked.

```csharp
namespace Tertius.Simulation;

public static class ComponentMembers
{
    public static bool TryFind(Type type, string name, out MemberInfo member, out Type memberType);
    public static IReadOnlyList<MemberInfo> Settable(Type type);   // in declaration order
}

/// <summary>
/// Compiled component factories. One instance per code set. Every content set over that code shares it, and
/// dropping it drops every reference to the component types.
/// </summary>
public sealed class ComponentFactories
{
    /// <summary>Validates the type and the fields, converts the values, and compiles a factory once per type and field-name set.</summary>
    public ComponentRecipe Recipe(Type type, IReadOnlyDictionary<string, object?> fields);
}

public sealed class ComponentRecipe
{
    public Type Type { get; }
    public IReadOnlyDictionary<string, object?> Fields { get; }
    public IComponent Create();
}

public readonly record struct BlueprintId
{
    public bool IsNone { get; }
}
```

### Performance budgets

These are targets, not measurements. The benchmarks of step 13 replace them with numbers.

| Work | Target | Notes |
|---|---|---|
| Build the contracts of 100 component types | Under 50 ms | Reflection, once per code set. A code reload pays it again. |
| Parse and bind 1,000 blueprint files of 2 KB each | Under 100 ms of CPU time | On one thread, with the contracts built before the timer starts. Phase 5 of plan 08 parses in parallel. |
| Prepare a scene of 10,000 entities | Under 100 ms | |
| Spawn a prepared scene of 10,000 entities | Under 30 ms | No reflection and no JSON. One delegate call per component. |
| Compile one factory | About 60 µs | Measured in the research probe |

Loading never runs during a tick.

### ARCHITECTURE.md changes from this plan

- The Blueprints section: blueprints come from files, inheritance uses the merge rule, the registry is frozen and shared per content set, and `BlueprintId` exists. Bound field values are immutable and shared by every spawn.
- A new section, "Content files": the units, the degree and sRGB literals, the value grammar, the merge rule and the scene spawn order. It also states that content files are plain JSON, which System.Text.Json binds with one options instance per code set.
- Projects: `src/Tertius.Content` depends on Core and Simulation.
- Frames: `World.Spawn` rejects an incomplete Kepler orbit.

## Steps

Each step writes its tests first and sees them fail for the right reason. The engine builds and every test passes after each step.

1. Write `PackageId` and `AssetRef`, with the grammar of plan 08. Write one test per grammar rule.
2. Test the unload risk first. Compile a small component assembly into a collectible `AssemblyLoadContext`. Build a `ContentContracts` over it, bind a component and export its schema. Drop every reference and unload, and make sure that a `WeakReference` to the context dies within 5 s. If it does not die on .NET 11, record the fallback of decision 18 in plans 08 and 10.
3. Write the parse of phase 5 and `ContentFormat`. Test comments with the hint, trailing commas, duplicate keys in a nested object, invalid UTF-8, a byte order mark and the depth limit. Test UTF-16 columns, `JsonPositions.Find`, each layout rule and a second format.
4. Write the Tertius converters. Test one read and one write per row of the value grammar. Test that each write binds back to an equal value, except a debug form. Test the degree literal, sRGB colors, CT3004 and node poses with a fake resolver. Test a `float` that rounds differently through a `double`, and the path of a `ContentValueException`.
5. Move the member rule into `ComponentMembers`, and the `Blueprints` tests stay green. Then write the modifier and `ContentContracts`. Test the member rules, `[JsonPropertyName]`, `[JsonIgnore]`, `[JsonRequired]` and `[Range]`. Test CT3005 for `T[]`, `List<T>` and a class with a `set` accessor. Test `IReadOnlyList<T>`, a union case from a second assembly, and the path in an exception of a wrapped setter. Test a positional record, also with a `[Range]` member.
6. Write the binder. Write one test per CT3xxx code and per mistake of "How a JSON object becomes a component". Test a union object without `kind`, and an integer for an enum. Test that one bad component leaves the next one bound. Test the positions of a value of the file being bound and of a value of a parent.
7. Write the merge and the source map. Test the examples in Appendix A of RFC 7386, the union rule and the converter rule. Test that a light `kind`, a component field `Kind` and a scene id `kind` merge as ordinary members. Test the member order, the source map, removals, and inputs that stay unchanged.
8. Make the Simulation and Core changes 2 to 9, one at a time, each with its tests first.
9. Write the file model types and their mapping to engine types. Test each example section of this plan against hand-built `BodyDesc`, `RenderData` and `FrameSpec` values. Use a fake `IAssetResolver` that records its calls. The keys `render.model` and `emissiveMap` bind from step 14 of plan 09, so their tests come with that step.
10. Load blueprints with a fake `IContentScope` that holds one package from a test folder. Test the qualification of references, inheritance, an abstract parent without required keys, topics, shared `ShapeDesc` objects, and inline primitives and materials. Test the order of the resolver calls. Done: the example blueprints without `render.model` register and spawn.
11. Prepare and spawn scenes. Test the spawn order, root frames with components and topics, the `parentMu` default and attachments. Test `Entity` and `FrameHandle` fields, the scene-wide values and every CT7xxx code. Done: the example scene spawns, and one tick runs.
12. Write the schema writer. Approve the generated schemas of the example package after a review. Add `check-jsonschema` to the dev shell and write the conformance test. Open the examples in VS Code. Make sure that component names, keys, enum names and union kinds complete, and that a wrong value gets a mark.
13. Write benchmarks for the budgets and report the numbers.

Plan 08 then replaces the fake scope with real packages.

## Testing

- Unit tests for each piece, as the steps list them, with the schema conformance test and the unload test of step 2.
- A corpus of files in `tests/Tertius.Content.Tests/Corpus/good/` and `bad/`. Each bad file has an `.expected.json` file with the expected code, line and column of each error. A new wording of a message does not break a test, but a new code or position does.
- Determinism: one scene spawned into two fresh worlds gives the same handles and poses. A parallel parse of the corpus gives the same trees and diagnostics as a serial parse.
- Lock-in tests for the decisions:
  - The degree literal, the default render flags and the `parentMu` default.
  - The union rule of the merge: a member named `kind` outside a union merges field by field.
  - Arrays and converter values that replace, and the spawn order tie-break.
  - Two spawns of one blueprint share one `ImmutableArray`, and a mutable member type gives CT3005.
  - A comment in a content file gives CT2001 with the hint.
  - A `null` in a blueprint with no parent leaves the C# initial value of the member.
  - A `BlueprintId` field can name its own blueprint.
  - The resolver calls come in the fixed order.
  - No bind or schema export uses `JsonSerializerOptions.Default`. The test project sets `JsonSerializerIsReflectionEnabledByDefault` to false, so such a use throws.
- Contracts, for the state that must stay coherent. `PreparedScene.AssertInvariants` states that the spawn order puts frames before contents and parents before attachments, and that every handle fix-up names an entity of the scene.

## Decided by the user

The user accepted every recommendation of this section on 2026-10-10.

1. Angles in files use radians, as in the code, with the `{"deg": x}` literal for readability. A plain number then always has the unit of the code, so no file has a hidden unit. The other option was degrees as plain numbers.
2. A scene is a flat set of entities keyed by scene id, each with `frame` and `attachTo`. Moves, patches and editor changes then touch one entry, and a git diff stays small. The other option was entities nested inside their frames.
3. The loader uses System.Text.Json and no other JSON package. The user accepted its costs: no comments, the positions of decision 15, and the unload risk of decision 18. The other option was a hand-written reader with its own type model.

## Out of scope and later

- Scene instancing: an entry that spawns another scene as a unit, which is a prefab. A sketch: the entry has `"scene"` instead of `"blueprint"`, and the ids of the inner scene get the entry id as a prefix. The editor of plan 10 can add it for prefabs.
- A save of a running world. A scene describes only a starting state.
- A spawn of a scene into a world that already has entities.
- A binary cooked format for faster loads. Measure the JSON load first.
- Dictionary members, C# `union` member types, Euler angles, and `Entity` values inside structs, records and lists.
- A parallel bind, which needs a first pass that resolves assets in order.
- Source generation for the engine types. Measure the contract build first.
- Completion of asset ids and blueprint ids in the schema.
- Localized text.
- Keeping the original whitespace of a hand-written file.
- A per-spawn copy of mutable field values. Immutable values cost nothing per spawn.
