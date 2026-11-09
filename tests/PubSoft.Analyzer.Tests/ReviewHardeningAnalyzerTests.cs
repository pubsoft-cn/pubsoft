using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace PubSoft.Analyzer.Tests;

/// <summary>TDD harness for <see cref="PubSoft.Analyzers.ReviewHardeningAnalyzer"/>.</summary>
public sealed class ReviewHardeningAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> Refs =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
    ];

    private static ImmutableArray<Diagnostic> Run(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create("T", [tree], Refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PubSoft.Analyzers.ReviewHardeningAnalyzer()))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static ImmutableArray<Diagnostic> Ids(string code, string id)
        => Run(code).Where(d => d.Id == id).ToImmutableArray();

    [Fact]
    public void ClockReadBypassingInjection_IsFlagged()
        => Assert.Single(Ids("class C { void M() { var t = System.TimeProvider.System.GetUtcNow(); } }", "PUBSOFT040"));

    [Fact]
    public void InjectedTimeProviderFallback_IsNotFlagged()
        => Assert.Empty(Ids("class C { private readonly TimeProvider _t; C(TimeProvider? t) { _t = t ?? TimeProvider.System; } }", "PUBSOFT040"));

    [Fact]
    public void EmptyCatch_IsFlagged()
        => Assert.Single(Ids("class C { void M() { try { } catch { } } }", "PUBSOFT041"));

    [Fact]
    public void TaskResult_IsFlagged()
        => Assert.Single(Ids("using System.Threading.Tasks; class C { void M(Task<int> t) { var x = t.Result; } }", "PUBSOFT042"));

    [Fact]
    public void RecordResultProperty_IsNotFlagged()
        => Assert.Empty(Ids("record R(int Result); class C { void M(R r) { var x = r.Result; } }", "PUBSOFT042"));

    [Fact]
    public void SemaphoreSlimWait_IsNotFlagged()
        => Assert.Empty(Ids("using System.Threading; class C { void M(SemaphoreSlim s) { s.Wait(0); } }", "PUBSOFT042"));

    [Fact]
    public void UnguardedEntityCtor_IsFlagged()
        => Assert.Single(Ids("class Entity<T> { public T Id = default!; } class C : Entity<long> { public C(long id) { Id = id; } }", "PUBSOFT043"));

    [Fact]
    public void GuardedEntityCtor_IsNotFlagged()
        => Assert.Empty(Ids("class Entity<T> { public T Id = default!; } class C : Entity<long> { public C(long id) { if (id <= 0) throw new System.ArgumentOutOfRangeException(); Id = id; } }", "PUBSOFT043"));

    [Fact]
    public void DuplicateSummary_IsFlagged()
        => Assert.Single(Ids("/// <summary>A</summary>\n/// <summary>B</summary>\nclass C { }", "PUBSOFT044"));

    [Fact]
    public void ExemptComment_Suppresses()
        => Assert.Empty(Ids("class C { void M() { try { } catch { /* pubsoft:exempt PUBSOFT041 \"reason\" */ } } }", "PUBSOFT041"));
}
