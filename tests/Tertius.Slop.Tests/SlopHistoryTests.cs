using AwesomeAssertions;
using Tertius.Slop.History;

namespace Tertius.Slop.Tests;

/// <summary>
/// The history of the slop metrics (docs/plans/code-quality-tooling.md, decision 18): the commits whose notes hold them,
/// as CSV, and the tickets of each commit, which its message names.
/// </summary>
public sealed class SlopHistoryTests
{
    [Fact]
    public void TheTicketsOfACommitAreTheTicketIdsInItsMessageInOrderWithoutRepeats() =>
        Commit("ter-2x99: close; tickets for ter-ab12 and ter-2x99\n\nSee ter-zz9y.", note: null).Tickets
            .Should().Equal("ter-2x99", "ter-ab12", "ter-zz9y");

    [Theory]
    [InlineData("Clean up code before push")]
    [InlineData("ter-2x9: a short id, and a long one ter-2x999")]
    [InlineData("Split the inter-face code")]
    public void AMessageWithoutATicketIdHasNoTickets(string message) =>
        Commit(message, note: null).Tickets.Should().BeEmpty();

    [Fact]
    public void TheSubjectOfACommitIsTheFirstLineOfItsMessage() =>
        Commit("ter-2x99: the verbosity\n\nThe body.", note: null).Subject.Should().Be("ter-2x99: the verbosity");

    [Fact]
    public void TheCsvHasOneRowPerNotedCommitWithItsDateBranchTicketsAndMeasures()
    {
        Commit noted = Commit("ter-2x99: x, and ter-ab12", SlopNoteTests.Note(erosion: 0.365, verbosity: 0.036));
        Commit other = Commit("A commit without a note", note: null) with { Sha = "b" };

        new SlopHistory([noted, other]).Csv.Should().Be(
            "commit,date,branch,tickets,erosion,verbosity,sloc\n" +
            "a1,2026-10-24T12:00:00+00:00,main,ter-2x99 ter-ab12,0.365,0.036,34450\n");
    }

    [Theory]
    [InlineData("odd,name", "\"odd,name\"")]
    [InlineData("say\"hi\"", "\"say\"\"hi\"\"\"")]
    public void ABranchNameWithACommaOrAQuoteIsQuoted(string branch, string cell)
    {
        SlopNote note = SlopNoteTests.Note(erosion: 0.5, verbosity: 0.25) with { Branch = branch };

        new SlopHistory([Commit("x", note)]).Csv.Should().EndWith($",{cell},,0.5,0.25,34450\n");
    }

    internal static Commit Commit(string message, SlopNote? note) => new()
    {
        Sha = "a1", FirstParent = "a0", Date = SlopNoteTests.Noon, Message = message, Note = note
    };
}
