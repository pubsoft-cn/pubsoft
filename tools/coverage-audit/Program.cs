// Member-level API coverage: reflection over built src DLLs (public surface)
// x Roslyn semantic analysis of test sources (referenced members).
using System.Reflection;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var srcBin = args.Length > 0 ? args[0] : "src";
var testsDir = args.Length > 1 ? args[1] : "tests";

// ── 1. enumerate public surface via reflection ──
var dlls = Directory.GetFiles(".", "PubSoft.*.dll", SearchOption.AllDirectories)
    .Where(f => f.Contains("bin/Release") && !f.Contains("ref") && !f.Contains("Tests") && !f.Contains("Smoke") && !f.Contains("Consumer"))
    .GroupBy(f => Path.GetFileName(f)).Select(g => g.First()).ToList();
// metadata-only load (no dependency resolution needed)
var resolver = new PathAssemblyResolver(
    ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(':')
    .Concat(dlls.Select(Path.GetFullPath)).Where(File.Exists));
using var mlc = new MetadataLoadContext(resolver);
var surface = new Dictionary<string, List<string>>();
var memberIfaces = new Dictionary<string, List<string>>();
foreach (var dll in dlls)
{
    Assembly asm;
    try { asm = mlc.LoadFromAssemblyPath(Path.GetFullPath(dll)); }
    catch { continue; }
    var members = new List<string>();
    Type[] exported;
    try { exported = asm.GetExportedTypes(); }
    catch { continue; }
    foreach (var t in exported)
    {
        if (t.FullName is null) continue;
        members.Add($"T:{t.Name}");
        var ifaces = new List<string>();
        try { foreach (var i in t.GetInterfaces()) ifaces.Add(i.Name); } catch { }
        foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            if (m is MethodInfo mi && !mi.IsSpecialName || m is ConstructorInfo || m is PropertyInfo || m is FieldInfo)
            {
                members.Add($"M:{t.Name}.{m.Name}");
                memberIfaces[$"M:{t.Name}.{m.Name}"] = ifaces;
            }
    }
    surface[asm.GetName().Name!] = members;
}

// ── 2. Roslyn: parse test sources, resolve referenced symbols ──
var trees = Directory.GetFiles(testsDir, "*.cs", SearchOption.AllDirectories)
    .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)).ToList();
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(':')
    .Concat(dlls.Select(Path.GetFullPath))
    .Where(File.Exists).Select(p => MetadataReference.CreateFromFile(p));
var comp = CSharpCompilation.Create("tests", trees, refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
var used = new HashSet<string>();
foreach (var tree in trees)
{
    var model = comp.GetSemanticModel(tree);
    foreach (var node in tree.GetRoot().DescendantNodes())
    {
        SymbolInfo info = node switch
        {
            InvocationExpressionSyntax inv => model.GetSymbolInfo(inv),
            MemberAccessExpressionSyntax ma => model.GetSymbolInfo(ma),
            ObjectCreationExpressionSyntax oc => model.GetSymbolInfo(oc),
            IdentifierNameSyntax id => model.GetSymbolInfo(id),
            GenericNameSyntax g => model.GetSymbolInfo(g),
            TypeOfExpressionSyntax to => model.GetSymbolInfo(to.Type),
            _ => default,
        };
        var sym = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        if (sym is null) continue;
        var containing = sym.ContainingType;
        if (containing is null) continue;
        if (sym is INamedTypeSymbol) used.Add($"T:{containing.Name}");
        else used.Add($"M:{containing.Name}.{sym.Name}");
        used.Add($"T:{containing.Name}"); // member access also covers the type
    }
}

// ── 3. diff ──
int total = 0, hit = 0;
var gaps = new Dictionary<string, List<string>>();
foreach (var (asm, members) in surface)
{
    foreach (var m in members)
    {
        total++;
        var memberName = m.StartsWith("M:") ? m[(m.LastIndexOf('.') + 1)..] : null;
        var covered2 = used.Contains(m)
            || memberName is not null
            && memberIfaces.TryGetValue(m, out var ifaces2)
            && ifaces2.Any(i => used.Contains($"M:{i}.{memberName}"));
        if (covered2) hit++;
        else
        {
            if (!gaps.TryGetValue(asm, out var l)) gaps[asm] = l = new();
            l.Add(m);
        }
    }
}
Console.WriteLine($"member-level coverage: {hit}/{total} = {hit * 100 / Math.Max(total, 1)}%");
foreach (var (asm, miss) in gaps.OrderByDescending(g => g.Value.Count))
{
    Console.WriteLine($"\n## {asm} ({miss.Count} uncovered)");
    foreach (var m in miss.Take(40)) Console.WriteLine("   " + m);
    if (miss.Count > 40) Console.WriteLine($"   ... +{miss.Count - 40} more");
}
