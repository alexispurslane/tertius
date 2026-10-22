using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The entity table's invariant check finds a column entry in a slot that is not live: in a slot that was never used, which
/// it checks in one pass over the unused tail of each column, and in a released slot, which it checks one by one.
/// </summary>
public class EntityTableInvariantTests
{
    [Fact]
    public void AColumnEntryInASlotThatWasNeverUsedFailsTheCheck()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        table.SpawnLive();
        table.AssertInvariants();

        numbers.AsSpan()[table.Capacity - 1] = 7;

        Action check = table.AssertInvariants;
        check.Should().Throw<ContractException>();
    }

    [Fact]
    public void AReferenceLeftInASlotThatWasNeverUsedFailsTheCheck()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<string?> names = table.AddColumn<string?>();
        table.SpawnLive();

        names.AsSpan()[table.Capacity - 1] = "left over";

        Action check = table.AssertInvariants;
        check.Should().Throw<ContractException>();
    }

    [Fact]
    public void AColumnEntryInAReleasedSlotFailsTheCheck()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        Entity gone = table.SpawnLive();
        table.SpawnLive();
        table.Release(gone);
        table.AssertInvariants();

        numbers.AsSpan()[(int)gone.Index] = 7;

        Action check = table.AssertInvariants;
        check.Should().Throw<ContractException>();
    }
}
