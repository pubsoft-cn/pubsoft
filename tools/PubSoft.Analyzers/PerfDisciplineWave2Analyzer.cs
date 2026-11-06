using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PubSoft.Analyzers;

/// <summary>
/// PUBSOFT012-029 — second wave of general perf/usage discipline. Ported rule shapes
/// from Meziantou.Analyzer / dotnet CA18xx / ZeroAlloc conventions; opt-out via
/// [BoundedCost("reason")] on member or declaring type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PerfDisciplineWave2Analyzer : DiagnosticAnalyzer
{
    public const string TryGetValueId = "PUBSOFT012";      // ContainsKey+indexer double lookup
    public const string CombineLinqId = "PUBSOFT013";      // Where().Any() → Any(pred)
    public const string AnyVsCountId = "PUBSOFT014";       // Count() > 0 → Any()
    public const string LockObjectId = "PUBSOFT015";       // lock(this/string/Type)
    public const string AsyncVoidId = "PUBSOFT016";        // async void non-event
    public const string SyncOverAsyncId = "PUBSOFT017";    // .Wait()/.Result/.GetAwaiter().GetResult()
    public const string TimeProviderId = "PUBSOFT018";     // DateTime.Now/UtcNow → TimeProvider
    public const string SharedRandomId = "PUBSOFT019";     // new Random() → Random.Shared
    public const string FrozenCollectionsId = "PUBSOFT020";// static readonly Dictionary/HashSet → Frozen
    public const string SpanOverSubstringId = "PUBSOFT021";// x.Substring(...) == y → AsSpan
    public const string CharLiteralId = "PUBSOFT022";      // string.Contains("x") → char overload
    public const string ToLowerInvariantId = "PUBSOFT023"; // ToLower()/ToUpper() → Invariant
    public const string RunContinuationsId = "PUBSOFT024"; // TaskCompletionSource without RunContinuationsAsynchronously
    public const string StaticArrayArgsId = "PUBSOFT025";  // new T[]{...} as argument → static field
    public const string StringBuilderId = "PUBSOFT026";    // string += in loop → StringBuilder
    public const string EnumToStringId = "PUBSOFT027";     // enum.ToString() allocates
    public const string ConcreteParamId = "PUBSOFT028";    // public API param concrete collections
    public const string KeysContainsId = "PUBSOFT029";     // .Keys.Contains → ContainsKey

    private static DiagnosticDescriptor Make(string id, string title, string message)
        => new DiagnosticDescriptor(id, title, message, "Performance", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor[] Rules = {
        Make(TryGetValueId, "Use TryGetValue instead of ContainsKey + indexer", "Double dictionary lookup in '{0}' — TryGetValue does it in one"),
        Make(CombineLinqId, "Combine LINQ predicate into the terminal call", "Where().{1}() in '{0}' — pass the predicate to the terminal call"),
        Make(AnyVsCountId, "Use Any() instead of Count() comparison", "Count() comparison in '{0}' — Any() short-circuits"),
        Make(LockObjectId, "lock on a private object field, never this/string/Type", "lock target in '{0}' is shared — deadlocks become possible"),
        Make(AsyncVoidId, "async void only for event handlers", "async void in '{0}' — exceptions escape the task pipeline"),
        Make(SyncOverAsyncId, "Do not block on async", "Sync-over-async in '{0}' — deadlock/pool-exhaustion path"),
        Make(TimeProviderId, "Use TimeProvider for clock reads", "Direct clock read in '{0}' — use ITimeProvider/TimeProvider so tests can substitute time"),
        Make(SharedRandomId, "new Random() per call seeds poorly", "new Random() in '{0}' — use Random.Shared or inject"),
        Make(FrozenCollectionsId, "Static lookup collections should be frozen", "static readonly {0} — FrozenDictionary/FrozenSet reads faster and blocks mutation"),
        Make(SpanOverSubstringId, "Compare via AsSpan instead of Substring", "Substring comparison in '{0}' — AsSpan avoids the allocation"),
        Make(CharLiteralId, "Use the char overload for single-character args", "'{0}' passes a single-char string — the char overload avoids the string lookup"),
        Make(ToLowerInvariantId, "Use ToLowerInvariant/ToUpperInvariant", "'{0}' uses the culture-sensitive casing — Invariant is cheaper and deterministic"),
        Make(RunContinuationsId, "TaskCompletionSource needs RunContinuationsAsynchronously", "TCS in '{0}' — continuations run inline on the completing thread without it"),
        Make(StaticArrayArgsId, "Hoist constant arrays into static readonly fields", "Array literal argument in '{0}' — allocates on every call"),
        Make(StringBuilderId, "Use StringBuilder for loop string building", "string += in loop in '{0}' — quadratic copying"),
        Make(EnumToStringId, "enum.ToString() allocates — cache the name", "enum ToString in '{0}' — cache or use nameof/FastEnum"),
        Make(ConcreteParamId, "Public params should take collection abstractions", "Parameter in '{0}' takes concrete collection — accept IReadOnlyList/IEnumerable"),
        Make(KeysContainsId, "Use ContainsKey/ContainsValue directly", "'{0}' filters via Keys/Values — call ContainsKey/ContainsValue"),
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(CheckMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(CheckObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(CheckInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(CheckBinary,
            SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression,
            SyntaxKind.GreaterThanExpression, SyntaxKind.LessThanExpression,
            SyntaxKind.GreaterThanOrEqualExpression, SyntaxKind.LessThanOrEqualExpression);
        context.RegisterSyntaxNodeAction(CheckLock, SyntaxKind.LockStatement);
        context.RegisterSyntaxNodeAction(CheckMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(CheckArgument, SyntaxKind.Argument);
        context.RegisterSyntaxNodeAction(CheckLoopAssign, SyntaxKind.ForStatement, SyntaxKind.ForEachStatement, SyntaxKind.WhileStatement);
        context.RegisterSyntaxNodeAction(CheckField, SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(CheckParameter, SyntaxKind.Parameter);
    }

    private static bool IsTaskType(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.Name is "Task" or "ValueTask" && t.ContainingNamespace?.ToString() == "System.Threading.Tasks") return true;
        return false;
    }

    private static void CheckMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(method)) return;

        // async void non-event
        if (method.Modifiers.Any(SyntaxKind.AsyncKeyword) && method.ReturnType.ToString() == "void"
            && !method.ParameterList.Parameters.Any(p => p.Type?.ToString()?.Contains("EventArgs") == true))
            if (!AnalyzerHelpers.IsExempt(method, "PUBSOFT016")) context.ReportDiagnostic(Diagnostic.Create(Rules[4], method.GetLocation(), AnalyzerHelpers.Label(method)));

        // ContainsKey + indexer on the same receiver inside the method
        var receivers = new System.Collections.Generic.HashSet<string>();
        foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "ContainsKey" && inv.ArgumentList.Arguments.Count == 1)
            {
                var receiver = ma.Expression.ToString();
                foreach (var indexer in method.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
                    if (indexer.Expression.ToString() == receiver)
                    {
                        if (!AnalyzerHelpers.IsExempt(inv, "PUBSOFT012")) context.ReportDiagnostic(Diagnostic.Create(Rules[0], inv.GetLocation(), AnalyzerHelpers.Label(inv)));
                        goto next;
                    }
            }
        next:;
        }
    }

    private static void CheckObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(creation)) return;
        var typeName = creation.Type.ToString();

        if (typeName is "Random" or "System.Random" && (creation.ArgumentList?.Arguments.Count ?? 0) == 0)
            if (!AnalyzerHelpers.IsExempt(creation, "PUBSOFT019")) context.ReportDiagnostic(Diagnostic.Create(Rules[7], creation.GetLocation(), AnalyzerHelpers.Label(creation)));

        if (typeName.StartsWith("TaskCompletionSource", System.StringComparison.Ordinal)
            && (creation.ArgumentList?.Arguments
                .Any(a => a.Expression.ToString().Contains("RunContinuationsAsynchronously")) != true))
            if (!AnalyzerHelpers.IsExempt(creation, "PUBSOFT024")) context.ReportDiagnostic(Diagnostic.Create(Rules[12], creation.GetLocation(), AnalyzerHelpers.Label(creation)));
    }

    private static void CheckInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(invocation) || invocation.Expression is not MemberAccessExpressionSyntax ma) return;
        var name = ma.Name.Identifier.Text;

        // sync-over-async: .Wait()/.Result/.GetAwaiter().GetResult() on Task
        if (name is "Wait" or "get_Result" && context.SemanticModel.GetTypeInfo(ma.Expression).Type is { } t && IsTaskType(t))
            if (!AnalyzerHelpers.IsExempt(invocation, "PUBSOFT017")) context.ReportDiagnostic(Diagnostic.Create(Rules[5], invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));
        if (name == "GetAwaiter" && context.SemanticModel.GetTypeInfo(ma.Expression).Type is { } tt && IsTaskType(tt)
            && invocation.Parent is MemberAccessExpressionSyntax { Name.Identifier.Text: "GetResult" })
            if (!AnalyzerHelpers.IsExempt(invocation, "PUBSOFT017")) context.ReportDiagnostic(Diagnostic.Create(Rules[5], invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));

        // .Where(...).Any()/Count()/First()/Last()/Single() — collapsible
        if (name is "Any" or "Count" or "First" or "Last" or "Single"
            && invocation.ArgumentList.Arguments.Count == 0
            && ma.Expression is InvocationExpressionSyntax inner
            && inner.Expression is MemberAccessExpressionSyntax innerMa && innerMa.Name.Identifier.Text == "Where")
            if (!AnalyzerHelpers.IsExempt(invocation, "PUBSOFT013")) context.ReportDiagnostic(Diagnostic.Create(Rules[1], invocation.GetLocation(),
                AnalyzerHelpers.Label(invocation), name));

        // char-overload single-char string literal
        if (name is "Contains" or "IndexOf" or "LastIndexOf" or "StartsWith" or "EndsWith" or "Replace" or "Split"
            && invocation.ArgumentList.Arguments.Count > 0
            && invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit
            && lit.Token.Value is string s && s.Length == 1)
            if (!AnalyzerHelpers.IsExempt(invocation, "PUBSOFT022")) context.ReportDiagnostic(Diagnostic.Create(Rules[10], invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));

        // ToLower()/ToUpper()
        if (name is "ToLower" or "ToUpper" && invocation.ArgumentList.Arguments.Count == 0)
            if (!AnalyzerHelpers.IsExempt(invocation, "PUBSOFT023")) context.ReportDiagnostic(Diagnostic.Create(Rules[11], invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));

        // enum.ToString()
        if (name == "ToString" && invocation.ArgumentList.Arguments.Count == 0
            && context.SemanticModel.GetTypeInfo(ma.Expression).Type?.TypeKind == TypeKind.Enum)
            if (!AnalyzerHelpers.IsExempt(invocation, "PUBSOFT027")) context.ReportDiagnostic(Diagnostic.Create(Rules[15], invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));

        // .Keys.Contains()/.Values.Contains()
        if (name == "Contains" && invocation.ArgumentList.Arguments.Count == 1
            && ma.Expression is MemberAccessExpressionSyntax parent
            && parent.Name.Identifier.Text is "Keys" or "Values")
            if (!AnalyzerHelpers.IsExempt(invocation, "PUBSOFT029")) context.ReportDiagnostic(Diagnostic.Create(Rules[17], invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));
    }

    private static void CheckBinary(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(binary)) return;

        // Count()/LongCount() compared against anything → Any()
        var sides = new[] { binary.Left, binary.Right };
        foreach (var side in sides)
            if (side is InvocationExpressionSyntax inv
                && inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text is "Count" or "LongCount")
            {
                if (!AnalyzerHelpers.IsExempt(binary, "PUBSOFT014")) context.ReportDiagnostic(Diagnostic.Create(Rules[2], binary.GetLocation(), AnalyzerHelpers.Label(binary)));
                return;
            }

        // x.Substring(...) == y
        foreach (var side in sides)
            if (side is InvocationExpressionSyntax inv
                && inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.Text == "Substring")
            {
                if (!AnalyzerHelpers.IsExempt(binary, "PUBSOFT021")) context.ReportDiagnostic(Diagnostic.Create(Rules[9], binary.GetLocation(), AnalyzerHelpers.Label(binary)));
                return;
            }
    }

    private static void CheckLock(SyntaxNodeAnalysisContext context)
    {
        var lockStmt = (LockStatementSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(lockStmt)) return;
        var operand = lockStmt.Expression;
        if (operand is ThisExpressionSyntax
            || operand is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
            || operand is TypeOfExpressionSyntax
            || operand is MemberAccessExpressionSyntax { Name.Identifier.Text: "GetType" }
            || operand is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.Text == "GetType")
            if (!AnalyzerHelpers.IsExempt(lockStmt, "PUBSOFT015")) context.ReportDiagnostic(Diagnostic.Create(Rules[3], lockStmt.GetLocation(), AnalyzerHelpers.Label(lockStmt)));
    }

    private static void CheckMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var ma = (MemberAccessExpressionSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(ma)) return;
        var name = ma.Name.Identifier.Text;
        var target = ma.Expression.ToString();

        // DateTime.Now/UtcNow/Today, DateTimeOffset.Now/UtcNow
        if ((target == "System.DateTime" || target == "DateTime") && name is "Now" or "UtcNow" or "Today"
            || (target == "System.DateTimeOffset" || target == "DateTimeOffset") && name is "Now" or "UtcNow")
            if (!AnalyzerHelpers.IsExempt(ma, "PUBSOFT018")) context.ReportDiagnostic(Diagnostic.Create(Rules[6], ma.GetLocation(), AnalyzerHelpers.Label(ma)));

        // Task.Result property access
        if (name == "Result" && context.SemanticModel.GetTypeInfo(ma.Expression).Type is { } t && IsTaskType(t))
            if (!AnalyzerHelpers.IsExempt(ma, "PUBSOFT017")) context.ReportDiagnostic(Diagnostic.Create(Rules[5], ma.GetLocation(), AnalyzerHelpers.Label(ma)));
    }

    private static void CheckArgument(SyntaxNodeAnalysisContext context)
    {
        var argument = (ArgumentSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(argument)) return;
        // only constant arrays are hoistable to static fields — runtime-value wrappers can't be
        static bool AllLiteral(ExpressionSyntax e) => e switch
        {
            ImplicitArrayCreationExpressionSyntax i => i.Initializer.Expressions.All(x => x is LiteralExpressionSyntax),
            CollectionExpressionSyntax c => c.Elements.All(x => x is ExpressionElementSyntax ee && ee.Expression is LiteralExpressionSyntax),
            ArrayCreationExpressionSyntax a => a.Initializer is not null && a.Initializer.Expressions.All(x => x is LiteralExpressionSyntax),
            _ => false,
        };
        if (argument.Expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax
            && AllLiteral(argument.Expression))
            if (!AnalyzerHelpers.IsExempt(argument, "PUBSOFT025")) context.ReportDiagnostic(Diagnostic.Create(Rules[13], argument.GetLocation(), AnalyzerHelpers.Label(argument)));
    }

    private static void CheckLoopAssign(SyntaxNodeAnalysisContext context)
    {
        // string += inside loop bodies
        foreach (var assign in context.Node.DescendantNodes().OfType<ExpressionSyntax>())
            if (assign is AssignmentExpressionSyntax ae && ae.IsKind(SyntaxKind.AddAssignmentExpression)
                && context.SemanticModel.GetTypeInfo(ae.Left).Type?.SpecialType == SpecialType.System_String
                && !AnalyzerHelpers.IsCold(context.Node))
            {
                if (!AnalyzerHelpers.IsExempt(ae, "PUBSOFT026")) context.ReportDiagnostic(Diagnostic.Create(Rules[14], ae.GetLocation(), AnalyzerHelpers.Label(ae)));
                return;
            }
    }

    private static void CheckField(SyntaxNodeAnalysisContext context)
    {
        var field = (FieldDeclarationSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(field)) return;
        var isStaticReadonly = field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);
        if (!isStaticReadonly) return;
        var typeName = field.Declaration.Type.ToString();
        if (typeName.StartsWith("Dictionary<") || typeName.StartsWith("HashSet<") || typeName.StartsWith("SortedDictionary<"))
            if (!AnalyzerHelpers.IsExempt(field, "PUBSOFT020")) context.ReportDiagnostic(Diagnostic.Create(Rules[8], field.GetLocation(), typeName));
    }

    private static void CheckParameter(SyntaxNodeAnalysisContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(parameter)) return;
        var method = parameter.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null || !method.Modifiers.Any(SyntaxKind.PublicKeyword)) return;
        var typeName = parameter.Type?.ToString() ?? "";
        if (typeName.StartsWith("List<") || typeName.StartsWith("Dictionary<") || typeName.StartsWith("HashSet<"))
            if (!AnalyzerHelpers.IsExempt(parameter, "PUBSOFT028")) context.ReportDiagnostic(Diagnostic.Create(Rules[16], parameter.GetLocation(), AnalyzerHelpers.Label(parameter)));
    }
}
