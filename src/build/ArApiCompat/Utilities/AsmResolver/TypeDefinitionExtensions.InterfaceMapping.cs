using AsmResolver.DotNet;

namespace CompatUnbreaker.Tool.Utilities.AsmResolver;

internal static partial class TypeDefinitionExtensions
{
    public static IMemberDefinition? FindImplementationForInterfaceMember(this TypeDefinition type, IMemberDefinition interfaceMember)
    {
        if (!interfaceMember.IsImplementableInterfaceMember())
        {
            return null;
        }

        if (type.IsInterface)
        {
            // TODO
        }

        return type.FindImplementationForInterfaceMemberInNonInterface(interfaceMember);
    }

    private static IMemberDefinition? FindImplementationForInterfaceMemberInNonInterface(this TypeDefinition type, IMemberDefinition interfaceMember)
    {
        var interfaceType = interfaceMember.DeclaringType;
        if (interfaceType == null || !interfaceType.IsInterface)
        {
            return null;
        }

        if (interfaceMember is not (MethodDefinition or PropertyDefinition or EventDefinition))
        {
            return null;
        }

        return null; // TODO
    }

    private static bool IsImplementableInterfaceMember(this IMemberDefinition member)
    {
        return !member.IsRoslynSealed() && (member.IsRoslynAbstract() || member.IsRoslynVirtual()) && (member.DeclaringType?.IsInterface ?? false);
    }
}
