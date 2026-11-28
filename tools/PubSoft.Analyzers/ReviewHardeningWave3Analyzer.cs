using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PubSoft.Analyzers;

/// <summary>
/// Third wave of narrow, high-signal review-hardening rules. Each pattern is a defect
/// class with mechanical detection: vacuous success checks, silently dropped parse/task
/// results, and forced GC pauses. Exempt via `pubsoft:exempt &lt;id&gt; "reason"` on the source line.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReviewHardeningWave3Analyzer : DiagnosticAnalyzer
{
    /// <summary>PUBSOFT059 rule id.</summary>
    public const string GcCollectId = "PUBSOFT059";
    /// <summary>PUBSOFT060 rule id.</summary>
    public const string VacuousRowsAffectedCheckId = "PUBSOFT060";
    /// <summary>PUBSOFT061 rule id.</summary>
    public const string IgnoredTryParseId = "PUBSOFT061";
    /// <summary>PUBSOFT062 rule id.</summary>
    public const string DiscardedTaskId = "PUBSOFT062";

    private static DiagnosticDescriptor Make(string id, string title, string message)
        => new DiagnosticDescriptor(id, title, message, "Hardening", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor[] Rules =
    {
        Make(GcCollectId, "GC.Collect in library code",
            "forcing a collection pauses every thread and fights the collector's own heuristics — library code must not decide global GC policy"),
        Make(VacuousRowsAffectedCheckId, "Rows-affected result checked with a vacuous comparison",
            "'{0}' passes even when zero rows were written — a successful write check is usually '== 1' or '> 0'; '> -1'/'>= 0'/'!= -1' only proves no exception was thrown"),
        Make(IgnoredTryParseId, "TryParse result ignored",
            "'{0}' discards the success flag — parse failure silently continues with the default value; branch on the result or exempt with the fallback rationale"),
        Make(DiscardedTaskId, "Task discarded fire-and-forget",
            "discarding '{0}' leaves exceptions unobserved and completion unordered — await it, track it, or exempt with the reason the fire-and-forget path is internally safe"),
    };

    private static readonly string[] RowsAffectedMethods =
    {
        "ExecuteNonQuery", "ExecuteNonQueryAsync",
        "ExecuteUpdate", "ExecuteUpdateAsync",
        "ExecuteDelete", "ExecuteDeleteAsync",
    };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(CheckGcCollect, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(CheckRowsAffectedComparison,
            SyntaxKind.GreaterThanExpression, SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.LessThanExpression, SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.NotEqualsExpression);
        context.RegisterSyntaxNodeAction(CheckIgnoredTryParse, SyntaxKind.ExpressionStatement);
        context.RegisterSyntaxNodeAction(CheckDiscardedTask, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void CheckGcCollect(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax inv
            || inv.Expression is not MemberAccessExpressionSyntax ma
            || ma.Name.Identifier.ValueText != "Collect")
            return;
        var symbol = context.SemanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
        var isGc = symbol?.ContainingType?.Name == "GC"
            && symbol.ContainingType.ContainingNamespace?.ToString() == "System";
        var fallback = symbol is null && ma.Expression.ToString() is "GC" or "System.GC";
        if ((isGc || fallback) && !AnalyzerHelpers.IsExempt(inv, GcCollectId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[0], ma.Name.GetLocation()));
    }

    private static void CheckRowsAffectedComparison(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not BinaryExpressionSyntax binary)
            return;
        ExpressionSyntax? rowsExpression = null;
        switch (binary.OperatorToken.ValueText)
        {
            case ">" when IsNegativeOne(binary.Right):
            case ">=" when IsZero(binary.Right):
            case "!=" when IsNegativeOne(binary.Right):
                rowsExpression = binary.Left;
                break;
            case "<" when IsNegativeOne(binary.Left):
            case "<=" when IsZero(binary.Left):
            case "!=" when IsNegativeOne(binary.Left):
                rowsExpression = binary.Right;
                break;
        }
        if (rowsExpression is null || !IsRowsAffectedExpression(rowsExpression, context.SemanticModel))
            return;
        if (!AnalyzerHelpers.IsExempt(binary, VacuousRowsAffectedCheckId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[1], binary.GetLocation(), AnalyzerHelpers.Label(binary)));
    }

    private static bool IsNegativeOne(ExpressionSyntax expression)
        => expression is PrefixUnaryExpressionSyntax unary
            && unary.OperatorToken.ValueText == "-"
            && unary.Operand is LiteralExpressionSyntax literal
            && literal.Token.ValueText == "1";

    private static bool IsZero(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal && literal.Token.ValueText == "0";

    private static bool IsRowsAffectedExpression(ExpressionSyntax expression, SemanticModel model)
    {
        if (IsRowsAffectedInvocation(expression))
            return true;
        if (expression is not IdentifierNameSyntax identifier)
            return false;
        var scope = expression.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        if (scope is null)
            return false;
        return scope.DescendantNodes().OfType<VariableDeclaratorSyntax>().Any(declarator =>
            declarator.Identifier.ValueText == identifier.Identifier.ValueText
            && declarator.Initializer is not null
            && IsRowsAffectedInvocation(declarator.Initializer.Value));
    }

    private static bool IsRowsAffectedInvocation(ExpressionSyntax expression)
    {
        if (expression is not InvocationExpressionSyntax inv)
            return false;
        var name = inv.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };
        return name is not null && RowsAffectedMethods.Contains(name);
    }

    private static void CheckIgnoredTryParse(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ExpressionStatementSyntax statement
            || statement.Expression is not InvocationExpressionSyntax inv
            || inv.Expression is not MemberAccessExpressionSyntax ma
            || ma.Name.Identifier.ValueText is not ("TryParse" or "TryParseExact"))
            return;
        if (!AnalyzerHelpers.IsExempt(statement, IgnoredTryParseId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[2], statement.GetLocation(), AnalyzerHelpers.Label(ma)));
    }

    private static void CheckDiscardedTask(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AssignmentExpressionSyntax assignment
            || assignment.Left is not IdentifierNameSyntax identifier
            || identifier.Identifier.ValueText != "_")
            return;
        var type = context.SemanticModel.GetTypeInfo(assignment.Right).Type as INamedTypeSymbol;
        if (!IsTaskLike(type))
            return;
        if (!AnalyzerHelpers.IsExempt(assignment, DiscardedTaskId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[3], assignment.GetLocation(), AnalyzerHelpers.Label(assignment.Right)));
    }

    private static bool IsTaskLike(INamedTypeSymbol? type)
        => type is not null
            && type.ContainingNamespace?.ToString() == "System.Threading.Tasks"
            && type.Name is "Task" or "ValueTask";
}
