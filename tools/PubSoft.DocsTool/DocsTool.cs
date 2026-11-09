using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PubSoft.DocsTool;

/// <summary>
/// README api-surface ledger, backed by the real compiled public surface —
/// reflection over the built PubSoft.* assemblies, not regex over source text.
/// --check: report STALE (backticked names absent from the surface) and MISSING
/// (surface symbols absent from the README); exit 1 on drift.
/// --sync: rewrite the machine-owned pubsoft:api-surface block in every package README.
/// </summary>
internal static class DocsTool
{
    private const string Start = "<!-- pubsoft:api-surface:start -->";
    private const string End = "<!-- pubsoft:api-surface:end -->";
    private static readonly string RepoRoot = LocateRepo();

    private static string LocateRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PubSoft.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private sealed record Pkg(string Name, string Dir, string Readme, List<string> Exts, List<string> Props, List<string> Types);

    public static int Run(string[] args)
    {
        var mode = args.FirstOrDefault() ?? "--check";
        var snippets = LoadSnippets();
        var snippetRows = mode == "--check" ? CheckSnippets(snippets) : SyncSnippets(snippets);
        var pkgs = LoadPackages();
        var apiRows = mode == "--check" ? PublicApi(verifyOnly: true) : PublicApi(verifyOnly: false);
        return mode switch
        {
            "--sync" => Sync(pkgs) | snippetRows | apiRows,
            "--check" => Check(pkgs) | snippetRows | apiRows,
            "--coverage" => Coverage(pkgs),
            _ => throw new ArgumentException($"unknown mode {mode}")
        };
    }

