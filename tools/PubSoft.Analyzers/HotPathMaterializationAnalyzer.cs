using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PubSoft.Analyzers;

/// <summary>
/// PUBSOFT003 — lookup-style types (*Registry/*Catalog/*Store/*Index/*Resolver/*Matcher)
/// must index their collections at construction, not re-filter them per call.
/// Catches the double-alloc anti-pattern: `x.Where(...).ToArray()` or
/// `Array.AsReadOnly(x.Where(...).ToArray())` inside a non-constructor member.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HotPathMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PUBSOFT003";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Lookup types must index at construction, not materialize per call",
        "'{0}' re-materializes a collection per call in '{1}' — build an index/snapshot in the constructor and return the stored read-only view",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly Regex LookupTypeName = new Regex(
        @"(Registry|Catalog|Store|Index|Resolver|Matcher|Lookup|Repository|Cache)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement);
    }

    private static bool HasBoundedCost(SyntaxNode node)
        => node.DescendantNodesAndSelf().OfType<AttributeSyntax>()
            .Any(a => a.Name.ToString() is "ColdPath" or "BoundedCostAttribute"
                or "PubSoft.Core.BoundedCost" or "PubSoft.Core.BoundedCostAttribute");

    private static string MethodLabel(SyntaxNode node)
    {
        var text = node.ToString();
        return text.Length <= 60 ? text : text.Substring(0, 60);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var declaring = context.Node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (declaring is null || !LookupTypeName.IsMatch(declaring.Identifier.Text))
            return;
        // [BoundedCost("reason")] opts out explicitly — name-matched so the
        // attribute can live in PubSoft.Core without an analyzer reference.
        if (HasBoundedCost(context.Node) || HasBoundedCost(declaring))
            return;

        foreach (var invocation in context.Node.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            // Array.AsReadOnly(X.Where(...).ToArray()) — double alloc per call
            if (invocation.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "AsReadOnly"
                && invocation.ArgumentList.Arguments.Count == 1
                && invocation.ArgumentList.Arguments[0].Expression.DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression is MemberAccessExpressionSyntax inner
                        && (inner.Name.Identifier.Text == "Where" || inner.Name.Identifier.Text == "Select")))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(),
                    declaring.Identifier.Text, MethodLabel(context.Node)));
                continue;
            }

            // X.Where(...).ToArray() / .ToList() — materializing filter per call
            if (invocation.Expression is MemberAccessExpressionSyntax terminal
                && (terminal.Name.Identifier.Text == "ToArray" || terminal.Name.Identifier.Text == "ToList")
                && terminal.Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression is MemberAccessExpressionSyntax filter
                        && (filter.Name.Identifier.Text == "Where" || filter.Name.Identifier.Text == "Select")))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(),
                    declaring.Identifier.Text, MethodLabel(context.Node)));
            }
        }
    }
}
