using System.Numerics;
using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The row types of the puff table: a row (<see cref="Puff"/>) holds only what differs from puff to puff, a range
/// (<see cref="PuffRange"/>) names the frame of a span of rows, and a view (<see cref="PuffView"/>) hands the rows, the
/// ranges and the looks to the render snapshot.
/// </summary>
public class PuffRowTests
{
    [Fact]
    public void APuffRowIs56Bytes()
    {
        // A place and a chord in doubles, an age and a look index, with no padding. Every live puff is one row, and the
        // step of the puffs walks the rows of each frame in one loop, so each byte of the row costs once per puff and step.
        Unsafe.SizeOf<Puff>().Should().Be(56);
    }

    [Fact]
    public void APuffViewHandsOutTheRowsTheRangesTheLooksAndTheStepThatItWasMadeWith()
    {
        var frame = new FrameHandle(new(3, 1), 2, 1);
        Puff[] rows = [new() { Place = new(1, 2, 3), Chord = new(4, 5, 6), Age = 0.25f, Look = 1 }];
        PuffRange[] ranges = [new(frame, 0, 1)];
        PuffLook[] looks =
        [
            default,
            new(Life: 2, StartRadius: 0.5f, EndRadius: 3, TemperatureRatio: 3, Shading: PuffShading.Glow(Vector3.One))
        ];

        var view = new PuffView(rows: rows, ranges: ranges, looks: looks, step: 1 / 60.0);

        view.Rows.ToArray().Should().Equal(rows);
        view.Ranges.ToArray().Should().Equal(ranges);
        view.Looks.ToArray().Should().Equal(looks);
        view.Step.Should().Be(1 / 60.0);
    }
}
