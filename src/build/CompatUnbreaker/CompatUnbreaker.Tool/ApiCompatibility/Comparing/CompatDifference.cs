using AsmResolver.DotNet;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing;

public abstract class CompatDifference
{
    public abstract string Message { get; }
    public abstract DifferenceType Type { get; }

    public override string ToString()
    {
        return $"{RemoveSuffix(GetType().Name, "Difference")} : {Message}";

        static string RemoveSuffix(string str, string suffix)
        {
            return str.EndsWith(suffix, StringComparison.Ordinal) ? str[..^suffix.Length] : str;
        }
    }
}

public abstract class CompatDifference<TMapper, TElement>(
    TMapper mapper
) : CompatDifference
    where TMapper : ElementMapper<TElement>
{
    public TMapper Mapper { get; } = mapper;
}

public abstract class TypeCompatDifference(TypeMapper mapper) : CompatDifference<TypeMapper, TypeDefinition>(mapper);

public abstract class MemberCompatDifference(MemberMapper mapper) : CompatDifference<MemberMapper, IMemberDefinition>(mapper);
