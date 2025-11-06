using AsmResolver.DotNet;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public sealed class CannotRemoveBaseTypeDifference(TypeMapper mapper) : TypeCompatDifference(mapper)
{
    public override string Message => $"Type '{Mapper.Left}' does not inherit from base type '{Mapper.Left.BaseType}' on {"right"} but it does on {"left"}";

    public override DifferenceType Type => DifferenceType.Changed;
}

public sealed class CannotRemoveBaseInterfaceDifference(TypeMapper mapper, TypeDefinition leftInterface) : TypeCompatDifference(mapper)
{
    public override string Message => $"Type '{Mapper.Left}' does not implement interface '{leftInterface}' on {"right"} but it does on {"left"}";

    public override DifferenceType Type => DifferenceType.Changed;
}

public sealed class CannotRemoveBaseTypeOrInterface : BaseRule
{
    public override void Run(TypeMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        if (left == null || right == null)
            return;

        if (!left.IsInterface && !right.IsInterface)
        {
            // if left and right are not interfaces check base types
            ValidateBaseTypeNotRemoved(mapper, differences);
        }

        ValidateInterfaceNotRemoved(mapper, differences);
    }

    private void ValidateBaseTypeNotRemoved(TypeMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        if (left == null || right == null)
            return;

        var leftBaseType = left.BaseType;
        var rightBaseType = right.BaseType;

        if (leftBaseType == null)
            return;

        while (rightBaseType != null)
        {
            // If we found the immediate left base type on right we can assume
            // that any removal of a base type up on the hierarchy will be handled
            // when validating the type which it's base type was actually removed.
            if (ExtendedSignatureComparer.VersionAgnostic.Equals(leftBaseType, rightBaseType))
                return;

            rightBaseType = rightBaseType.Resolve().BaseType;
        }

        differences.Add(new CannotRemoveBaseTypeDifference(mapper));
    }

    private void ValidateInterfaceNotRemoved(TypeMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        if (left == null || right == null)
            return;

        var rightInterfaces = new HashSet<TypeDefinition>(right.GetAllBaseInterfaces(), ExtendedSignatureComparer.VersionAgnostic);

        foreach (var leftInterface in left.GetAllBaseInterfaces())
        {
            // Ignore non visible interfaces based on the run Settings
            // If TypeKind == Error it means the Roslyn couldn't resolve it,
            // so we are running with a missing assembly reference to where that type ef is defined.
            // However we still want to consider it as Roslyn does resolve it's name correctly.
            if (!leftInterface.IsVisibleOutsideOfAssembly( /* TODO IncludeInternalSymbols */ false))
                return;

            if (!rightInterfaces.Contains(leftInterface))
            {
                differences.Add(new CannotRemoveBaseInterfaceDifference(mapper, leftInterface));
                return;
            }
        }
    }
}
