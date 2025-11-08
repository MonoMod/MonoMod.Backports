using AsmResolver.DotNet;

namespace ArApiCompat.ApiCompatibility.AssemblyMapping;

public sealed class MemberMapper(MapperSettings settings, TypeMapper declaringType) : ElementMapper<IMemberDefinition>(settings)
{
    public TypeMapper DeclaringType { get; } = declaringType;
}
