using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using AsmResolver.DotNet;

namespace CompatUnbreaker.Tool.Utilities.AsmResolver;

internal static partial class DefinitionModifiersExtensions
{
    public static bool HasIsReadOnlyAttribute(this IHasCustomAttribute hasCustomAttribute)
    {
        return hasCustomAttribute.HasCustomAttribute("System.Runtime.CompilerServices", "IsReadOnlyAttribute");
    }

    public static bool IsReadOnly(this MethodDefinition method)
    {
        return method.HasIsReadOnlyAttribute();
    }

    public static bool IsReadOnly(this PropertyDefinition property)
    {
        property = property.GetLeastOverriddenMember(property.DeclaringType);
        return property.SetMethod == null;
    }

    [SuppressMessage("Design", "MA0138:Do not use \'Async\' suffix when a method does not return an awaitable type", Justification = "It's not actually async")]
    public static bool IsAsync(this MethodDefinition method)
    {
        return method.HasCustomAttribute("System.Runtime.CompilerServices", "AsyncStateMachineAttribute");
    }

    public static bool IsVolatile(this FieldDefinition field)
    {
        return field.Signature?.FieldType.HasRequiredCustomModifier("System.Runtime.CompilerServices", "IsVolatile") == true;
    }

    public static bool IsRequired(this IMemberDefinition member)
    {
        return member is IHasCustomAttribute hasCustomAttribute &&
               hasCustomAttribute.HasCustomAttribute("System.Runtime.CompilerServices", "RequiredMemberAttribute");
    }

    [GeneratedRegex("<([a-zA-Z_0-9]*)>F([0-9A-F]{64})__", RegexOptions.Compiled | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 100)]
    private static partial Regex FileTypeOrdinalPattern { get; }

    public static bool IsFileLocal(this TypeDefinition type)
    {
        return type.Name is not null && FileTypeOrdinalPattern.IsMatch(type.Name);
    }
}
