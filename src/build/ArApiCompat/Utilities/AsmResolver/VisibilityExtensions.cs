using AsmResolver.DotNet;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal static class VisibilityExtensions
{
    public static bool IsVisibleOutsideOfAssembly(
        this IMemberDefinition member,
        bool includeInternalSymbols = false,
        bool includeEffectivelyPrivateSymbols = false
    )
    {
        return member.GetAccessibility() switch
        {
            Accessibility.Public => true,
            Accessibility.Protected => includeEffectivelyPrivateSymbols || member.DeclaringType == null || !member.DeclaringType.IsEffectivelySealed(includeInternalSymbols),
            Accessibility.ProtectedOrInternal => includeEffectivelyPrivateSymbols || includeInternalSymbols || member.DeclaringType == null || !member.DeclaringType.IsEffectivelySealed(includeInternalSymbols),
            Accessibility.ProtectedAndInternal => includeInternalSymbols && (includeEffectivelyPrivateSymbols || member.DeclaringType == null || !member.DeclaringType.IsEffectivelySealed(includeInternalSymbols)),
            Accessibility.Private => false,
            Accessibility.Internal => includeInternalSymbols,
            _ => false,
        };
    }

    public static bool IsEffectivelySealed(this TypeDefinition type, bool includeInternalSymbols)
    {
        return type.IsSealed || !HasVisibleConstructor(type, includeInternalSymbols);
    }

    private static bool HasVisibleConstructor(TypeDefinition type, bool includeInternalSymbols)
    {
        foreach (var method in type.Methods)
        {
            if (method is not { IsConstructor: true, IsStatic: false })
                continue;

            if (method.IsVisibleOutsideOfAssembly(includeInternalSymbols, includeEffectivelyPrivateSymbols: true))
                return true;
        }

        return false;
    }
}
