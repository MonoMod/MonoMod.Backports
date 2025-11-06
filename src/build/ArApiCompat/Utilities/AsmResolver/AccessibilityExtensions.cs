using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal static class AccessibilityExtensions
{
    public static Accessibility GetAccessibility(this IMemberDefinition member)
    {
        return member switch
        {
            TypeDefinition type => type.GetAccessibility(),
            MethodDefinition method => method.GetAccessibility(),
            FieldDefinition field => field.GetAccessibility(),
            PropertyDefinition property => property.GetAccessibility(),
            EventDefinition @event => @event.GetAccessibility(),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }

    public static Accessibility GetAccessibility(this TypeDefinition type)
    {
        return (type.Attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.NotPublic => Accessibility.Internal,
            TypeAttributes.Public => Accessibility.Public,
            TypeAttributes.NestedPublic => Accessibility.Public,
            TypeAttributes.NestedPrivate => Accessibility.Private,
            TypeAttributes.NestedFamily => Accessibility.Protected,
            TypeAttributes.NestedAssembly => Accessibility.Internal,
            TypeAttributes.NestedFamilyAndAssembly => Accessibility.ProtectedAndInternal,
            TypeAttributes.NestedFamilyOrAssembly => Accessibility.ProtectedOrInternal,
            _ => throw new Exception(),
        };
    }

    public static Accessibility GetAccessibility(this MethodDefinition method)
    {
        return (method.Attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Private => Accessibility.Private,
            MethodAttributes.FamilyAndAssembly => Accessibility.ProtectedAndInternal,
            MethodAttributes.Assembly => Accessibility.Internal,
            MethodAttributes.Family => Accessibility.Protected,
            MethodAttributes.FamilyOrAssembly => Accessibility.ProtectedOrInternal,
            MethodAttributes.Public => Accessibility.Public,
            _ => throw new Exception(),
        };
    }

    public static Accessibility GetAccessibility(this FieldDefinition field)
    {
        return (field.Attributes & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Private => Accessibility.Private,
            FieldAttributes.FamilyAndAssembly => Accessibility.ProtectedAndInternal,
            FieldAttributes.Assembly => Accessibility.Internal,
            FieldAttributes.Family => Accessibility.Protected,
            FieldAttributes.FamilyOrAssembly => Accessibility.ProtectedOrInternal,
            FieldAttributes.Public => Accessibility.Public,
            _ => throw new Exception(),
        };
    }

    public static Accessibility GetAccessibility(this PropertyDefinition property)
    {
        // if (property.IsOverride())
        // {
        //     // TODO https://github.com/dotnet/roslyn/blob/c3c7ad6a866dd0b857ad14ce683987c39d2b8fe0/src/Compilers/CSharp/Portable/Symbols/Metadata/PE/PEPropertySymbol.cs#L458-L478
        // }

        return GetAccessibilityFromAccessors(property.GetMethod, property.SetMethod);
    }

    public static Accessibility GetAccessibility(this EventDefinition @event)
    {
        return GetAccessibilityFromAccessors(@event.AddMethod, @event.RemoveMethod);
    }

    private static Accessibility GetAccessibilityFromAccessors(MethodDefinition? accessor1, MethodDefinition? accessor2)
    {
        var accessibility1 = accessor1?.GetAccessibility();
        var accessibility2 = accessor2?.GetAccessibility();

        if (accessibility1 == null)
        {
            return accessibility2 ?? Accessibility.NotApplicable;
        }

        if (accessibility2 == null)
        {
            return accessibility1.Value;
        }

        return GetAccessibilityFromAccessors(accessibility1.Value, accessibility2.Value);
    }

    private static Accessibility GetAccessibilityFromAccessors(Accessibility accessibility1, Accessibility accessibility2)
    {
        var minAccessibility = (accessibility1 > accessibility2) ? accessibility2 : accessibility1;
        var maxAccessibility = (accessibility1 > accessibility2) ? accessibility1 : accessibility2;

        return minAccessibility == Accessibility.Protected && maxAccessibility == Accessibility.Internal
            ? Accessibility.ProtectedOrInternal
            : maxAccessibility;
    }
}
