using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity rules about whole functions and their locals (docs/plans/code-quality-tooling.md, decision 17): the
/// user's pass-through, single-use local and repeated guard rules, and the scb-check rule for short helpers used once.
/// </summary>
public sealed class FunctionRuleTests
{
    [Theory]
    [InlineData("void Add(Item item) => items.Add(item);")]
    [InlineData("int Count(string s, int from) { return Counter.Count(s, from); }")]
    [InlineData("void Reset() { this.inner.Reset(); }")]
    [InlineData("T Get<T>(int id) => store.Get<T>(id);")]
    [InlineData(
        "void Move(Body body, Pose to, double time, bool wake) => mover.Move(body: body, to: to, time: time, wake: wake);")]
    public void AMethodWhoseWholeBodyPassesItsParametersToAnotherMethodIsFlagged(string method) =>
        Source.RulesIn($"class C {{ {method} }}").Should().Equal("pass-through-method");

    [Theory]
    [InlineData("void Add(Item item) => items.Add(item, 1);")]
    [InlineData("void Add(Item a, Item b) => items.Add(b, a);")]
    [InlineData("int Length(string s) => s.Length;")]
    [InlineData("bool Has(Item item) => items.Contains(item) && item.Live;")]
    [InlineData("static Query All() => default(Query).All();")]
    [InlineData("int Next(Random random) => random.Next();")]
    [InlineData("public override string ToString() => inner.ToString();")]
    [InlineData("[Conditional(\"DEBUG\")] void Check(bool ok) => Debug.Assert(ok);")]
    [InlineData("void IDisposable.Dispose() => inner.Dispose();")]
    public void AMethodThatDoesMoreThanPassItsParametersOnIsNotFlagged(string method) =>
        Source.RulesIn($"class C {{ {method} }}").Should().BeEmpty();

    [Fact]
    public void AShortPrivateMethodCalledOnceIsFlagged() =>
        Source.Findings("""
                        class C
                        {
                            int F(int x) => Twice(x) + 1;
                            private int Twice(int x) => x * 2;
                        }
                        """).Should().ContainSingle().Which.Should().Be(new Finding
        {
            Rule = "low-use-short-function", File = Source.FileName, Line = 4, EndLine = 4
        });

    [Theory]
    [InlineData("int F(int x) => Twice(x) + Twice(x + 1);")]
    [InlineData("int F(int x) => x switch { 1 => Twice(x), _ => 0 };")]
    [InlineData("int F(int[] xs) => xs.Sum(Twice);")]
    public void AShortPrivateMethodCalledTwiceOrFromASwitchArmOrAsAMethodGroupIsNotFlagged(string caller) =>
        Source.RulesIn($$"""
                         class C
                         {
                             {{caller}}
                             private int Twice(int x) => x * 2;
                         }
                         """).Should().BeEmpty();

    [Theory]
    [InlineData("public int Twice(int x) => x * 2;")]
    [InlineData("private int Twice(int x)\n{\n    int y = x;\n    y = y * 2;\n    return y;\n}")]
    [InlineData("[Conditional(\"DEBUG\")] private int Twice(int x) => x * 2;")]
    [InlineData("private partial int Twice(int x);\nprivate partial int Twice(int x) => x * 2;")]
    public void APublicLongerAttributedOrPartialMethodCalledOnceIsNotFlagged(string callee) =>
        Source.RulesIn($$"""
                         class C
                         {
                             int F(int x) => Twice(x) + 1;
                             {{callee}}
                         }
                         """).Should().BeEmpty();

    [Fact]
    public void AShortMethodWhoseInliningWouldPushItsCallerAboveTheComplexityLimitIsNotFlagged() =>
        Source.RulesIn("""
                       class C
                       {
                           int F(int x)
                           {
                               if (x == 1) x++; if (x == 2) x++; if (x == 3) x++; if (x == 4) x++; if (x == 5) x++;
                               if (x == 6) x++; if (x == 7) x++; if (x == 8) x++; if (x == 9) x++;
                               return Pick(x);
                           }
                           private int Pick(int x) => x > 0 ? x * 3 : 0;
                       }
                       """).Should().BeEmpty("the caller has CC 10, and the callee would add 1");

