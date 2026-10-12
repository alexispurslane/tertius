namespace Tertius.Simulation;

/// <summary>The World's operations on numeric frames that <see cref="NumericClustering"/> calls at the sync point.</summary>
internal interface INumericFrameHost
{
    /// <summary>Creates a numeric frame under the semantic frame <paramref name="parent"/>, with no bodies yet.</summary>
    FrameHandle CreateNumericFrame(FrameHandle parent, Double3 position, Double3 velocity, NumericMode mode);

    /// <summary>
    /// Moves the numeric frame <paramref name="frame"/> by <paramref name="offset"/> and adds <paramref name="velocity"/>
    /// to its velocity, while <paramref name="members"/>, every entity with a body whose physics home it is, keep their
    /// places and velocities in its parent.
    /// </summary>
    void ShiftNumericFrame(FrameHandle frame, Double3 offset, Double3 velocity, List<Entity> members);

    /// <summary>Moves the bodies of <paramref name="group"/> into <paramref name="home"/>, as <see cref="World.MoveBodies"/>.</summary>
    void MoveBodiesHome(List<Entity> group, FrameHandle home);

    /// <summary>Destroys a numeric frame that holds no body and no frame.</summary>
    void DestroyEmptyNumericFrame(FrameHandle frame);

    /// <summary>Hands the changes so far to physics as one batch.</summary>
    void FlushPhysicsChanges();
}

