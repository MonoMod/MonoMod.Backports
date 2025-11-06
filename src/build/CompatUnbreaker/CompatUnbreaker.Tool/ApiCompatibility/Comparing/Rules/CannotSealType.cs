using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public sealed class CannotSealTypeDifference(TypeMapper mapper) : TypeCompatDifference(mapper)
{
    public override string Message => Mapper.Right.IsSealed
        ? $"Type '{Mapper.Right}' has the sealed modifier on {"right"} but not on {"left"}"
        : $"Type '{Mapper.Right}' is sealed because it has no visible constructor on {"right"} but it does on {"left"}";

    public override DifferenceType Type => DifferenceType.Changed;
}

public sealed class CannotSealType : BaseRule
{
    public override void Run(TypeMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        if (left == null || right == null || left.IsInterface || right.IsInterface)
            return;

        var isLeftSealed = left.IsEffectivelySealed( /* TODO includeInternalSymbols */ false);
        var isRightSealed = right.IsEffectivelySealed( /* TODO includeInternalSymbols */ false);

        if (!isLeftSealed && isRightSealed)
        {
            differences.Add(new CannotSealTypeDifference(mapper));
        }
    }
}
