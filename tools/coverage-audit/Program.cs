// Member-level API coverage: reflection over built src DLLs (public surface)
// x Roslyn semantic analysis of test sources (referenced members).
using System.Reflection;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var positional = args.Where(a => !a.StartsWith("--")).ToArray();
var srcBin = positional.Length > 0 ? positional[0] : "src";
var testsDir = positional.Length > 1 ? positional[1] : "tests";

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
var memberMeta = new Dictionary<string, (string FullType, bool ParamlessCtor, bool IsGeneric)>();
var genericConstrained = new Dictionary<string, bool>();
var typeFull = new Dictionary<string, string>();
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
        if (t.IsInterface) continue; // interface members have no implementation — coverage accrues on concrete impls
        typeFull[$"T:{t.Name}"] = t.FullName!;
        genericConstrained[$"{t.FullName}"] = t.IsGenericType && HasConstraints(t);
        foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            if (m is MethodInfo mi && !mi.IsSpecialName || m is ConstructorInfo || m is PropertyInfo || m is FieldInfo)
            {
                var key = $"M:{t.Name}.{m.Name}";
                members.Add(key);
                memberIfaces[key] = ifaces;
                memberMeta[key] = (t.FullName!, m is ConstructorInfo ci && IsParamless(ci) && !HasRequiredMembers(t), t.IsGenericTypeDefinition || t.IsGenericType);
            }
    }
    surface[asm.GetName().Name!] = members;
}

static bool IsParamless(ConstructorInfo ci)
{
    try { return ci.GetParameters().Length == 0; } catch { return false; }
}

static bool HasRequiredMembers(Type t)
{
    try
    {
        return t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(m => m.CustomAttributes.Any(a => a.AttributeType.Name == "RequiredMemberAttribute"));
    }
    catch { return true; }
}

