using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace PubSoft.Analyzer.Tests;

/// <summary>TDD harness for <see cref="PubSoft.Analyzers.ReviewHardeningWave3Analyzer"/>.</summary>
public sealed class ReviewHardeningWave3AnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> Refs =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Collections.Concurrent.ConcurrentDictionary<,>).Assembly.Location),
    ];

    private static ImmutableArray<Diagnostic> Run(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create("T", [tree], Refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PubSoft.Analyzers.ReviewHardeningWave3Analyzer()))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static ImmutableArray<Diagnostic> Ids(string code, string id)
        => Run(code).Where(d => d.Id == id).ToImmutableArray();

    // ── PUBSOFT059: GC.Collect in library code ──

    [Fact]
    public void GcCollectInDispose_IsFlagged()
        => Assert.Single(Ids(
            "class C : System.IDisposable { public void Dispose() { System.GC.Collect(); } }",
            "PUBSOFT059"));

    [Fact]
    public void GcCollectAnywhere_IsFlagged()
        => Assert.Single(Ids(
            "class C { void M() { System.GC.Collect(2); } }",
            "PUBSOFT059"));

    [Fact]
    public void UnrelatedCollectMethod_IsNotFlagged()
        => Assert.Empty(Ids(
            "class GC2 { public void Collect() { } } class C { void M(GC2 g) { g.Collect(); } }",
            "PUBSOFT059"));

    [Fact]
    public void GcCollectExempt_IsNotFlagged()
        => Assert.Empty(Ids(
            "class C { void M() { System.GC.Collect(); } } // pubsoft:exempt PUBSOFT059 \"interop harness forces finalization\"",
            "PUBSOFT059"));

    // ── PUBSOFT060: rowsAffected treated as success with a vacuous comparison ──

    private const string UpdateStub =
        "static class Ef { public static int ExecuteUpdate(this object o) => 0; public static System.Threading.Tasks.Task<int> ExecuteUpdateAsync(this object o) => null!; } ";

    [Fact]
    public void DirectGreaterThanMinusOne_IsFlagged()
        => Assert.Single(Ids(
            UpdateStub + "class C { bool M(object q) { return q.ExecuteUpdate() > -1; } }",
            "PUBSOFT060"));

    [Fact]
    public void LocalGreaterOrEqualZero_IsFlagged()
        => Assert.Single(Ids(
            UpdateStub + "class C { bool M(object q) { var rows = q.ExecuteUpdate(); return rows >= 0; } }",
            "PUBSOFT060"));

    [Fact]
    public void LocalNotEqualMinusOne_IsFlagged()
        => Assert.Single(Ids(
            UpdateStub + "class C { bool M(object q) { var rowsAffected = q.ExecuteUpdate(); return rowsAffected != -1; } }",
            "PUBSOFT060"));

    [Fact]
    public void ReversedOperandOrder_IsFlagged()
        => Assert.Single(Ids(
            UpdateStub + "class C { bool M(object q) { var rows = q.ExecuteUpdate(); return -1 < rows; } }",
            "PUBSOFT060"));

    [Fact]
    public void ExactOneCheck_IsNotFlagged()
        => Assert.Empty(Ids(
            UpdateStub + "class C { bool M(object q) { var rows = q.ExecuteUpdate(); return rows == 1; } }",
            "PUBSOFT060"));

    [Fact]
    public void PositiveCheck_IsNotFlagged()
        => Assert.Empty(Ids(
            UpdateStub + "class C { bool M(object q) { return q.ExecuteUpdate() > 0; } }",
            "PUBSOFT060"));

    [Fact]
    public void UnrelatedLocalGreaterThanMinusOne_IsNotFlagged()
        => Assert.Empty(Ids(
            "class C { bool M() { var rows = 5; return rows > -1; } }",
            "PUBSOFT060"));

    // ── PUBSOFT061: ignored TryParse result ──

    [Fact]
    public void BareTryParseStatement_IsFlagged()
        => Assert.Single(Ids(
            "class C { void M(string s) { int.TryParse(s, out var x); } }",
            "PUBSOFT061"));

    [Fact]
    public void BareTryParseExactStatement_IsFlagged()
        => Assert.Single(Ids(
            "class C { void M(string s) { System.DateTime.TryParseExact(s, \"f\", null, 0, out var x); } }",
            "PUBSOFT061"));

    [Fact]
    public void TryParseInCondition_IsNotFlagged()
        => Assert.Empty(Ids(
            "class C { bool M(string s) { if (!int.TryParse(s, out var x)) return false; return x > 0; } }",
            "PUBSOFT061"));

    [Fact]
    public void TryParseAssigned_IsNotFlagged()
        => Assert.Empty(Ids(
            "class C { void M(string s) { var ok = int.TryParse(s, out var x); } }",
            "PUBSOFT061"));

    // ── PUBSOFT062: discarded task fire-and-forget ──

    private const string TaskStub =
        "using System.Threading.Tasks; class C { static Task WorkAsync() => Task.CompletedTask; static Task<int> CountAsync() => Task.FromResult(1); ";

    [Fact]
    public void DiscardedTaskDiscard_IsFlagged()
        => Assert.Single(Ids(
            TaskStub + "void M() { _ = WorkAsync(); } }",
            "PUBSOFT062"));

    [Fact]
    public void DiscardedGenericTask_IsFlagged()
        => Assert.Single(Ids(
            TaskStub + "void M() { _ = CountAsync(); } }",
            "PUBSOFT062"));

    [Fact]
    public void DiscardedValueTask_IsFlagged()
        => Assert.Single(Ids(
            TaskStub + "static System.Threading.Tasks.ValueTask VAsync() => default; void M() { _ = VAsync(); } }",
            "PUBSOFT062"));

    [Fact]
    public void AwaitedTask_IsNotFlagged()
        => Assert.Empty(Ids(
            TaskStub + "async Task M() { await WorkAsync(); } }",
            "PUBSOFT062"));

    [Fact]
    public void ReturnedTask_IsNotFlagged()
        => Assert.Empty(Ids(
            TaskStub + "Task M() => WorkAsync(); }",
            "PUBSOFT062"));

    [Fact]
    public void DiscardExempt_IsNotFlagged()
        => Assert.Empty(Ids(
            TaskStub + "void M() { _ = WorkAsync(); } } // pubsoft:exempt PUBSOFT062 \"exceptions captured inside\"",
            "PUBSOFT062"));

    [Fact]
    public void DiscardedNonTask_IsNotFlagged()
        => Assert.Empty(Ids(
            "class C { static int Count() => 1; void M() { _ = Count(); } }",
            "PUBSOFT062"));
}
