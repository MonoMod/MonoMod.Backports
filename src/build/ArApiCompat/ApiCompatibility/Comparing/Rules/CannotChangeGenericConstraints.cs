using ArApiCompat.ApiCompatibility.AssemblyMapping;
using ArApiCompat.Utilities.AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;
using System.Data;
using System.Diagnostics;

namespace ArApiCompat.ApiCompatibility.Comparing.Rules;

#pragma warning disable CS9113 // Parameter is unread.
public sealed class CannotChangeGenericConstraintDifference(DifferenceType type, IMemberDefinition left, IMemberDefinition right, GenericParameter leftTypeParameter, string constraint) : CompatDifference
{
    public override string Message => $"Cannot {(type == DifferenceType.Added ? "add" : "remove")} constraint '{constraint}' on type parameter '{leftTypeParameter}' of '{left}'";
    public override DifferenceType Type => type;
}

public sealed class CannotChangeGenericVarianceDifference(DifferenceType type, IMemberDefinition left, GenericParameter leftTypeParameter, string variance, string newVariance) : CompatDifference
{
    public override DifferenceType Type => type;

    public override string Message => $"Cannot change variance of type parameter '{leftTypeParameter}' on '{left}' from {variance} to {newVariance}";
}
#pragma warning restore CS9113 // Parameter is unread.

public sealed class CannotChangeGenericConstraints : BaseRule
{
    public override void Run(TypeMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;
        if (left == null || right == null)
            return;

        var leftTypeParameters = left.GenericParameters;
        var rightTypeParameters = right.GenericParameters;

        // can remove constraints on sealed classes since no code should observe broader set of type parameters
        var permitConstraintRemoval = left.IsSealed;

        CompareTypeParameters(leftTypeParameters, rightTypeParameters, left, right, permitConstraintRemoval, differences);
    }

    public override void Run(MemberMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;
        if (left is not MethodDefinition leftMethod || right is not MethodDefinition rightMethod)
        {
            return;
        }

        var leftTypeParameters = leftMethod.GenericParameters;
        var rightTypeParameters = rightMethod.GenericParameters;

        var permitConstraintRemoval = !leftMethod.IsVirtual;

        CompareTypeParameters(leftTypeParameters, rightTypeParameters, left, right, permitConstraintRemoval, differences);
    }

    private static void CompareTypeParameters(
        IList<GenericParameter> leftTypeParameters,
        IList<GenericParameter> rightTypeParameters,
        IMemberDefinition left,
        IMemberDefinition right,
        bool permitConstraintRemoval,
        IList<CompatDifference> differences
    )
    {
        Debug.Assert(leftTypeParameters.Count == rightTypeParameters.Count);
        for (var i = 0; i < leftTypeParameters.Count; i++)
        {
            var leftTypeParam = leftTypeParameters[i];
            var rightTypeParam = rightTypeParameters[i];

            var addedConstraints = new List<string>();
            var removedConstraints = new List<string>();

            CompareBoolConstraint(typeParam => typeParam.HasReferenceTypeConstraint, "class");
            CompareBoolConstraint(typeParam => typeParam.HasNotNullableValueTypeConstraint, "struct");
            CompareBoolConstraint(typeParam => typeParam.HasDefaultConstructorConstraint, "new()");


            // CompareBoolConstraint(typeParam => typeParam.HasNotNullConstraint, "notnull"); TODO
            // CompareBoolConstraint(typeParam => typeParam.HasUnmanagedTypeConstraint, "unmanaged"); TODO

            // unmanaged implies struct
            // CompareBoolConstraint(typeParam => typeParam.HasValueTypeConstraint & !typeParam.HasUnmanagedTypeConstraint, "struct"); TODO

            var rightOnlyConstraints = ToHashSet(rightTypeParam.Constraints);
            rightOnlyConstraints.ExceptWith(ToHashSet(leftTypeParam.Constraints));

            // we could allow an addition if removals are allowed, and the addition is a less-derived base type or interface
            // for example: changing a constraint from MemoryStream to Stream on a sealed type, or non-virtual member
            // but we'll leave this to suppressions

            addedConstraints.AddRange(rightOnlyConstraints.Where(x => x is not null).Select(x => x!.FullName));

            // additions
            foreach (var addedConstraint in addedConstraints)
            {
                differences.Add(new CannotChangeGenericConstraintDifference(DifferenceType.Added, left, right, leftTypeParam, addedConstraint));
            }

            // removals
            // we could allow a removal in the case of reducing to more-derived interfaces if those interfaces were previous constraints
            // for example if IB : IA and a type is constrained by both IA and IB, it's safe to remove IA since it's implied by IB
            // but we'll leave this to suppressions

            if (!permitConstraintRemoval)
            {
                var leftOnlyConstraints = ToHashSet(leftTypeParam.Constraints);
                leftOnlyConstraints.ExceptWith(ToHashSet(rightTypeParam.Constraints));
                removedConstraints.AddRange(leftOnlyConstraints.Where(x => x is not null).Select(x => x!.FullName));

                foreach (var removedConstraint in removedConstraints)
                {
                    differences.Add(new CannotChangeGenericConstraintDifference(DifferenceType.Removed, left, right, leftTypeParam, removedConstraint));
                }
            }

            void CompareBoolConstraint(Func<GenericParameter, bool> boolConstraint, string constraintName)
            {
                var leftBoolConstraint = boolConstraint(leftTypeParam);
                var rightBoolConstraint = boolConstraint(rightTypeParam);

                // addition
                if (!leftBoolConstraint && rightBoolConstraint)
                {
                    addedConstraints.Add(constraintName);
                }
                // removal
                else if (!permitConstraintRemoval && leftBoolConstraint && !rightBoolConstraint)
                {
                    removedConstraints.Add(constraintName);
                }
            }

            // while we're here, also check for variance. Variance can ALWAYS be ADDED, but never CHANGED.
            // In otherwords, we can make the following changes, and only them:
            //   Invariant -> Covariant
            //   Invariant -> Contravariant
            var leftVariance = leftTypeParam.Variance;
            var rightVariance = rightTypeParam.Variance;
            if (leftVariance != rightVariance && leftVariance != GenericParameterAttributes.NonVariant)
            {
                differences.Add(new CannotChangeGenericVarianceDifference(
                    rightVariance is GenericParameterAttributes.NonVariant ? DifferenceType.Removed : DifferenceType.Changed,
                    left, leftTypeParam, FormatVariance(leftVariance), FormatVariance(rightVariance)));
            }
        }
    }

    private static HashSet<ITypeDefOrRef?> ToHashSet(IList<GenericParameterConstraint> constraints)
    {
        return constraints
            .Select(c => c.Constraint)
            .Where(c => c != null)
            .ToHashSet(ExtendedSignatureComparer.VersionAgnostic!);
    }

    private static string FormatVariance(GenericParameterAttributes variance)
        => variance switch
        {
            GenericParameterAttributes.NonVariant => "invariant",
            GenericParameterAttributes.Covariant => "covariant ('out')",
            GenericParameterAttributes.Contravariant => "contravariant ('in')",
            _ => variance.ToString(),
        };
}
