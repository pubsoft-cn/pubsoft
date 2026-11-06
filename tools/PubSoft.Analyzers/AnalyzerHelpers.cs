using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PubSoft.Analyzers;

internal static class AnalyzerHelpers
{
    /// <summary>[BoundedCost("reason")] on the member or any enclosing type opts out — name-matched.</summary>
    public static bool IsCold(SyntaxNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n is not MemberDeclarationSyntax member) continue;
            foreach (var list in member.AttributeLists)
                foreach (var a in list.Attributes)
                    if (a.Name.ToString() is "BoundedCost" or "BoundedCostAttribute"
                        or "PubSoft.Core.BoundedCost" or "PubSoft.Core.BoundedCostAttribute")
                        return true;
        }
        return false;
    }

    /// <summary>pubsoft:exempt <id> "reason" line comment — checks the node's source line.</summary>
    public static bool IsExempt(SyntaxNode node, string diagnosticId)
    {
        var line = node.SyntaxTree.GetText().Lines[node.GetLocation().GetLineSpan().StartLinePosition.Line].ToString();
        return Exemption.IsExempt(line, diagnosticId);
    }

    public static string Label(SyntaxNode node)
    {
        var text = node.ToString();
        return text.Length <= 50 ? text : text.Substring(0, 50);
    }
}
