using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal readonly partial struct MemberClonerLite
{
    public MethodDefinition CloneMethod(MethodDefinition method)
    {
        if (method.Name is null)
            throw new ArgumentException($"Method {method} has no name.");
        if (method.Signature is null)
            throw new ArgumentException($"Method {method} has no signature.");

        var clonedMethod = new MethodDefinition(method.Name, method.Attributes, (MethodSignature?) method.Signature?.ImportWith(_importer))
        {
            ImplAttributes = method.ImplAttributes,
        };

        clonedMethod.Parameters.PullUpdatesFromMethodSignature();

        foreach (var parameterDef in method.ParameterDefinitions)
            clonedMethod.ParameterDefinitions.Add(CloneParameterDefinition(parameterDef));

        CloneCustomAttributes(method, clonedMethod);
        CloneGenericParameters(method, clonedMethod);
        CloneSecurityDeclarations(method, clonedMethod);

        clonedMethod.ImplementationMap = CloneImplementationMap(method.ImplementationMap);

        return clonedMethod;
    }

    private ParameterDefinition CloneParameterDefinition(ParameterDefinition parameterDef)
    {
        var clonedParameterDef = new ParameterDefinition(parameterDef.Sequence, parameterDef.Name, parameterDef.Attributes);
        CloneCustomAttributes(parameterDef, clonedParameterDef);
        clonedParameterDef.Constant = CloneConstant(parameterDef.Constant);
        return clonedParameterDef;
    }
}
