using AsmResolver.DotNet;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal static class MemberDefinitionExtensions
{
    public static bool IsStatic(this IMemberDefinition member)
    {
        return member switch
        {
            TypeDefinition type => type is { IsSealed: true, IsAbstract: true },
            MethodDefinition method => method.IsStatic,
            FieldDefinition field => field.IsStatic,
            PropertyDefinition property => property.GetMethod?.IsStatic != false && property.SetMethod?.IsStatic != false,
            EventDefinition @event => @event.AddMethod?.IsStatic != false && @event.RemoveMethod?.IsStatic != false,
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }
}
