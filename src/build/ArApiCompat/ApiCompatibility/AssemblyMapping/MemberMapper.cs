using AsmResolver.DotNet;

namespace CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;

public sealed class MemberMapper(MapperSettings settings, TypeMapper declaringType) : ElementMapper<IMemberDefinition>
{
    public TypeMapper DeclaringType { get; } = declaringType;
}
