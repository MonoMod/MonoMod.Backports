using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal sealed class TypeMapVisitor(
    Dictionary<TypeSignature, TypeSignature> typeMap
) : ITypeSignatureVisitor<TypeSignature>
{
    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitArrayType(ArrayTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        var result = new ArrayTypeSignature(signature.BaseType.AcceptVisitor(this));
        foreach (var dimension in signature.Dimensions)
            result.Dimensions.Add(new ArrayDimension(dimension.Size, dimension.LowerBound));
        return result;
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitBoxedType(BoxedTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        return new BoxedTypeSignature(signature.BaseType.AcceptVisitor(this));
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitByReferenceType(ByReferenceTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        return new ByReferenceTypeSignature(signature.BaseType.AcceptVisitor(this));
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitCorLibType(CorLibTypeSignature signature)
    {
        return typeMap.GetValueOrDefault(signature, signature);
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitCustomModifierType(CustomModifierTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        return new CustomModifierTypeSignature(VisitType(signature.ModifierType), signature.IsRequired, signature.BaseType.AcceptVisitor(this));
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitGenericInstanceType(GenericInstanceTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        var result = new GenericInstanceTypeSignature(VisitType(signature.GenericType), signature.IsValueType);
        foreach (var argument in signature.TypeArguments)
            result.TypeArguments.Add(argument.AcceptVisitor(this));
        return result;
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitGenericParameter(GenericParameterSignature signature)
    {
        return typeMap.GetValueOrDefault(signature, signature);
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitPinnedType(PinnedTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        return new PinnedTypeSignature(signature.BaseType.AcceptVisitor(this));
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitPointerType(PointerTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        return new PointerTypeSignature(signature.BaseType.AcceptVisitor(this));
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitSentinelType(SentinelTypeSignature signature)
    {
        return typeMap.GetValueOrDefault(signature, signature);
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitSzArrayType(SzArrayTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        return new SzArrayTypeSignature(signature.BaseType.AcceptVisitor(this));
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitTypeDefOrRef(TypeDefOrRefSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        return new TypeDefOrRefSignature(VisitType(signature.Type), signature.IsValueType);
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitFunctionPointerType(FunctionPointerTypeSignature signature)
    {
        if (typeMap.TryGetValue(signature, out var mapped)) return mapped;

        return new FunctionPointerTypeSignature(VisitMethodSignature(signature.Signature));
    }

    private ITypeDefOrRef VisitType(ITypeDefOrRef type)
    {
        if (type is TypeSpecification { Signature: { } signature })
        {
            return new TypeSpecification(signature.AcceptVisitor(this));
        }

        return type;
    }

    private MethodSignature VisitMethodSignature(MethodSignature signature)
    {
        var parameterTypes = new TypeSignature[signature.ParameterTypes.Count];
        for (var i = 0; i < parameterTypes.Length; i++)
            parameterTypes[i] = signature.ParameterTypes[i].AcceptVisitor(this);

        var result = new MethodSignature(signature.Attributes, signature.ReturnType.AcceptVisitor(this), parameterTypes)
        {
            GenericParameterCount = signature.GenericParameterCount,
        };

        for (var i = 0; i < signature.SentinelParameterTypes.Count; i++)
            result.SentinelParameterTypes.Add(signature.SentinelParameterTypes[i].AcceptVisitor(this));

        return result;
    }
}
