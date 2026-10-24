using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity rules about exceptions and defensive guards, ported from the scb-check slop rules
/// (docs/plans/code-quality-tooling.md, decision 17): for each rule, code that it flags and a near miss.
/// </summary>
public sealed class ExceptionRuleTests
{
    [Fact]
    public void EachTryOfAFunctionWithThreeOrMoreTryStatementsIsFlagged() =>
        Source.RulesInMethod("""
                             try { x(); } finally { y(); }
                             try { x(); } finally { y(); }
                             try { x(); } finally { y(); }
                             """).Should().Equal("defensive-try-soup-function", "defensive-try-soup-function",
            "defensive-try-soup-function");

    [Fact]
    public void AFunctionWithTwoTryStatementsIsNotFlagged() =>
        Source.RulesInMethod("""
                             try { x(); } finally { y(); }
                             try { x(); } finally { y(); }
                             """).Should().BeEmpty();

    [Fact]
    public void EachCatchAllOfAFunctionWithTwoCatchAllsIsFlagged() =>
        Source.RulesInMethod("""
                             try { x(); } catch (Exception e) { Log(e); }
                             try { y(); } catch { Log(); }
                             """).Should()
            .Equal("defensive-except-exception-heavy", "defensive-except-exception-heavy");

    [Fact]
    public void AFunctionWithOneCatchAllAndOneSpecificCatchIsNotFlagged() =>
        Source.RulesInMethod("""
                             try { x(); } catch (System.Exception e) { Log(e); }
                             try { y(); } catch (IOException e) { Log(e); }
                             """).Should().BeEmpty();

    [Theory]
    [InlineData("catch (Exception) { return null; }")]
    [InlineData("catch { return false; }")]
    [InlineData("catch (System.Exception e) { return default; }")]
    [InlineData("catch (Exception) { return 0; }")]
    [InlineData("catch (Exception) { return []; }")]
    public void ACatchAllThatOnlyReturnsAConstantIsFlagged(string clause) =>
        Source.RulesInMethod($"try {{ return Parse(s); }} {clause}").Should().Contain("except-return-static-sentinel");

    [Theory]
    [InlineData("catch (FormatException) { return 0; }")]
    [InlineData("catch (Exception e) { Log(e); return 0; }")]
    [InlineData("catch (Exception) { return fallback; }")]
    [InlineData("catch (Exception) { return [fallback]; }")]
    [InlineData("catch (Exception e) when (e is IOException) { return 0; }")]
    public void ASpecificCatchOrACatchThatDoesMoreIsNotFlaggedAsASentinel(string clause) =>
        Source.RulesInMethod($"try {{ return Parse(s); }} {clause}").Should()
            .NotContain("except-return-static-sentinel");

    [Fact]
    public void ACatchThatOnlyRethrowsIsFlagged() =>
        Source.RulesInMethod("try { x(); } catch (IOException) { throw; }").Should().Equal("except-bare-reraise");

    [Fact]
    public void ACatchThatOnlyRethrowsTheCaughtExceptionByNameIsFlagged() =>
        Source.RulesInMethod("try { x(); } catch (IOException e) { throw e; }").Should().Equal("except-bare-reraise");

    [Fact]
    public void ACatchThatThrowsAnotherExceptionIsNotARethrow() =>
        Source.RulesInMethod("try { x(); } catch (IOException e) { throw other; }").Should()
            .NotContain("except-bare-reraise");

    [Fact]
    public void ACatchThatRethrowsBeforeABroaderCatchIsNotFlagged() =>
        Source.RulesInMethod("try { x(); } catch (IOException) { throw; } catch (Exception e) { Log(e); }")
            .Should().BeEmpty("it keeps the narrower exception out of the broader catch");

    [Fact]
    public void AnEmptyCatchIsFlagged() =>
        Source.RulesInMethod("try { x(); } catch (IOException) { }").Should().Equal("except-pass-silence");

    [Fact]
    public void ACatchWithAStatementThatReportsTheErrorIsNotFlaggedAsSilent() =>
        Source.RulesInMethod("try { x(); } catch (IOException e) { Log(e); }").Should().BeEmpty();

    [Theory]
    [InlineData("catch (IOException) { count++; }")]
    [InlineData("catch (IOException e) { Release(); }")]
    [InlineData("catch (Exception) { return; }")]
    public void ACatchThatNeitherThrowsNorReadsTheErrorNorReportsItIsFlaggedAsSilent(string clause) =>
        Source.RulesInMethod($"try {{ x(); }} {clause}").Should().Equal("except-pass-silence");

    [Theory]
    [InlineData("catch (IOException e) { failure = e; }")]
    [InlineData("catch (IOException) { error.WriteLine(\"the file did not open\"); }")]
    [InlineData("catch (IOException) { Report(\"the file did not open\"); }")]
    [InlineData("catch (IOException) { Log(\"the file did not open\"); }")]
    [InlineData("catch (IOException) { Warn(\"the file did not open\"); }")]
    [InlineData("catch (IOException) { Print(\"the file did not open\"); }")]
    [InlineData("catch (IOException) { Trace(\"the file did not open\"); }")]
    [InlineData("catch (IOException) { throw new InvalidOperationException(\"no file\"); }")]
    public void ACatchThatThrowsReadsTheErrorOrReportsItIsNotFlaggedAsSilent(string clause) =>
        Source.RulesInMethod($"try {{ x(); }} {clause}").Should().NotContain("except-pass-silence");

