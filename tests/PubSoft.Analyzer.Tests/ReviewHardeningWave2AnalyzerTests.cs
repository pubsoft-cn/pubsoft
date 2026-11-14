using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace PubSoft.Analyzer.Tests;

/// <summary>TDD harness for <see cref="PubSoft.Analyzers.ReviewHardeningWave2Analyzer"/>.</summary>
public sealed class ReviewHardeningWave2AnalyzerTests
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
        return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PubSoft.Analyzers.ReviewHardeningWave2Analyzer()))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static ImmutableArray<Diagnostic> Ids(string code, string id)
        => Run(code).Where(d => d.Id == id).ToImmutableArray();

    // ── PUBSOFT056: mutate the enumerated collection inside its own foreach ──

    [Fact]
    public void RemoveInsideForeach_IsFlagged()
        => Assert.Single(Ids(
            "using System.Collections.Generic; class C { Dictionary<string,int> _d = new(); void M() { foreach (var p in _d) _d.Remove(p.Key); } }",
            "PUBSOFT056"));

    [Fact]
    public void AddInsideForeach_IsFlagged()
        => Assert.Single(Ids(
            "using System.Collections.Generic; class C { List<int> _l = new(); void M() { foreach (var x in _l) _l.Add(x); } }",
            "PUBSOFT056"));

    [Fact]
    public void KeysViewMutated_IsFlagged()
        => Assert.Single(Ids(
            "using System.Collections.Generic; class C { Dictionary<string,int> _d = new(); void M() { foreach (var k in _d.Keys) _d.Remove(k); } }",
            "PUBSOFT056"));

    [Fact]
    public void IndexAssignInsideForeach_IsFlagged()
        => Assert.Single(Ids(
            "using System.Collections.Generic; class C { Dictionary<string,int> _d = new(); void M() { foreach (var p in _d) _d[p.Key] = 0; } }",
            "PUBSOFT056"));

    [Fact]
    public void ConcurrentDictionary_IsNotFlagged()
        => Assert.Empty(Ids(
            "using System.Collections.Concurrent; class C { ConcurrentDictionary<string,int> _d = new(); void M() { foreach (var p in _d) _d.TryRemove(p.Key, out _); } }",
            "PUBSOFT056"));

    [Fact]
    public void SnapshotToList_IsNotFlagged()
        => Assert.Empty(Ids(
            "using System.Collections.Generic; using System.Linq; class C { Dictionary<string,int> _d = new(); void M() { foreach (var p in _d.ToList()) _d.Remove(p.Key); } }",
            "PUBSOFT056"));

    [Fact]
    public void MutatingOtherCollection_IsNotFlagged()
        => Assert.Empty(Ids(
            "using System.Collections.Generic; class C { Dictionary<string,int> _d = new(); Dictionary<string,int> _o = new(); void M() { foreach (var p in _d) _o.Remove(p.Key); } }",
            "PUBSOFT056"));

    [Fact]
    public void MutationExempt_IsNotFlagged()
        => Assert.Empty(Ids(
            "using System.Collections.Generic; class C { Dictionary<string,int> _d = new(); void M() { foreach (var p in _d) _d.Remove(p.Key); } } // pubsoft:exempt PUBSOFT056 \"intentional\"",
            "PUBSOFT056"));

    // ── PUBSOFT057: disposing an IHttpClientFactory-created client ──

    private const string FactoryStubs =
        "class HttpClient : System.IDisposable { public void Dispose() { } } " +
        "interface IHttpClientFactory { HttpClient CreateClient(string name); } ";

    [Fact]
    public void UsingVarFactoryClient_IsFlagged()
        => Assert.Single(Ids(
            FactoryStubs + "class C { void M(IHttpClientFactory f) { using var c = f.CreateClient(\"x\"); } }",
            "PUBSOFT057"));

    [Fact]
    public void UsingStatementFactoryClient_IsFlagged()
        => Assert.Single(Ids(
            FactoryStubs + "class C { void M(IHttpClientFactory f) { using (var c = f.CreateClient(\"x\")) { } } }",
            "PUBSOFT057"));

    [Fact]
    public void UsingExpressionFactoryClient_IsFlagged()
        => Assert.Single(Ids(
            FactoryStubs + "class C { void M(IHttpClientFactory f) { using (f.CreateClient(\"x\")) { } } }",
            "PUBSOFT057"));

    [Fact]
    public void UndisposedFactoryClient_IsNotFlagged()
        => Assert.Empty(Ids(
            FactoryStubs + "class C { void M(IHttpClientFactory f) { var c = f.CreateClient(\"x\"); } }",
            "PUBSOFT057"));

    [Fact]
    public void NonFactoryCreateClient_IsNotFlagged()
        => Assert.Empty(Ids(
            "class HttpClient : System.IDisposable { public void Dispose() { } } class Maker { public HttpClient CreateClient(string n) => new(); } " +
            "class C { void M(Maker m) { using var c = m.CreateClient(\"x\"); } }",
            "PUBSOFT057"));

    [Fact]
    public void OtherDisposable_IsNotFlagged()
        => Assert.Empty(Ids(
            "class Res : System.IDisposable { public void Dispose() { } } " +
            "class C { void M() { using var r = new Res(); } }",
            "PUBSOFT057"));

    // ── PUBSOFT058: reflection type enumeration without ReflectionTypeLoadException guard ──

    [Fact]
    public void UnguardedGetExportedTypes_IsFlagged()
        => Assert.Single(Ids(
            "using System.Reflection; class C { void M(Assembly a) { var t = a.GetExportedTypes(); } }",
            "PUBSOFT058"));

    [Fact]
    public void UnguardedGetTypes_IsFlagged()
        => Assert.Single(Ids(
            "using System.Reflection; class C { void M(Assembly a) { var t = a.GetTypes(); } }",
            "PUBSOFT058"));

    [Fact]
    public void GuardedGetExportedTypes_IsNotFlagged()
        => Assert.Empty(Ids(
            "using System.Reflection; class C { void M(Assembly a) { try { var t = a.GetExportedTypes(); } catch (ReflectionTypeLoadException) { } } }",
            "PUBSOFT058"));

    [Fact]
    public void GetExportedTypesExempt_IsNotFlagged()
        => Assert.Empty(Ids(
            "using System.Reflection; class C { void M(Assembly a) { var t = a.GetExportedTypes(); } // pubsoft:exempt PUBSOFT058 \"assembly set is trusted\" }",
            "PUBSOFT058"));
}
