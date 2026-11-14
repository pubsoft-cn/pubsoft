using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PubSoft.Analyzers;

/// <summary>
/// Second wave of rules extracted from the external code-review sweep. Defect-class
/// patterns only — mechanical detection, low false-positive bar, warn (not error) on
/// first landing. Exempt via `pubsoft:exempt &lt;id&gt; "reason"` on the source line.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReviewHardeningWave2Analyzer : DiagnosticAnalyzer
{
    /// <summary>PUBSOFT056 rule id.</summary>
    public const string MutateDuringEnumerationId = "PUBSOFT056";
    /// <summary>PUBSOFT057 rule id.</summary>
    public const string DisposeFactoryClientId = "PUBSOFT057";
    /// <summary>PUBSOFT058 rule id.</summary>
    public const string UnguardedTypeEnumerationId = "PUBSOFT058";

    private static DiagnosticDescriptor Make(string id, string title, string message)
        => new DiagnosticDescriptor(id, title, message, "Hardening", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor[] Rules =
    {
        Make(MutateDuringEnumerationId, "Collection mutated during its own enumeration",
            "'{0}' is mutated inside a foreach over itself — non-concurrent collections throw InvalidOperationException; snapshot (.ToList()) or collect-then-remove"),
        Make(DisposeFactoryClientId, "Disposing an IHttpClientFactory-created client",
            "factory-created HttpClient carries no owned lifetime — drop the using; disposal is a misleading no-op over the pooled handler"),
        Make(UnguardedTypeEnumerationId, "Reflection type enumeration without ReflectionTypeLoadException guard",
            "'{0}' throws ReflectionTypeLoadException on partially loadable assemblies — wrap in try and salvage ex.Types.Where(t => t != null)"),
    };

    private static readonly string[] MutatingMethods =
    {
        "Add", "TryAdd", "AddRange", "Insert", "Remove", "TryRemove", "RemoveAll",
        "RemoveRange", "TryUpdate", "Clear", "Enqueue", "Dequeue", "Push", "Pop",
    };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(CheckMutateDuringEnumeration, SyntaxKind.ForEachStatement);
        context.RegisterSyntaxNodeAction(CheckDisposeFactoryClient,
            SyntaxKind.LocalDeclarationStatement, SyntaxKind.UsingStatement);
        context.RegisterSyntaxNodeAction(CheckUnguardedTypeEnumeration, SyntaxKind.InvocationExpression);
    }

    private static void CheckMutateDuringEnumeration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ForEachStatementSyntax f)
            return;
        var expr = f.Expression.ToString();
        // A call in the expression (ToList()/Where()/factory method) either snapshots or
        // escapes same-symbol matching — either way the pattern cannot be proven locally.
        if (expr.IndexOf('(') >= 0)
            return;
        // Live views still enumerate the underlying collection — resolve the view's
        // source so .Keys/.Values on a concurrent dictionary are not misjudged by the
        // ICollection<T> facade they return.
        var baseText = expr;
        ExpressionSyntax typeNode = f.Expression;
        if (f.Expression is MemberAccessExpressionSyntax view
            && view.Name.Identifier.ValueText is "Keys" or "Values")
        {
            baseText = view.Expression.ToString();
            typeNode = view.Expression;
        }
        // System.Collections.Concurrent.* explicitly tolerates mutation during enumeration.
        var type = context.SemanticModel.GetTypeInfo(typeNode).Type;
        if (type?.ContainingNamespace?.ToString().StartsWith("System.Collections.Concurrent", System.StringComparison.Ordinal) == true)
            return;
        if (string.IsNullOrWhiteSpace(baseText) || !baseText.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.'))
            return;

        var mutates = f.Statement.DescendantNodes().Any(n =>
            n is InvocationExpressionSyntax inv
                && inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Expression.ToString() == baseText
                && MutatingMethods.Contains(ma.Name.Identifier.ValueText)
            || n is ElementAccessExpressionSyntax ea
                && ea.Expression.ToString() == baseText
                && ea.Parent is AssignmentExpressionSyntax assign
                && assign.Left == ea);
        if (mutates && !AnalyzerHelpers.IsExempt(f, MutateDuringEnumerationId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[0], f.ForEachKeyword.GetLocation(), baseText));
    }

    private static void CheckDisposeFactoryClient(SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax? resource = null;
        VariableDeclarationSyntax? decl = null;
        if (context.Node is LocalDeclarationStatementSyntax local && local.UsingKeyword != default)
            decl = local.Declaration;
        else if (context.Node is UsingStatementSyntax u)
        {
            decl = u.Declaration;
            resource = u.Expression;
        }
        else
            return;

        var flagged = decl?.Variables.Any(v => IsFactoryClientCreation(v.Initializer?.Value, context.SemanticModel)) == true
            || IsFactoryClientCreation(resource, context.SemanticModel);
        if (flagged && !AnalyzerHelpers.IsExempt(context.Node, DisposeFactoryClientId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[1], context.Node.GetLocation()));
    }

    private static bool IsFactoryClientCreation(ExpressionSyntax? expression, SemanticModel model)
    {
        if (expression is not InvocationExpressionSyntax inv
            || inv.Expression is not MemberAccessExpressionSyntax ma
            || ma.Name.Identifier.ValueText != "CreateClient")
            return false;
        var receiverType = model.GetTypeInfo(ma.Expression).Type?.Name;
        return receiverType?.Contains("HttpClientFactory", System.StringComparison.Ordinal) == true
            || (receiverType is null && ma.Expression.ToString().Contains("actory", System.StringComparison.Ordinal));
    }

    private static void CheckUnguardedTypeEnumeration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax inv
            || inv.Expression is not MemberAccessExpressionSyntax ma
            || ma.Name.Identifier.ValueText is not ("GetExportedTypes" or "GetTypes"))
            return;
        var inTry = inv.Ancestors().OfType<TryStatementSyntax>().Any(t => t.Block.Span.Contains(inv.Span));
        if (!inTry && !AnalyzerHelpers.IsExempt(inv, UnguardedTypeEnumerationId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[2], ma.Name.GetLocation(), ma.ToString()));
    }
}
