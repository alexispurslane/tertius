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

        public Scene()
        {
            Spatial = new SpatialIndex(Entities, Frames);
            Root = NewFrame(FrameHandle.Null);
        }

        public FrameHandle NewFrame(FrameHandle parent) =>
            Frames.Create(new Entity(nextOwnerSlot++, 1), parent, DPose.Identity, FrameBounds.Sphere(1e12f), MotionDriver.Static);

        /// <summary>Makes an entity live at a position. The index does not know about it until <see cref="Settle"/>.</summary>
        public Entity Add(FrameHandle frame, Double3 position)
        {
            Entity entity = Entities.Reserve();
            Entities.Activate(entity, RowAt(frame, position), new RenderData(), [], [], null, FrameHandle.Null);
            return entity;
        }

        /// <summary>Writes the back row, as the sync point does. The front row, and so every query, does not change until <see cref="Settle"/>.</summary>
        public void Move(Entity entity, FrameHandle frame, Double3 position) => Entities.SetBackTransform(entity, RowAt(frame, position));

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
            Spatial.QueryRadius(frame, center, radius, found);
            return found;
        }

        private static TransformRow RowAt(FrameHandle frame, Double3 position) =>
            new() { Frame = frame, Pose = new DPose(position, DQuaternion.Identity) };
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
            var position = new Double3(random.NextDouble() * 1000 - 500, random.NextDouble() * 1000 - 500, random.NextDouble() * 1000 - 500);
            points.Add((scene.Add(scene.Root, position), position));
        }
        scene.Settle();

        // The radii run from 1 m to 687 m. The small ones are answered from the few cells around the center, and the
        // large ones from every occupied cell, so both ways of walking the grid are checked against the same search.
        int nonEmpty = 0;
        for (int query = 0; query < 50; query++)
        {
            var center = new Double3(random.NextDouble() * 1200 - 600, random.NextDouble() * 1200 - 600, random.NextDouble() * 1200 - 600);
            double radius = 1 + query * 14;

            Entity[] expected = points
                .Where(p => Square(p.Position.X - center.X) + Square(p.Position.Y - center.Y) + Square(p.Position.Z - center.Z) <= radius * radius)
                .Select(p => p.Entity)
                .ToArray();
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
            scene.Add(scene.Root, new Double3(random.NextDouble() * 400, random.NextDouble() * 400, random.NextDouble() * 400));
        scene.Settle();
        var centers = Enumerable.Range(0, 64).Select(_ => new Double3(random.NextDouble() * 400, random.NextDouble() * 400, random.NextDouble() * 400)).ToArray();
        Entity[][] oneAtATime = centers.Select(center => scene.Find(scene.Root, center, 60).ToArray()).ToArray();

        var together = new Entity[centers.Length][];
        Parallel.For(0, centers.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => together[i] = scene.Find(scene.Root, centers[i], 60).ToArray());

        for (int i = 0; i < centers.Length; i++)
            together[i].Should().Equal(oneAtATime[i], $"query {i}");
        oneAtATime.Sum(found => found.Length).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ARadiusQueryListsEntitiesByEntityIndexWhateverOrderTheyEnteredTheirCell()
    {
        var scene = new Scene();
        Entity[] spawned = Enumerable.Range(0, 5).Select(_ => scene.Add(scene.Root, new Double3(500, 0, 0))).ToArray();
        scene.Settle();

        // Move them into one new cell in the reverse of slot order, so that the cell lists them from the highest slot down.
        foreach (Entity entity in spawned.Reverse())
            scene.Move(entity, scene.Root, new Double3(1, 1, 1));
        scene.Settle();

        scene.Find(scene.Root, new Double3(1, 1, 1), 5).Should().Equal(spawned);
    }

    [Fact]
    public void ARadiusQueryClearsTheListItIsGivenBeforeFilling()
    {
        var scene = new Scene();
        Entity near = scene.Add(scene.Root, new Double3(1, 0, 0));
        scene.Settle();
        Entity leftover = new(77, 1);
        var results = new List<Entity> { leftover };

        scene.Spatial.QueryRadius(scene.Root, Double3.Zero, 10, results);
        results.Should().Equal(near);

        scene.Spatial.QueryRadius(scene.Root, new Double3(900, 0, 0), 10, results);
        results.Should().BeEmpty("nothing is near the second center, and the first answer must not stay in the list");
    }

    // ---- The exact edge of the radius ----------------------------------------------------------------------------------

    [Fact]
    public void AnEntityExactlyOnTheRadiusIsFoundAndOneJustBeyondItIsNot()
    {
        var scene = new Scene();
        // 3-4-5 triangle in the XY plane: the first is exactly 5 m from the origin, the second a little farther.
        Entity onTheEdge = scene.Add(scene.Root, new Double3(3, 4, 0));
        scene.Add(scene.Root, new Double3(3, 4.001, 0));
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 5).Should().Equal(onTheEdge);
    }

    [Fact]
    public void ARadiusOfZeroFindsOnlyEntitiesExactlyAtTheCenter()
    {
        var scene = new Scene();
        Entity atCenter = scene.Add(scene.Root, new Double3(10, 20, 30));
        scene.Add(scene.Root, new Double3(10, 20, 30.001));
        scene.Settle();

        scene.Find(scene.Root, new Double3(10, 20, 30), 0).Should().Equal(atCenter);
    }

    [Fact]
    public void ARadiusLargerThanTheWholeFrameFindsEveryEntityInIt()
    {
        var scene = new Scene();
        Entity[] spawned =
        [
            scene.Add(scene.Root, new Double3(-9000, 4, 12)),
            scene.Add(scene.Root, new Double3(250, -80000, 3)),
            scene.Add(scene.Root, new Double3(0, 0, 7e6)),
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
        Entity before = scene.Add(scene.Root, new Double3(edge - 0.5, 0, 0));
        Entity after = scene.Add(scene.Root, new Double3(edge + 0.5, 0, 0));
        Entity farAfter = scene.Add(scene.Root, new Double3(edge + 3, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new Double3(edge, 0, 0), 1).Should().Equal(before, after);
        scene.Find(scene.Root, new Double3(edge, 0, 0), 5).Should().Equal(before, after, farAfter);
    }

    [Fact]
    public void AQuerySphereThatCrossesTheOriginFindsEntitiesInTheCellsOfNegativeCoordinates()
    {
        var scene = new Scene();
        Entity below = scene.Add(scene.Root, new Double3(-0.5, -0.5, -0.5));
        Entity above = scene.Add(scene.Root, new Double3(0.5, 0.5, 0.5));
        Entity farBelow = scene.Add(scene.Root, new Double3(-3 * SpatialGrid.CellSize, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 1).Should().Equal(below, above);
        scene.Find(scene.Root, new Double3(-3 * SpatialGrid.CellSize, 0, 0), 1).Should().Equal(farBelow);
    }

    [Fact]
    public void AnEntityThatMovesToAnotherCellIsFoundThereAndNoLongerWhereItWas()
    {
        var scene = new Scene();
        Entity mover = scene.Add(scene.Root, new Double3(5, 0, 0));
        scene.Settle();
        double farAway = 10 * SpatialGrid.CellSize;

        scene.Move(mover, scene.Root, new Double3(farAway, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new Double3(5, 0, 0), 3).Should().BeEmpty();
        scene.Find(scene.Root, new Double3(farAway, 0, 0), 3).Should().Equal(mover);
    }

    [Fact]
    public void AnEntityThatMovesWithinItsCellIsFoundAtItsNewPositionOnly()
    {
        var scene = new Scene();
        Entity mover = scene.Add(scene.Root, new Double3(2, 0, 0));
        scene.Settle();

        scene.Move(mover, scene.Root, new Double3(9, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new Double3(2, 0, 0), 3).Should().BeEmpty("the exact distance test uses the new position even though the cell is the same");
        scene.Find(scene.Root, new Double3(9, 0, 0), 3).Should().Equal(mover);
    }

    [Fact]
    public void AnEntityThatMovesToAnotherFrameIsFoundInThatFrameOnly()
    {
        var scene = new Scene();
        FrameHandle station = scene.NewFrame(scene.Root);
        Entity traveler = scene.Add(scene.Root, new Double3(4, 0, 0));
        scene.Settle();

        scene.Move(traveler, station, new Double3(4, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new Double3(4, 0, 0), 2).Should().BeEmpty();
        scene.Find(station, new Double3(4, 0, 0), 2).Should().Equal(traveler);
    }

    [Fact]
    public void AQueryOfOneFrameNeverFindsTheEntitiesOfAnotherFrameAtTheSamePosition()
    {
        var scene = new Scene();
        FrameHandle station = scene.NewFrame(scene.Root);
        Entity inRoot = scene.Add(scene.Root, new Double3(1, 1, 1));
        Entity inStation = scene.Add(station, new Double3(1, 1, 1));
        scene.Settle();

        scene.Find(scene.Root, new Double3(1, 1, 1), 5).Should().Equal(inRoot);
        scene.Find(station, new Double3(1, 1, 1), 5).Should().Equal(inStation);
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
        Entity mover = scene.Add(scene.Root, new Double3(2, 0, 0));
        scene.Settle();

        scene.Move(mover, scene.Root, new Double3(500, 0, 0));

        scene.Find(scene.Root, new Double3(2, 0, 0), 3).Should().Equal(mover);
        scene.Find(scene.Root, new Double3(500, 0, 0), 3).Should().BeEmpty();
        scene.Settle();
        scene.Find(scene.Root, new Double3(2, 0, 0), 3).Should().BeEmpty();
        scene.Find(scene.Root, new Double3(500, 0, 0), 3).Should().Equal(mover);
    }

    [Fact]
    public void AnEntityThatLeavesACellAndComesBackIsListedOnceInIt()
    {
        // Moving out of a cell and later back into it must not leave the cell with a stale or duplicate entry.
        var scene = new Scene();
        Entity wanderer = scene.Add(scene.Root, new Double3(1, 0, 0));
        Entity stayer = scene.Add(scene.Root, new Double3(2, 0, 0));
        scene.Settle();

        scene.Move(wanderer, scene.Root, new Double3(10 * SpatialGrid.CellSize, 0, 0));
        scene.Settle();
        scene.Move(wanderer, scene.Root, new Double3(1, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new Double3(1.5, 0, 0), 3).Should().Equal(wanderer, stayer);
    }

    // The model is a plain list of (entity, frame, position). Some moves are small steps, which mostly stay in the cell, and
    // some are jumps, which leave it; some change the frame; entities also die and new ones take their slots.
    [Theory]
    [InlineData(5)]
    [InlineData(99)]
    [InlineData(20261010)]
    public void ARandomSequenceOfMovesSpawnsAndReleasesAgreesWithASearchThroughEveryEntity(int seed)
    {
        var scene = new Scene();
        var random = new Random(seed);
        FrameHandle[] frames = [scene.Root, scene.NewFrame(scene.Root), scene.NewFrame(scene.Root)];
        var model = new Dictionary<Entity, (FrameHandle Frame, Double3 Position)>();

        Double3 RandomPosition(double extent) =>
            new((random.NextDouble() - 0.5) * extent, (random.NextDouble() - 0.5) * extent, (random.NextDouble() - 0.5) * extent);

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
                if (!model.TryGetValue(mover, out var now))
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
                Entity[] expected = model
                    .Where(pair => pair.Value.Frame == frame
                        && Square(pair.Value.Position.X - center.X) + Square(pair.Value.Position.Y - center.Y) + Square(pair.Value.Position.Z - center.Z) <= radius * radius)
                    .Select(pair => pair.Key)
                    .Order()
                    .ToArray();

                scene.Find(frame, center, radius).Should().Equal(expected, $"seed {seed}, round {round}, query {query}: frame {frame}, center {center}, radius {radius}");
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
        FrameHandle[] frames = Enumerable.Range(0, 40).Select(_ => scene.NewFrame(scene.Root)).ToArray();
        FrameHandle last = frames[^1];
        last.Index.Should().BeGreaterThan(16, "the test needs a frame past the starting room");
        Entity inLast = scene.Add(last, new Double3(6, 0, 0));
        Entity inFirst = scene.Add(frames[0], new Double3(6, 0, 0));
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
        Entity near = scene.Add(scene.Root, new Double3(1, 0, 0));
        Entity far = scene.Add(scene.Root, new Double3(500, 0, 0));
        Entity alsoNear = scene.Add(scene.Root, new Double3(2, 0, 0));
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
        Entity near = scene.Add(scene.Root, new Double3(1, 0, 0));
        Entity alsoNear = scene.Add(scene.Root, new Double3(2, 0, 0));
        scene.Settle();
        scene.Entities.Release(near);

        Entity successor = scene.Add(scene.Root, new Double3(3, 0, 0));

        successor.Index.Should().Be(near.Index, "the slot is reused");
        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(alsoNear);
        scene.Settle();
        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(successor, alsoNear);
    }

    [Fact]
    public void ReleasingOneOfSeveralEntitiesInACellLeavesTheOthersFound()
    {
        var scene = new Scene();
        Entity[] sharing = Enumerable.Range(0, 4).Select(i => scene.Add(scene.Root, new Double3(i, 0, 0))).ToArray();
        scene.Settle();

        scene.Entities.Release(sharing[1]);

        scene.Spatial.AssertInvariants();
        scene.Find(scene.Root, new Double3(1.5, 0, 0), 5).Should().Equal(sharing[0], sharing[2], sharing[3]);
    }

    [Fact]
    public void ACellEmptiedByReleasesTakesNewEntitiesAgain()
    {
        var scene = new Scene();
        Entity first = scene.Add(scene.Root, new Double3(1, 0, 0));
        Entity second = scene.Add(scene.Root, new Double3(2, 0, 0));
        scene.Settle();
        scene.Entities.Release(first);
        scene.Entities.Release(second);
        scene.Find(scene.Root, Double3.Zero, 10).Should().BeEmpty();

        Entity newcomer = scene.Add(scene.Root, new Double3(1.5, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, Double3.Zero, 10).Should().Equal(newcomer);
    }

    [Fact]
    public void AnEntityReleasedBeforeTheUpdateThatWouldHavePlacedItIsNeverFound()
    {
        var scene = new Scene();
        Entity brief = scene.Add(scene.Root, new Double3(1, 0, 0));
        Entity lasting = scene.Add(scene.Root, new Double3(2, 0, 0));

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
        Entity here = scene.Add(scene.Root, new Double3(1e30, 0, 0));
        scene.Add(scene.Root, new Double3(2e30, 0, 0));
        scene.Settle();

        scene.Find(scene.Root, new Double3(1e30, 0, 0), 10).Should().Equal(here);
    }

    [Fact]
    public void AnEntityWithAnUnusablePositionNeverMatchesAQueryAndDoesNotBreakTheIndex()
    {
        var scene = new Scene();
        Entity broken = scene.Add(scene.Root, new Double3(double.NaN, 0, 0));
        Entity fine = scene.Add(scene.Root, new Double3(1, 0, 0));
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

        Action query = () => scene.Spatial.QueryRadius(scene.Root, Double3.Zero, radius, results);

        query.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("radius");
    }

    [Fact]
    public void ANullFrameIsRefused()
    {
        var scene = new Scene();

        Action query = () => scene.Spatial.QueryRadius(FrameHandle.Null, Double3.Zero, 1, new List<Entity>());

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

        Action query = () => scene.Spatial.QueryRadius(scene.Root, Double3.Zero, 1, null!);

        query.Should().Throw<ArgumentNullException>();
    }
}
