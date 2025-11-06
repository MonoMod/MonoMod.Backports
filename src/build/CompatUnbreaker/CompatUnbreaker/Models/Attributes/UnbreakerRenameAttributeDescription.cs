using System.Diagnostics;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Models.Attributes;

internal sealed class UnbreakerRenameAttributeDescription(Utf8String @namespace, Utf8String name)
    : AttributeDescription<RenameData>(@namespace, name)
{
    public override RenameData CreateData(CustomAttribute customAttribute)
    {
        ArgumentNullException.ThrowIfNull(customAttribute.Constructor);
        ArgumentNullException.ThrowIfNull(customAttribute.Constructor.ContextModule);
        ArgumentNullException.ThrowIfNull(customAttribute.Signature);

        var typeFactory = customAttribute.Constructor.ContextModule.CorLibTypeFactory;

        if (CheckSignature(typeFactory.Type(), typeFactory.String))
        {
            var arguments = customAttribute.Signature.FixedArguments;
            Debug.Assert(arguments.Count == 2);

            return new RenameData.TypeRename(
                (TypeSignature) arguments[0].Element!,
                (Utf8String) arguments[1].Element!
            );
        }

        if (CheckSignature(typeFactory.Type(), typeFactory.String, typeFactory.String))
        {
            var arguments = customAttribute.Signature.FixedArguments;
            Debug.Assert(arguments.Count == 3);

            return new RenameData.MemberRename(
                (TypeSignature) arguments[0].Element!,
                (Utf8String) arguments[1].Element!,
                (Utf8String) arguments[2].Element!
            );
        }

        throw new ArgumentException($"Invalid signature for '{customAttribute}'.", nameof(customAttribute));

        bool CheckSignature(params ITypeDescriptor[] parameterTypes)
        {
            return SignatureComparer.Default.Equals(
                customAttribute.Constructor?.Signature?.ParameterTypes,
                parameterTypes.Select(t => t.ToTypeSignature())
            );
        }
    }
}

internal abstract record RenameData
{
    public sealed record NamespaceRename(Utf8String NamespaceName, Utf8String NewNamespaceName) : RenameData;

    public sealed record TypeRename(TypeSignature Type, Utf8String NewTypeName) : RenameData;

    public sealed record MemberRename(TypeSignature Type, Utf8String MemberName, Utf8String NewMemberName) : RenameData;
}
