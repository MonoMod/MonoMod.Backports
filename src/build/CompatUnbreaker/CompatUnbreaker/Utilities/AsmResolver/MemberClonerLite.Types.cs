using AsmResolver.DotNet;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal partial struct MemberClonerLite
{
    public TypeDefinition CloneType(TypeDefinition type)
    {
        var clonedType = new TypeDefinition(type.Namespace, type.Name, type.Attributes, type.BaseType?.ImportWith(_importer));

        // Copy interface implementations.
        foreach (var implementation in type.Interfaces)
            clonedType.Interfaces.Add(CloneInterfaceImplementation(implementation));

        // Copy method implementations.
        foreach (var implementation in type.MethodImplementations)
        {
            clonedType.MethodImplementations.Add(new MethodImplementation(
                implementation.Declaration?.ImportWith(_importer),
                implementation.Body?.ImportWith(_importer)
            ));
        }

        // Clone class layout.
        if (type.ClassLayout is { } layout)
            clonedType.ClassLayout = new ClassLayout(layout.PackingSize, layout.ClassSize);

        // Clone remaining metadata.
        CloneCustomAttributes(type, clonedType);
        CloneGenericParameters(type, clonedType);
        CloneSecurityDeclarations(type, clonedType);

        return clonedType;
    }

    private InterfaceImplementation CloneInterfaceImplementation(InterfaceImplementation implementation)
    {
        var clonedImplementation = new InterfaceImplementation(implementation.Interface?.ImportWith(_importer));
        CloneCustomAttributes(implementation, clonedImplementation);
        return clonedImplementation;
    }
}
