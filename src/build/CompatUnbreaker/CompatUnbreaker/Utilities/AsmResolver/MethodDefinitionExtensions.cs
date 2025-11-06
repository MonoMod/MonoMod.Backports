using System.Runtime.CompilerServices;
using AsmResolver.DotNet;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal static class MethodDefinitionExtensions
{
    public static bool IsExtension(this MethodDefinition method)
    {
        return method.GetExtensionAttribute() != null;
    }

    public static CustomAttribute? GetExtensionAttribute(this MethodDefinition method)
    {
        return method.FindCustomAttributes("System.Runtime.CompilerServices", nameof(ExtensionAttribute)).FirstOrDefault();
    }
}