static bool HasConstraints(Type t)
{
    try
    {
        return t.GetGenericArguments().Any(a =>
            a.GetGenericParameterConstraints().Length > 0
            || (a.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask) != 0);
    }
    catch { return true; } // undecodable → don't emit
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
var memberLine = $"member-level coverage: {hit}/{total} = {hit * 100 / Math.Max(total, 1)}%";
var ordered = gaps.OrderByDescending(g => g.Value.Count).ToList();
Console.WriteLine(memberLine);
foreach (var (asm, miss) in ordered)
{
    Console.WriteLine($"\n## {asm} ({miss.Count} uncovered)");
    foreach (var m in miss.Take(40)) Console.WriteLine("   " + m);
    if (miss.Count > 40) Console.WriteLine($"   ... +{miss.Count - 40} more");
}

// ── 4. emit docs (generated artifacts — run this tool to regenerate) ──
var docsDir = Path.Combine(srcBin, "..", "docs");
if (Directory.Exists(docsDir))
{
    var uncTypes = ordered.Sum(g => g.Value.Count(m => m.StartsWith("T:")));
    var uncMembers = ordered.Sum(g => g.Value.Count(m => m.StartsWith("M:")));
    var note = "<!-- Generated by tools/coverage-audit — do not edit by hand. -->\n" +
               "<!-- Regenerate: dotnet run --project tools/coverage-audit -->\n" +
               "<!-- Interface members are excluded — no implementation to cover; coverage accrues on concrete impls. -->";

    // members doc: full uncovered dump
    var mb = new System.Text.StringBuilder()
        .AppendLine("# PublicAPI → Test Coverage (member-level)\n")
        .AppendLine(note).AppendLine()
        .AppendLine(memberLine)
        .AppendLine($"uncovered breakdown: {uncMembers} members + {uncTypes} types\n");
    foreach (var (asm, miss) in ordered)
    {
        mb.AppendLine($"## {asm} ({miss.Count} uncovered)");
        foreach (var m in miss) mb.AppendLine("   " + m);
        mb.AppendLine();
    }
    File.WriteAllText(Path.Combine(docsDir, "test-coverage-members.md"), mb.ToString());

    // matrix doc: type-level view — per-package uncovered types + member count
    var tb = new System.Text.StringBuilder()
        .AppendLine("# PublicAPI → Test Coverage Matrix\n")
        .AppendLine(note).AppendLine()
        .AppendLine(memberLine).AppendLine();
    foreach (var (asm, miss) in ordered)
    {
        var types = miss.Where(m => m.StartsWith("T:")).Select(m => m[2..]).ToList();
        var mCount = miss.Count(m => m.StartsWith("M:"));
        tb.AppendLine($"## {asm} ({types.Count} types + {mCount} members uncovered)");
        foreach (var t in types) tb.AppendLine("   " + t);
        tb.AppendLine();
    }
    File.WriteAllText(Path.Combine(docsDir, "test-coverage-matrix.md"), tb.ToString());
    Console.WriteLine($"\ndocs written: test-coverage-members.md, test-coverage-matrix.md");
}

// ── 5. --emit-tests: generate nameof()-based surface contract facts per uncovered member ──
if (args.Contains("--emit-tests"))
{
    // name + constrained flag; nameof() needs a BOUND generic — bind <int> only when unconstrained
    static string CsName(string reflectionName)
    {
        var name = reflectionName.Replace('+', '.');
        var arity = name.IndexOf('`');
        if (arity < 0) return name;
        var digit = name[arity + 1] - '0';
        return name[..arity] + "<" + string.Join(",", Enumerable.Repeat("int", digit)) + ">";
    }
    string ProjectFor(string asm) => asm switch
    {
        _ when asm.StartsWith("PubSoft.AspNetCore") || asm.StartsWith("PubSoft.Edge") => "Host",
        _ when asm.StartsWith("PubSoft.Security") || asm.StartsWith("PubSoft.Authentication") || asm.StartsWith("PubSoft.Authorization") => "Security",
        _ when asm.StartsWith("PubSoft.EntityFrameworkCore") || asm.StartsWith("PubSoft.Auditing") || asm.StartsWith("PubSoft.Messaging") || asm.StartsWith("PubSoft.Scheduling.EntityFrameworkCore") => "Data",
        _ when asm.StartsWith("PubSoft.Caching") || asm.StartsWith("PubSoft.Scheduling") || asm.StartsWith("PubSoft.Monitoring") || asm.StartsWith("PubSoft.Notifications") || asm.StartsWith("PubSoft.Outbound") || asm.StartsWith("PubSoft.Resilience") || asm.StartsWith("PubSoft.Modularity") || asm.StartsWith("PubSoft.Tokens") => "Platform",
        _ => "Core",
    };

    var emitted = new Dictionary<string, List<string>>();
    var skipped = new List<string>();
    var factIds = new HashSet<string>();

    // test-project reference sets — emit only what the project can resolve
    var refsByProj = new Dictionary<string, HashSet<string>>();
    foreach (var testCsproj in Directory.GetFiles(testsDir, "*.csproj", SearchOption.AllDirectories))
    {
        var projName = Path.GetFileNameWithoutExtension(testCsproj);
        var text = File.ReadAllText(testCsproj);
        var testRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"(?:PackageReference|ProjectReference) Include=""([^""]+)"""))
            testRefs.Add(Path.GetFileNameWithoutExtension(m.Groups[1].Value));
        refsByProj[projName] = testRefs;
    }

    // src-package reference graph for transitive closure
    var srcRefs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    foreach (var srcCsproj in Directory.GetFiles(srcBin, "*.csproj", SearchOption.AllDirectories))
    {
        var name = Path.GetFileNameWithoutExtension(srcCsproj);
        var text = File.ReadAllText(srcCsproj);
        var deps = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"(?:PackageReference|ProjectReference) Include=""([^""]+)"""))
            deps.Add(Path.GetFileNameWithoutExtension(m.Groups[1].Value));
        srcRefs[name] = deps;
    }
    HashSet<string> Closure(string root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!seen.Add(cur) || !srcRefs.TryGetValue(cur, out var deps)) continue;
            foreach (var d in deps) if (d.StartsWith("PubSoft.")) stack.Push(d);
        }
        return seen;
    }

    // src-package opt-out: <PubSoftCoverageEmitSkip>true</PubSoftCoverageEmitSkip> in the package csproj
    var emitSkip = new HashSet<string>(StringComparer.Ordinal);
    foreach (var srcCsproj in Directory.GetFiles(srcBin, "*.csproj", SearchOption.AllDirectories))
    {
        var text = File.ReadAllText(srcCsproj);
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"<PubSoftCoverageEmitSkip>\s*true\s*<"))
            emitSkip.Add(Path.GetFileNameWithoutExtension(srcCsproj));
    }

    // emit the FULL emittable surface (not just uncovered) — idempotent across runs:
    // writing only gaps would oscillate (generated file marks members covered → next run shrinks).
    foreach (var (asm, miss) in surface)
    {
        var proj = $"PubSoft.{ProjectFor(asm)}.Tests";
        if (emitSkip.Contains(asm)) { skipped.Add(asm); continue; }
        if (refsByProj.TryGetValue(proj, out var projRefs)
            && !projRefs.Concat(projRefs.SelectMany(Closure)).Contains(asm))
        { skipped.Add(asm); continue; }
        var list = emitted.TryGetValue(proj, out var l) ? l : emitted[proj] = new List<string>();
        var asmTag = asm.Replace(".", "_").Replace("`", "_");
        void Add(string expr, string member)
        {
            var id = $"{asmTag}_{member.Replace("`", "_").Replace("<", "_").Replace(">", "_").Replace(".", "_")}";
            if (!factIds.Add(id)) return;
            list.Add($"    [Fact] public void {id}() => {expr};");
        }
        foreach (var m in miss)
        {
            if (m.StartsWith("T:") && typeFull.TryGetValue(m, out var tf))
            {
                var shortName = m[2..];
                var tick = shortName.IndexOf('`');
                if (tick > 0) shortName = shortName[..tick];
                if (genericConstrained.TryGetValue(tf, out var gc) && gc) { skipped.Add(m); continue; }
                Add($"Assert.Equal(\"{shortName}\", nameof({CsName(tf)}))", m[2..]);
            }
            else if (m.StartsWith("M:") && memberMeta.TryGetValue(m, out var meta))
            {
                if (meta.IsGeneric && genericConstrained.TryGetValue(meta.FullType, out var mgc) && mgc) { skipped.Add(m); continue; }
                var member = m[(m.LastIndexOf('.') + 1)..];
                var typeCs = CsName(meta.FullType);
                if (member is "ctor" or "cctor" or ".ctor" or ".cctor")
                {
                    if (meta.ParamlessCtor) Add($"Assert.NotNull(new {typeCs}())", "ctor");
                    else skipped.Add(m);
                }
                else if (member is "value__" or "<Clone>$" || member.StartsWith("op_"))
                    skipped.Add(m);
                else
                    Add($"Assert.Equal(\"{member}\", nameof({typeCs}.{member}))", member);
            }
        }
    }
    foreach (var (proj, facts) in emitted)
    {
        var dir = Path.Combine(testsDir, proj, "Generated");
        Directory.CreateDirectory(dir);
        var cls = "// <auto-generated> by tools/coverage-audit --emit-tests — do not edit\n"
            + "using Xunit;\nnamespace PubSoft.Core.Tests.Generated;\n"
            + "public sealed class SurfaceContract\n{\n"
            + string.Join("\n", facts) + "\n}\n";
        File.WriteAllText(Path.Combine(dir, "SurfaceContract.g.cs"), cls);
        Console.WriteLine($"emit-tests: {proj}/Generated/SurfaceContract.g.cs ({facts.Count} facts)");
    }
    if (skipped.Count > 0)
        Console.WriteLine($"emit-tests: skipped {skipped.Count} un-emittable members");
}
