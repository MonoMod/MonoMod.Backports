using AsmResolver.DotNet;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public sealed class CannotAddMemberToInterfaceDifference(MemberMapper mapper) : MemberCompatDifference(mapper)
{
    public override string Message => $"Cannot add abstract member '{Mapper.Right}' to {"right"} because it does not exist on {"left"}";
    public override DifferenceType Type => DifferenceType.Added;
}

public sealed class CannotAddMemberToInterface : BaseRule
{
    public override void Run(MemberMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        if (left == null && right != null && right.DeclaringType.IsInterface)
        {
            // Fields in interface can only be static which is not considered a break.
            if (right is FieldDefinition)
                return;

            // Event and property accessors are covered by finding the property or event implementation
            // for interface member on the containing type.
            if (right is MethodDefinition ms && IsEventOrPropertyAccessor(ms))
                return;

            // If there is a default implementation provided is not a breaking change to add an interface member.
            if (right.DeclaringType.FindImplementationForInterfaceMember(right) != null)
                return;

            differences.Add(new CannotAddMemberToInterfaceDifference(mapper));
        }
    }

    private static bool IsEventOrPropertyAccessor(MethodDefinition symbol)
    {
        foreach (var property in symbol.DeclaringType.Properties)
        {
            if (symbol == property.GetMethod || symbol == property.SetMethod)
                return true;
        }

        foreach (var @event in symbol.DeclaringType.Events)
        {
            if (symbol == @event.AddMethod || symbol == @event.RemoveMethod)
                return true;
        }

        return false;
    }
}
