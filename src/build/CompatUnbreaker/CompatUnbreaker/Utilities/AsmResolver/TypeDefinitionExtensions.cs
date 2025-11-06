using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal static class TypeDefinitionExtensions
{
    public static MethodDefinition? TryGetExtensionMarkerMethod(this TypeDefinition type)
    {
        const string ExtensionMarkerMethodName = "<Extension>$";

        foreach (var method in type.Methods)
        {
            if (method.IsSpecialName && method.Name == ExtensionMarkerMethodName)
            {
                return method;
            }
        }

        return null;
    }

    public static MethodDefinition? FindCorrespondingExtensionImplementationMethod(this TypeDefinition @this, MethodDefinition method, TypeSignature extensionParameter)
    {
        foreach (var candidate in @this.DeclaringType!.Methods)
        {
            if (!candidate.IsStatic || candidate.Name != method.Name)
            {
                continue;
            }

            var signature = method.Signature!;
            var candidateSignature = candidate.Signature!;

            if (candidateSignature.GenericParameterCount != @this.GenericParameters.Count + signature.GenericParameterCount)
            {
                continue;
            }

            var additionalParameterCount = method.IsStatic ? 0 : 1;
            if (candidateSignature.ParameterTypes.Count != additionalParameterCount + signature.ParameterTypes.Count)
            {
                continue;
            }

            var typeMap = new Dictionary<TypeSignature, TypeSignature>(SignatureComparer.Default);
            {
                var index = 0;

                for (var i = 0; i < @this.GenericParameters.Count; i++)
                {
                    typeMap[new GenericParameterSignature(GenericParameterType.Type, i)] = new GenericParameterSignature(GenericParameterType.Method, index++);
                }

                for (var i = 0; i < signature.GenericParameterCount; i++)
                {
                    typeMap[new GenericParameterSignature(GenericParameterType.Method, i)] = new GenericParameterSignature(GenericParameterType.Method, index++);
                }
            }

            var typeMapVisitor = new TypeMapVisitor(typeMap);

            if (!SignatureComparer.Default.Equals(candidateSignature.ReturnType, signature.ReturnType.AcceptVisitor(typeMapVisitor)))
            {
                continue;
            }

            if (!method.IsStatic && !SignatureComparer.Default.Equals(candidateSignature.ParameterTypes[0], extensionParameter.AcceptVisitor(typeMapVisitor)))
            {
                continue;
            }

            if (!SignatureComparer.Default.Equals(
                    candidateSignature.ParameterTypes.Skip(additionalParameterCount),
                    signature.ParameterTypes.Select(s => s.AcceptVisitor(typeMapVisitor))
                ))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }
}