    [Fact]
    public void ACatchThatATryMethodTurnsIntoFalseIsNotFlaggedAsSilent() =>
        Source.RulesIn("""
                       class C
                       {
                           bool TryRead(out int value)
                           {
                               try { value = Read(); return true; }
                               catch (IOException) { value = 0; return false; }
                           }
                       }
                       """).Should().BeEmpty("a Try method reports an expected failure by its result (STYLE.md)");

    [Theory]
    [InlineData("catch (IOException e) { Log(e); throw; }")]
    [InlineData("catch (IOException e) { error.WriteLine(e.Message); throw; }")]
    [InlineData("catch (IOException e) { Report(e); Trace(e); throw e; }")]
    public void ACatchThatLogsAndRethrowsIsFlagged(string clause) =>
        Source.RulesInMethod($"try {{ x(); }} {clause}").Should().Equal("log-and-rethrow");

    [Theory]
    [InlineData("catch (IOException) { Release(); throw; }")]
    [InlineData("catch (IOException e) { Log(e); }")]
    [InlineData("catch (IOException e) { Log(e); throw new InvalidOperationException(\"no file\", e); }")]
    public void ACatchThatCleansUpBeforeARethrowOrDoesNotRethrowIsNotLogAndRethrow(string clause) =>
        Source.RulesInMethod($"try {{ x(); }} {clause}").Should().NotContain("log-and-rethrow");

    [Theory]
    [InlineData(
        "if (index < 0) throw new ArgumentOutOfRangeException(nameof(index)); Contract.Requires(index < count);")]
    [InlineData("ArgumentOutOfRangeException.ThrowIfNegative(index); Contract.Requires(index < count);")]
    [InlineData("Contract.Assert(index < count); if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));")]
    public void AContractAndAGuardOnTheSameValueAreFlagged(string body) =>
        Source.RulesIn($$"""
                         class C
                         {
                             public void F(int index)
                             {
                                 {{body}}
                                 Use(index);
                             }
                         }
                         """).Should().Equal("contract-and-guard");

    [Theory]
    [InlineData("if (index < 0) throw new ArgumentOutOfRangeException(nameof(index)); Contract.Requires(count > 0);")]
    [InlineData("if (index < 0) Log(index); Contract.Requires(index < count);")]
    [InlineData("Contract.Requires(index < count); Contract.Assert(index >= 0);")]
    [InlineData(
        "if (world.Limits.Most <= index) throw new InvalidOperationException(\"full\"); Contract.Ensures(!world.IsFull);")]
    public void AContractOnAnotherValueOrBesideNoGuardIsNotFlagged(string body) =>
        Source.RulesIn($$"""
                         class C
                         {
                             public void F(int index)
                             {
                                 {{body}}
                                 Use(index);
                             }
                         }
                         """).Should().BeEmpty();

    [Fact]
    public void EachNullGuardOfANonPublicFunctionWithTwoOrMoreIsFlagged() =>
        Source.RulesIn("""
                       class C
                       {
                           private int F(string? a, string? b)
                           {
                               if (a is null) return 0;
                               if (b == null) throw new ArgumentException("b");
                               return 1;
                           }
                       }
                       """).Should().Equal("defensive-function-nullable-heavy", "defensive-function-nullable-heavy");

    [Theory]
    [InlineData("public")]
    [InlineData("protected")]
    public void TheNullGuardsOfAPublicApiAreNotFlagged(string access) =>
        Source.RulesIn($$"""
                         class C
                         {
                             {{access}} int F(string? a, string? b)
                             {
                                 if (a is null) return 0;
                                 if (b == null) throw new ArgumentException("b");
                                 return 1;
                             }
                         }
                         """).Should().BeEmpty("a public API keeps guards that are always on");

    [Theory]
    [InlineData("interface I { void F(Body a, Body b) { if (a is null) return; if (b is null) return; a.Push(b); } }")]
    [InlineData(
        "class C { public Body Held { set { if (value is null) return; if (other is null) return; held = value; } } }")]
    public void TheNullGuardsOfAnInterfaceMemberOrAPublicAccessorAreNotFlagged(string type) =>
        Source.RulesIn(type).Should().BeEmpty();

    [Fact]
    public void ANonPublicFunctionWithOneNullGuardAndOnePatternBindingIsNotFlagged() =>
        Source.RulesIn("""
                       class C
                       {
                           private int F(string? a, string? b)
                           {
                               if (a is null) return 0;
                               if (Find(b) is not { } found) return 0;
                               return found.Length;
                           }
                       }
                       """).Should().BeEmpty("a pattern that tests and binds is the shell form of STYLE.md");
}