    private static List<Pkg> LoadPackages()
    {
        var list = new List<Pkg>();
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(RepoRoot, "src"), "*", SearchOption.AllDirectories)
                     .Where(d => File.Exists(Path.Combine(d, "README.md")) && Directory.GetFiles(d, "*.csproj").Length > 0))
        {
            var name = Path.GetFileName(dir);
            var dll = Directory.EnumerateFiles(dir, name + ".dll", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Contains(Path.Combine("bin", "Release")));
            var exts = new List<string>(); var props = new List<string>(); var types = new List<string>();
            if (dll is not null)
            {
                Assembly asm;
                try { asm = Assembly.LoadFrom(dll); }
                catch { continue; }
                foreach (var t in SafeTypes(asm))
                {
                    types.Add(t.Name.Split('`')[0]);
                    if (t.IsAbstract && t.IsSealed) // static class
                        exts.AddRange(t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .Where(m => m.Name.Contains("PubSoft"))
                            .Select(m => m.Name));
                    props.AddRange(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanWrite).Select(p => p.Name));
                }
            }
            list.Add(new Pkg(name, dir, Path.Combine(dir, "README.md"), exts.Distinct().OrderBy(x => x).ToList(), props.Distinct().OrderBy(x => x).ToList(), types));
        }
        return list;
    }

    private static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try { return asm.GetExportedTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static int Sync(List<Pkg> pkgs)
    {
        var changed = 0;
        foreach (var p in pkgs)
        {
            var rm = File.ReadAllText(p.Readme);
            var block = RenderBlock(p);
            string next;
            if (rm.Contains(Start) && rm.Contains(End))
                next = Regex.Replace(rm, Regex.Escape(Start) + ".*?" + Regex.Escape(End), block, RegexOptions.Singleline);
            else
            {
                var m = Regex.Match(rm, @"(?im)^## [^\n]*surface[^\n]*$");
                if (m.Success)
                {
                    var nxt = Regex.Match(rm[(m.Index + m.Length)..], @"(?m)^## ");
                    var pos = nxt.Success ? m.Index + m.Length + nxt.Index : rm.Length;
                    next = rm[..pos].TrimEnd() + "\n\n" + block + "\n\n" + rm[pos..];
                }
                else
                    next = rm.TrimEnd() + "\n\n## API surface\n\n" + block + "\n";
            }
            if (next != rm) { File.WriteAllText(p.Readme, next); changed++; }
        }
        Console.WriteLine($"updated {changed} READMEs");
        return 0;
    }

    private static string RenderBlock(Pkg p)
    {
        var sb = new StringBuilder(Start).Append('\n')
            .Append("\nMachine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).\n\n");
        if (p.Exts.Count > 0)
        {
            sb.Append("**Registration**\n\n| Method |\n|---|---|\n");
            foreach (var m in p.Exts) sb.Append("| `").Append(m).Append("` |\n");
            sb.Append('\n');
        }
        if (p.Props.Count > 0)
            sb.Append("**(options & records)**\n\n`").Append(string.Join("`, `", p.Props.Where(n => n.Length > 4))).Append("`\n\n");
        return sb.Append(End).ToString();
    }

    // ---- type-level test coverage: public types referenced in the layer's test sources ----
    private static readonly Dictionary<string, string?> LayerMap = new()
    {
        ["core"] = "PubSoft.Core.Tests",
        ["platform"] = "PubSoft.Platform.Tests",
        ["host"] = "PubSoft.Host.Tests",
        ["data"] = "PubSoft.Data.Tests",
        ["messaging"] = "PubSoft.Platform.Tests",
        ["security"] = "PubSoft.Security.Tests",
        ["testing"] = null,
        ["analyzers"] = null,
    };

    private static int Coverage(List<Pkg> pkgs)
    {
        var testsDir = Path.Combine(RepoRoot, "tests");
        var testSrc = Directory.EnumerateDirectories(testsDir).ToDictionary(
            d => Path.GetFileName(d)!, d => string.Join("\n", Directory.EnumerateFiles(d, "*.cs").Select(f => File.ReadAllText(f))));
        var allSrc = string.Join("\n", testSrc.Values);
        var total = 0; var covered = 0; var gaps = new Dictionary<string, List<string>>(); var misplaced = new Dictionary<string, int>();
        foreach (var p in pkgs)
        {
            var layer = new DirectoryInfo(p.Dir).Parent!.Name;
            if (!LayerMap.TryGetValue(layer, out var tname) || tname is null) continue;
            var layerSrc = testSrc.GetValueOrDefault(tname, "");
            foreach (var t in p.Types)
            {
                var simple = t.Split('.').Last();
                total++;
                if (allSrc.Contains(simple))
                {
                    covered++;
                    if (!layerSrc.Contains(simple))
                        misplaced[p.Name] = misplaced.GetValueOrDefault(p.Name) + 1;
                }
                else
                    (gaps.TryGetValue(p.Name, out var g) ? g : gaps[p.Name] = []).Add(simple);
            }
        }
        Console.WriteLine($"type-level coverage (global): {covered}/{total} = {covered * 100 / Math.Max(total, 1)}%");
        foreach (var (k, miss) in gaps)
        {
            Console.WriteLine($"\n## {k} ({miss.Count} uncovered)");
            foreach (var t in miss.Take(30)) Console.WriteLine($"   {t}");
            if (miss.Count > 30) Console.WriteLine($"   ... +{miss.Count - 30} more");
        }
        if (misplaced.Count > 0)
        {
            Console.WriteLine("\n## covered-but-wrong-layer");
            foreach (var (k, n) in misplaced) Console.WriteLine($"   {k}: {n}");
        }
        return 0; // informational — not a hard gate
    }

    // ---- PublicAPI.*.txt baselines via the RS0016/RS0017 analyzer codefix ----
    private static int PublicApi(bool verifyOnly)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet",
            $"format analyzers PubSoft.sln {(verifyOnly ? "--verify-no-changes " : "")}--diagnostics RS0016 RS0017 --severity warn")
        { WorkingDirectory = RepoRoot, RedirectStandardOutput = true, UseShellExecute = false };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return proc.ExitCode == 0 ? 0 : 1;
    }

    // ---- named snippets: README fenced blocks <= #region snippet_Name ----
    private static readonly Regex SnippetRe = new(@"<!-- pubsoft:snippet:(\w+) -->\s*
?
```(csharp|cs)
(.*?)```", RegexOptions.Singleline);

    private static Dictionary<string, string> LoadSnippets()
    {
        var map = new Dictionary<string, string>();
        var dir = Path.Combine(RepoRoot, "tests", "PubSoft.DocsSnippets");
        foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(text, @"(?m)^[ 	]*#region snippet_(\w+)
?
(.*?)^[ 	]*#endregion", RegexOptions.Singleline))
            {
                // strip common leading indentation so the block pastes flush-left
                var body = m.Groups[2].Value.TrimEnd('\n');
                var lines = body.Split('\n');
                var indent = lines.Where(l => l.Trim().Length > 0)
                    .Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
                map[m.Groups[1].Value] = string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l)).Trim('\n');
            }
        }
        return map;
    }

    private static int SyncSnippets(Dictionary<string, string> snippets)
    {
        var changed = 0;
        foreach (var rmd in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "README.md", SearchOption.AllDirectories))
        {
            var rm = File.ReadAllText(rmd);
            var next = SnippetRe.Replace(rm, m => snippets.TryGetValue(m.Groups[1].Value, out var code)
                ? $"<!-- pubsoft:snippet:{m.Groups[1].Value} -->\n```csharp\n{code}\n```"
                : m.Value);
            if (next != rm) { File.WriteAllText(rmd, next); changed++; }
        }
        return changed > 0 ? 0 : 0; // sync never fails; count is informational
    }

    private static int CheckSnippets(Dictionary<string, string> snippets)
    {
        var rows = 0;
        foreach (var rmd in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "README.md", SearchOption.AllDirectories))
        {
            var rm = File.ReadAllText(rmd);
            foreach (Match m in SnippetRe.Matches(rm))
            {
                var name = m.Groups[1].Value;
                if (!snippets.TryGetValue(name, out var code))
                { Console.WriteLine($"== {Path.GetFileName(Path.GetDirectoryName(rmd))}\n   STALE(snippet-missing-region): {name}"); rows++; }
                else if (m.Groups[3].Value.TrimEnd() != code.TrimEnd())
                { Console.WriteLine($"== {Path.GetFileName(Path.GetDirectoryName(rmd))}\n   STALE(snippet-drift): {name}"); rows++; }
            }
        }
        return rows;
    }

    private static readonly Regex FenceRe = new(@"```(?:csharp|cs)\n(.*?)```", RegexOptions.Singleline);
    // PubSoft-branded names only — non-branded identifiers can't be separated
    // from host-side example stubs (AppDbContext, MyXxxProvider) without noise;
    // interface-type renames are already caught by the prose STALE scan.
    private static readonly Regex ApiNameRe = new(@"\b(\w*PubSoft\w+)\b");

    private static int Check(List<Pkg> pkgs)
    {
        var allSymbols = pkgs.SelectMany(p => p.Types.Concat(p.Exts).Concat(p.Props)).ToHashSet();
        var rows = 0;
        var claimRe = new Regex(@"`(AddPubSoft\w+|UsePubSoft\w+|[A-Z]\w*Options\b|\w*Provider\b|\w*Extensions\b)\(?`?");
        var bcl = new HashSet<string> { "BuildServiceProvider", "ServiceProvider", "IServiceProvider", "ServiceCollection", "IServiceCollection", "TimeProvider", "ForwardedHeadersOptions", "JwtBearerOptions", "JsonSerializerOptions", "IConnectionMultiplexer", "IHealthChecksBuilder", "DbContextOptions", "AddDbContext", "GetRequiredService", "IReadOnlyList", "DbContext", "IServiceProvider", "ILoggerFactory", "HttpClient", "IConfiguration" };
        foreach (var p in pkgs)
        {
            var rm = File.ReadAllText(p.Readme);
            var claimed = claimRe.Matches(rm).Select(m => m.Groups[1].Value).Where(c => c.Length > 6 && !bcl.Contains(c)).ToHashSet();
            var stale = claimed.Where(c => !allSymbols.Contains(c)).OrderBy(x => x).ToList();
            var missing = p.Exts.Concat(p.Props.Where(n => n.Length > 4)).Where(x => !rm.Contains(x)).OrderBy(x => x).ToList();
            // fenced csharp blocks: API-looking identifiers must resolve in the surface
            var blockStale = FenceRe.Matches(rm)
                .SelectMany(f => ApiNameRe.Matches(f.Groups[1].Value).Select(m => m.Groups[1].Value))
                .Where(n => !allSymbols.Contains(n) && !bcl.Contains(n))
                .Distinct().OrderBy(x => x).ToList();
            if (stale.Count == 0 && missing.Count == 0 && blockStale.Count == 0) continue;
            rows++;
            Console.WriteLine($"== {p.Name}");
            foreach (var x in stale) Console.WriteLine($"   STALE(doc-only): {x}");
            foreach (var x in missing) Console.WriteLine($"   MISSING(in-code): {x}");
            foreach (var x in blockStale) Console.WriteLine($"   STALE(block-symbol): {x}");
        }
        return rows > 0 ? 1 : 0;
    }
}