    [Theory]
    [InlineData("var total = a + b; return Scale(total);")]
    [InlineData("string name = Describe(x); Print(name); return 0;")]
    public void ALocalThatOnlyFeedsTheNextStatementIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("single-use-local");

    [Theory]
    [InlineData("var t = a + b; Use(t); return t;")]
    [InlineData("var t = a + b; Use(a); return t;")]
    [InlineData("bool finite = a && b; if (!finite) return 0; return 1;")]
    [InlineData("var finite = a > 0; if (!finite) return 0; return 1;")]
    [InlineData("var t = a + b; foreach (var x in xs) Use(t, x); return 0;")]
    [InlineData("var t = a + b; ys = xs.Select(x => x + t); return 0;")]
    [InlineData("var t = a + b; Change(ref t); return 0;")]
    [InlineData("List<int> t = new(); Use(t); return 0;")]
    [InlineData("using var t = Open(); return Read(t);")]
    [InlineData("var t = a + b; t++; return 0;")]
    [InlineData("const int t = 2; return t * a;")]
    [InlineData("bool done = Step(); Use(done); return 0;")]
    public void ALocalThatIsUsedAgainOrLaterOrNamesAConditionIsNotFlagged(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();

    [Theory]
    [InlineData(
        "private void F(Body body) { if (body is null) throw new ArgumentNullException(nameof(body)); body.Push(); }")]
    [InlineData("internal int F(string s) { if (s == null) return 0; return s.Length; }")]
    [InlineData("void F(Body body) { ArgumentNullException.ThrowIfNull(body); body.Push(); }")]
    [InlineData(
        "private void F(Body body) { held = body ?? throw new ArgumentNullException(nameof(body)); held.Push(); }")]
    public void ANullGuardOnANonNullableParameterOfANonPublicFunctionIsFlagged(string method) =>
        Source.RulesIn($"class C {{ {method} }}").Should().Equal("guard-repeats-caller-check");

    [Theory]
    [InlineData("public void F(Body body) { ArgumentNullException.ThrowIfNull(body); body.Push(); }")]
    [InlineData("private void F(Body? body) { if (body is null) return; body.Push(); }")]
    [InlineData("private void F<T>(T item) { if (item is null) return; Use(item); }")]
    [InlineData("private void F(Body body) { if (other is null) return; body.Push(); }")]
    [InlineData("private void F(Body body) { if (body is null) Log(); body.Push(); }")]
    public void ANullGuardOfAPublicApiANullableParameterOrAnotherValueIsNotFlagged(string method) =>
        Source.RulesIn($"class C {{ {method} }}").Should().BeEmpty();

    [Fact]
    public void ANullGuardOfAnInterfaceMemberIsNotFlagged() =>
        Source.RulesIn("interface I { void F(Body body) { ArgumentNullException.ThrowIfNull(body); body.Push(); } }")
            .Should().BeEmpty();

    [Fact]
    public void ANullGuardOfALocalFunctionInAPublicMethodIsFlagged() =>
        Source.RulesIn("""
                       class C
                       {
                           public void F(Body body)
                           {
                               void Push(Body each) { ArgumentNullException.ThrowIfNull(each); each.Push(); each.Wake(); }
                               Push(body);
                               Push(body);
                           }
                       }
                       """).Should().Equal("guard-repeats-caller-check");

    [Fact]
    public void AShortMethodWhoseInliningWouldMakeItsCallerTooLongIsNotFlagged()
    {
        string calls = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"        Use({i});"));
        Source.RulesIn($$"""
                         class C
                         {
                             int F(int x)
                             {
                         {{calls}}
                                 return Twice(x) + 1;
                             }
                             private int Twice(int x) => x * 2;
                         }
                         """).Should().BeEmpty();
    }
}
