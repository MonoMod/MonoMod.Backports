using AsmResolver.DotNet;

namespace CompatUnbreaker.Tool.Utilities.AsmResolver;

internal static partial class TypeDefinitionExtensions
{
    public static ReadOnlySpan<char> GetUnmangledName(this TypeDefinition typeDefinition)
    {
        var arity = typeDefinition.GenericParameters.Count;
        if (arity == 0) return typeDefinition.Name;
        return MetadataHelpers.UnmangleMetadataNameForArity(typeDefinition.Name, arity);
    }

    public static bool IsRecord(this TypeDefinition type)
    {
        return type.Methods.Any(m => m.Name == "<Clone>$");
    }

    public static IEnumerable<TypeDefinition> GetAllBaseTypes(this TypeDefinition type)
    {
        if (type.IsInterface)
        {
            foreach (var interfaceImplementation in type.Interfaces)
            {
                if (interfaceImplementation.Interface == null) continue;

                var @interface = interfaceImplementation.Interface.Resolve()
                                 ?? throw new InvalidOperationException($"Couldn't resolve interface '{interfaceImplementation.Interface}'");

                yield return @interface;
                foreach (var baseInterface in @interface.GetAllBaseTypes())
                    yield return baseInterface;
            }
        }
        else if (type.BaseType != null)
        {
            var baseType = type.BaseType.Resolve()
                           ?? throw new InvalidOperationException($"Couldn't resolve base type '{type.BaseType}'");

            yield return baseType;
            foreach (var parentBaseType in baseType.GetAllBaseTypes())
                yield return parentBaseType;
        }
    }

    public static IEnumerable<TypeDefinition> GetAllBaseInterfaces(this TypeDefinition type)
    {
        foreach (var interfaceImplementation in type.Interfaces)
        {
            if (interfaceImplementation.Interface == null) continue;

            var @interface = interfaceImplementation.Interface.Resolve()
                             ?? throw new InvalidOperationException($"Couldn't resolve interface '{interfaceImplementation.Interface}'");

            yield return @interface;
            foreach (var baseInterface in @interface.GetAllBaseInterfaces())
                yield return baseInterface;
        }

        foreach (var baseType in type.GetAllBaseTypes())
        {
            foreach (var baseInterface in baseType.GetAllBaseInterfaces())
                yield return baseInterface;
        }
    }

    public static IEnumerable<IMemberDefinition> GetMembers(this TypeDefinition type, bool includeNestedTypes = true)
    {
        foreach (var field in type.Fields)
            yield return field;

        foreach (var property in type.Properties)
            yield return property;

        foreach (var @event in type.Events)
            yield return @event;

        foreach (var method in type.Methods)
            yield return method;

        if (includeNestedTypes)
        {
            foreach (var nestedType in type.NestedTypes)
                yield return nestedType;
        }
    }
}
