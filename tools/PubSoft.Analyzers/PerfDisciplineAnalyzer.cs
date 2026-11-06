using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PubSoft.Analyzers;

/// <summary>
/// PUBSOFT004-011 — performance/usage discipline ported into this repo's analyzer.
/// Rule shape and coverage intentionally mirror well-known open-source analyzers
/// (Meziantou.Analyzer MAxxxx family, dotnet/roslyn-analyzers CA18xx) so no external
/// package enters our dependency graph — provenance declared, implementation ours.
/// [BoundedCost("reason")] on the member or declaring type opts out.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PerfDisciplineAnalyzer : DiagnosticAnalyzer
{
    public const string StringEqualityId = "PUBSOFT004";   // ==/!= on strings → string.Equals
    public const string ParamNameId = "PUBSOFT005";        // ArgumentException without nameof()
    public const string ConfigureAwaitId = "PUBSOFT006";   // await without ConfigureAwait
    public const string FormatProviderId = "PUBSOFT007";   // Convert.To*/Parse/ToString without IFormatProvider
    public const string ComparerId = "PUBSOFT008";         // Dictionary/HashSet<string> without comparer
    public const string RegexTimeoutId = "PUBSOFT009";     // new Regex/GeneratedRegex without timeout
    public const string HashCodeId = "PUBSOFT010";         // string.GetHashCode() → StringComparer.*.GetHashCode
    public const string ConcreteReturnId = "PUBSOFT011";   // public API returning List/Dictionary/HashSet

    private static DiagnosticDescriptor Make(string id, string title, string message)
        => new DiagnosticDescriptor(id, title, message, "Performance", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StringEquality = Make(StringEqualityId,
        "Use string.Equals instead of ==/!=", "String comparison in '{0}' uses ==/!= — use string.Equals with an explicit StringComparison");
    public static readonly DiagnosticDescriptor ParamName = Make(ParamNameId,
        "ArgumentException should name the parameter", "Throw in '{0}' omits the parameter name — add nameof(param) so callers get actionable diagnostics");
    public static readonly DiagnosticDescriptor ConfigureAwait = Make(ConfigureAwaitId,
        "Library awaits must call ConfigureAwait(false)", "Await in '{0}' lacks ConfigureAwait(false) — library code must not capture the caller's context");
    public static readonly DiagnosticDescriptor FormatProvider = Make(FormatProviderId,
        "Pass an IFormatProvider explicitly", "Culture-sensitive call in '{0}' has no IFormatProvider — use the overload with CultureInfo.InvariantCulture or declare intent");
    public static readonly DiagnosticDescriptor Comparer = Make(ComparerId,
        "String-keyed collections need an explicit comparer", "String-keyed collection in '{0}' omits the comparer — StringComparer.Ordinal/OrdinalIgnoreCase makes ordering semantics explicit");
    public static readonly DiagnosticDescriptor RegexTimeout = Make(RegexTimeoutId,
        "Regex requires a match timeout", "Regex in '{0}' has no timeout — unbounded matching is a ReDoS vector; pass a matchTimeout");
    public static readonly DiagnosticDescriptor HashCode = Make(HashCodeId,
        "Use StringComparer.GetHashCode", "GetHashCode() on a string in '{0}' — use StringComparer.Ordinal.GetHashCode to pin the comparison semantics");
    public static readonly DiagnosticDescriptor ConcreteReturn = Make(ConcreteReturnId,
        "Public members should return collection abstractions", "Public member '{0}' returns a concrete collection — return IReadOnlyList/IReadOnlyDictionary and let the type own its mutations");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(StringEquality, ParamName, ConfigureAwait, FormatProvider, Comparer, RegexTimeout, HashCode, ConcreteReturn);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(CheckBinary, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        context.RegisterSyntaxNodeAction(CheckThrow, SyntaxKind.ThrowStatement, SyntaxKind.ThrowExpression);
        context.RegisterSyntaxNodeAction(CheckAwait, SyntaxKind.AwaitExpression);
        context.RegisterSyntaxNodeAction(CheckInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(CheckObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(CheckGeneratedRegex, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(CheckConcreteReturn, SyntaxKind.MethodDeclaration, SyntaxKind.PropertyDeclaration);
    }


    private static void CheckBinary(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(binary)) return;
        var left = context.SemanticModel.GetTypeInfo(binary.Left).Type;
        var right = context.SemanticModel.GetTypeInfo(binary.Right).Type;
        if (left?.SpecialType == SpecialType.System_String && right?.SpecialType == SpecialType.System_String)
            context.ReportDiagnostic(Diagnostic.Create(StringEquality, binary.GetLocation(), AnalyzerHelpers.Label(binary)));
    }

    private static void CheckThrow(SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax? expression = context.Node switch
        {
            ThrowStatementSyntax t => t.Expression,
            ThrowExpressionSyntax t => t.Expression,
            _ => null,
        };
        if (expression is not ObjectCreationExpressionSyntax creation || AnalyzerHelpers.IsCold(context.Node)) return;
        var typeName = creation.Type.ToString();
        if (!(typeName.Contains("ArgumentException") || typeName.Contains("ArgumentNullException")
            || typeName.Contains("ArgumentOutOfRangeException") || typeName.Contains("ArgumentNullOrWhiteSpace"))) return;
        if (creation.ArgumentList?.Arguments.Count > 0) return; // unnamed form only
        context.ReportDiagnostic(Diagnostic.Create(ParamName, creation.GetLocation(), AnalyzerHelpers.Label(creation)));
    }

    private static void CheckAwait(SyntaxNodeAnalysisContext context)
    {
        var awaitExpr = (AwaitExpressionSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(awaitExpr)) return;
        if (awaitExpr.Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
            .Any(i => i.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.Text == "ConfigureAwait"))
            return;
        // Task.Yield-like awaits and foreach-await are exempt patterns the rule cannot model — skip them via suppression comment.
        context.ReportDiagnostic(Diagnostic.Create(ConfigureAwait, awaitExpr.GetLocation(), AnalyzerHelpers.Label(awaitExpr)));
    }

    private static void CheckInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(invocation) || invocation.Expression is not MemberAccessExpressionSyntax ma) return;
        var name = ma.Name.Identifier.Text;

        // string.GetHashCode()
        if (name == "GetHashCode" && invocation.ArgumentList.Arguments.Count == 0
            && context.SemanticModel.GetTypeInfo(ma.Expression).Type?.SpecialType == SpecialType.System_String)
        {
            context.ReportDiagnostic(Diagnostic.Create(HashCode, invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));
            return;
        }

        // Convert.To*(x) / int.Parse(s) / ToString() without provider — check argument count heuristics
        var receiver = ma.Expression.ToString();
        if (receiver == "System.Convert" || receiver == "Convert")
        {
            if (invocation.ArgumentList.Arguments.Count == 1)
                context.ReportDiagnostic(Diagnostic.Create(FormatProvider, invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));
            return;
        }
        if ((name == "Parse" || name == "TryParse" || name == "ToString")
            && invocation.ArgumentList.Arguments.Count <= 1)
        {
            var targetType = context.SemanticModel.GetTypeInfo(ma.Expression).Type;
            if (targetType?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64
                or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_DateTime)
                context.ReportDiagnostic(Diagnostic.Create(FormatProvider, invocation.GetLocation(), AnalyzerHelpers.Label(invocation)));
        }
    }

    private static void CheckObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(creation)) return;
        var typeName = creation.Type.ToString();

        // new Dictionary<string, ...>/HashSet<string>/SortedDictionary<string, ...> without comparer
        if (typeName.StartsWith("Dictionary<string", System.StringComparison.Ordinal)
            || typeName.StartsWith("HashSet<string", System.StringComparison.Ordinal)
            || typeName.StartsWith("SortedDictionary<string", System.StringComparison.Ordinal))
        {
            var hasComparer = creation.ArgumentList?.Arguments
                .Any(a => a.Expression.ToString().Contains("StringComparer")) == true;
            if (!hasComparer)
                context.ReportDiagnostic(Diagnostic.Create(Comparer, creation.GetLocation(), AnalyzerHelpers.Label(creation)));
            return;
        }

        // new Regex(pattern) / new Regex(pattern, options) — only the 3-arg ctor with timeout is safe
        if (typeName.EndsWith("Regex", System.StringComparison.Ordinal)
            && (creation.ArgumentList?.Arguments.Count ?? 0) < 3)
            context.ReportDiagnostic(Diagnostic.Create(RegexTimeout, creation.GetLocation(), AnalyzerHelpers.Label(creation)));
    }

    private static void CheckGeneratedRegex(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (AnalyzerHelpers.IsCold(method)) return;
        foreach (var list in method.AttributeLists)
            foreach (var attribute in list.Attributes)
                if (attribute.Name.ToString().Contains("GeneratedRegex")
                    && (attribute.ArgumentList?.Arguments
                        .Any(a => a.NameEquals?.Name.Identifier.Text == "matchTimeoutMilliseconds") != true))
                    context.ReportDiagnostic(Diagnostic.Create(RegexTimeout, attribute.GetLocation(), AnalyzerHelpers.Label(method)));
    }

    private static void CheckConcreteReturn(SyntaxNodeAnalysisContext context)
    {
        TypeSyntax? returnType = context.Node switch
        {
            MethodDeclarationSyntax m when m.Modifiers.Any(SyntaxKind.PublicKeyword) => m.ReturnType,
            PropertyDeclarationSyntax p when p.Modifiers.Any(SyntaxKind.PublicKeyword) => p.Type,
            _ => null,
        };
        if (returnType is null || AnalyzerHelpers.IsCold(context.Node)) return;
        var type = context.SemanticModel.GetTypeInfo(returnType).Type as INamedTypeSymbol;
        if (type is null) return;
        var name = type.Name;
        if (name is "List" or "Dictionary" or "HashSet" or "SortedDictionary" or "LinkedList" or "Queue" or "Stack")
            context.ReportDiagnostic(Diagnostic.Create(ConcreteReturn, returnType.GetLocation(), AnalyzerHelpers.Label(context.Node)));
    }
}
