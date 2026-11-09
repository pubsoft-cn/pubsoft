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
    /// <summary>PUBSOFT045 rule id.</summary>
    public const string BroadCatchSwallowId = "PUBSOFT045";
    /// <summary>PUBSOFT046 rule id.</summary>
    public const string NullForgivingId = "PUBSOFT046";
    /// <summary>PUBSOFT047 rule id.</summary>
    public const string NewRandomId = "PUBSOFT047";
    /// <summary>PUBSOFT048 rule id.</summary>
    public const string PragmaDisableId = "PUBSOFT048";
    /// <summary>PUBSOFT049 rule id.</summary>
    public const string UncheckedIndexOfId = "PUBSOFT049";
    /// <summary>PUBSOFT050 rule id.</summary>
    public const string AsyncVoidId = "PUBSOFT050";
    /// <summary>PUBSOFT051 rule id.</summary>
    public const string MutableStaticId = "PUBSOFT051";
    /// <summary>PUBSOFT052 rule id.</summary>
    public const string UnsafeCodeId = "PUBSOFT052";
    /// <summary>PUBSOFT053 rule id.</summary>
    public const string NotImplementedThrowId = "PUBSOFT053";
    /// <summary>PUBSOFT054 rule id.</summary>
    public const string PostfixReturnId = "PUBSOFT054";

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
        Make(BroadCatchSwallowId, "Broad exception swallow",
            "catch (Exception) discards the failure — log, wrap, or rethrow; if intentional, mark pubsoft:exempt {0}"),
        Make(NullForgivingId, "Null-forgiving suppression",
            "'!' / 'default!' hides a possible null dereference — prove non-null or restructure; if provable, mark pubsoft:exempt {0}"),
        Make(NewRandomId, "new Random() is not thread-safe",
            "use Random.Shared — per-instance Random can produce degenerate sequences under contention"),
        Make(PragmaDisableId, "Pragma warning suppression without reason",
            "#pragma warning disable needs an adjacent justification comment (// pubsoft:exempt or why-text)"),
        Make(UncheckedIndexOfId, "IndexOf result used without a negative check",
            "'{0}' is used to index without checking for -1 — guard the missing case"),
        Make(AsyncVoidId, "async void method",
            "async void '{0}' escapes the exception pipeline — use Task; only event handlers may be async void"),
        Make(MutableStaticId, "Mutable static state",
            "static field '{0}' is mutable shared state — prefer readonly/const or scoped state; if intentional, mark pubsoft:exempt {0}"),
        Make(UnsafeCodeId, "unsafe code block",
            "unsafe '{0}' bypasses managed safety — this library stays verifiably managed; if justified, mark pubsoft:exempt {0}"),
        Make(NotImplementedThrowId, "NotImplementedException in shipped code",
            "throw new NotImplementedException — either implement, or throw a meaningful fail-closed exception"),
        Make(PostfixReturnId, "Postfix ++/-- inside return",
            "return '{0}' yields the pre-increment value — increment first or return the plain variable"),
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
        context.RegisterSyntaxNodeAction(CheckBroadCatch, SyntaxKind.CatchClause);
        context.RegisterSyntaxNodeAction(CheckNullForgiving, SyntaxKind.SuppressNullableWarningExpression, SyntaxKind.DefaultExpression);
        context.RegisterSyntaxNodeAction(CheckNewRandom, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterSyntaxTreeAction(CheckPragmaDisable);
        context.RegisterSyntaxNodeAction(CheckUncheckedIndexOf, SyntaxKind.VariableDeclaration);
        context.RegisterSyntaxNodeAction(CheckAsyncVoid, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(CheckMutableStatic, SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(CheckUnsafe, SyntaxKind.UnsafeStatement, SyntaxKind.MethodDeclaration, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(CheckNotImplementedThrow, SyntaxKind.ThrowStatement, SyntaxKind.ThrowExpression);
        context.RegisterSyntaxNodeAction(CheckPostfixReturn, SyntaxKind.ReturnStatement);
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
    private static void CheckBroadCatch(SyntaxNodeAnalysisContext context)
    {
        // catch (Exception) / bare catch that neither rethrows nor touches the
        // exception object — generic-exception-swallow pattern from the scans.
        if (context.Node is not CatchClauseSyntax c || c.Block.Statements.Count == 0)
            return;
        var typeName = c.Declaration?.Type.ToString() ?? "Exception";
        if (typeName is not ("Exception" or "System.Exception"))
            return;
        var body = c.Block.ToString();
        var usesEx = c.Declaration?.Identifier.ValueText is { Length: > 0 } id && body.Contains(id);
        var rethrows = c.Block.DescendantNodes().OfType<ThrowStatementSyntax>().Any()
            || c.Block.DescendantNodes().OfType<ThrowExpressionSyntax>().Any();
        var logged = body.Contains("Log") || body.Contains("Trace") || body.Contains("Debug.Write");
        var hasReturn = c.Block.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any();
        if (!usesEx && !rethrows && !logged && !hasReturn && !AnalyzerHelpers.IsExempt(c, BroadCatchSwallowId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[5], c.GetLocation(), BroadCatchSwallowId));
    }

    private static void CheckNullForgiving(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.SyntaxTree.FilePath.Contains("/tests/", StringComparison.Ordinal))
            return;
        if (context.Node is PostfixUnaryExpressionSyntax postfix
            && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)
            && postfix.Operand is LiteralExpressionSyntax operand
            && (operand.IsKind(SyntaxKind.NullLiteralExpression) || operand.IsKind(SyntaxKind.DefaultLiteralExpression)))
        {
            if (!AnalyzerHelpers.IsExempt(postfix, NullForgivingId))
                context.ReportDiagnostic(Diagnostic.Create(Rules[6], postfix.GetLocation(), NullForgivingId));
        }
    }

    private static void CheckNewRandom(SyntaxNodeAnalysisContext context)
    {
        var name = context.Node is ObjectCreationExpressionSyntax o ? o.Type.ToString()
            : context.Node is ImplicitObjectCreationExpressionSyntax i ? context.SemanticModel.GetTypeInfo(i).Type?.ToString() : null;
        if (name is "System.Random" or "Random" or "global::System.Random"
            && !AnalyzerHelpers.IsExempt(context.Node, NewRandomId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[7], context.Node.GetLocation()));
    }

    private static void CheckUncheckedIndexOf(SyntaxNodeAnalysisContext context)
    {
        // var i = xs.IndexOf(y); followed by xs[i] / RemoveAt(i) / Insert(i,..) in the
        // same method without an intervening `i < 0` / `i >= 0` check.
        if (context.Node is not VariableDeclarationSyntax decl) return;
        var v = decl.Variables.FirstOrDefault();
        var init = v?.Initializer?.Value;
        if (init is not InvocationExpressionSyntax inv) return;
        var callee = inv.Expression.ToString();
        if (!callee.EndsWith(".IndexOf", StringComparison.Ordinal)) return;
        var varName = v!.Identifier.ValueText;
        var method = decl.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        if (method is null) return;
        var guarded = method.DescendantNodes().OfType<BinaryExpressionSyntax>()
            .Any(b => b.Left.ToString() == varName && b.OperatorToken.ValueText is "<" or ">=" or "=="
                || b.Right.ToString() == varName && b.OperatorToken.ValueText is ">" or "<=" or "!=" or "==");
        if (guarded) return;
        var usedAsIndex = method.DescendantNodes().OfType<ElementAccessExpressionSyntax>()
            .Any(e => e.ArgumentList.Arguments.Any(a => a.Expression.ToString() == varName))
            || method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(i => (i.Expression.ToString().EndsWith(".RemoveAt") || i.Expression.ToString().EndsWith(".Insert"))
                    && i.ArgumentList.Arguments.Any(a => a.Expression.ToString() == varName));
        if (usedAsIndex && !AnalyzerHelpers.IsExempt(decl, UncheckedIndexOfId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[9], decl.GetLocation(), varName));
    }

    private static void CheckAsyncVoid(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax m) return;
        if (!m.Modifiers.Any(SyntaxKind.AsyncKeyword)) return;
        if (m.ReturnType is not PredefinedTypeSyntax p || !p.Keyword.IsKind(SyntaxKind.VoidKeyword)) return;
        // event-handler shape (object sender, TEventArgs e) is the only legal async void
        var ps = m.ParameterList.Parameters;
        var isEventHandler = ps.Count == 2
            && ps[0].Type?.ToString() is "object" or "object?"
            && ps[1].Type?.ToString().EndsWith("EventArgs", StringComparison.Ordinal) == true;
        if (!isEventHandler && !AnalyzerHelpers.IsExempt(m, AsyncVoidId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[10], m.Identifier.GetLocation(), m.Identifier.ValueText));
    }

    private static void CheckMutableStatic(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not FieldDeclarationSyntax f) return;
        if (!f.Modifiers.Any(SyntaxKind.StaticKeyword)) return;
        if (f.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) || f.Modifiers.Any(SyntaxKind.ConstKeyword)) return;
        var name = f.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? "field";
        if (!AnalyzerHelpers.IsExempt(f, MutableStaticId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[11], f.GetLocation(), name));
    }

    private static void CheckUnsafe(SyntaxNodeAnalysisContext context)
    {
        var isUnsafe = context.Node is UnsafeStatementSyntax
            || context.Node is MemberDeclarationSyntax md && md.Modifiers.Any(SyntaxKind.UnsafeKeyword);
        if (isUnsafe && !AnalyzerHelpers.IsExempt(context.Node, UnsafeCodeId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[12], context.Node.GetLocation(), context.Node.Kind().ToString()));
    }

    private static void CheckNotImplementedThrow(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.SyntaxTree.FilePath.Contains("/tests/", StringComparison.Ordinal)) return;
        var thrown = context.Node is ThrowStatementSyntax ts ? ts.Expression
            : context.Node is ThrowExpressionSyntax te ? te.Expression : null;
        if (thrown is ObjectCreationExpressionSyntax o && o.Type.ToString().EndsWith("NotImplementedException", StringComparison.Ordinal)
            && !AnalyzerHelpers.IsExempt(context.Node, NotImplementedThrowId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[13], context.Node.GetLocation()));
    }

    private static void CheckPostfixReturn(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ReturnStatementSyntax r) return;
        if (r.Expression is PostfixUnaryExpressionSyntax post
            && (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression))
            && !AnalyzerHelpers.IsExempt(r, PostfixReturnId))
            context.ReportDiagnostic(Diagnostic.Create(Rules[14], r.GetLocation(), post.Operand.ToString()));
    }

    private static void CheckPragmaDisable(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!trivia.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia))
                continue;
            var text = trivia.ToString();
            var line = trivia.GetLocation().GetLineSpan().StartLinePosition.Line;
            var source = context.Tree.GetText(context.CancellationToken).ToString().Split('\n');
            var hasReason = line + 1 < source.Length && (source[line].Contains("pubsoft:exempt") || source[line].Contains("//"));
            if (!hasReason)
                context.ReportDiagnostic(Diagnostic.Create(Rules[8], trivia.GetLocation()));
        }
    }
}
