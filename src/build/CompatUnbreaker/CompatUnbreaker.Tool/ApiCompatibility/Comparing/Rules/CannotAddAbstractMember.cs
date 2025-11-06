using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public sealed class CannotAddAbstractMemberDifference(MemberMapper mapper) : MemberCompatDifference(mapper)
{
    public override string Message => $"Cannot add abstract member '{Mapper.Right}' to {"right"} because it does not exist on {"left"}";
    public override DifferenceType Type => DifferenceType.Added;
}

public sealed class CannotAddAbstractMember : BaseRule
{
    public override void Run(MemberMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        if (left == null && right != null && right.IsRoslynAbstract())
        {
            // We need to make sure left declaring type is not sealed, as unsealing a type is not a breaking change.
            // So if in this version of left and right, right is unsealing the type, abstract members can be added.
            // checking for member additions on interfaces is checked on its own rule.
            var leftDeclaringType = mapper.DeclaringType.Left;
            if (!leftDeclaringType.IsInterface && !leftDeclaringType.IsEffectivelySealed(/* TODO includeInternalSymbols */ false))
            {
                differences.Add(new CannotAddAbstractMemberDifference(mapper));
            }
        }
    }
}
