using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using CompatUnbreaker.Tool.Utilities;
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
    public static SyntaxNode TypeDeclaration(this SyntaxGenerator syntaxGenerator, TypeDefinition type, Func<IMemberDefinition, bool>? memberFilter = null)
    {
        ArgumentNullException.ThrowIfNull(type.Name);

        var name = MetadataHelpers.InferTypeArityAndUnmangleMetadataName(type.Name.Value, out var arity).ToString();

        var typeContext = TypeContext.From(type, type);

        var interfaces = type.Interfaces.Select(i =>
        {
            ArgumentNullException.ThrowIfNull(i.Interface);
            return syntaxGenerator.TypeExpression(i.Interface, typeContext);
        });

        var members = type.GetMembers().Where(syntaxGenerator.CanBeDeclared);
        if (memberFilter != null) members = members.Where(memberFilter);

        SyntaxNode declaration;

        if (type.IsDelegate)
        {
            var invoke = type.Methods.Single(m => m.Name == "Invoke");
            ArgumentNullException.ThrowIfNull(invoke.Signature);

            typeContext = TypeContext.From(invoke, invoke);
            var returnType = invoke.Signature.ReturnType;

            declaration = syntaxGenerator.DelegateDeclaration(
                name,
                typeParameters: null,
                parameters: invoke.Parameters.Select(p => syntaxGenerator.ParameterDeclaration(p)),
                returnType: returnType.ElementType == ElementType.Void ? null : syntaxGenerator.TypeExpression(returnType, typeContext),
                accessibility: (Accessibility) type.GetAccessibility(),
                modifiers: type.GetModifiers()
            );
        }
        else if (type.IsEnum)
        {
            var underlyingType = type.GetEnumUnderlyingType();

            declaration = syntaxGenerator.EnumDeclaration(
                name,
                underlyingType: underlyingType is null or { ElementType: ElementType.I4 }
                    ? null
                    : syntaxGenerator.TypeExpression(underlyingType, typeContext),
                accessibility: (Accessibility) type.GetAccessibility(),
                members: type.Fields.Where(f => f.Name != "value__").Select(m => syntaxGenerator.Declaration(m, memberFilter))
            );
        }
        else if (type.IsValueType)
        {
            declaration = syntaxGenerator.StructDeclaration(
                type.IsRecord(),
                name,
                typeParameters: null,
                accessibility: (Accessibility) type.GetAccessibility(),
                modifiers: type.GetModifiers(),
                interfaceTypes: interfaces,
                members: members.Select(m => syntaxGenerator.Declaration(m, memberFilter))
            );
        }
        else if (type.IsInterface)
        {
            declaration = syntaxGenerator.InterfaceDeclaration(
                name,
                typeParameters: null,
                accessibility: (Accessibility) type.GetAccessibility(),
                interfaceTypes: interfaces,
                members: members.Select(m => syntaxGenerator.Declaration(m, memberFilter))
            );
        }
        else if (type.IsClass)
        {
            if (type.GetSynthesizedConstructor() is { } synthesizedConstructor)
            {
                members = members.Where(m => m != synthesizedConstructor);
            }

            declaration = syntaxGenerator.ClassDeclaration(
                type.IsRecord(),
                name,
                typeParameters: null,
                accessibility: (Accessibility) type.GetAccessibility(),
                modifiers: type.GetModifiers(),
                baseType: type.BaseType != null && type.BaseType.ToTypeSignature().ElementType != ElementType.Object ? syntaxGenerator.TypeExpression(type.BaseType, typeContext) : null,
                interfaceTypes: interfaces,
                members: members.Select(m => syntaxGenerator.Declaration(m, memberFilter)).Select(d => d.WithLeadingTrivia(SyntaxFactory.LineFeed))
            );
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        declaration = syntaxGenerator.WithTypeParametersAndConstraints(declaration, type.GenericParameters.TakeLast(arity).ToArray(), typeContext);
        declaration = syntaxGenerator.AddAttributes(declaration, type.CustomAttributes);

        if (declaration is TypeDeclarationSyntax { Members.Count: 0 } or EnumDeclarationSyntax { Members.Count: 0 })
        {
            declaration = ((BaseTypeDeclarationSyntax) declaration)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                .WithOpenBraceToken(default)
                .WithCloseBraceToken(default);
        }

        return declaration;
    }

    public static bool CanBeDeclared(this SyntaxGenerator syntaxGenerator, IMemberDefinition member)
    {
        ArgumentNullException.ThrowIfNull(member.Name);

        if (member is IHasCustomAttribute hasCustomAttribute && hasCustomAttribute.IsCompilerGenerated())
        {
            return false;
        }

        if (member is MethodDefinition method)
        {
            if (method.IsConstructor || method.IsDestructor())
            {
                return true;
            }

            if (method.IsPropertyAccessor() || method.IsEventAccessor())
            {
                return false;
            }
        }

        var name = MetadataHelpers.InferTypeArityAndUnmangleMetadataName(member.Name, out _).ToString();
        return SyntaxFacts.IsValidIdentifier(name);
    }

    private static MethodDefinition? GetSynthesizedConstructor(this TypeDefinition type)
    {
        if (type.IsStatic() || type.IsInterface)
            return null;

        var isRecord = type.IsRecord();

        MethodDefinition? constructor = null;

        foreach (var method in type.Methods)
        {
            if (method is not { IsStatic: false, IsConstructor: true })
                continue;

            // Ignore the record copy constructor
            if (isRecord &&
                method.Parameters.Count == 1 &&
                method.GenericParameters.Count == 0 &&
                SignatureComparer.Default.Equals(method.Parameters[0].ParameterType, type.ToTypeSignature()))
            {
                continue;
            }

            if (constructor != null)
            {
                return null;
            }

            constructor = method;
        }

        if (constructor == null)
            return null;

        if (constructor.Parameters.Count != 0 ||
            (constructor.Attributes & MethodAttributes.MemberAccessMask) != (type.IsAbstract ? MethodAttributes.Family : MethodAttributes.Public) ||
            constructor.CustomAttributes.Any(a => !IsReserved(a)))
        {
            return null;
        }

        if (constructor.CilMethodBody is not { } methodBody ||
            !methodBody.Instructions.Match(
                i => i.OpCode == CilOpCodes.Ldarg_0,
                i => i.OpCode == CilOpCodes.Call &&
                     i.Operand is IMethodDescriptor method &&
                     method.DeclaringType == type.BaseType &&
                     method.Name == constructor.Name &&
                     method.Signature is { HasThis: true, ParameterTypes.Count: 0 },
                i => i.OpCode == CilOpCodes.Ret
            ))
        {
            return null;
        }

        return constructor;
    }
}