/// <summary>
/// Puts bodies into numeric frames and takes them out (docs/plans/numeric-frames.md, "Membership"). It runs once at each
/// sync point, after the physics output and the spawns have landed, on one thread. Within each semantic frame it links
/// every pair of bodies that the merge test says can touch within the lookahead time, holds the members of a frame
/// together until they have been apart for the split cooldown, and treats each linked group as one cluster. A cluster
/// that is fast gets a falling frame, one that is slow and far a resting frame, and the rest stay in the semantic frame's
/// own system. A cluster keeps the numeric frame that holds most of its mass, so the smaller groups move. Each kept frame
/// takes over its members' common velocity when it passes the drain speed, and moves its origin to their center of mass
/// when they drift far. Frames that end up empty go away.
/// </summary>
/// <remarks>
/// Every step runs in a fixed order: semantic frames by index, bodies by entity slot, clusters by their first body. So two
/// runs with the same input make the same frames in the same order. The merge test compares every pair of bodies in a
/// semantic frame that has a numeric frame or a body that needs one, which costs the square of their count.
/// </remarks>
internal sealed class NumericClustering(NumericFrameOptions options, EntityTable entities, Attachments attachments, FrameTree frames,
    IPhysicsBackend physics, INumericFrameHost host)
{
    /// <summary>One entity with a body that is not attached, as the sync point left it.</summary>
    private struct Body
    {
        public Entity Entity;
        public FrameHandle Semantic;
        public FrameHandle Home;
        /// <summary>The position in <see cref="Semantic"/>, in meters.</summary>
        public Double3 Position;
        /// <summary>The velocity relative to <see cref="Semantic"/>, in m/s.</summary>
        public Double3 Velocity;
        /// <summary>The radius of the body's bounding sphere, in meters.</summary>
        public double Radius;
        public double Mass;
        /// <summary>A static body cannot take a velocity, so its cluster cannot fall.</summary>
        public bool Static;
        /// <summary>It owns a frame that physics does not drive, so it stays in its semantic frame's own system.</summary>
        public bool Fixed;
        /// <summary>The order in which the gather found it: entity slot order.</summary>
        public int Order;
    }

    /// <summary>A group of bodies of one semantic frame that the merge test links.</summary>
    private struct Cluster
    {
        /// <summary>The range of the cluster's bodies in <see cref="clustered"/>.</summary>
        public int Start, End;
        /// <summary>The mass-weighted velocity of the bodies that can move, relative to the semantic frame; zero when anchored.</summary>
        public Double3 Velocity;
        /// <summary>The center of mass, in the semantic frame.</summary>
        public Double3 Center;
        /// <summary>True when a body of the cluster is static, so the cluster cannot fall.</summary>
        public bool Anchored;
        /// <summary>True when a body of the cluster stays in the semantic frame's own system, so the whole cluster does.</summary>
        public bool Fixed;
        /// <summary>The numeric frame that holds the most of the cluster's mass, or null.</summary>
        public FrameHandle Frame;
        public double FrameMass;
        /// <summary>True when the cluster keeps <see cref="Frame"/>; false when another cluster with more mass in it does.</summary>
        public bool Keeps;
        /// <summary>The mode that the cluster's frame gets, or None for the semantic frame's own system.</summary>
        public NumericMode Mode;
    }

    private static readonly Comparison<Body> BySemanticThenOrder = (a, b) =>
        a.Semantic.Index != b.Semantic.Index ? a.Semantic.Index.CompareTo(b.Semantic.Index) : a.Order.CompareTo(b.Order);

    private readonly List<Body> bodies = new();
    // Union-find parents by index in bodies, the cluster of each body, and the bodies in cluster order.
    private int[] links = [];
    private int[] clusterOf = [];
    private readonly List<int> clustered = new();
    private readonly List<Cluster> clusters = new();
    // How long the members of each numeric frame have been apart, in seconds. A frame that is together has no entry.
    private readonly Dictionary<FrameHandle, double> apart = new();
    // The cluster that keeps each numeric frame this run.
    private readonly Dictionary<FrameHandle, int> keepers = new();
    private readonly List<FrameHandle> homes = new();
    private readonly List<double> homeMasses = new();
    private readonly List<Entity> group = new();
    private readonly HashSet<FrameHandle> occupied = new();
    private readonly List<FrameHandle> frameScratch = new();
    private readonly List<FrameHandle> childScratch = new();

    /// <summary>Runs the membership rules once, over a sync point of <paramref name="dt"/> seconds.</summary>
    public void Run(double dt)
    {
        Gather();
        for (int start = 0; start < bodies.Count;)
        {
            int end = start + 1;
            while (end < bodies.Count && bodies[end].Semantic == bodies[start].Semantic)
                end++;
            if (NeedsWork(start, end))
                ClusterSemanticFrame(start, end, dt);
            start = end;
        }
        host.FlushPhysicsChanges();
        if (CollectEmptyFrames())
            host.FlushPhysicsChanges();
    }

    private void Gather()
    {
        bodies.Clear();
        foreach (Entity entity in entities.LiveEntities)
        {
            if (entities.Body(entity) is not { } desc || !attachments.ParentOf(entity).IsNull)
                continue;
            ref readonly TransformRow row = ref entities.BackRow(entity);
            if (row.Frame.IsNull)
                continue;
            FrameHandle own = entities.OwnedFrame(entity);
            bodies.Add(new Body
            {
                Entity = entity,
                Semantic = row.Frame,
                Home = row.PhysicsHome,
                Position = row.Pose.Position,
                Velocity = frames.HomeVelocity(row.PhysicsHome, row.Frame) + row.LinearVelocity,
                Radius = physics.BoundingRadius(desc.Shape),
                Mass = desc.Mass > 0 ? desc.Mass : 1,
                Static = desc.Motion == BodyMotion.Static,
                Fixed = !own.IsNull && frames.Driver(own) != MotionDriver.Physics,
                Order = bodies.Count,
            });
        }
        bodies.Sort(BySemanticThenOrder);
        if (links.Length < bodies.Count)
        {
            links = new int[Math.Max(bodies.Count, 2 * links.Length)];
            clusterOf = new int[links.Length];
        }
    }

    /// <summary>
    /// True when a body of the range is in a numeric frame or needs one by itself. A cluster's common speed and center
    /// never pass those of its farthest and fastest body, so a range where no body needs a frame has no cluster that does.
    /// </summary>
    private bool NeedsWork(int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            Body body = bodies[i];
            if (body.Home != body.Semantic)
                return true;
            if (!body.Fixed && ((!body.Static && body.Velocity.Length >= options.CreateSpeed) || body.Position.Length >= options.CreateDistance))
                return true;
        }
        return false;
    }

    private void ClusterSemanticFrame(int start, int end, double dt)
    {
        for (int i = start; i < end; i++)
            links[i] = i;
        for (int i = start; i < end; i++)
            for (int j = i + 1; j < end; j++)
                if (MayTouch(bodies[i], bodies[j]))
                    Union(i, j);
        HoldFramesTogether(start, end, dt);
        BuildClusters(start, end);
        ChooseKeepers();
        // Every shift lands before any move: a body that leaves a shifted frame is converted from its shifted state.
        for (int c = 0; c < clusters.Count; c++)
            if (clusters[c].Keeps)
                AdjustFrame(clusters[c], start, end);
        for (int c = 0; c < clusters.Count; c++)
            MoveHome(clusters[c]);
    }

    /// <summary>
    /// The merge test: true when the bounding spheres of the two bodies, grown by their relative speed times the
    /// lookahead time, overlap. Two bodies of one numeric frame stay linked up to the split margin farther.
    /// </summary>
    private bool MayTouch(in Body a, in Body b)
    {
        double reach = a.Radius + b.Radius + (a.Velocity - b.Velocity).Length * options.LookaheadTime;
        if (a.Home == b.Home && a.Home != a.Semantic)
            reach += options.SplitMargin;
        return (a.Position - b.Position).LengthSquared <= reach * reach;
    }

    /// <summary>
    /// Keeps the members of each numeric frame in one cluster until they have been apart for the split cooldown. Then they
    /// may split, and the frame's clock starts again.
    /// </summary>
    private void HoldFramesTogether(int start, int end, double dt)
    {
        homes.Clear();
        for (int i = start; i < end; i++)
            if (bodies[i].Home != bodies[i].Semantic && !homes.Contains(bodies[i].Home))
                homes.Add(bodies[i].Home);
        foreach (FrameHandle frame in homes)
        {
            int first = -1;
            bool together = true;
            for (int i = start; i < end && together; i++)
            {
                if (bodies[i].Home != frame)
                    continue;
                if (first < 0)
                    first = i;
                else
                    together = Find(i) == Find(first);
            }
            if (together)
            {
                apart.Remove(frame);
                continue;
            }
            double time = apart.GetValueOrDefault(frame) + dt;
            if (time >= options.SplitCooldown)
            {
                apart.Remove(frame);
                continue;
            }
            apart[frame] = time;
            for (int i = first + 1; i < end; i++)
                if (bodies[i].Home == frame)
                    Union(first, i);
        }
    }

    /// <summary>Numbers the clusters by their first body, lists their bodies in that order, and sums their motion.</summary>
    private void BuildClusters(int start, int end)
    {
        clusters.Clear();
        for (int i = start; i < end; i++)
            clusterOf[i] = -1;
        for (int i = start; i < end; i++)
        {
            int root = Find(i);
            if (clusterOf[root] < 0)
            {
                clusterOf[root] = clusters.Count;
                clusters.Add(default);
            }
            clusterOf[i] = clusterOf[root];
        }
        // A counting sort: each cluster's range starts after the bodies of the clusters before it.
        clustered.Clear();
        for (int i = start; i < end; i++)
        {
            clustered.Add(0);
            clusters[clusterOf[i]] = clusters[clusterOf[i]] with { End = clusters[clusterOf[i]].End + 1 };
        }
        int next = 0;
        for (int c = 0; c < clusters.Count; c++)
        {
            int size = clusters[c].End;
            clusters[c] = new Cluster { Start = next, End = next };
            next += size;
        }
        for (int i = start; i < end; i++)
        {
            Cluster cluster = clusters[clusterOf[i]];
            clustered[cluster.End] = i;
            clusters[clusterOf[i]] = cluster with { End = cluster.End + 1 };
        }
        for (int c = 0; c < clusters.Count; c++)
            clusters[c] = Summarize(clusters[c].Start, clusters[c].End);
    }

    private Cluster Summarize(int first, int last)
    {
        var cluster = new Cluster { Start = first, End = last };
        Double3 momentum = Double3.Zero, moment = Double3.Zero;
        double movingMass = 0, mass = 0;
        homes.Clear();
        homeMasses.Clear();
        for (int k = first; k < last; k++)
        {
            Body body = bodies[clustered[k]];
            cluster.Anchored |= body.Static;
            cluster.Fixed |= body.Fixed;
            mass += body.Mass;
            moment += body.Position * body.Mass;
            if (!body.Static)
            {
                movingMass += body.Mass;
                momentum += body.Velocity * body.Mass;
            }
            if (body.Home == body.Semantic)
                continue;
            int h = homes.IndexOf(body.Home);
            if (h < 0)
            {
                homes.Add(body.Home);
                homeMasses.Add(body.Mass);
            }
            else
            {
                homeMasses[h] += body.Mass;
            }
        }
        cluster.Center = moment / mass;
        cluster.Velocity = cluster.Anchored || movingMass == 0 ? Double3.Zero : momentum / movingMass;
        for (int h = 0; h < homes.Count; h++)
        {
            if (homeMasses[h] > cluster.FrameMass)
            {
                cluster.Frame = homes[h];
                cluster.FrameMass = homeMasses[h];
            }
        }
        cluster.Mode = ChooseMode(cluster);
        return cluster;
    }

    /// <summary>
    /// The mode of the cluster's frame, or None for the semantic frame's own system. A cluster that is in a numeric frame
    /// leaves it only below the lower thresholds, and a resting frame starts to fall only above the threshold to get one.
    /// </summary>
    private NumericMode ChooseMode(in Cluster cluster)
    {
        if (cluster.Fixed)
            return NumericMode.None;
        double speed = cluster.Velocity.Length, distance = cluster.Center.Length;
        bool fast = !cluster.Anchored && speed >= options.CreateSpeed;
        return (cluster.Frame.IsNull ? NumericMode.None : frames.Numeric(cluster.Frame)) switch
        {
            NumericMode.Falling when !cluster.Anchored && speed >= options.LeaveSpeed => NumericMode.Falling,
            NumericMode.Falling or NumericMode.Resting => fast ? NumericMode.Falling : distance >= options.LeaveDistance ? NumericMode.Resting : NumericMode.None,
            _ => fast ? NumericMode.Falling : distance >= options.CreateDistance ? NumericMode.Resting : NumericMode.None,
        };
    }

    /// <summary>
    /// Gives each numeric frame to the cluster with the most mass in it, or the first such cluster on a tie. The other
    /// clusters in it split off.
    /// </summary>
    private void ChooseKeepers()
    {
        keepers.Clear();
        for (int c = 0; c < clusters.Count; c++)
        {
            Cluster cluster = clusters[c];
            if (cluster.Mode == NumericMode.None || cluster.Frame.IsNull)
                continue;
            if (keepers.TryGetValue(cluster.Frame, out int other))
            {
                if (clusters[other].FrameMass >= cluster.FrameMass)
                    continue;
                clusters[other] = clusters[other] with { Keeps = false };
            }
            keepers[cluster.Frame] = c;
            clusters[c] = cluster with { Keeps = true };
        }
    }

    /// <summary>
    /// Gives the kept frame its mode, its members' common velocity when it passes the drain speed or when the frame starts
    /// to fall, no velocity when it comes to rest, and its members' center of mass as its origin when they drift far.
    /// </summary>
    private void AdjustFrame(in Cluster cluster, int start, int end)
    {
        FrameHandle frame = cluster.Frame;
        NumericMode mode = frames.Numeric(frame);
        Double3 position = frames.LocalPose(frame).Position;
        Double3 velocity = frames.LinearVelocity(frame);
        Double3 newVelocity = cluster.Mode == NumericMode.Resting ? Double3.Zero
            : mode == NumericMode.Resting || (cluster.Velocity - velocity).Length > options.DrainSpeed ? cluster.Velocity : velocity;
        Double3 newPosition = (cluster.Center - position).Length > options.RecenterDistance ? cluster.Center : position;
        if (cluster.Mode == NumericMode.Falling && mode == NumericMode.Resting)
            frames.SetNumericMode(frame, NumericMode.Falling);
        if (newPosition != position || newVelocity != velocity)
        {
            group.Clear();
            for (int i = start; i < end; i++)
                if (bodies[i].Home == frame)
                    group.Add(bodies[i].Entity);
            host.ShiftNumericFrame(frame, newPosition - position, newVelocity - velocity, group);
        }
        if (cluster.Mode == NumericMode.Resting && mode == NumericMode.Falling)
            frames.SetNumericMode(frame, NumericMode.Resting);
    }

    /// <summary>Moves every body of the cluster that is not in its home there, making a new frame when the cluster needs one.</summary>
    private void MoveHome(in Cluster cluster)
    {
        FrameHandle semantic = bodies[clustered[cluster.Start]].Semantic;
        FrameHandle home = cluster.Mode == NumericMode.None ? semantic : cluster.Keeps ? cluster.Frame : FrameHandle.Null;
        group.Clear();
        for (int k = cluster.Start; k < cluster.End; k++)
        {
            Body body = bodies[clustered[k]];
            if (!body.Fixed && body.Home != home)
                group.Add(body.Entity);
        }
        if (group.Count == 0)
            return;
        if (home.IsNull)
        {
            Double3 velocity = cluster.Mode == NumericMode.Falling ? cluster.Velocity : Double3.Zero;
            home = host.CreateNumericFrame(semantic, cluster.Center, velocity, cluster.Mode);
        }
        host.MoveBodiesHome(group, home);
        for (int k = cluster.Start; k < cluster.End; k++)
        {
            int i = clustered[k];
            if (!bodies[i].Fixed)
                bodies[i] = bodies[i] with { Home = home };
        }
    }

    /// <summary>Destroys every numeric frame that no body calls home and that holds no frame. True when it destroyed one.</summary>
    private bool CollectEmptyFrames()
    {
        occupied.Clear();
        foreach (Body body in bodies)
            if (body.Home != body.Semantic)
                occupied.Add(body.Home);
        frameScratch.Clear();
        frames.GetAllInTree(frameScratch);
        bool destroyed = false;
        foreach (FrameHandle frame in frameScratch)
        {
            if (!frames.IsNumeric(frame) || occupied.Contains(frame))
                continue;
            childScratch.Clear();
            frames.GetTreeChildren(frame, childScratch);
            if (childScratch.Count > 0)
                continue;
            host.DestroyEmptyNumericFrame(frame);
            apart.Remove(frame);
            destroyed = true;
        }
        return destroyed;
    }

    private int Find(int i)
    {
        while (links[i] != i)
        {
            links[i] = links[links[i]];
            i = links[i];
        }
        return i;
    }

    /// <summary>Joins the two sets under the root with the lower index, so that the result never depends on the order of the calls.</summary>
    private void Union(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra < rb)
            links[rb] = ra;
        else if (rb < ra)
            links[ra] = rb;
    }
}
