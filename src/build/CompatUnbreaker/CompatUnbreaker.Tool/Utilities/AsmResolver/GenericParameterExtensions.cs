using AsmResolver.DotNet;

namespace CompatUnbreaker.Tool.Utilities.AsmResolver;

internal static class GenericParameterExtensions
{
    public static bool HasUnmanagedTypeConstraint(this GenericParameter genericParameter)
    {
        return genericParameter.HasNotNullableValueTypeConstraint &&
               genericParameter.HasCustomAttribute("System.Runtime.CompilerServices", "IsUnmanagedAttribute") &&
               genericParameter.Constraints.Any(c => c.Constraint?.ToTypeSignature().HasRequiredCustomModifier("System.Runtime.InteropServices", "UnmanagedType") == true);
    }
}
