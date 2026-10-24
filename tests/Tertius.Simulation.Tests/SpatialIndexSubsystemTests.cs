using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The spatial index on its own, over a hand-built entity table and frame tree. A radius query must give the same
/// answer as a search through every entity, whatever the grid does to narrow it; entities must be found where they
/// are now and not where they were; and a released entity must be gone.
/// </summary>
public class SpatialIndexSubsystemTests
{
    /// <summary>
    /// The pieces of the sync point that the index depends on: live entities with front rows, and the step that brings
    /// the index up to date once the rows have been copied forward.
    /// </summary>
    private sealed class Scene
    {
        public readonly EntityTable Entities = TableSupport.NewTable();
        public readonly FrameTree Frames = new();
        public readonly SpatialIndex Spatial;
        public readonly FrameHandle Root;

        // Frame owners are entity handles that the table never hands out, so that no frame owner is a live entity here.
        private uint nextOwnerSlot = 100_000;

        /// <param name="onWorkers">
        /// If true, every update finds the cells of the moved entities on four workers, however few entities moved.
        /// </param>
        public Scene(bool onWorkers = false)
        {
            Spatial = onWorkers
                ? new(Entities, Frames, new() { MaxDegreeOfParallelism = 4 }) { ParallelThreshold = 1 }
                : new SpatialIndex(Entities, Frames);
            Root = NewFrame(FrameHandle.Null);
        }

        /// <summary>
        /// A scene whose index was given four workers on <paramref name="workers"/>, and finds the cells on them from
        /// <paramref name="threshold"/> moved entities on.
        /// </summary>
        public Scene(TaskScheduler workers, int threshold)
        {
            Spatial = new(Entities, Frames, new() { MaxDegreeOfParallelism = 4, TaskScheduler = workers })
            {
                ParallelThreshold = threshold
            };
            Root = NewFrame(FrameHandle.Null);
        }

        public FrameHandle NewFrame(FrameHandle parent) =>
            Frames.Create(frameOwner: new(nextOwnerSlot++, 1), parent: parent, localPose: DPose.Identity,
                frameBounds: FrameBounds.Sphere(1e12f));

        /// <summary>Makes an entity live at a position. The index does not know about it until <see cref="Settle"/>.</summary>
        public Entity Add(FrameHandle frame, Double3 position)
        {
            Entity entity = Entities.Reserve();
            Entities.Activate(entity: entity, row: RowAt(frame, position), render: new(), components: [], wants: [],
                body: null, bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
            return entity;
        }

        /// <summary>Writes the back row, as the sync point does. The front row, and so every query, does not change until
        /// <see cref="Settle"/>.</summary>
        public void Move(Entity entity, FrameHandle frame, Double3 position) =>
            Entities.SetBackTransform(entity, RowAt(frame, position));

        /// <summary>Copies the dirty rows forward and updates the index: the end of a sync point.</summary>
        public void Settle()
        {
            Entities.CopyDirtyRows();
            Spatial.Update();
            Spatial.AssertInvariants();
        }

        public List<Entity> Find(FrameHandle frame, Double3 center, double radius)
        {
            var found = new List<Entity>();
            Spatial.QueryRadius(frame: frame, center: center, radius: radius, results: found);
            return found;
        }

        /// <summary>
        /// The entities of the cells around a sphere that are within its radius, unsorted: cell by cell, and in each
        /// cell in the order that the cell lists them, as <see cref="SpatialIndex.CollectNear"/> hands them out.
        /// </summary>
        public Entity[] InCellOrder(FrameHandle frame, Double3 center, double radius)
        {
            var into = new Entity[4096];
            return into[..Spatial.CollectNear(frame: frame, center: center, radius: radius, into: into)];
        }

        private static TransformRow RowAt(FrameHandle frame, Double3 position) =>
            new() { Frame = frame, Pose = new(position, DQuaternion.Identity) };
    }

    // ---- A radius query is a search through every entity -------------------------------------------------------------

