using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;
using Microsoft.CodeAnalysis.Editing;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static class DeclarationModifiersExtensions
{
    public static DeclarationModifiers GetModifiers(this IMemberDefinition member)
    {
        var field = member as FieldDefinition;
        var property = member as PropertyDefinition;
        var method = member as MethodDefinition;
        var type = member as TypeDefinition;
        var isConst = field?.IsLiteral == true || field?.Constant != null;

        return DeclarationModifiers.None
            .WithIsStatic(member.IsStatic() && !isConst)
            .WithIsAbstract(member.IsRoslynAbstract())
            .WithIsReadOnly(field?.IsInitOnly == true || property?.IsReadOnly() == true || type?.IsReadOnly == true || method?.IsReadOnly() == true)
            .WithIsVirtual(member.IsRoslynVirtual())
            .WithIsOverride(member.IsOverride())
            .WithIsSealed(member.IsRoslynSealed())
            .WithIsConst(isConst)
            // .WithIsUnsafe(symbol.RequiresUnsafeModifier()) TODO
            .WithIsRef(field?.Signature?.FieldType is ByReferenceTypeSignature || type?.IsByRefLike == true)
            .WithIsVolatile(field?.IsVolatile() == true)
            .WithIsExtern(member.IsExtern())
            .WithAsync(method?.IsAsync() == true)
            .WithIsRequired(member.IsRequired())
            .WithIsFile(type?.IsFileLocal() == true);
    }
}
