using AsmResolver.DotNet;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;

public sealed class MapperSettings
{
    public Func<IMemberDefinition, bool> Filter { get; set; } = DefaultFilter;

    private static bool DefaultFilter(IMemberDefinition member)
    {
        return member.IsVisibleOutsideOfAssembly() &&
               (member is not TypeDefinition type || type.Namespace != "System.Runtime.CompilerServices");
    }
}
