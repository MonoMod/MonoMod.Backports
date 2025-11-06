using ArApiCompat.Utilities.AsmResolver;
using AsmResolver.DotNet;

namespace ArApiCompat.ApiCompatibility.AssemblyMapping;

public sealed class MapperSettings
{
    public Func<IMemberDefinition, bool> Filter { get; set; } = DefaultFilter;

    private static bool DefaultFilter(IMemberDefinition member)
    {
        return member.IsVisibleOutsideOfAssembly();
    }
}
