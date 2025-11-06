using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal readonly partial struct MemberClonerLite
{
    private void CloneSecurityDeclarations(IHasSecurityDeclaration sourceProvider, IHasSecurityDeclaration clonedProvider)
    {
        foreach (var declaration in sourceProvider.SecurityDeclarations)
            clonedProvider.SecurityDeclarations.Add(CloneSecurityDeclaration(declaration));
    }

    private SecurityDeclaration CloneSecurityDeclaration(SecurityDeclaration declaration)
    {
        return new(declaration.Action, ClonePermissionSet(declaration.PermissionSet));
    }

    private PermissionSetSignature? ClonePermissionSet(PermissionSetSignature? permissionSet)
    {
        if (permissionSet is null)
            return null;

        var result = new PermissionSetSignature();
        foreach (var attribute in permissionSet.Attributes)
            result.Attributes.Add(CloneSecurityAttribute(attribute));
        return result;
    }

    private SecurityAttribute CloneSecurityAttribute(SecurityAttribute attribute)
    {
        var result = new SecurityAttribute(attribute.AttributeType.ImportWith(_importer));

        foreach (var argument in attribute.NamedArguments)
        {
            var newArgument = new CustomAttributeNamedArgument(
                argument.MemberType,
                argument.MemberName,
                argument.ArgumentType.ImportWith(_importer),
                CloneCustomAttributeArgument(argument.Argument));

            result.NamedArguments.Add(newArgument);
        }

        return result;
    }
}
