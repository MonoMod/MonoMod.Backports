using AsmResolver.DotNet;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public sealed class CannotAddSealedToInterfaceMemberDifference(MemberMapper mapper) : MemberCompatDifference(mapper)
{
    public override string Message => $"Cannot add sealed keyword to default interface member '{Mapper.Right}'";
    public override DifferenceType Type => DifferenceType.Added;
}

public sealed class CannotRemoveVirtualFromMemberDifference(MemberMapper mapper) : MemberCompatDifference(mapper)
{
    public override string Message => $"Cannot remove virtual keyword from member '{Mapper.Right}'";
    public override DifferenceType Type => DifferenceType.Removed;
}

public sealed class CannotAddOrRemoveVirtualKeyword : BaseRule
{
    private static bool IsSealed(IMemberDefinition member) => member.IsRoslynSealed() || (!member.IsRoslynVirtual() && !member.IsRoslynAbstract());

    public override void Run(MemberMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        // Members must exist
        if (left is null || right is null)
        {
            return;
        }

        if (left.DeclaringType.IsInterface || right.DeclaringType.IsInterface)
        {
            if (!IsSealed(left) && IsSealed(right))
            {
                // Introducing the sealed keyword to an interface method is a breaking change.
                differences.Add(new CannotAddSealedToInterfaceMemberDifference(mapper));
            }

            return;
        }

        if (left.IsRoslynVirtual())
        {
            // Removing the virtual keyword from a member in a sealed type won't be a breaking change.
            if (left.DeclaringType.IsEffectivelySealed( /* TODO includeInternalSymbols */ false))
            {
                return;
            }

            // If left is virtual and right is not, then emit a diagnostic
            // specifying that the virtual modifier cannot be removed.
            if (!right.IsRoslynVirtual())
            {
                differences.Add(new CannotRemoveVirtualFromMemberDifference(mapper));
            }
        }
    }
}
