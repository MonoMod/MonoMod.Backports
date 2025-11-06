using System.Diagnostics;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    public static SyntaxNode AddAttributes(this SyntaxGenerator syntaxGenerator, SyntaxNode declaration, IEnumerable<CustomAttribute> attributes)
    {
        return syntaxGenerator.AddAttributes(declaration, syntaxGenerator.Attributes(attributes));
    }

    public static IEnumerable<SyntaxNode> Attributes(this SyntaxGenerator syntaxGenerator, IEnumerable<CustomAttribute> attributes)
    {
        return attributes.Where(a => !IsReserved(a)).Select(syntaxGenerator.Attribute);
    }

    private static bool IsReserved(CustomAttribute attribute)
    {
        var type = attribute.Constructor?.DeclaringType;
        if (type == null) return false;

        if (type.IsTypeOf("System", "ObsoleteAttribute"))
        {
            const string ByRefLikeMarker = "Types with embedded references are not supported in this version of your compiler.";
            const string RequiredMembersMarker = "Constructors of types with required members are not supported in this version of your compiler.";

            return attribute.Signature?.FixedArguments.FirstOrDefault()?.Element is Utf8String { Value: ByRefLikeMarker or RequiredMembersMarker };
        }

        if (type.Namespace == "System.Runtime.CompilerServices")
        {
            switch (type.Name)
            {
                case "DynamicAttribute":
                case "IsReadOnlyAttribute":
                case "RequiresLocationAttribute":
                case "IsUnmanagedAttribute":
                case "IsByRefLikeAttribute":
                case "CompilerFeatureRequiredAttribute":
                case "TupleElementNamesAttribute":
                case "NullableAttribute":
                case "NullableContextAttribute":
                case "NullablePublicOnlyAttribute":
                // case "NativeIntegerAttribute": TODO roslyn doesn't emit this for me? huh?
                case "ExtensionAttribute":
                case "RequiredMemberAttribute":
                case "ScopedRefAttribute":
                case "RefSafetyRulesAttribute":
                case "ParamCollectionAttribute":
                    return true;
            }
        }
        else if (type.Namespace == "System")
        {
            switch (type.Name)
            {
                case "ParamArrayAttribute":
                    return true;
            }
        }

        return false;
    }

    public static SyntaxNode Attribute(this SyntaxGenerator syntaxGenerator, CustomAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute.Constructor);
        ArgumentNullException.ThrowIfNull(attribute.Constructor.DeclaringType);
        ArgumentNullException.ThrowIfNull(attribute.Constructor.Signature);
        ArgumentNullException.ThrowIfNull(attribute.Signature);

        var args = attribute.Signature.FixedArguments.Select((a, i) =>
            {
                var parameterType = attribute.Constructor.Signature.ParameterTypes[i];
                Debug.Assert(SignatureComparer.Default.Equals(parameterType, a.ArgumentType));
                return syntaxGenerator.AttributeArgument(syntaxGenerator.AttributeArgumentExpression(a));
            })
            .Concat(attribute.Signature.NamedArguments.Select(n => syntaxGenerator.AttributeArgument(n.MemberName, syntaxGenerator.AttributeArgumentExpression(n.Argument))))
            .ToArray();

        return syntaxGenerator.Attribute(
            name: syntaxGenerator.TypeExpression(attribute.Constructor.DeclaringType, TypeContext.Empty),
            attributeArguments: args.Length > 0 ? args : null
        );
    }

    private static SyntaxNode AttributeArgumentExpression(this SyntaxGenerator syntaxGenerator, CustomAttributeArgument argument)
    {
        var value = argument.ArgumentType.ElementType == ElementType.SzArray
            ? (argument.IsNullArray ? null : argument.Elements)
            : argument.Element;

        var typeContext = TypeContext.Empty; // CAs can't use generics
        return syntaxGenerator.LiteralExpression(argument.ArgumentType, value, typeContext);
    }

    public static SyntaxNode LiteralExpression(this SyntaxGenerator syntaxGenerator, TypeSignature typeSignature, object? value, TypeContext typeContext)
    {
        if (value == null)
        {
            if (!typeSignature.IsValueType)
            {
                return syntaxGenerator.CastExpression(
                    syntaxGenerator.NullableTypeExpression(syntaxGenerator.TypeExpression(typeSignature, typeContext)),
                    syntaxGenerator.NullLiteralExpression()
                );
            }

            return syntaxGenerator.DefaultExpression(syntaxGenerator.TypeExpression(typeSignature, typeContext));
        }

        if (value is BoxedArgument boxedArgument)
        {
            // null objects get serialized as boxed null strings
            if (typeSignature.ElementType == ElementType.Object && boxedArgument.Type.ElementType == ElementType.String && boxedArgument.Value == null)
            {
                return syntaxGenerator.LiteralExpression(typeSignature, null, typeContext);
            }

            return syntaxGenerator.CastExpression(
                syntaxGenerator.TypeExpression(typeSignature, typeContext),
                syntaxGenerator.LiteralExpression(boxedArgument.Type, boxedArgument.Value, typeContext)
            );
        }

        if (typeSignature.ElementType == ElementType.String)
        {
            return syntaxGenerator.LiteralExpression(((Utf8String) value).Value);
        }

        if (typeSignature.ElementType.IsPrimitive())
        {
            return syntaxGenerator.LiteralExpression(value);
        }

        if (typeSignature is SzArrayTypeSignature arrayTypeSignature)
        {
            var baseType = arrayTypeSignature.BaseType;
            var values = (IList<object?>) value;

            return syntaxGenerator.ArrayCreationExpression(
                syntaxGenerator.TypeExpression(baseType, typeContext),
                values.Select(o => syntaxGenerator.LiteralExpression(baseType, o, typeContext))
            );
        }

        if (typeSignature.IsTypeOf("System", "Type"))
        {
            var type = (ITypeDescriptor) value;
            return syntaxGenerator.TypeOfExpression(syntaxGenerator.TypeExpression(type, typeContext));
        }

        if (typeSignature.Resolve() is { IsEnum: true } enumType)
        {
            return syntaxGenerator.CreateEnumConstantValue(enumType, value, typeContext);
        }

        throw new NotSupportedException($"Couldn't generate a LiteralExpression for type '{typeSignature}' with value '{value}'");
    }

    private static bool IsPrimitive(this ElementType elementType)
    {
        switch (elementType)
        {
            case ElementType.Void:
            case ElementType.Boolean:
            case ElementType.Char:
            case ElementType.I1 or ElementType.I2 or ElementType.I4 or ElementType.I8:
            case ElementType.U1 or ElementType.U2 or ElementType.U4 or ElementType.U8:
            case ElementType.R4 or ElementType.R8:
            case ElementType.I or ElementType.U:
                return true;

            default:
                return false;
        }
    }
}
