using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal static class CorLibTypeFactoryExtensions
{
    public static TypeReference Type(this CorLibTypeFactory corLibTypeFactory)
    {
        var scope = corLibTypeFactory.CorLibScope;
        return new TypeReference(scope.ContextModule, scope, "System"u8, "Type"u8);
    }
}