    // The expected answer is worked out here with the plain distance formula, entity by entity, never with the index.
    // Entities are added in slot order, so the entities within the radius, in the order of the list, are sorted by slot.
    [Theory]
    [InlineData(1)]
    [InlineData(20261010)]
    [InlineData(777)]
    public void ARadiusQueryFindsTheSameEntitiesAsASearchThroughEverySeededPoint(int seed)
    {
        var scene = new Scene();
        var random = new Random(seed);
        var points = new List<(Entity Entity, Double3 Position)>();
        for (int i = 0; i < 2000; i++)
        {
            // A cube 1000 m on a side around the origin, so that cells with negative coordinates are used too.
            var position = new Double3(random.NextDouble() * 1000 - 500, random.NextDouble() * 1000 - 500,
                random.NextDouble() * 1000 - 500);
            points.Add((scene.Add(scene.Root, position), position));
        }

        scene.Settle();

        // The radii run from 1 m to 687 m. The small ones are answered from the few cells around the center, and the
        // large ones from every occupied cell, so both ways of walking the grid are checked against the same search.
        int nonEmpty = 0;
        for (int query = 0; query < 50; query++)
        {
            var center = new Double3(random.NextDouble() * 1200 - 600, random.NextDouble() * 1200 - 600,
                random.NextDouble() * 1200 - 600);
            double radius = 1 + query * 14;

            Entity[] expected =
            [
                .. points
                    .Where(p => Square(p.Position.X - center.X) + Square(p.Position.Y - center.Y) +
                        Square(p.Position.Z - center.Z) <= radius * radius)
                    .Select(p => p.Entity)
            ];
            List<Entity> found = scene.Find(scene.Root, center, radius);

            found.Should().Equal(expected, $"seed {seed}, query {query}: center {center}, radius {radius}");
            nonEmpty += expected.Length > 0 ? 1 : 0;
        }

        nonEmpty.Should().BeGreaterThan(30, "the queries must find entities, or agreeing on 'nothing' proves little");
    }

    private static double Square(double value) => value * value;

    [Fact]
    public void QueriesFromSeveralThreadsAtOnceGiveTheSameAnswersAsOneAtATime()
    {
        // During a tick, every worker queries the index at the same time. It does not change then, so they must not disturb each other.
        var scene = new Scene();
        var random = new Random(31);
        for (int i = 0; i < 1000; i++)
            scene.Add(scene.Root, new(random.NextDouble() * 400, random.NextDouble() * 400, random.NextDouble() * 400));
        scene.Settle();
        Double3[] centers =
        [
            .. Enumerable.Range(0, 64).Select(_ =>
                new Double3(random.NextDouble() * 400, random.NextDouble() * 400, random.NextDouble() * 400))
        ];
        Entity[][] oneAtATime = [.. centers.Select(center => scene.Find(scene.Root, center, 60).ToArray())];

        var together = new Entity[centers.Length][];
        Parallel.For(fromInclusive: 0, toExclusive: centers.Length,
            parallelOptions: new() { MaxDegreeOfParallelism = 8 },
            body: i => together[i] = [.. scene.Find(scene.Root, centers[i], 60)]);

        for (int i = 0; i < centers.Length; i++)
            together[i].Should().Equal(oneAtATime[i], $"query {i}");
        oneAtATime.Sum(found => found.Length).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ARadiusQueryListsEntitiesByEntityIndexWhateverOrderTheyEnteredTheirCell()
    {
        var scene = new Scene();
        Entity[] spawned = [.. Enumerable.Range(0, 5).Select(_ => scene.Add(scene.Root, new(500, 0, 0)))];
        scene.Settle();

        // Move them into one new cell in the reverse of slot order, so that the cell lists them from the highest slot down.
        foreach (Entity entity in spawned.Reverse())
            scene.Move(entity, scene.Root, new(1, 1, 1));
        scene.Settle();

        scene.Find(scene.Root, new(1, 1, 1), 5).Should().Equal(spawned);
    }

    [Fact]
    public void ARadiusQueryClearsTheListItIsGivenBeforeFilling()
    {
        var scene = new Scene();
        Entity near = scene.Add(scene.Root, new(1, 0, 0));
        scene.Settle();
        Entity leftover = new(77, 1);
        var results = new List<Entity> { leftover };

        scene.Spatial.QueryRadius(frame: scene.Root, center: Double3.Zero, radius: 10, results: results);
        results.Should().Equal(near);

        scene.Spatial.QueryRadius(frame: scene.Root, center: new(900, 0, 0), radius: 10, results: results);
        results.Should().BeEmpty("nothing is near the second center, and the first answer must not stay in the list");
    }

    // ---- The exact edge of the radius ----------------------------------------------------------------------------------

    [Fact]
    public void AnEntityExactlyOnTheRadiusIsFoundAndOneJustBeyondItIsNot()
    {
        var scene = new Scene();
        // 3-4-5 triangle in the XY plane: the first is exactly 5 m from the origin, the second a little farther.
        Entity onTheEdge = scene.Add(scene.Root, new(3, 4, 0));
        scene.Add(scene.Root, new(3, 4.001, 0));
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 5).Should().Equal(onTheEdge);
    }

