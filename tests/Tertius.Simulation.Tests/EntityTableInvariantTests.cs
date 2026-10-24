using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The entity table finds a column entry in a slot that is not live: in a slot that was never used, when the table first
/// takes the slot, and in a released slot, in its invariant check. The check leaves the slots that were never used to
/// their first use, so that its cost follows the slots in use and not the capacity.
/// </summary>
public sealed class EntityTableInvariantTests
{
    [Fact]
    public void TheInvariantCheckLeavesASlotThatWasNeverUsedToItsFirstUse()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        table.SpawnLive();

        numbers.AsSpan()[table.Capacity - 1] = 7;

        Action check = table.AssertInvariants;
        check.Should().NotThrow("a scan of every slot that was never used made the check cost O(capacity) per tick");
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

#if TERTIUS_CONTRACTS
    [Fact]
    public void AColumnEntryInASlotThatWasNeverUsedFailsAContractWhenTheTableTakesTheSlot()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        table.SpawnLive();

        numbers.AsSpan()[table.SlotCount] = 7;

        Action reserve = () => table.Reserve();
        reserve.Should().Throw<ContractException>().WithMessage("Precondition*");
    }

    [Fact]
    public void AReferenceLeftInASlotThatWasNeverUsedFailsAContractWhenTheTableTakesTheSlot()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<string?> names = table.AddColumn<string?>();
        table.SpawnLive();

        names.AsSpan()[table.SlotCount] = "left over";

        Action reserve = () => table.Reserve();
        reserve.Should().Throw<ContractException>().WithMessage("Precondition*");
    }

    [Fact]
    public void AColumnEntryInASlotThatASpawnTookPastItsPoolFailsAContractWhenTheTableCoversTheSlot()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        Entity spawner = table.SpawnLive();
        table.BeginTick(spawnPoolSize: 0);
        Entity spawned = table.ReserveForSpawn(spawner);

        numbers.AsSpan()[(int)spawned.Index] = 7;

        Action end = () => table.EndGameplay();
        end.Should().Throw<ContractException>().WithMessage("Precondition*");
    }
#endif
}
