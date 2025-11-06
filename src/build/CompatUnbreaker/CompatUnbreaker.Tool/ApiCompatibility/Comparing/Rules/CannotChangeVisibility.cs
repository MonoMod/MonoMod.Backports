using AsmResolver.DotNet;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;
using Microsoft.CodeAnalysis;
using Accessibility = CompatUnbreaker.Utilities.AsmResolver.Accessibility;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public sealed class CannotReduceVisibilityDifference(IMemberDefinition left, IMemberDefinition right) : CompatDifference
{
    public override string Message => $"Visibility of '{left}' reduced from '{left.GetAccessibility()}' to '{right.GetAccessibility()}'";
    public override DifferenceType Type => DifferenceType.Changed;
}

public sealed class CannotChangeVisibility : BaseRule
{
    public override void Run(TypeMapper mapper, IList<CompatDifference> differences)
    {
        Run(mapper.Left, mapper.Right, differences);
    }

    public override void Run(MemberMapper mapper, IList<CompatDifference> differences)
    {
        Run(mapper.Left, mapper.Right, differences);
    }

    private static Accessibility NormalizeInternals(Accessibility a) => a switch
    {
        Accessibility.ProtectedOrInternal => Accessibility.Protected,
        Accessibility.ProtectedAndInternal or Accessibility.Internal => Accessibility.Private,
        _ => a,
    };

    private int CompareAccessibility(Accessibility a, Accessibility b)
    {
        // if (!_settings.IncludeInternalSymbols) TODO
        {
            a = NormalizeInternals(a);
            b = NormalizeInternals(b);
        }

        if (a == b)
        {
            return 0;
        }

        return (a, b) switch
        {
            (Accessibility.Public, _) => 1,
            (_, Accessibility.Public) => -1,
            (Accessibility.ProtectedOrInternal, _) => 1,
            (_, Accessibility.ProtectedOrInternal) => -1,
            (Accessibility.Protected or Accessibility.Internal, _) => 1,
            (_, Accessibility.Protected or Accessibility.Internal) => -1,
            (Accessibility.ProtectedAndInternal, _) => 1,
            (_, Accessibility.ProtectedAndInternal) => -1,
            _ => throw new NotImplementedException(),
        };
    }

    private void Run(IMemberDefinition? left, IMemberDefinition? right, IList<CompatDifference> differences)
    {
        // The MemberMustExist rule handles missing symbols and therefore this rule only runs when left and right is not null.
        if (left is null || right is null)
        {
            return;
        }

        var leftAccess = left.GetAccessibility();
        var rightAccess = right.GetAccessibility();
        int accessComparison = CompareAccessibility(leftAccess, rightAccess);

        if (accessComparison > 0)
        {
            differences.Add(new CannotReduceVisibilityDifference(left, right));
        }
    }
}
