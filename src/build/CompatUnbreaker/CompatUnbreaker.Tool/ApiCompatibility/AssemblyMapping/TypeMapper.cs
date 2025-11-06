using AsmResolver.DotNet;
using CompatUnbreaker.Tool.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;

public sealed class TypeMapper(MapperSettings settings, TypeMapper? declaringType = null) : ElementMapper<TypeDefinition>
{
    private readonly Dictionary<ITypeDescriptor, TypeMapper> _nestedTypes = new(ExtendedSignatureComparer.VersionAgnostic);
    private readonly Dictionary<IMemberDescriptor, MemberMapper> _members = new(ExtendedSignatureComparer.VersionAgnostic);

    public TypeMapper? DeclaringType { get; } = declaringType;

    public IEnumerable<TypeMapper> NestedTypes => _nestedTypes.Values;
    public IEnumerable<MemberMapper> Members => _members.Values;

    public override void Add(TypeDefinition value, ElementSide side)
    {
        base.Add(value, side);

        foreach (var member in value.GetMembers(includeNestedTypes: false))
        {
            if (settings.Filter(member))
            {
                AddOrCreateMapper(member, side);
            }
        }

        foreach (var nestedType in value.NestedTypes)
        {
            if (settings.Filter(nestedType))
            {
                AddOrCreateMapper(nestedType, side);
            }
        }
    }

    private void AddOrCreateMapper(TypeDefinition nestedType, ElementSide side)
    {
        if (!_nestedTypes.TryGetValue(nestedType, out var mapper))
        {
            mapper = new TypeMapper(settings, this);
            _nestedTypes.Add(nestedType, mapper);
        }

        mapper.Add(nestedType, side);
    }

    private void AddOrCreateMapper(IMemberDefinition member, ElementSide side)
    {
        if (!_members.TryGetValue(member, out var mapper))
        {
            mapper = new MemberMapper(settings, this);
            _members.Add(member, mapper);
        }

        mapper.Add(member, side);
    }
}
