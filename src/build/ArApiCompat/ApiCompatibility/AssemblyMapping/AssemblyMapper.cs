using AsmResolver.DotNet;
using CompatUnbreaker.Tool.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;

public sealed class AssemblyMapper(MapperSettings settings) : ElementMapper<AssemblyDefinition>
{
    private readonly Dictionary<ITypeDescriptor, TypeMapper> _types = new(ExtendedSignatureComparer.VersionAgnostic);

    public IEnumerable<TypeMapper> Types => _types.Values;

    public override void Add(AssemblyDefinition value, ElementSide side)
    {
        base.Add(value, side);

        foreach (var module in value.Modules)
        {
            foreach (var type in module.TopLevelTypes)
            {
                if (type.Namespace is null || !type.Namespace.Value.StartsWith("System.Threading.Tasks")) continue;
                if (settings.Filter(type))
                {
                    AddOrCreateMapper(type, side);
                }
            }

            foreach (var exportedType in module.ExportedTypes)
            {
                var type = exportedType.Resolve();
                if (type == null)
                {
                    Console.WriteLine($"Failed to resolve exported type: {exportedType.FullName}");
                    return;
                }

                if (settings.Filter(type))
                {
                    AddOrCreateMapper(type, side);
                }
            }
        }
    }

    private void AddOrCreateMapper(TypeDefinition type, ElementSide side)
    {
        if (!_types.TryGetValue(type, out var mapper))
        {
            mapper = new TypeMapper(settings);
            _types.Add(type, mapper);
        }

        mapper.Add(type, side);
    }

    public static AssemblyMapper Create(AssemblyDefinition left, AssemblyDefinition right, MapperSettings? settings = null)
    {
        var result = new AssemblyMapper(settings ?? new MapperSettings());

        result.Add(left, ElementSide.Left);
        result.Add(right, ElementSide.Right);

        return result;
    }
}
