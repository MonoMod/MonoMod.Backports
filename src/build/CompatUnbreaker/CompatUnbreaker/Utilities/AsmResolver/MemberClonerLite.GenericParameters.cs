using AsmResolver.DotNet;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal readonly partial struct MemberClonerLite
{
    private void CloneGenericParameters(IHasGenericParameters sourceProvider, IHasGenericParameters clonedProvider)
    {
        foreach (var parameter in sourceProvider.GenericParameters)
            clonedProvider.GenericParameters.Add(CloneGenericParameter(parameter));
    }

    private GenericParameter CloneGenericParameter(GenericParameter parameter)
    {
        var clonedParameter = new GenericParameter(parameter.Name, parameter.Attributes);

        foreach (var constraint in parameter.Constraints)
            clonedParameter.Constraints.Add(CloneGenericParameterConstraint(constraint));

        CloneCustomAttributes(parameter, clonedParameter);
        return clonedParameter;
    }

    private GenericParameterConstraint CloneGenericParameterConstraint(GenericParameterConstraint constraint)
    {
        var clonedConstraint = new GenericParameterConstraint(constraint.Constraint?.ImportWith(_importer));

        CloneCustomAttributes(constraint, clonedConstraint);
        return clonedConstraint;
    }
}
