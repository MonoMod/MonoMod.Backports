using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Tool.Utilities.AsmResolver;

internal static class TypeSignatureExtensions
{
    public static bool HasCustomModifier(this TypeSignature signature, Func<CustomModifierTypeSignature, bool> predicate)
    {
        while (signature is CustomModifierTypeSignature customModifierTypeSignature)
        {
            if (predicate(customModifierTypeSignature))
            {
                return true;
            }

            signature = customModifierTypeSignature.BaseType;
        }

        return false;
    }

    public static bool HasRequiredCustomModifier(this TypeSignature signature, string? @namespace, string? name)
    {
        return signature.HasCustomModifier(m => m.IsRequired && m.ModifierType.IsTypeOf(@namespace, name));
    }

    public static ReadOnlySpan<char> GetUnmangledName(this GenericInstanceTypeSignature genericInstanceTypeSignature)
    {
        return MetadataHelpers.UnmangleMetadataNameForArity(genericInstanceTypeSignature.GenericType.Name, genericInstanceTypeSignature.TypeArguments.Count);
    }
}
