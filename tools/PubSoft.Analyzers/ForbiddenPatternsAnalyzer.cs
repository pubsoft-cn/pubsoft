using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace PubSoft.Analyzers;

/// <summary>
/// PUBSOFT002 — forbidden-identifier scan at compile time. Pattern lists are injected
/// via MSBuild properties (forwarded from environment variables, never committed):
///   PUBSOFT_FORBIDDEN_PATTERNS                  → applies to every scanned file
///   PUBSOFT_FORBIDDEN_PATTERNS_EXCLUDING_TESTS  → skipped under any tests/ path
/// Unset = no-op (local builds); CI injects them so the build fails on any leak.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForbiddenPatternsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PUBSOFT002";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Forbidden identifier in tracked content",
        "'{0}' line {1} matches a forbidden pattern",
        "Compliance",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(Start);
    }

    private static void Start(CompilationStartAnalysisContext context)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        var strict = Compile(options, "PUBSOFT_FORBIDDEN_PATTERNS");
        var lenient = Compile(options, "PUBSOFT_FORBIDDEN_PATTERNS_EXCLUDING_TESTS");
        if (strict is null && lenient is null) return;

        context.RegisterSyntaxTreeAction(c => Scan(c.Tree, c.ReportDiagnostic, strict, lenient, c.CancellationToken));
        context.RegisterAdditionalFileAction(c => ScanText(c.AdditionalFile, c.ReportDiagnostic, strict, lenient, c.CancellationToken));
    }

    private static Regex? Compile(AnalyzerConfigOptions options, string name)
    {
        if (!options.TryGetValue("build_property." + name, out var pattern)
            || string.IsNullOrWhiteSpace(pattern))
            return null;
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static bool IsTestPath(string path)
        => path.Contains("/tests/") || path.Contains("\\tests\\");

    private static void Scan(SyntaxTree tree, System.Action<Diagnostic> report,
        Regex? strict, Regex? lenient, System.Threading.CancellationToken ct)
        => ScanText(tree.FilePath, tree.GetText(ct), report, strict, lenient);

    private static void ScanText(AdditionalText file, System.Action<Diagnostic> report,
        Regex? strict, Regex? lenient, System.Threading.CancellationToken ct)
        => ScanText(file.Path, file.GetText(ct), report, strict, lenient);

    private static void ScanText(string path, SourceText? text, System.Action<Diagnostic> report,
        Regex? strict, Regex? lenient)
    {
        if (text is null) return;
        var patterns = IsTestPath(path) ? strict : Merge(strict, lenient);
        if (patterns is null) return;
        foreach (var line in text.Lines)
        {
            var match = patterns.Match(line.ToString());
            if (match.Success)
            {
                report(Diagnostic.Create(
                    Rule,
                    Location.Create(path, line.Span, text.Lines.GetLinePositionSpan(line.Span)),
                    System.IO.Path.GetFileName(path), line.LineNumber + 1));
            }
        }
    }

    private static Regex? Merge(Regex? strict, Regex? lenient)
        => strict is null ? lenient
            : lenient is null ? strict
            : new Regex(strict.ToString() + "|" + lenient.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
