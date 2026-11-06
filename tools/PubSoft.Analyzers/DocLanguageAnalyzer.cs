using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace PubSoft.Analyzers;

/// <summary>
/// PUBSOFT037 — XML doc comments must be English. Source ships one doc language;
/// localized IntelliSense goes through zh-CN satellite xml at pack time, not
/// mixed-language /// comments. Opt out via a pubsoft:exempt line comment.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocLanguageAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PUBSOFT037";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "XML doc comments must be English",
        "/// comment on '{0}' contains non-ASCII text — docs are single-language in source; zh-CN goes through the satellite xml pipeline",
        "Documentation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(CheckTree);
    }

    private static void CheckTree(SyntaxTreeAnalysisContext context)
    {
        var path = context.Tree.FilePath;
        if (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            return;

        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            if (!trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineDocumentationCommentTrivia)
                && !trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineDocumentationCommentTrivia))
                continue;

            var member = trivia.Token.Parent?.AncestorsAndSelf()
                .FirstOrDefault(a => a is Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax);
            if (member is not null && AnalyzerHelpers.IsExempt(member, DiagnosticId))
                continue;

            var text = trivia.ToFullString();
            foreach (var ch in text)
            {
                if (ch > 0x7f)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule, trivia.GetLocation(), member?.ToString().Split('(')[0].Trim() ?? "member"));
                    break;
                }
            }
        }
    }
}
