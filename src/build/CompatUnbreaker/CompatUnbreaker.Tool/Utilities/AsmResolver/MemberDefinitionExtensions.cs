using AsmResolver.DotNet;

namespace CompatUnbreaker.Tool.Utilities.AsmResolver;

internal static class MemberDefinitionExtensions
{
    public static bool IsRoslynAbstract(this IMemberDefinition member)
    {
        return member switch
        {
            TypeDefinition type => type is { IsAbstract: true, IsSealed: false },
            MethodDefinition method => method.IsAbstract,
            FieldDefinition => false,
            PropertyDefinition property => property.GetMethod is { IsAbstract: true } || property.SetMethod is { IsAbstract: true },
            EventDefinition @event => @event.AddMethod is { IsAbstract: true } || @event.RemoveMethod is { IsAbstract: true },
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }

    public static bool IsRoslynSealed(this IMemberDefinition member)
    {
        return member switch
        {
            TypeDefinition type => type is { IsSealed: true, IsAbstract: false },
            MethodDefinition method => method.IsFinal &&
                                       (method.DeclaringType is { IsInterface: true }
                                           ? method is { IsAbstract: true, IsVirtual: true, IsNewSlot: false }
                                           : !method.IsAbstract && method.IsOverride()),
            FieldDefinition => false,
            PropertyDefinition property => property.GetMethod?.IsRoslynSealed() != false && property.SetMethod?.IsRoslynSealed() != false,
            EventDefinition @event => @event.AddMethod?.IsRoslynSealed() == true || @event.RemoveMethod?.IsRoslynSealed() == true,
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }

    public static bool IsOverride(this IMemberDefinition member)
    {
        return member switch
        {
            TypeDefinition => false,
            MethodDefinition method => method.DeclaringType is not { IsInterface: true } &&
                                       method.IsVirtual &&
                                       !method.IsDestructor() &&
                                       ((!method.IsNewSlot && method.DeclaringType?.BaseType != null) || method.IsExplicitClassOverride()),
            FieldDefinition => false,
            PropertyDefinition property => property.GetMethod?.IsOverride() == true || property.SetMethod?.IsOverride() == true,
            EventDefinition @event => @event.AddMethod?.IsOverride() == true || @event.RemoveMethod?.IsOverride() == true,
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }

    public static bool IsRoslynVirtual(this IMemberDefinition member)
    {
        return member switch
        {
            TypeDefinition => false,
            MethodDefinition method => method.IsVirtual && !method.IsDestructor() && !method.IsFinal && !method.IsRoslynAbstract() &&
                                       (method.DeclaringType?.IsInterface == true
                                           ? method.IsStatic || method.IsNewSlot
                                           : !method.IsOverride()),
            FieldDefinition => false,
            PropertyDefinition property => !property.IsOverride() && !property.IsRoslynAbstract() &&
                                           (property.GetMethod?.IsRoslynVirtual() == true || property.SetMethod?.IsRoslynVirtual() == true),
            EventDefinition @event => !@event.IsOverride() && !@event.IsRoslynAbstract() &&
                                      (@event.AddMethod?.IsRoslynVirtual() == true || @event.RemoveMethod?.IsRoslynVirtual() == true),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }

    public static bool IsExtern(this IMemberDefinition member)
    {
        return member switch
        {
            TypeDefinition => false,
            MethodDefinition method => method.IsPInvokeImpl || method is { IsAbstract: false, HasMethodBody: false },
            FieldDefinition => false,
            PropertyDefinition property => property.GetMethod?.IsExtern() == true || property.SetMethod?.IsExtern() == true,
            EventDefinition @event => @event.AddMethod?.IsExtern() == true || @event.RemoveMethod?.IsExtern() == true,
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }

    public static T GetLeastOverriddenMember<T>(this T member, TypeDefinition? accessingTypeOpt)
        where T : IMemberDefinition
    {
        return member; // TODO
    }
}
