namespace Tertius.Core.Tests;

public sealed class ContractTests
{
#if TERTIUS_CONTRACTS
    [Fact]
    public void FailedPreconditionNamesTheExpression()
    {
        int slot = 3;
        ContractException ex = Assert.Throws<ContractException>(() => Contract.Requires(slot < 0));
        Assert.Equal("Precondition failed: slot < 0", ex.Message);
    }

    [Fact]
    public void PostconditionsAndAssertionsReportTheirKind()
    {
        Assert.StartsWith(
            "Postcondition failed:",
            Assert.Throws<ContractException>(() => Contract.Ensures(false)).Message
        );
        Assert.StartsWith("Assertion failed:", Assert.Throws<ContractException>(() => Contract.Assert(false)).Message);
    }
#endif

    [Fact]
    public void InvariantsAreCheckedInEveryBuild()
    {
        ContractException ex = Assert.Throws<ContractException>(() => Contract.Invariant(1 + 1 == 3));
        Assert.Equal("Invariant failed: 1 + 1 == 3", ex.Message);
    }

    [Fact]
    public void HoldingContractsDoNothing()
    {
        Contract.Requires(true);
        Contract.Ensures(true);
        Contract.Assert(true);
        Contract.Invariant(true);
    }

#if !TERTIUS_CONTRACTS
    [Fact]
    public void ConditionalContractsCompileOutWithoutTheSymbol() => Contract.Requires(false);
#endif
}
