using System.Runtime.InteropServices;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Accessibility = Microsoft.CodeAnalysis.Accessibility;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    // private static bool IsValidStaticUserDefinedOperatorSignature(this MethodDefinition method, int parameterCount)
    // {
    //     if (method.Signature.ReturnType.ElementType == ElementType.Void ||
    //         method.GenericParameters.Count > 0 ||
    //         method.Signature.CallingConvention == CallingConventionAttributes.VarArg ||
    //         method.Parameters.Count != parameterCount ||
    //         method.IsParams())
    //     {
    //         return false;
    //     }
    //
    //     return method.HasValidOperatorParameterRefKinds();
    // }
    //
    // private static bool HasValidOperatorParameterRefKinds(this MethodDefinition method)
    // {
    //     if (this.ParameterRefKinds.IsDefault)
    //     {
    //         return true;
    //     }
    //
    //     foreach (var kind in this.ParameterRefKinds)
    //     {
    //         switch (kind)
    //         {
    //             case RefKind.None:
    //             case RefKind.In:
    //                 continue;
    //             case RefKind.Out:
    //             case RefKind.Ref:
    //             case RefKind.RefReadOnlyParameter:
    //                 return false;
    //             default:
    //                 throw ExceptionUtilities.UnexpectedValue(kind);
    //         }
    //     }
    //
    //     return true;
    // }

    public static SyntaxNode MethodDeclaration(this SyntaxGenerator syntaxGenerator, MethodDefinition method, IEnumerable<SyntaxNode>? statements = null)
    {
        if (method.IsConstructor)
        {
            return syntaxGenerator.ConstructorDeclaration(method);
        }

        if (method.IsDestructor())
        {
            throw new NotImplementedException();
        }

        if (method.IsSpecialName && SyntaxFacts.GetOperatorKind(method.Name) != SyntaxKind.None)
        {
            var returnTypeContext2 = TypeContext.From(method, method.Parameters.ReturnParameter.GetOrCreateDefinition());
            var decl2 = syntaxGenerator.OperatorDeclaration(
                method.Name,
                isImplicitConversion: method.Name.Value is WellKnownMemberNames.ImplicitConversionName,
                parameters: method.Parameters.Select(p => syntaxGenerator.ParameterDeclaration(p)),
                returnType: method.Signature.ReturnType.ElementType == ElementType.Void ? null : TypeExpression(syntaxGenerator, method.Signature.ReturnType, method.Parameters.ReturnParameter.GetRefKind(), returnTypeContext2),
                accessibility: (Accessibility) method.GetAccessibility(),
                modifiers: method.GetModifiers(),
                statements: statements
            );

            return decl2;
        }

        // if (method.MethodKind == MethodKind.Destructor)
        // {
        //     return syntaxGenerator.DestructorDeclaration(method);
        // }
        //
        // if (method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion)
        // {
        //     return syntaxGenerator.OperatorDeclaration(method);
        // }

        var typeContext = TypeContext.From(method, method);

        var returnTypeContext = TypeContext.From(method, method.Parameters.ReturnParameter.GetOrCreateDefinition());

        var decl = MethodDeclaration(
            syntaxGenerator,
            method.Name,
            typeParameters: method.GenericParameters.Select(syntaxGenerator.TypeParameter),
            parameters: method.Parameters.Select(p => syntaxGenerator.ParameterDeclaration(p)),
            returnType: method.Signature.ReturnType.ElementType == ElementType.Void ? null : TypeExpression(syntaxGenerator, method.Signature.ReturnType, method.Parameters.ReturnParameter.GetRefKind(), returnTypeContext),
            accessibility: (Accessibility) method.GetAccessibility(),
            modifiers: method.GetModifiers(),
            statements: statements
        );

        if (method.GenericParameters.Count > 0)
        {
            // // Overrides are special.  Specifically, in an override, if a type parameter has no constraints, then we
            // // want to still add `where T : default` if that type parameter is used with NRT (e.g. `T?`) that way
            // // the language can distinguish if this is a Nullable Value Type or not.
            // if (method.IsOverride())
            // {
            //     foreach (var typeParameter in method.GenericParameters)
            //     {
            //         if (HasNullableAnnotation(typeParameter, method))
            //         {
            //             if (!HasSomeConstraint(typeParameter))
            //             {
            //                 // if there are no constraints, add `where T : default` so it's known this not an NVT
            //                 // and is just an unconstrained type parameter.
            //                 decl = WithDefaultConstraint(decl, typeParameter.Name);
            //             }
            //             else if (!typeParameter.HasValueTypeConstraint)
            //             {
            //                 // if there are some constraints, add `where T : class` so it's known this is not an NVT
            //                 // and must specifically be some reference type.
            //                 decl = WithTypeConstraint(decl, typeParameter.Name, SpecialTypeConstraintKind.ReferenceType);
            //             }
            //         }
            //     }
            // }
            // else
            {
                decl = syntaxGenerator.WithTypeParametersAndConstraints(decl, method.GenericParameters, typeContext);
            }
        }

        // if (method.ExplicitInterfaceImplementations.Length > 0)
        // {
        //     decl = this.WithExplicitInterfaceImplementations(decl,
        //         ImmutableArray<ISymbol>.CastUp(method.ExplicitInterfaceImplementations));
        // }

        if (method.IsPInvokeImpl)
        {
            var implementationMap = method.ImplementationMap;

            var dllName = implementationMap.Scope.Name.Value;

            var arguments = new List<SyntaxNode>
            {
                syntaxGenerator.AttributeArgument(syntaxGenerator.LiteralExpression(dllName)),
            };

            if (implementationMap.Name != method.Name)
            {
                arguments.Add(syntaxGenerator.AttributeArgument(
                    "EntryPoint",
                    syntaxGenerator.LiteralExpression(implementationMap.Name.Value)
                ));
            }

            var charSet = implementationMap.Attributes & ImplementationMapAttributes.CharSetMask;
            if (charSet != ImplementationMapAttributes.CharSetNotSpec)
            {
                arguments.Add(syntaxGenerator.AttributeArgument(
                    "CharSet",
                    syntaxGenerator.MemberAccessExpression(SyntaxFactory.ParseTypeName("System.Runtime.InteropServices.CharSet"), charSet switch
                    {
                        ImplementationMapAttributes.CharSetAnsi => nameof(CharSet.Ansi),
                        ImplementationMapAttributes.CharSetUnicode => nameof(CharSet.Unicode),
                        ImplementationMapAttributes.CharSetAuto => nameof(CharSet.Auto),
                        _ => throw new ArgumentOutOfRangeException(),
                    })
                ));
            }

            if ((implementationMap.Attributes & ImplementationMapAttributes.SupportsLastError) != 0)
            {
                arguments.Add(syntaxGenerator.AttributeArgument("SetLastError", syntaxGenerator.TrueLiteralExpression()));
            }

            if ((implementationMap.Attributes & ImplementationMapAttributes.NoMangle) != 0)
            {
                arguments.Add(syntaxGenerator.AttributeArgument("ExactSpelling", syntaxGenerator.TrueLiteralExpression()));
            }

            var callingConvention = implementationMap.Attributes & ImplementationMapAttributes.CallConvMask;
            if (callingConvention != ImplementationMapAttributes.CallConvWinapi)
            {
                arguments.Add(syntaxGenerator.AttributeArgument(
                    "CallingConvention",
                    syntaxGenerator.MemberAccessExpression(SyntaxFactory.ParseTypeName("System.Runtime.InteropServices.CallingConvention"), callingConvention switch
                    {
                        ImplementationMapAttributes.CallConvCdecl => nameof(CallingConvention.Cdecl),
                        ImplementationMapAttributes.CallConvStdcall => nameof(CallingConvention.StdCall),
                        ImplementationMapAttributes.CallConvThiscall => nameof(CallingConvention.ThisCall),
                        ImplementationMapAttributes.CallConvFastcall => nameof(CallingConvention.FastCall),
                        _ => throw new ArgumentOutOfRangeException(),
                    })
                ));
            }


            var bestFitMapping = implementationMap.Attributes & ImplementationMapAttributes.BestFitMask;
            if (bestFitMapping != ImplementationMapAttributes.BestFitUseAssem)
            {
                arguments.Add(syntaxGenerator.AttributeArgument(
                    "BestFitMapping",
                    bestFitMapping switch
                    {
                        ImplementationMapAttributes.BestFitEnabled => syntaxGenerator.TrueLiteralExpression(),
                        ImplementationMapAttributes.BestFitDisabled => syntaxGenerator.FalseLiteralExpression(),
                        _ => throw new ArgumentOutOfRangeException(),
                    })
                );
            }

            if (!method.PreserveSignature)
            {
                arguments.Add(syntaxGenerator.AttributeArgument("PreserveSig", syntaxGenerator.FalseLiteralExpression()));
            }

            var throwOnUnmappableChar = implementationMap.Attributes & ImplementationMapAttributes.ThrowOnUnmappableCharMask;
            if (throwOnUnmappableChar != ImplementationMapAttributes.ThrowOnUnmappableCharUseAssem)
            {
                arguments.Add(syntaxGenerator.AttributeArgument(
                    "ThrowOnUnmappableChar",
                    throwOnUnmappableChar switch
                    {
                        ImplementationMapAttributes.ThrowOnUnmappableCharEnabled => syntaxGenerator.TrueLiteralExpression(),
                        ImplementationMapAttributes.ThrowOnUnmappableCharDisabled => syntaxGenerator.FalseLiteralExpression(),
                        _ => throw new ArgumentOutOfRangeException(),
                    })
                );
            }

            decl = syntaxGenerator.AddAttributes(decl, syntaxGenerator.Attribute(
                "System.Runtime.InteropServices.DllImportAttribute",
                arguments
            ));
        }

        if (method.IsExtern())
        {
            decl = ((BaseMethodDeclarationSyntax) decl)
                .WithBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(SyntaxFactory.LineFeed))
                .WithLeadingTrivia(SyntaxFactory.LineFeed);
        }

        var attributes = syntaxGenerator.Attributes(method.CustomAttributes)
            .Concat(
                syntaxGenerator.Attributes(method.Parameters.ReturnParameter.GetOrCreateDefinition().CustomAttributes)
                    .Cast<AttributeListSyntax>()
                    .Select(a => a.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.ReturnKeyword))))
            );

        return syntaxGenerator.AddAttributes(decl, attributes);

        // bool HasNullableAnnotation(ITypeParameterSymbol typeParameter, IMethodSymbol method)
        // {
        //     return method.ReturnType.GetReferencedTypeParameters().Any(t => IsNullableAnnotatedTypeParameter(typeParameter, t)) ||
        //            method.Parameters.Any(p => p.Type.GetReferencedTypeParameters().Any(t => IsNullableAnnotatedTypeParameter(typeParameter, t)));
        // }
        //
        // static bool IsNullableAnnotatedTypeParameter(ITypeParameterSymbol typeParameter, ITypeParameterSymbol current)
        // {
        //     return Equals(current, typeParameter) && current.NullableAnnotation == NullableAnnotation.Annotated;
        // }
    }

    private static SyntaxNode ConstructorDeclaration(
        this SyntaxGenerator syntaxGenerator,
        MethodDefinition constructorMethod,
        IEnumerable<SyntaxNode>? baseConstructorArguments = null,
        IEnumerable<SyntaxNode>? statements = null
    )
    {
        return syntaxGenerator.AddAttributes(
            syntaxGenerator.ConstructorDeclaration(
                constructorMethod.DeclaringType != null ? constructorMethod.DeclaringType.Name : "New",
                constructorMethod.Parameters.Select(p => syntaxGenerator.ParameterDeclaration(p)),
                (Accessibility) constructorMethod.GetAccessibility(),
                constructorMethod.GetModifiers(),
                baseConstructorArguments,
                statements
            ),
            constructorMethod.CustomAttributes
        );
    }

    public static SyntaxNode ParameterDeclaration(this SyntaxGenerator syntaxGenerator, Parameter symbol, SyntaxNode? initializer = null)
    {
        var definition = symbol.GetOrCreateDefinition();

        var refKind = symbol.GetRefKind();

        var typeContext = TypeContext.From(definition.Method, definition);
        var type = syntaxGenerator.TypeExpression(symbol.ParameterType, typeContext);
        if (type is RefTypeSyntax refType)
        {
            type = refType.Type;
        }

        var isParams = definition.IsParams();

        return syntaxGenerator.AddAttributes(
            syntaxGenerator.ParameterDeclaration(
                symbol.Name,
                type,
                initializer ?? (definition.Constant is { } constant
                    ? syntaxGenerator.LiteralExpression(symbol.ParameterType, constant.InterpretData(), typeContext)
                    : null),
                refKind,
                isExtension: symbol.Index == 0 && definition.Method?.IsExtension() == true,
                isParams,
                isScoped: symbol.IsScoped(refKind) && !isParams
            ),
            definition.CustomAttributes
        );
    }

    private static RefKind GetRefKind(this Parameter parameter)
    {
        if (parameter.ParameterType.StripModifiers() is ByReferenceTypeSignature)
        {
            var definition = parameter.GetOrCreateDefinition();
            ArgumentNullException.ThrowIfNull(definition.Method);

            var isReturn = parameter == definition.Method.Parameters.ReturnParameter;

            if (definition.IsOut)
            {
                return RefKind.Out;
            }

            if (!isReturn && definition.HasCustomAttribute("System.Runtime.CompilerServices", "RequiresLocationAttribute"))
            {
                return RefKind.RefReadOnlyParameter;
            }

            if (definition.HasIsReadOnlyAttribute())
            {
                return RefKind.In;
            }

            return RefKind.Ref;
        }

        return RefKind.None;
    }

    private static bool IsScoped(this Parameter parameter, RefKind refKind)
    {
        var scopedKind = parameter.GetScopedKind(refKind);

        if (refKind is RefKind.Ref or RefKind.In or RefKind.RefReadOnlyParameter && scopedKind == ScopedKind.ScopedRef)
            return true;

        bool isByRefLike;

        if (parameter.ParameterType is ByReferenceTypeSignature)
        {
            isByRefLike = true;
        }
        else if (parameter.ParameterType is GenericParameterSignature genericParameterSignature)
        {
            var method = parameter.GetOrCreateDefinition().Method;

            var genericParameters = genericParameterSignature.ParameterType switch
            {
                GenericParameterType.Type => method.DeclaringType.GenericParameters,
                GenericParameterType.Method => method.GenericParameters,
                _ => throw new ArgumentOutOfRangeException(),
            };

            var genericParameter = genericParameters[genericParameterSignature.Index];

            isByRefLike = genericParameter.HasAllowByRefLike;
        }
        else if (parameter.ParameterType is TypeDefOrRefSignature or CorLibTypeSignature or GenericInstanceTypeSignature)
        {
            var parameterType = parameter.ParameterType.Resolve()
                                ?? throw new InvalidOperationException($"Couldn't resolve parameter type '{parameter.ParameterType}'");

            isByRefLike = parameterType.IsByRefLike;
        }
        else
        {
            isByRefLike = false;
        }

        return refKind is RefKind.None && isByRefLike && scopedKind == ScopedKind.ScopedValue;
    }

    private static ScopedKind GetScopedKind(this Parameter parameter, RefKind refKind)
    {
        var definition = parameter.GetOrCreateDefinition();

        if (definition.HasCustomAttribute("System.Diagnostics.CodeAnalysis", "UnscopedRefAttribute"))
        {
            return ScopedKind.None;
        }

        if (definition.HasCustomAttribute("System.Runtime.CompilerServices", "ScopedRefAttribute"))
        {
            if (parameter.ParameterType.StripModifiers() is ByReferenceTypeSignature)
            {
                return ScopedKind.ScopedRef;
            }

            return ScopedKind.ScopedValue;
        }

        if ( /* TODO module.UseUpdatedEscapeRules && */ refKind == RefKind.Out)
        {
            return ScopedKind.ScopedRef;
        }

        return ScopedKind.None;
    }
}
