using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PubSoft.Analyzers;

/// <summary>
/// Rules extracted from the external code-review sweep — recurring defect patterns that
/// static analysis can catch mechanically instead of relying on human review every release.
/// Exempt via `pubsoft:exempt &lt;id&gt; "reason"` on the source line.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReviewHardeningAnalyzer : DiagnosticAnalyzer
{
    /// <summary>PUBSOFT040 rule id.</summary>
    public const string SystemTimeProviderId = "PUBSOFT040";
    /// <summary>PUBSOFT041 rule id.</summary>
    public const string EmptyCatchId = "PUBSOFT041";
    /// <summary>PUBSOFT042 rule id.</summary>
    public const string BlockingWaitId = "PUBSOFT042";
    /// <summary>PUBSOFT043 rule id.</summary>
    public const string UnguardedEntityCtorId = "PUBSOFT043";
    /// <summary>PUBSOFT044 rule id.</summary>
    public const string DuplicateSummaryId = "PUBSOFT044";

    private static DiagnosticDescriptor Make(string id, string title, string message)
        => new DiagnosticDescriptor(id, title, message, "Hardening", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor[] Rules =
    {
        Make(SystemTimeProviderId, "Inject TimeProvider instead of TimeProvider.System",
            "'{0}' binds to TimeProvider.System — take TimeProvider as a dependency so the clock is testable"),
        Make(EmptyCatchId, "Empty catch swallows failures",
            "empty catch block — log or rethrow; if swallowing is intentional, mark pubsoft:exempt {0}"),
        Make(BlockingWaitId, "Blocking wait on Task",
            "'{0}' blocks on an async operation — use await; sync-over-async deadlocks under synchronization contexts"),
        Make(UnguardedEntityCtorId, "Entity ctor must guard the id parameter",
            "ctor '{0}' assigns an entity id without a positive-id guard — validate id > 0 like sibling entities"),
        Make(DuplicateSummaryId, "Duplicate /// <summary> element",
            "two consecutive <summary> elements — merge into one doc comment"),
    };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(CheckClockAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(CheckEmptyCatch, SyntaxKind.CatchClause);
        context.RegisterSyntaxNodeAction(CheckBlockingWait, SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(CheckEntityCtor, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxTreeAction(CheckDuplicateSummary);
    }

    private static void CheckClockAccess(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax m)
            return;
        var name = m.Name.Identifier.ValueText;
        var target = m.Expression.ToString();
        if (name == "System" && target is "System.TimeProvider" or "TimeProvider")
        {
            // The `?? TimeProvider.System` / `??=` fallback inside an injected-TimeProvider
            // constructor is the sanctioned default — only flag reads that bypass injection
            // entirely (TimeProvider.System.GetUtcNow() / GetTimestamp() / GetLocalNow()).
            var isClockRead = m.Parent is MemberAccessExpressionSyntax parentAccess
                && parentAccess.Expression == m
                && parentAccess.Name.Identifier.ValueText.StartsWith("Get", StringComparison.Ordinal);
            if (isClockRead && !AnalyzerHelpers.IsExempt(m, SystemTimeProviderId))
                context.ReportDiagnostic(Diagnostic.Create(Rules[0], m.GetLocation(), m.ToString()));
        }
    }

    private static void CheckEmptyCatch(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not CatchClauseSyntax c)
            return;
        var statements = c.Block.Statements;
        if (statements.Count == 0 && !AnalyzerHelpers.IsExempt(c, EmptyCatchId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[1], c.GetLocation(), EmptyCatchId));
    }

    private static void CheckBlockingWait(SyntaxNodeAnalysisContext context)
    {
        // Task/ValueTask receivers only — result records (x.Result) and SemaphoreSlim.Wait(0)
        // probes are legitimate and must not be flagged.
        if (context.Node is MemberAccessExpressionSyntax m)
        {
            if (m.Name.Identifier.ValueText == "Result" && m.Parent is not InvocationExpressionSyntax
                && IsTaskLike(context.SemanticModel.GetTypeInfo(m.Expression).Type)
                && !AnalyzerHelpers.IsExempt(m, BlockingWaitId))
                context.ReportDiagnostic(Diagnostic.Create(Rules[2], m.GetLocation(), m.ToString()));
            return;
        }
        if (context.Node is not InvocationExpressionSyntax inv)
            return;
        if (inv.Expression is not MemberAccessExpressionSyntax waitAccess)
            return;
        var methodName = waitAccess.Name.Identifier.ValueText;
        var isWait = methodName == "Wait"
            || (methodName == "GetResult" && waitAccess.Expression.ToString().EndsWith("GetAwaiter()", StringComparison.Ordinal));
        if (isWait && IsTaskLike(context.SemanticModel.GetTypeInfo(waitAccess.Expression).Type)
            && !AnalyzerHelpers.IsExempt(inv, BlockingWaitId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[2], inv.GetLocation(), waitAccess.ToString()));
    }

    private static void CheckEntityCtor(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ConstructorDeclarationSyntax ctor
            || ctor.Parent is not ClassDeclarationSyntax cls
            || cls.BaseList is null)
            return;
        var bases = cls.BaseList.Types.Select(t => t.ToString());
        if (!bases.Any(b => b.StartsWith("Entity<") || b.Contains("AuditedEntity<") || b.Contains("AggregateRoot<") || b.Contains("AuditedAggregateRoot")))
            return;
        var idParam = ctor.ParameterList.Parameters.FirstOrDefault(p => p.Identifier.ValueText == "id");
        if (idParam is null)
            return;
        var body = ctor.Body?.ToString() ?? ctor.ExpressionBody?.ToString() ?? "";
        var guarded = body.Contains("<= 0") || body.Contains("ThrowIf") || body.Contains("Guard.") || body.Contains("ArgumentOutOfRangeException");
        if (!guarded && !AnalyzerHelpers.IsExempt(ctor, UnguardedEntityCtorId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[3], ctor.Identifier.GetLocation(), ctor.Identifier.ValueText));
    }

    private static bool IsTaskLike(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.Name is "Task" or "ValueTask" or "ConfiguredTaskAwaitable" or "ConfiguredValueTaskAwaitable")
                return true;
        return false;
    }

    private static void CheckDuplicateSummary(SyntaxTreeAnalysisContext context)
    {
        var text = context.Tree.GetText().ToString();
        var idx = 0;
        while (true)
        {
            var first = text.IndexOf("/// <summary>", idx, System.StringComparison.Ordinal);
            if (first < 0) return;
            var close = text.IndexOf("</summary>", first + 12, System.StringComparison.Ordinal);
            if (close < 0) return;
            var after = text.IndexOf("///", close + 10, System.StringComparison.Ordinal);
            if (after < 0) return;
            // Duplicate = the very next /// line after a closed summary opens ANOTHER summary —
            // anything between the closer and that /// must be only whitespace.
            var gap = text.Substring(close + 10, after - close - 10);
            var isAdjacentDocLine = gap.All(c => c is ' ' or '\t' or '\r' or '\n');
            var nextTag = text.IndexOf("<summary>", after, System.StringComparison.Ordinal);
            if (isAdjacentDocLine && nextTag > 0 && nextTag - after <= 4 && text.Substring(after, nextTag - after).Trim() == "///")
            {
                context.ReportDiagnostic(Diagnostic.Create(Rules[4],
                    Location.Create(context.Tree, new Microsoft.CodeAnalysis.Text.TextSpan(after, 12))));
                return;
            }
            idx = after;
        }
    }
}
