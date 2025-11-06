using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing;

public sealed class ApiComparer
{
    private static readonly IEnumerable<BaseRule> s_rules =
    [
        // new AssemblyIdentityMustMatch(),
        new CannotAddAbstractMember(),
        new CannotAddMemberToInterface(),
        new CannotAddOrRemoveVirtualKeyword(),
        new CannotRemoveBaseTypeOrInterface(),
        new CannotSealType(),
        new EnumsMustMatch(),
        new MembersMustExist(),
        new CannotChangeVisibility(),
        new CannotChangeGenericConstraints(),
    ];

    private readonly List<CompatDifference> _compatDifferences = [];

    public IEnumerable<CompatDifference> CompatDifferences => _compatDifferences;

    public void Compare(AssemblyMapper assemblyMapper)
    {
        AddDifferences(assemblyMapper);

        foreach (var typeMapper in assemblyMapper.Types)
        {
            Compare(typeMapper);
        }
    }

    private void Compare(TypeMapper typeMapper)
    {
        AddDifferences(typeMapper);

        if (typeMapper.Left == null || typeMapper.Right == null) return;

        foreach (var nestedTypeMapper in typeMapper.NestedTypes)
        {
            Compare(nestedTypeMapper);
        }

        foreach (var memberMapper in typeMapper.Members)
        {
            Compare(memberMapper);
        }
    }

    private void Compare(MemberMapper memberMapper)
    {
        AddDifferences(memberMapper);
    }

    private void AddDifferences(AssemblyMapper assemblyMapper)
    {
        foreach (var rule in s_rules)
        {
            rule.Run(assemblyMapper, _compatDifferences);
        }
    }

    private void AddDifferences(TypeMapper typeMapper)
    {
        foreach (var rule in s_rules)
        {
            rule.Run(typeMapper, _compatDifferences);
        }
    }

    private void AddDifferences(MemberMapper memberMapper)
    {
        foreach (var rule in s_rules)
        {
            rule.Run(memberMapper, _compatDifferences);
        }
    }
}
