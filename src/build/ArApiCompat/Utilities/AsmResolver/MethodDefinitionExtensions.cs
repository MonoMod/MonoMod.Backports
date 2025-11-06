using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace CompatUnbreaker.Tool.Utilities.AsmResolver;

internal static class MethodDefinitionExtensions
{
    public static bool IsDestructor(this MethodDefinition method)
    {
        if (method.DeclaringType == null || method.DeclaringType.IsInterface ||
            method.IsStatic || method.Name != "Finalize")
        {
            return false;
        }

        foreach (var methodImplementation in method.DeclaringType.MethodImplementations)
        {
            if (methodImplementation.Body == method)
            {
                var declaration = methodImplementation.Declaration;
                if (declaration != null &&
                    declaration.DeclaringType?.ToTypeSignature() is CorLibTypeSignature { ElementType: ElementType.Object } &&
                    declaration.Name == "Finalize")
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsExplicitInterfaceImplementation(this MethodDefinition method)
    {
        if (method is { IsVirtual: true, IsFinal: true, DeclaringType: not null })
        {
            foreach (var implementation in method.DeclaringType.MethodImplementations)
            {
                if (implementation.Body == method)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsExplicitClassOverride(this MethodDefinition method)
    {
        if (method.DeclaringType == null)
        {
            return false;
        }

        foreach (var methodImplementation in method.DeclaringType.MethodImplementations)
        {
            if (methodImplementation.Body == method)
            {
                if (methodImplementation.Declaration?.DeclaringType?.Resolve()?.IsInterface == false)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsPropertyAccessor(this MethodDefinition method)
    {
        if (method.DeclaringType == null) return false;

        foreach (var property in method.DeclaringType.Properties)
        {
            if (property.GetMethod == method || property.SetMethod == method)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsEventAccessor(this MethodDefinition method)
    {
        if (method.DeclaringType == null) return false;

        foreach (var @event in method.DeclaringType.Events)
        {
            if (@event.AddMethod == method || @event.RemoveMethod == method)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsParams(this MethodDefinition method)
    {
        return method.Parameters.Count != 0 && method.Parameters[^1].Definition?.IsParams() == true;
    }

    public static bool IsParams(this ParameterDefinition parameter)
    {
        return parameter.HasCustomAttribute("System", "ParamArrayAttribute") ||
               parameter.HasCustomAttribute("System.Runtime.CompilerServices", "ParamCollectionAttribute");
    }
}
