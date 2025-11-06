using AsmResolver.DotNet;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public sealed class TypeMustExistDifference(TypeMapper mapper) : TypeCompatDifference(mapper)
{
    public override string Message => $"Type '{Mapper.Left}' exists on {"left"} but not on {"right"}";
    public override DifferenceType Type => DifferenceType.Removed;
}

public sealed class MemberMustExistDifference(MemberMapper mapper) : MemberCompatDifference(mapper)
{
    public override string Message => $"Member '{Mapper.Left}' exists on {"left"} but not on {"right"}";
    public override DifferenceType Type => DifferenceType.Removed;
}

public sealed class MembersMustExist : BaseRule
{
    public override void Run(TypeMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        if (left != null && right == null)
        {
            differences.Add(new TypeMustExistDifference(mapper));
        }
    }

    public override void Run(MemberMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        if (left != null && right == null)
        {
            if (ShouldReportMissingMember(left, mapper.DeclaringType.Right))
            {
                differences.Add(new MemberMustExistDifference(mapper));
            }
        }
    }

    private static bool ShouldReportMissingMember(IMemberDefinition member, TypeDefinition? declaringType)
    {
        // TODO make this an option I guess
        // // Events and properties are handled via their accessors.
        // if (member is PropertyDefinition or EventDefinition)
        //     return false;

        if (member is MethodDefinition method)
        {
            // Will be handled by a different rule
            if (method.IsExplicitInterfaceImplementation())
                return false;

            // If method is an override or is promoted to the base type should not be reported.
            if (method.IsOverride() || FindMatchingOnBaseType(method, declaringType))
                return false;
        }

        return true;
    }

    private static bool FindMatchingOnBaseType(MethodDefinition method, TypeDefinition? declaringType)
    {
        // Constructors cannot be promoted
        if (method.IsConstructor)
            return false;

        if (declaringType != null)
        {
            foreach (var type in declaringType.GetAllBaseTypes())
            {
                foreach (var candidate in type.Methods)
                {
                    if (IsMatchingMethod(method, candidate))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsMatchingMethod(MethodDefinition method, MethodDefinition candidate)
    {
        return method.Name == candidate.Name &&
               ExtendedSignatureComparer.VersionAgnostic.Equals(method.Signature, candidate.Signature);
    }
}