    [Fact]
    public void ARadiusOfZeroFindsOnlyEntitiesExactlyAtTheCenter()
    {
        var scene = new Scene();
        Entity atCenter = scene.Add(scene.Root, new(10, 20, 30));
        scene.Add(scene.Root, new(10, 20, 30.001));
        scene.Settle();

        scene.Find(scene.Root, new(10, 20, 30), 0).Should().Equal(atCenter);
    }

    [Fact]
    public void ARadiusLargerThanTheWholeFrameFindsEveryEntityInIt()
    {
        var scene = new Scene();
        Entity[] spawned =
        [
            scene.Add(scene.Root, new(-9000, 4, 12)),
            scene.Add(scene.Root, new(250, -80000, 3)),
            scene.Add(scene.Root, new(0, 0, 7e6))
        ];
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 1e12).Should().Equal(spawned);
    }

    // ---- Cells --------------------------------------------------------------------------------------------------------

    [Fact]
    public void AQuerySphereThatCrossesACellEdgeFindsEntitiesOnBothSidesOfIt()
    {
        var scene = new Scene();
        double edge = SpatialGrid.CellSize;
        Entity before = scene.Add(scene.Root, new(edge - 0.5, 0, 0));
        Entity after = scene.Add(scene.Root, new(edge + 0.5, 0, 0));
        Entity farAfter = scene.Add(scene.Root, new(edge + 3, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new(edge, 0, 0), 1).Should().Equal(before, after);
        scene.Find(scene.Root, new(edge, 0, 0), 5).Should().Equal(before, after, farAfter);
    }

    [Fact]
    public void AQuerySphereThatCrossesTheOriginFindsEntitiesInTheCellsOfNegativeCoordinates()
    {
        var scene = new Scene();
        Entity below = scene.Add(scene.Root, new(-0.5, -0.5, -0.5));
        Entity above = scene.Add(scene.Root, new(0.5, 0.5, 0.5));
        Entity farBelow = scene.Add(scene.Root, new(-3 * SpatialGrid.CellSize, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 1).Should().Equal(below, above);
        scene.Find(scene.Root, new(-3 * SpatialGrid.CellSize, 0, 0), 1).Should().Equal(farBelow);
    }

    [Fact]
    public void AnEntityThatMovesToAnotherCellIsFoundThereAndNoLongerWhereItWas()
    {
        var scene = new Scene();
        Entity mover = scene.Add(scene.Root, new(5, 0, 0));
        scene.Settle();
        double farAway = 10 * SpatialGrid.CellSize;

        scene.Move(mover, scene.Root, new(farAway, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new(5, 0, 0), 3).Should().BeEmpty();
        scene.Find(scene.Root, new(farAway, 0, 0), 3).Should().Equal(mover);
    }

    [Fact]
    public void AnEntityThatMovesWithinItsCellIsFoundAtItsNewPositionOnly()
    {
        var scene = new Scene();
        Entity mover = scene.Add(scene.Root, new(2, 0, 0));
        scene.Settle();

        scene.Move(mover, scene.Root, new(9, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new(2, 0, 0), 3).Should()
            .BeEmpty("the exact distance test uses the new position even though the cell is the same");
        scene.Find(scene.Root, new(9, 0, 0), 3).Should().Equal(mover);
    }

    [Fact]
    public void AnEntityThatMovesToAnotherFrameIsFoundInThatFrameOnly()
    {
        var scene = new Scene();
        FrameHandle station = scene.NewFrame(scene.Root);
        Entity traveler = scene.Add(scene.Root, new(4, 0, 0));
        scene.Settle();

        scene.Move(traveler, station, new(4, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new(4, 0, 0), 2).Should().BeEmpty();
        scene.Find(station, new(4, 0, 0), 2).Should().Equal(traveler);
    }

    [Fact]
    public void AQueryOfOneFrameNeverFindsTheEntitiesOfAnotherFrameAtTheSamePosition()
    {
        var scene = new Scene();
        FrameHandle station = scene.NewFrame(scene.Root);
        Entity inRoot = scene.Add(scene.Root, new(1, 1, 1));
        Entity inStation = scene.Add(station, new(1, 1, 1));
        scene.Settle();

        scene.Find(scene.Root, new(1, 1, 1), 5).Should().Equal(inRoot);
        scene.Find(station, new(1, 1, 1), 5).Should().Equal(inStation);
    }

    [Fact]
    public void TheOwnerOfARootFrameIsInNoGrid()
    {
        // It is in no frame, so its position has no meaning in any grid.
        var scene = new Scene();
        scene.Add(FrameHandle.Null, Double3.Zero);
        Entity ordinary = scene.Add(scene.Root, Double3.Zero);
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(ordinary);
    }

    [Fact]
    public void QueriesSeeTheStateOfTheLastUpdateUntilTheNextOneEvenIfRowsChangedMeanwhile()
    {
        // During a tick, gameplay writes the back rows. Queries in that tick must see where everything was at its start.
        var scene = new Scene();
        Entity mover = scene.Add(scene.Root, new(2, 0, 0));
        scene.Settle();

        scene.Move(mover, scene.Root, new(500, 0, 0));

        scene.Find(scene.Root, new(2, 0, 0), 3).Should().Equal(mover);
        scene.Find(scene.Root, new(500, 0, 0), 3).Should().BeEmpty();
        scene.Settle();
        scene.Find(scene.Root, new(2, 0, 0), 3).Should().BeEmpty();
        scene.Find(scene.Root, new(500, 0, 0), 3).Should().Equal(mover);
    }

    [Fact]
    public void AnEntityThatLeavesACellAndComesBackIsListedOnceInIt()
    {
        // Moving out of a cell and later back into it must not leave the cell with a stale or duplicate entry.
        var scene = new Scene();
        Entity wanderer = scene.Add(scene.Root, new(1, 0, 0));
        Entity stayer = scene.Add(scene.Root, new(2, 0, 0));
        scene.Settle();

        scene.Move(wanderer, scene.Root, new(10 * SpatialGrid.CellSize, 0, 0));
        scene.Settle();
        scene.Move(wanderer, scene.Root, new(1, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new(1.5, 0, 0), 3).Should().Equal(wanderer, stayer);
    }

    [Fact]
    public void AnEntityThatLeavesItsOwnCellForAnOccupiedOneLeavesNoTraceInTheCellsFilledAfterIt()
    {
        // The cell that its only entity leaves for an occupied cell is gone, and its storage goes back to the grid for a
        // later cell. That later cell must hold only its own entities.
        var scene = new Scene();
        double cell = SpatialGrid.CellSize;
        Entity leaver = scene.Add(scene.Root, new(1, 0, 0));
        Entity host = scene.Add(scene.Root, new(10 * cell + 1, 0, 0));
        scene.Settle();

        scene.Move(leaver, scene.Root, new(10 * cell + 2, 0, 0));
        scene.Settle();
        Entity newcomer = scene.Add(scene.Root, new(20 * cell + 1, 0, 0));
        scene.Settle();

        scene.InCellOrder(scene.Root, new(10 * cell, 0, 0), 10 * cell + 10).Should().BeEquivalentTo(
            [leaver, host, newcomer],
            "every cell of the frame lists each of its entities once");
        scene.Find(scene.Root, new(1, 0, 0), 3).Should().BeEmpty();
        scene.Find(scene.Root, new(10 * cell + 1.5, 0, 0), 3).Should().Equal(leaver, host);
        scene.Find(scene.Root, new(20 * cell + 1, 0, 0), 3).Should().Equal(newcomer);
    }

    [Fact]
    public void TwoEntitiesAloneInTheirCellsThatTradeCellsInOneUpdateAreEachFoundInTheOthersCell()
    {
        var scene = new Scene();
        Double3 west = new(1, 0, 0), east = new(10 * SpatialGrid.CellSize + 1, 0, 0);
        Entity first = scene.Add(scene.Root, west);
        Entity second = scene.Add(scene.Root, east);
        scene.Settle();

        scene.Move(first, scene.Root, east);
        scene.Move(second, scene.Root, west);
        scene.Settle();

        scene.Find(scene.Root, west, 1).Should().Equal(second);
        scene.Find(scene.Root, east, 1).Should().Equal(first);
    }

    [Fact]
    public void AnEntityThatLeavesASharedCellForAnEmptyOneLeavesTheOthersFoundWhereTheyWere()
    {
        var scene = new Scene();
        Entity[] sharing = [.. Enumerable.Range(1, 3).Select(i => scene.Add(scene.Root, new(i, 0, 0)))];
        Double3 farAway = new(10 * SpatialGrid.CellSize, 0, 0);
        scene.Settle();

        scene.Move(sharing[1], scene.Root, farAway);
        scene.Settle();

        scene.Find(scene.Root, new(2, 0, 0), 5).Should().Equal(sharing[0], sharing[2]);
        scene.Find(scene.Root, farAway, 1).Should().Equal(sharing[1]);
    }

    [Fact]
    public void AnEntityThatLeavesEveryFrameIsInNoGridAnyMore()
    {
        // An entity whose front row names no frame is in no grid, like the owner of a root frame.
        var scene = new Scene();
        Entity leaver = scene.Add(scene.Root, new(1, 0, 0));
        Entity stayer = scene.Add(scene.Root, new(2, 0, 0));
        scene.Settle();

        scene.Move(leaver, FrameHandle.Null, new(1, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(stayer);
    }

    // ---- An update on the workers -------------------------------------------------------------------------------------

    // The workers only find the new cell of each moved entity; this thread then changes the cells in the order in which
    // the entities moved. So the grids must end in the state of the update on this thread alone, down to the order inside
    // each cell, which CollectNear hands out unsorted. The scene is dense, about four entities to a cell, and a third of
    // them jump in each round, so that many enter the same cell in the same update, where any other order would show.
    [Theory]
    [InlineData(3)]
    [InlineData(41)]
    [InlineData(20261018)]
    public void AnUpdateOnTheWorkersLeavesEveryCellListingTheSameEntitiesInTheSameOrderAsAnUpdateOnOneThread(int seed)
    {
        var alone = new Scene();
        var onWorkers = new Scene(onWorkers: true);
        Scene[] scenes = [alone, onWorkers];
        var random = new Random(seed);
        // Both scenes make the same frames and the same entities in the same order, so the handles are the same in both.
        FrameHandle[] frames = [alone.Root, alone.NewFrame(alone.Root), alone.NewFrame(alone.Root)];
        onWorkers.NewFrame(onWorkers.Root).Should().Be(frames[1]);
        onWorkers.NewFrame(onWorkers.Root).Should().Be(frames[2]);
        var frameOf = new Dictionary<Entity, FrameHandle>();
        var live = new List<Entity>();
        var cube = new Double3(48, 48, 48);

        // A cube of 3 by 3 by 3 cells in each frame.
        Double3 InCube() => new(random.NextDouble() * 96, random.NextDouble() * 96, random.NextDouble() * 96);

        void Spawn()
        {
            FrameHandle frame = frames[random.Next(frames.Length)];
            Double3 position = InCube();
            Entity entity = alone.Add(frame, position);
            onWorkers.Add(frame, position).Should().Be(entity);
            frameOf[entity] = frame;
            live.Add(entity);
        }

        for (int i = 0; i < 300; i++)
            Spawn();
        foreach (Scene scene in scenes)
            scene.Settle();

        for (int round = 0; round < 40; round++)
        {
            for (int i = 0; i < 100; i++)
            {
                Entity mover = live[random.Next(live.Count)];
                FrameHandle frame = random.Next(5) == 0 ? frames[random.Next(frames.Length)] : frameOf[mover];
                Double3 position = random.Next(3) == 0
                    ? InCube()
                    : alone.Entities.BackRow(mover).Pose.Position +
                      new Double3(random.NextDouble() - 0.5, random.NextDouble() - 0.5, 0);
                foreach (Scene scene in scenes)
                    scene.Move(mover, frame, position);
                frameOf[mover] = frame;
            }

            for (int i = 0; i < 5; i++)
            {
                int index = random.Next(live.Count);
                Entity doomed = live[index];
                live.RemoveAt(index);
                frameOf.Remove(doomed);
                foreach (Scene scene in scenes)
                    scene.Entities.Release(doomed).Should().BeTrue();
                Spawn();
            }

            foreach (Scene scene in scenes)
                scene.Settle();

            foreach (FrameHandle frame in frames)
            {
                Entity[] expected = alone.InCellOrder(frame, cube, 150);
                onWorkers.InCellOrder(frame, cube, 150).Should()
                    .Equal(expected, $"seed {seed}, round {round}, frame {frame}");
                expected.Should().HaveCount(live.Count(entity => frameOf[entity] == frame),
                    "the sphere holds every entity of the frame");
            }
        }
    }

    [Fact]
    public void AnUpdateWithFewerMovedEntitiesThanTheThresholdFindsTheirCellsOnTheCallingThread()
    {
        var workers = new CountingScheduler();
        var scene = new Scene(workers, threshold: 10);
        Entity[] spawned = [.. Enumerable.Range(0, 9).Select(i => scene.Add(scene.Root, new(i * 100, 0, 0)))];

        scene.Settle();

        workers.Queued.Should().Be(0);
        scene.Find(scene.Root, Double3.Zero, 1e4).Should().Equal(spawned);
    }

    [Fact]
    public void AnUpdateWithAsManyMovedEntitiesAsTheThresholdFindsTheirCellsOnTheWorkersTheIndexWasGiven()
    {
        var workers = new CountingScheduler();
        var scene = new Scene(workers, threshold: 10);
        Entity[] spawned = [.. Enumerable.Range(0, 10).Select(i => scene.Add(scene.Root, new(i * 100, 0, 0)))];

        scene.Settle();

        workers.Queued.Should().BeGreaterThan(0);
        scene.Find(scene.Root, Double3.Zero, 1e4).Should().Equal(spawned);
    }

    // The model is a plain list of (entity, frame, position). Some moves are small steps, which mostly stay in the cell, and
    // some are jumps, which leave it; some change the frame; entities also die and new ones take their slots. Each seed
    // runs once with every update on this thread and once with every update on the workers.
    [Theory]
    [InlineData(5, false)]
    [InlineData(99, false)]
    [InlineData(20261010, false)]
    [InlineData(5, true)]
    [InlineData(99, true)]
    [InlineData(20261010, true)]
    public void ARandomSequenceOfMovesSpawnsAndReleasesAgreesWithASearchThroughEveryEntity(int seed, bool onWorkers)
    {
        var scene = new Scene(onWorkers);
        var random = new Random(seed);
        FrameHandle[] frames = [scene.Root, scene.NewFrame(scene.Root), scene.NewFrame(scene.Root)];
        var model = new Dictionary<Entity, (FrameHandle Frame, Double3 Position)>();

        Double3 RandomPosition(double extent) =>
            new((random.NextDouble() - 0.5) * extent, (random.NextDouble() - 0.5) * extent,
                (random.NextDouble() - 0.5) * extent);

        void Spawn()
        {
            FrameHandle frame = frames[random.Next(frames.Length)];
            Double3 position = RandomPosition(400);
            model[scene.Add(frame, position)] = (frame, position);
        }

        for (int i = 0; i < 150; i++)
            Spawn();
        scene.Settle();

        for (int round = 0; round < 80; round++)
        {
            Entity[] entities = [.. model.Keys.Order()];
            for (int i = 0; i < 20; i++)
            {
                Entity mover = entities[random.Next(entities.Length)];
                if (!model.TryGetValue(mover, out (FrameHandle Frame, Double3 Position) now))
                    continue;
                FrameHandle frame = random.Next(7) == 0 ? frames[random.Next(frames.Length)] : now.Frame;
                Double3 position = random.Next(2) == 0 ? now.Position + RandomPosition(6) : RandomPosition(400);
                scene.Move(mover, frame, position);
                model[mover] = (frame, position);
            }

            for (int i = 0; i < 3; i++)
            {
                Entity doomed = entities[random.Next(entities.Length)];
                if (model.Remove(doomed))
                    scene.Entities.Release(doomed).Should().BeTrue();
                Spawn();
            }

            scene.Settle();

            for (int query = 0; query < 5; query++)
            {
                FrameHandle frame = frames[random.Next(frames.Length)];
                Double3 center = RandomPosition(400);
                double radius = random.Next(1, 250);
                Entity[] expected =
                [
                    .. model
                        .Where(pair => pair.Value.Frame == frame
                                       && Square(pair.Value.Position.X - center.X) +
                                       Square(pair.Value.Position.Y - center.Y) +
                                       Square(pair.Value.Position.Z - center.Z) <= radius * radius)
                        .Select(pair => pair.Key)
                        .Order()
                ];

                scene.Find(frame, center, radius).Should().Equal(expected,
                    $"seed {seed}, on workers {onWorkers}, round {round}, query {query}: frame {frame}, center {center}, radius {radius}");
            }
        }
    }

    // ---- Frames the index has not seen --------------------------------------------------------------------------------

    [Fact]
    public void AFrameWithNoEntitiesHasNothingToFind()
    {
        var scene = new Scene();
        FrameHandle empty = scene.NewFrame(scene.Root);
        scene.Add(scene.Root, Double3.Zero);
        scene.Settle();

        scene.Find(empty, Double3.Zero, 1e9).Should().BeEmpty();
    }

    [Fact]
    public void EntitiesInFramesWithHighIndicesAreFoundToo()
    {
        // The index starts with room for 16 frames and grows when a frame past that gets its first entity.
        var scene = new Scene();
        FrameHandle[] frames = [.. Enumerable.Range(0, 40).Select(_ => scene.NewFrame(scene.Root))];
        FrameHandle last = frames[^1];
        last.Index.Should().BeGreaterThan(16, "the test needs a frame past the starting room");
        Entity inLast = scene.Add(last, new(6, 0, 0));
        Entity inFirst = scene.Add(frames[0], new(6, 0, 0));
        scene.Settle();

        scene.Find(last, Double3.Zero, 10).Should().Equal(inLast);
        scene.Find(frames[0], Double3.Zero, 10).Should().Equal(inFirst);
        scene.Find(frames[20], Double3.Zero, 10).Should().BeEmpty();
    }

    // ---- Releasing ----------------------------------------------------------------------------------------------------

    [Fact]
    public void ReleasingAnEntityTakesItOutOfItsCell()
    {
        var scene = new Scene();
        Entity near = scene.Add(scene.Root, new(1, 0, 0));
        Entity far = scene.Add(scene.Root, new(500, 0, 0));
        Entity alsoNear = scene.Add(scene.Root, new(2, 0, 0));
        scene.Settle();
        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(near, alsoNear);

        scene.Entities.Release(near);

        scene.Spatial.AssertInvariants();
        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(alsoNear);
        scene.Find(scene.Root, Double3.Zero, 1000).Should().Equal(far, alsoNear);
    }

    [Fact]
    public void TheEntityThatReusesAReleasedSlotIsFoundOnlyAfterTheNextUpdate()
    {
        var scene = new Scene();
        Entity near = scene.Add(scene.Root, new(1, 0, 0));
        Entity alsoNear = scene.Add(scene.Root, new(2, 0, 0));
        scene.Settle();
        scene.Entities.Release(near);

        Entity successor = scene.Add(scene.Root, new(3, 0, 0));

        successor.Index.Should().Be(near.Index, "the slot is reused");
        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(alsoNear);
        scene.Settle();
        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(successor, alsoNear);
    }

    [Fact]
    public void ReleasingOneOfSeveralEntitiesInACellLeavesTheOthersFound()
    {
        var scene = new Scene();
        Entity[] sharing = [.. Enumerable.Range(0, 4).Select(i => scene.Add(scene.Root, new(i, 0, 0)))];
        scene.Settle();

        scene.Entities.Release(sharing[1]);

        scene.Spatial.AssertInvariants();
        scene.Find(scene.Root, new(1.5, 0, 0), 5).Should().Equal(sharing[0], sharing[2], sharing[3]);
    }

    [Fact]
    public void ACellEmptiedByReleasesTakesNewEntitiesAgain()
    {
        var scene = new Scene();
        Entity first = scene.Add(scene.Root, new(1, 0, 0));
        Entity second = scene.Add(scene.Root, new(2, 0, 0));
        scene.Settle();
        scene.Entities.Release(first);
        scene.Entities.Release(second);
        scene.Find(scene.Root, Double3.Zero, 10).Should().BeEmpty();

        Entity newcomer = scene.Add(scene.Root, new(1.5, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(newcomer);
    }

    [Fact]
    public void AnEntityReleasedBeforeTheUpdateThatWouldHavePlacedItIsNeverFound()
    {
        var scene = new Scene();
        Entity brief = scene.Add(scene.Root, new(1, 0, 0));
        Entity lasting = scene.Add(scene.Root, new(2, 0, 0));

        scene.Entities.Release(brief);
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(lasting);
    }

    // ---- Positions the grid cannot represent --------------------------------------------------------------------------

    [Fact]
    public void PositionsFarBeyondAnyCellStillGetTheirExactDistanceTest()
    {
        // Past 3e16 m the cell number is clamped so that it fits a long. The entities then share a cell, and the exact
        // distance test must still tell them apart.
        var scene = new Scene();
        Entity here = scene.Add(scene.Root, new(1e30, 0, 0));
        scene.Add(scene.Root, new(2e30, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new(1e30, 0, 0), 10).Should().Equal(here);
    }

    [Fact]
    public void AnEntityWithAnUnusablePositionNeverMatchesAQueryAndDoesNotBreakTheIndex()
    {
        var scene = new Scene();
        Entity broken = scene.Add(scene.Root, new(double.NaN, 0, 0));
        Entity fine = scene.Add(scene.Root, new(1, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(fine);
        scene.Find(scene.Root, Double3.Zero, 1e12).Should().Equal(fine);
        scene.Entities.Release(broken);
        scene.Spatial.AssertInvariants();
    }

    // ---- Arguments ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void ARadiusThatIsNegativeOrNotANumberIsRefused(double radius)
    {
        var scene = new Scene();
        var results = new List<Entity>();

        Action query = () =>
            scene.Spatial.QueryRadius(frame: scene.Root, center: Double3.Zero, radius: radius, results: results);

        query.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("radius");
    }

    [Fact]
    public void ANullFrameIsRefused()
    {
        var scene = new Scene();

        Action query = () =>
            scene.Spatial.QueryRadius(frame: FrameHandle.Null, center: Double3.Zero, radius: 1, results: []);

        query.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ADestroyedFrameIsRefusedAndDoesNotAnswerFromItsOldGrid()
    {
        var scene = new Scene();
        FrameHandle station = scene.NewFrame(scene.Root);
        scene.Add(station, Double3.Zero);
        scene.Settle();
        scene.Frames.Destroy(station);

        Action query = () => scene.Find(station, Double3.Zero, 10);

        query.Should().Throw<InvalidOperationException>("the frame handle is stale");
    }

    [Fact]
    public void ANullResultListIsRefused()
    {
        var scene = new Scene();

        Action query = () =>
            scene.Spatial.QueryRadius(frame: scene.Root, center: Double3.Zero, radius: 1, results: null!);

        query.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AThresholdOfTheCellSearchOnTheWorkersBelowOneMovedEntityIsRefused(int threshold)
    {
        var scene = new Scene();

        Action set = () => scene.Spatial.ParallelThreshold = threshold;

        set.Should().Throw<ArgumentOutOfRangeException>();
        scene.Spatial.ParallelThreshold.Should()
            .Be(SpatialIndex.DefaultParallelThreshold, "a refused threshold changes nothing");
    }
}
