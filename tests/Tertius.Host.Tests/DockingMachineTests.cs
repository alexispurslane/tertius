using Tertius.Demo;
using SpinUp = Tertius.Demo.SpinUp;

namespace Tertius.Host.Tests;

/// <summary>
/// The docking's one flat state machine, <see cref="DockingAutopilot.Next"/>, row by row, with no World
/// (docs/plans/blue-danube-demo.md, "The docking", "The phases"). Each row is one case and one event: the order
/// <see cref="Dock"/> from the script, or a report of the case that flew the tick.
/// </summary>
public sealed class DockingMachineTests
{
    private static readonly DockingEvent[] Every =
    [
        new Dock(),
        new AtRollWindow(12.5),
        new SpunUp(),
        new EntryEnded(),
        new Landed(),
        new Lost("its hull has no mass"),
    ];

    private static readonly DockingState[] Flying = [new Waiting(), new SpinUp(4), new Entry(), new MoveOut()];

    [Fact]
    public void TheOrderStartsTheWaitFromStandby() =>
        DockingAutopilot.Next(new Standby(), new Dock()).Should().Be(new DockingState(new Waiting()));

    [Fact]
    public void TheRollWindowStartsTheSpinUpAtTheTimeOfTheReport() =>
        DockingAutopilot
            .Next(new Waiting(), new AtRollWindow(4874.5))
            .Should()
            .Be(new DockingState(new SpinUp(4874.5)));

    [Fact]
    public void TheEndOfTheSpinUpStartsTheEntry() =>
        DockingAutopilot.Next(new SpinUp(4), new SpunUp()).Should().Be(new DockingState(new Entry()));

    [Fact]
    public void TheEndOfTheEntryStartsTheMoveOut() =>
        DockingAutopilot.Next(new Entry(), new EntryEnded()).Should().Be(new DockingState(new MoveOut()));

    [Fact]
    public void TheLandingDocks() =>
        DockingAutopilot.Next(new MoveOut(), new Landed()).Should().Be(new DockingState(new Docked()));

    /// <summary>A loss halts every case that flies: the wait, the spin-up, the entry and the move out.</summary>
    [Fact]
    public void ALossHaltsEveryCaseThatFlies()
    {
        foreach (DockingState state in Flying)
            DockingAutopilot
                .Next(state, new Lost("its station is gone or owns no frame"))
                .Should()
                .Be(new DockingState(new Halted()), $"a loss halts {state}");
    }

    /// <summary>A loss in standby changes nothing: the autopilot looks at its station only once it is ordered to dock.</summary>
    [Fact]
    public void ALossInStandbyChangesNothing() =>
        DockingAutopilot
            .Next(new Standby(), new Lost("its hull has no mass"))
            .Should()
            .Be(new DockingState(new Standby()));

    /// <summary>A second order changes nothing in any case after standby, so the script cannot restart a docking.</summary>
    [Fact]
    public void ASecondOrderChangesNothing()
    {
        foreach (DockingState state in Flying.Append(new Docked()).Append(new Halted()))
            DockingAutopilot.Next(state, new Dock()).Should().Be(state, $"the order changes nothing in {state}");
    }

    /// <summary>Docked and halted are final: no event leaves them.</summary>
    [Fact]
    public void DockedAndHaltedAreFinal()
    {
        foreach (DockingState state in new DockingState[] { new Docked(), new Halted() })
        foreach (DockingEvent report in Every)
            DockingAutopilot.Next(state, report).Should().Be(state, $"{report} leaves {state} as it is");
    }

    /// <summary>
    /// A report changes only the case that makes it: a report that belongs to another case leaves the case as it is. So
    /// the machine never skips a phase.
    /// </summary>
    [Fact]
    public void AReportChangesOnlyTheCaseThatMakesIt()
    {
        (DockingState State, DockingEvent Report)[] own =
        [
            (new Waiting(), new AtRollWindow(12.5)),
            (new SpinUp(4), new SpunUp()),
            (new Entry(), new EntryEnded()),
            (new MoveOut(), new Landed()),
        ];
        foreach (DockingState state in Flying.Prepend(new Standby()))
        foreach (DockingEvent report in Every.Where(report => report.Value is not (Dock or Lost)))
            if (!own.Contains((state, report)))
                DockingAutopilot.Next(state, report).Should().Be(state, $"{report} leaves {state} as it is");
    }
}
