using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace PubSoft.Analyzers;

/// <summary>
/// PUBSOFT001 — volatile repo counts (N packages/tests/endpoints/…) must not appear
/// in markdown prose. Numbers in fenced code blocks are fine; prose counts drift
/// stale the moment code changes. Generated baselines (PublicAPI.*.txt) are the
/// only sanctioned home for counts derived from the code.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocNumbersAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PUBSOFT001";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Volatile counts must not live in documentation",
        "Markdown '{0}' line {1} embeds a volatile count '{2}' — remove it; counts belong only in generated artifacts",
        "Documentation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // count + unit word — the drifting pattern this rule exists to catch
    private static readonly Regex VolatileCount = new Regex(
        @"\d+[\s~约]*(个|款|条|项|包|packages?|tests?|用例|consumers?|endpoints?|端点|检查|middleware|中间件)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterAdditionalFileAction(AnalyzeFile);
    }

    private static void AnalyzeFile(AdditionalFileAnalysisContext context)
    {
        var path = context.AdditionalFile.Path;
        if (!path.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase)) return;

        var text = context.AdditionalFile.GetText(context.CancellationToken);
        if (text is null) return;

        var inFence = false;
        foreach (var line in text.Lines)
        {
            var s = line.ToString();
            if (s.TrimStart().StartsWith("```", System.StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            var match = VolatileCount.Match(s);
            if (match.Success)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    Location.Create(path, line.Span, text.Lines.GetLinePositionSpan(line.Span)),
                    System.IO.Path.GetFileName(path), line.LineNumber + 1, match.Value));
            }
        }
    }
}
