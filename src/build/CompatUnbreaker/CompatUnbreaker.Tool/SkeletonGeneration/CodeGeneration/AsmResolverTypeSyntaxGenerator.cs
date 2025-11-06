using System.Diagnostics;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using CompatUnbreaker.Tool.Utilities;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal sealed class AsmResolverTypeSyntaxGenerator : ITypeSignatureVisitor<TypeContext, TypeSyntax>
{
    private static AsmResolverTypeSyntaxGenerator Instance { get; } = new();

    private static TTypeSyntax AddInformationTo<TTypeSyntax>(TTypeSyntax syntax, ITypeDescriptor symbol)
        where TTypeSyntax : TypeSyntax
    {
        // syntax = syntax.WithPrependedLeadingTrivia(ElasticMarker).WithAppendedTrailingTrivia(ElasticMarker);
        // syntax = syntax.WithAdditionalAnnotations(SymbolAnnotation.Create(symbol));

        return syntax;
    }

    public static TypeSyntax TypeExpression(ITypeDescriptor type, TypeContext context)
    {
        return Instance.VisitTypeDescriptor(type, context);
    }

    private TypeSyntax VisitTypeDescriptor(ITypeDescriptor type, TypeContext context) => type switch
    {
        TypeSignature signature => signature.AcceptVisitor(this, context),
        ITypeDefOrRef reference => VisitTypeDefOrRef(reference, context),
        ExportedType exported => VisitSimpleType(exported, context),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private TypeSyntax VisitTypeDefOrRef(ITypeDefOrRef type, TypeContext context) => type switch
    {
        TypeSpecification specification => VisitTypeDescriptor(specification.Signature, context),
        _ => VisitSimpleType(type, context),
    };

    private TypeSyntax VisitSimpleType(ITypeDescriptor symbol, TypeContext context, IList<TypeSignature>? typeArguments = null)
    {
        var name = MetadataHelpers.InferTypeArityAndUnmangleMetadataName(symbol.Name, out var arity).ToString();

        var isNonGenericValueType = arity > 0
            ? symbol.IsTypeOf("System", "Nullable`1")
            : symbol.IsValueType;

        var nullableAnnotation = isNonGenericValueType
            ? NullableAnnotation.Oblivious
            : context.Transform.TryConsumeNullableTransform() ?? NullableAnnotation.Oblivious;

        if (context.Transform.TryConsumeDynamicTransform() == true)
        {
            return IdentifierName("dynamic");
        }

        if (typeArguments != null && IsTupleTypeOfCardinality(new GenericInstanceTypeSignature(symbol.ToTypeDefOrRef(), true, typeArguments.ToArray()), out var cardinality))
        {
            var names = context.Transform.ConsumeTupleElementNames(cardinality);

            var elements = default(SeparatedSyntaxList<TupleElementSyntax>);

            for (var i = 0; i < Math.Min(typeArguments.Count, ValueTupleRestPosition - 1); i++)
            {
                var elementTypeSignature = typeArguments[i];
                var elementType = elementTypeSignature.AcceptVisitor(this, context);

                elements = elements.Add(
                    names.IsEmpty
                        ? TupleElement(elementType)
                        : TupleElement(elementType, names[i]?.ToIdentifierToken() ?? default)
                );
            }

            if (typeArguments.Count >= ValueTupleRestPosition)
            {
                var rest = (TupleTypeSyntax) typeArguments[ValueTupleRestPosition - 1].AcceptVisitor(this, context);

                for (var i = 0; i < rest.Elements.Count; i++)
                {
                    var element = rest.Elements[i];

                    elements = elements.Add(
                        names.IsEmpty
                            ? element
                            : element.WithIdentifier(names[ValueTupleRestPosition - 1 + i]?.ToIdentifierToken() ?? default)
                    );
                }
            }

            return TupleType(elements);
        }

        SimpleNameSyntax simpleNameSyntax;

        if (arity == 0)
        {
            simpleNameSyntax = name.ToIdentifierName();
        }
        else
        {
            simpleNameSyntax = GenericName(
                name.ToIdentifierToken(),
                TypeArgumentList([
                    .. typeArguments == null
                        ? Enumerable.Repeat(OmittedTypeArgument(), arity)
                        : typeArguments.Select(t => VisitTypeDescriptor(t, context)),
                ])
            );
        }

        TypeSyntax typeSyntax;

        if (symbol.DeclaringType != null)
        {
            var declaringTypeSyntax = VisitTypeDescriptor(symbol.DeclaringType, context);
            if (declaringTypeSyntax is NameSyntax declaringTypeName)
            {
                typeSyntax = QualifiedName(declaringTypeName, simpleNameSyntax);
            }
            else
            {
                typeSyntax = simpleNameSyntax;
            }

            typeSyntax = AddInformationTo(typeSyntax, symbol);
        }
        else
        {
            typeSyntax = AddInformationTo(AddNamespace(symbol.Namespace, simpleNameSyntax), symbol);
        }

        if (nullableAnnotation == NullableAnnotation.Annotated)
        {
            typeSyntax = AddInformationTo(NullableType(typeSyntax), symbol);
        }

        return typeSyntax;
    }

    private const int ValueTupleRestPosition = 8;

    private static bool IsTupleTypeOfCardinality(TypeSignature signature, out int tupleCardinality)
    {
        if (signature is GenericInstanceTypeSignature genericSignature &&
            genericSignature.Namespace == "System" &&
            genericSignature.GetUnmangledName() is "ValueTuple")
        {
            var arity = genericSignature.TypeArguments.Count;

            if (arity is >= 0 and < ValueTupleRestPosition)
            {
                tupleCardinality = arity;
                return true;
            }

            if (arity == ValueTupleRestPosition)
            {
                var typeToCheck = genericSignature;
                var levelsOfNesting = 0;

                do
                {
                    levelsOfNesting++;
                    typeToCheck = (GenericInstanceTypeSignature) typeToCheck.TypeArguments[ValueTupleRestPosition - 1];
                } while (SignatureComparer.Default.Equals(typeToCheck.GenericType, genericSignature.GenericType));

                arity = typeToCheck.TypeArguments.Count;

                if (arity is > 0 and < ValueTupleRestPosition && IsTupleTypeOfCardinality(typeToCheck, out tupleCardinality))
                {
                    Debug.Assert(tupleCardinality < ValueTupleRestPosition);
                    tupleCardinality += (ValueTupleRestPosition - 1) * levelsOfNesting;
                    return true;
                }
            }
        }

        tupleCardinality = 0;
        return false;
    }

    private static NameSyntax AddNamespace(string? @namespace, SimpleNameSyntax name)
    {
        if (string.IsNullOrEmpty(@namespace))
        {
            return AddGlobalAlias(name);
        }

        var parts = @namespace.Split('.');
        NameSyntax namespaceName = parts[0].ToIdentifierName();
        for (var i = 1; i < parts.Length; i++)
        {
            namespaceName = QualifiedName(namespaceName, parts[i].ToIdentifierName());
        }

        return QualifiedName(ParseName(@namespace), name);
    }

    private static AliasQualifiedNameSyntax AddGlobalAlias(SimpleNameSyntax syntax)
    {
        return AliasQualifiedName(CreateGlobalIdentifier(), syntax);
    }

    private static IdentifierNameSyntax CreateGlobalIdentifier() => IdentifierName(Token(SyntaxKind.GlobalKeyword));

    public TypeSyntax VisitBoxedType(BoxedTypeSignature signature, TypeContext context)
    {
        throw new NotImplementedException();
    }

    public TypeSyntax VisitByReferenceType(ByReferenceTypeSignature signature, TypeContext context)
    {
        return RefType(signature.BaseType.AcceptVisitor(this, context));
    }

    public TypeSyntax VisitCorLibType(CorLibTypeSignature signature, TypeContext context)
    {
        // SyntaxKind? kind = signature.ElementType switch
        // {
        //     ElementType.Boolean => SyntaxKind.BoolKeyword,
        //     ElementType.U1 => SyntaxKind.ByteKeyword,
        //     ElementType.I1 => SyntaxKind.SByteKeyword,
        //     ElementType.I4 => SyntaxKind.IntKeyword,
        //     ElementType.U4 => SyntaxKind.UIntKeyword,
        //     ElementType.I2 => SyntaxKind.ShortKeyword,
        //     ElementType.U2 => SyntaxKind.UShortKeyword,
        //     ElementType.I8 => SyntaxKind.LongKeyword,
        //     ElementType.U8 => SyntaxKind.ULongKeyword,
        //     ElementType.R4 => SyntaxKind.FloatKeyword,
        //     ElementType.R8 => SyntaxKind.DoubleKeyword,
        //     ElementType.String => SyntaxKind.StringKeyword,
        //     ElementType.Char => SyntaxKind.CharKeyword,
        //     ElementType.Object => SyntaxKind.ObjectKeyword,
        //     ElementType.Void => SyntaxKind.VoidKeyword,
        //     _ => null,
        // };
        //
        // if (kind != null)
        // {
        //     return PredefinedType(Token(kind.Value));
        // }

        return VisitTypeDefOrRef(signature.Type, context);
    }

    public TypeSyntax VisitCustomModifierType(CustomModifierTypeSignature signature, TypeContext context)
    {
        return signature.BaseType.AcceptVisitor(this, context);
    }

    public TypeSyntax VisitGenericInstanceType(GenericInstanceTypeSignature signature, TypeContext context)
    {
        return VisitSimpleType(signature.GenericType, context, signature.TypeArguments);
    }

    public TypeSyntax VisitGenericParameter(GenericParameterSignature signature, TypeContext context)
    {
        var nullableAnnotation = context.Transform.TryConsumeNullableTransform();
        if (context.Transform.TryConsumeDynamicTransform() == true)
        {
            return IdentifierName("dynamic");
        }

        var genericParameter = context.Generic.GetGenericParameter(signature);

        TypeSyntax result = AddInformationTo(
            genericParameter != null
                ? genericParameter.Name.Value.ToIdentifierName()
                : signature.Name.ToIdentifierName(),
            signature
        );

        return nullableAnnotation == NullableAnnotation.Annotated ? AddInformationTo(NullableType(result), signature) : result;
    }

    public TypeSyntax VisitPinnedType(PinnedTypeSignature signature, TypeContext context)
    {
        throw new NotImplementedException();
    }

    public TypeSyntax VisitPointerType(PointerTypeSignature signature, TypeContext context)
    {
        return PointerType(signature.BaseType.AcceptVisitor(this, context));
    }

    public TypeSyntax VisitSentinelType(SentinelTypeSignature signature, TypeContext context)
    {
        throw new NotImplementedException();
    }

    public TypeSyntax VisitSzArrayType(SzArrayTypeSignature signature, TypeContext context)
    {
        var nullableAnnotation = context.Transform.TryConsumeNullableTransform();
        if (context.Transform.TryConsumeDynamicTransform() == true)
        {
            return IdentifierName("dynamic");
        }

        var result = ArrayType(signature.BaseType.AcceptVisitor(this, context), [ArrayRankSpecifier()]);
        return nullableAnnotation == NullableAnnotation.Annotated ? AddInformationTo(NullableType(result), signature) : result;
    }

    public TypeSyntax VisitArrayType(ArrayTypeSignature signature, TypeContext context)
    {
        var nullableAnnotation = context.Transform.TryConsumeNullableTransform();
        if (context.Transform.TryConsumeDynamicTransform() == true)
        {
            return IdentifierName("dynamic");
        }

        var result = ArrayType(signature.BaseType.AcceptVisitor(this, context), SingletonList(ArrayRankSpecifier(
            [.. Enumerable.Repeat<ExpressionSyntax>(OmittedArraySizeExpression(), signature.Rank)]
        )));
        return nullableAnnotation == NullableAnnotation.Annotated ? AddInformationTo(NullableType(result), signature) : result;
    }

    public TypeSyntax VisitTypeDefOrRef(TypeDefOrRefSignature signature, TypeContext context)
    {
        return VisitTypeDefOrRef(signature.Type, context);
    }

    public TypeSyntax VisitFunctionPointerType(FunctionPointerTypeSignature signature, TypeContext context)
    {
        FunctionPointerCallingConventionSyntax? callingConventionSyntax = null;
        // For varargs there is no C# syntax. You get a use-site diagnostic if you attempt to use it, and just
        // making a default-convention symbol is likely good enough. This is only observable through metadata
        // that always be uncompilable in C# anyway.
        if (signature.Signature.CallingConvention is not CallingConventionAttributes.Default and not CallingConventionAttributes.VarArg)
        {
            IEnumerable<FunctionPointerUnmanagedCallingConventionSyntax> conventionsList = signature.Signature.CallingConvention switch
            {
                CallingConventionAttributes.C => [GetConventionForString("Cdecl")],
                CallingConventionAttributes.StdCall => [GetConventionForString("Stdcall")],
                CallingConventionAttributes.ThisCall => [GetConventionForString("Thiscall")],
                CallingConventionAttributes.FastCall => [GetConventionForString("Fastcall")],
                // CallingConventionAttributes.Unmanaged =>
                //     // All types that come from CallingConventionTypes start with "CallConv". We don't want the prefix for the actual
                //     // syntax, so strip it off
                //     signature.Signature.UnmanagedCallingConventionTypes.IsEmpty
                //         ? null
                //         : symbol.Signature.UnmanagedCallingConventionTypes.Select(type => GetConventionForString(type.Name["CallConv".Length..])),

                _ => throw new Exception(),
            };

            callingConventionSyntax = FunctionPointerCallingConvention(
                Token(SyntaxKind.UnmanagedKeyword),
                conventionsList is object
                    ? FunctionPointerUnmanagedCallingConventionList([.. conventionsList])
                    : null);

            static FunctionPointerUnmanagedCallingConventionSyntax GetConventionForString(string identifier)
                => FunctionPointerUnmanagedCallingConvention(Identifier(identifier));
        }

        // var parameters = signature.Signature.ParameterTypes.Select(p => (p.Type, RefKindModifiers: CSharpSyntaxGeneratorInternal.GetParameterModifiers(p)))
        //     .Concat([
        //         (
        //             Type: signature.Signature.ReturnType,
        //             RefKindModifiers: CSharpSyntaxGeneratorInternal.GetParameterModifiers(isScoped: false, symbol.Signature.RefKind, isParams: false, forFunctionPointerReturnParameter: true))
        //     ])
        //     .SelectAsArray(t => FunctionPointerParameter(t.Type.GenerateTypeSyntax()).WithModifiers(t.RefKindModifiers));

        var parameters = signature.Signature.ParameterTypes
            .Append(signature.Signature.ReturnType)
            .Select(t => FunctionPointerParameter(VisitTypeDescriptor(t, context)));

        return AddInformationTo(FunctionPointerType(callingConventionSyntax, FunctionPointerParameterList([.. parameters])), signature);
    }
}
