using AsmResolver.DotNet;
using AsmResolver.DotNet.Cloning;
using AsmResolver.DotNet.Signatures;
using System.Diagnostics.CodeAnalysis;

namespace Postprocess;

internal sealed class ClonedReferenceRewriter(MemberCloneResult cloneResult) : ITypeSignatureVisitor<TypeSignature>
{
    public void RewriteReferences(ModuleDefinition module)
    {
        foreach (var type in module.GetAllTypes())
        {
            type.BaseType = Rewrite(type.BaseType);
            foreach (var field in type.Fields)
            {
                if (field.Signature is { } sig)
                {
                    sig.FieldType = sig.FieldType.AcceptVisitor(this);
                }

                foreach (var ca in field.CustomAttributes)
                {
                    Rewrite(ca);
                }
            }
            foreach (var method in type.Methods)
            {
                if (method.Signature is { } sig)
                {
                    Rewrite(sig);
                }

                foreach (var ca in method.CustomAttributes)
                {
                    Rewrite(ca);
                }

                foreach (var gparam in method.GenericParameters)
                {
                    foreach (var ca in gparam.CustomAttributes)
                    {
                        Rewrite(ca);
                    }

                    foreach (var constraint in gparam.Constraints)
                    {
                        constraint.Constraint = Rewrite(constraint.Constraint);
                    }
                }

                // TODO rewrite body
                if (method.CilMethodBody is { } body)
                {
                    foreach (var ins in body.Instructions)
                    {
                        if (ins.Operand is IMemberDescriptor desc)
                        {
                            ins.Operand = Rewrite(desc);
                        }
                    }

                    foreach (var loc in body.LocalVariables)
                    {
                        loc.VariableType = loc.VariableType.AcceptVisitor(this);
                    }

                    foreach (var exh in body.ExceptionHandlers)
                    {
                        exh.ExceptionType = Rewrite(exh.ExceptionType);
                    }

                }
            }
            foreach (var gparam in type.GenericParameters)
            {
                foreach (var ca in gparam.CustomAttributes)
                {
                    Rewrite(ca);
                }

                foreach (var constraint in gparam.Constraints)
                {
                    constraint.Constraint = Rewrite(constraint.Constraint);
                }
            }
            foreach (var evt in type.Events)
            {
                foreach (var ca in evt.CustomAttributes)
                {
                    Rewrite(ca);
                }

            }
            foreach (var prop in type.Properties)
            {
                foreach (var ca in prop.CustomAttributes)
                {
                    Rewrite(ca);
                }

            }
            foreach (var ca in type.CustomAttributes)
            {
                Rewrite(ca);
            }
            for (var i = 0; i < type.MethodImplementations.Count; i++)
            {
                var impl = type.MethodImplementations[i];
                type.MethodImplementations[i] = new(Rewrite(impl.Declaration), Rewrite(impl.Body));
            }
        }
    }

    [return: NotNullIfNotNull(nameof(reference))]
    private ITypeDefOrRef? Rewrite(ITypeDefOrRef? reference)
    {
        if (reference is null) return null;
        var resolved = reference.Resolve();
        if (resolved is not null && cloneResult.ContainsClonedMember(resolved))
        {
            return cloneResult.GetClonedMember(resolved);
        }
        return reference;
    }
    [return: NotNullIfNotNull(nameof(reference))]
    private IMethodDefOrRef? Rewrite(IMethodDefOrRef? reference)
    {
        if (reference is null) return null;
        var resolved = reference.Resolve();
        if (resolved is not null && cloneResult.ContainsClonedMember(resolved))
        {
            return cloneResult.GetClonedMember(resolved);
        }
        return reference;
    }
    [return: NotNullIfNotNull(nameof(reference))]
    private IMemberDescriptor? Rewrite(IMemberDescriptor? reference)
    {
        if (reference is null) return null;
        var resolved = reference.Resolve();
        if (resolved is not null && cloneResult.ContainsClonedMember(resolved))
        {
            return cloneResult.GetClonedMember(resolved);
        }
        return reference;
    }

    private void Rewrite(CustomAttribute ca)
    {
        ca.Constructor = (ICustomAttributeType?)Rewrite(ca.Constructor);

        var sig = ca.Signature;
        if (sig is null) return;
        for (var i = 0; i < sig.FixedArguments.Count; i++)
        {
            sig.FixedArguments[i].ArgumentType = sig.FixedArguments[i].ArgumentType.AcceptVisitor(this);
        }
        for (var i = 0; i < sig.NamedArguments.Count; i++)
        {
            sig.NamedArguments[i].ArgumentType = sig.NamedArguments[i].ArgumentType.AcceptVisitor(this);
            sig.NamedArguments[i].Argument.ArgumentType = sig.NamedArguments[i].Argument.ArgumentType.AcceptVisitor(this);
        }
    }

    private MethodSignature Rewrite(MethodSignature sig)
    {
        sig.ReturnType = sig.ReturnType.AcceptVisitor(this);
        for (var i = 0; i < sig.ParameterTypes.Count; i++)
        {
            sig.ParameterTypes[i] = sig.ParameterTypes[i].AcceptVisitor(this);
        }
        for (var i = 0; i < sig.SentinelParameterTypes.Count; i++)
        {
            sig.SentinelParameterTypes[i] = sig.SentinelParameterTypes[i].AcceptVisitor(this);
        }
        return sig;
    }

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitArrayType(ArrayTypeSignature signature)
        => new ArrayTypeSignature(signature.BaseType.AcceptVisitor(this), signature.Dimensions.ToArray());

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitBoxedType(BoxedTypeSignature signature)
        => new BoxedTypeSignature(signature.BaseType.AcceptVisitor(this));

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitByReferenceType(ByReferenceTypeSignature signature)
        => new ByReferenceTypeSignature(signature.BaseType.AcceptVisitor(this));

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitCorLibType(CorLibTypeSignature signature)
        => signature;

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitCustomModifierType(CustomModifierTypeSignature signature)
        => new CustomModifierTypeSignature(
            Rewrite(signature.ModifierType), signature.IsRequired, signature.BaseType.AcceptVisitor(this));

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitFunctionPointerType(FunctionPointerTypeSignature signature)
        => new FunctionPointerTypeSignature(Rewrite(signature.Signature));

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitGenericInstanceType(GenericInstanceTypeSignature signature)
        => new GenericInstanceTypeSignature(Rewrite(signature.GenericType), signature.IsValueType,
            signature.TypeArguments.Select(t => t.AcceptVisitor(this)).ToArray());

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitGenericParameter(GenericParameterSignature signature)
        => new GenericParameterSignature(signature.ParameterType, signature.Index);

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitPinnedType(PinnedTypeSignature signature)
        => new PinnedTypeSignature(signature.BaseType.AcceptVisitor(this));

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitPointerType(PointerTypeSignature signature)
        => new PointerTypeSignature(signature.BaseType.AcceptVisitor(this));

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitSentinelType(SentinelTypeSignature signature) => signature;

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitSzArrayType(SzArrayTypeSignature signature)
        => new SzArrayTypeSignature(signature.BaseType.AcceptVisitor(this));

    TypeSignature ITypeSignatureVisitor<TypeSignature>.VisitTypeDefOrRef(TypeDefOrRefSignature signature)
        => new TypeDefOrRefSignature(Rewrite(signature.Type));
}
