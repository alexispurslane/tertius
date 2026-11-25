using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Tertius.Assets;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The props of the Blue Danube art in the station (docs/plans/blue-danube-demo.md, "The fit to the art"): a work area
    /// on the bay's deck, spinward of the stair's ramp, and a passenger terminal on the wheel's floor at the lift's foot,
    /// antispinward of it. Every place is in the station's axes. The props are drawn models with no body, since nothing
    /// touches them: each keeps <see cref="Reach"/> of its kind plus a person's width clear of the person's way, the parked
    /// hull, the lift and the coda. The terminal's two lamps hang on a steel post, <c>terminal-post</c>, which is drawn with
    /// no body too. The gray box draws none of them.
    /// </summary>
    public static class StationProps
    {
        /// <summary>The kind of a prop: which of the art's models it draws.</summary>
        public enum PropKind
        {
            CargoPod,
            StorageCart,
            Toolbox,
            Thermos,
            Stool,
            StreetSeating,
            CagedSconce,
        }

        /// <summary>
        /// One placement of a model, in the station's axes. A prop on a floor stands with its model's lowest point on the
        /// floor and the middle of its footprint over <paramref name="Place"/>. A prop on a wall hangs with its back, the
        /// model's +Z face, on the wall and its middle at <paramref name="Place"/>.
        /// </summary>
        /// <param name="Id">The scene id of the prop.</param>
        /// <param name="Kind">The model that it draws.</param>
        /// <param name="Place">The point on the floor or the wall, in meters.</param>
        /// <param name="Up">The unit axis up from the floor, which the model's +Y follows.</param>
        /// <param name="Front">The unit axis across <paramref name="Up"/> that the model's front, its -Z, faces.</param>
        /// <param name="OnWall">True for a prop that hangs on a wall.</param>
        [SuppressMessage(
            "Tertius.Unions",
            "TU0005",
            Justification = "The kind says which model the prop draws. The place, the up, the front and the mount mean something for every kind."
        )]
        public readonly record struct Prop(
            string Id,
            PropKind Kind,
            Double3 Place,
            Double3 Up,
            Double3 Front,
            bool OnWall = false
        );

        /// <summary>
        /// How far a prop of <paramref name="kind"/> reaches across its up from its place, in meters: at least half the
        /// diagonal of its model's footprint, as the converter gives the models (BlueDanubeArtFitTests).
        /// </summary>
        public static double Reach(PropKind kind) =>
            kind switch
            {
                PropKind.CargoPod => 1.8,
                PropKind.StorageCart => 1.0,
                PropKind.Toolbox => 0.35,
                PropKind.Thermos => 0.15,
                PropKind.Stool => 0.3,
                PropKind.StreetSeating => 2.3,
                PropKind.CagedSconce => 0.7,
            };

        /// <summary>How high the middle of a sconce hangs above the floor under it, in meters.</summary>
        public const double SconceHeight = 2.5;

        /// <summary>
        /// The work area on the bay's deck: two cargo pods, the storage cart, the toolbox, a stool and a thermos beside it,
        /// 8 to 14 m spinward of the axis's plane, beside the stair's ramp and the parked hull, and two caged sconces on the
        /// bay's spinward wall above them, facing into the bay.
        /// </summary>
        public static IReadOnlyList<Prop> BayWorkArea { get; } = BayProps();

        /// <summary>
        /// The passenger terminal on the wheel's floor, antispinward of the lift's foot: two benches of the street seating
        /// across the axis, facing the lift, two stools behind them, and the post with two caged sconces, one facing the lift
        /// and one facing +Y.
        /// </summary>
        public static IReadOnlyList<Prop> Terminal { get; } = TerminalProps();

        /// <summary>
        /// The terminal's post, in the station's axes: a steel post 0.3 m square and <see cref="PostHeight"/> tall on the
        /// wheel's floor, 4 m antispinward of the lift's spoke, on the axis's plane.
        /// </summary>
        public static Double3 PostPlace => FloorPlace(across: -4, y: 0);

        /// <summary>How tall the terminal's post stands, in meters.</summary>
        public const float PostHeight = 3;

        /// <summary>Half the side of the terminal's post, in meters.</summary>
        public const float PostHalf = 0.15f;

        private static StationLayout Layout => DemoStation.Layout;

        /// <summary>The place on the wheel's floor <paramref name="across"/> meters spinward of the lift's spoke, at <paramref name="y"/>.</summary>
        private static Double3 FloorPlace(double across, double y) =>
            Layout.WheelFloor.Above(Math.PI + Math.Asin(across / Layout.WheelFloor.Radius), 0, y);

        private static IReadOnlyList<Prop> BayProps()
        {
            double deck = Layout.Deck,
                wall = Layout.BayHalfAcross;
            Double3 up = Double3.UnitX,
                towardLane = -Double3.UnitZ;
            Prop OnDeck(string id, PropKind kind, double y, double z) =>
                new(Id: id, Kind: kind, Place: new(deck, y, z), Up: up, Front: towardLane);
            Prop OnBayWall(string id, double y) =>
                new(
                    Id: id,
                    Kind: PropKind.CagedSconce,
                    Place: new(deck + SconceHeight, y, wall),
                    Up: up,
                    Front: towardLane,
                    OnWall: true
                );
            return
            [
                OnDeck(id: "bay-pod-0", kind: PropKind.CargoPod, y: 28, z: 12.8),
                OnDeck(id: "bay-pod-1", kind: PropKind.CargoPod, y: 24.6, z: 12.8),
                OnDeck(id: "bay-cart", kind: PropKind.StorageCart, y: 18.5, z: 11),
                OnDeck(id: "bay-toolbox", kind: PropKind.Toolbox, y: 16.8, z: 9.6),
                OnDeck(id: "bay-stool", kind: PropKind.Stool, y: 19.8, z: 9.2),
                OnDeck(id: "bay-thermos", kind: PropKind.Thermos, y: 20.3, z: 8.8),
                OnBayWall("bay-sconce-0", y: 18.5),
                OnBayWall("bay-sconce-1", y: 26.5),
            ];
        }

        private static IReadOnlyList<Prop> TerminalProps()
        {
            Double3 post = PostPlace,
                postUp = FloorUp(post);
            Prop OnFloor(string id, PropKind kind, double across, double y)
            {
                Double3 place = FloorPlace(across: across, y: y),
                    up = FloorUp(place);
                return new(Id: id, Kind: kind, Place: place, Up: up, Front: Spinward(up));
            }

            Prop OnPost(string id, Double3 face) =>
                new(
                    Id: id,
                    Kind: PropKind.CagedSconce,
                    Place: post + postUp * SconceHeight + face * PostHalf,
                    Up: postUp,
                    Front: face,
                    OnWall: true
                );
            return
            [
                OnFloor(id: "terminal-seating-0", kind: PropKind.StreetSeating, across: -3.5, y: 3.2),
                OnFloor(id: "terminal-seating-1", kind: PropKind.StreetSeating, across: -3.5, y: -3.2),
                OnFloor(id: "terminal-stool-0", kind: PropKind.Stool, across: -6.5, y: 1.6),
                OnFloor(id: "terminal-stool-1", kind: PropKind.Stool, across: -6.5, y: -1.2),
                OnPost("terminal-sconce-0", Spinward(postUp)),
                OnPost("terminal-sconce-1", Double3.UnitY),
            ];
        }

        /// <summary>The up of the wheel's floor at <paramref name="place"/>: toward the axis, across the facet under it.</summary>
        public static Double3 FloorUp(Double3 place) => -Layout.WheelFloor.Normal(place);

        /// <summary>The spinward unit axis across <paramref name="up"/>, a floor's up, and the station's axis.</summary>
        private static Double3 Spinward(Double3 up) => Double3.Cross(up, Double3.UnitY).Normalized();

        /// <summary>The model that a prop of <paramref name="kind"/> draws, of <paramref name="models"/>.</summary>
        public static StoredModel ModelOf(BlueDanubeModels models, PropKind kind) =>
            kind switch
            {
                PropKind.CargoPod => models.CargoPod,
                PropKind.StorageCart => models.StorageCart,
                PropKind.Toolbox => models.Toolbox,
                PropKind.Thermos => models.Thermos,
                PropKind.Stool => models.Stool,
                PropKind.StreetSeating => models.StreetSeating,
                PropKind.CagedSconce => models.CagedSconce,
            };

        /// <summary>
        /// The pose of <paramref name="prop"/>'s model, whose bounds are <paramref name="bounds"/> in its own axes: turned so
        /// that its -Z faces the prop's front and its +Y the prop's up, and placed as <see cref="Prop"/> says.
        /// </summary>
        public static DPose PoseOf(in Prop prop, BoundingBox bounds)
        {
            DQuaternion turn = AttitudeAutopilot.Facing(prop.Front, prop.Up);
            Vector3 middle = bounds.Center;
            Double3 anchor = prop.OnWall
                ? new Double3(middle.X, middle.Y, bounds.Max.Z)
                : new Double3(middle.X, bounds.Min.Y, middle.Z);
            return new(prop.Place - turn.Rotate(anchor), turn);
        }

        /// <summary>
        /// Spawns every prop of <see cref="BayWorkArea"/> and <see cref="Terminal"/> in <paramref name="station"/>, the
        /// station's frame, drawn with <paramref name="models"/>, and the terminal's post. Each placement of a model shares
        /// its model's id. Each sconce carries a household bulb's light in its middle.
        /// </summary>
        public static void Spawn(
            World world,
            FrameHandle station,
            SceneInstance scene,
            IRenderResources resources,
            BlueDanubeModels models
        )
        {
            foreach (Prop prop in BayWorkArea.Concat(Terminal))
            {
                StoredModel model = ModelOf(models, prop.Kind);
                RenderData render = new()
                {
                    Model = model.Id,
                    BoundingRadius = model.BoundingRadius,
                    Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                };
                if (prop.Kind == PropKind.CagedSconce)
                    render = render with
                    {
                        Light = LampLight(offset: model.Bounds.Center, power: HouseholdBulbPower) with
                        {
                            CastsShadow = false,
                        },
                    };
                scene.Add(
                    prop.Id,
                    world.Spawn(
                        new()
                        {
                            Frame = station,
                            Pose = PoseOf(prop, model.Bounds),
                            Render = render,
                        }
                    )
                );
            }

            Double3 up = FloorUp(PostPlace);
            var post = new Vector3(PostHalf, PostHeight / 2, PostHalf);
            scene.Add(
                "terminal-post",
                world.Spawn(
                    new()
                    {
                        Frame = station,
                        Pose = new(
                            PostPlace + up * (PostHeight / 2),
                            AttitudeAutopilot.Facing(Spinward(up), up)
                        ),
                        Render = new()
                        {
                            Mesh = resources.CreateMesh(Primitives.Box(post)),
                            Material = resources.CreateMaterial(DemoStation.DeckSteel),
                            BoundingRadius = post.Length(),
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
                    }
                )
            );
        }
    }
}
