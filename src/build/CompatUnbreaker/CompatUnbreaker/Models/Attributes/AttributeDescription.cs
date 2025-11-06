using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace CompatUnbreaker.Models.Attributes;

internal static class AttributeDescription
{
    private static Utf8String CompatUnbreakerAttributesNamespace { get; } = "CompatUnbreaker.Attributes"u8;

    public static MarkerAttributeDescription UnbreakerConstructorAttribute { get; } = new(CompatUnbreakerAttributesNamespace, "UnbreakerConstructorAttribute"u8);
    public static SingleValueAttributeDescription<TypeSignature> UnbreakerExtensionAttribute { get; } = new(CompatUnbreakerAttributesNamespace, "UnbreakerExtensionAttribute"u8);
    public static MarkerAttributeDescription UnbreakerExtensionsAttribute { get; } = new(CompatUnbreakerAttributesNamespace, "UnbreakerExtensionsAttribute"u8);

    public static MarkerAttributeDescription UnbreakerFieldAttribute { get; } = new(CompatUnbreakerAttributesNamespace, "UnbreakerFieldAttribute"u8);

    public static UnbreakerRenameAttributeDescription UnbreakerRenameAttribute { get; } = new(CompatUnbreakerAttributesNamespace, "UnbreakerRenameAttribute"u8);
    public static SingleValueAttributeDescription<TypeSignature> UnbreakerReplaceAttribute { get; } = new(CompatUnbreakerAttributesNamespace, "UnbreakerReplaceAttribute"u8);
    public static SingleValueAttributeDescription<Utf8String> UnbreakerShimAttribute { get; } = new(CompatUnbreakerAttributesNamespace, "UnbreakerShimAttribute"u8);
}

internal abstract class AttributeDescription<TData>(Utf8String @namespace, Utf8String name)
{
    public Utf8String Namespace { get; } = @namespace;

    public Utf8String Name { get; } = name;

    public abstract TData CreateData(CustomAttribute customAttribute);
}

internal sealed class MarkerAttributeDescription(Utf8String @namespace, Utf8String name) : AttributeDescription<CustomAttribute>(@namespace, name)
{
    public override CustomAttribute CreateData(CustomAttribute customAttribute) => customAttribute;
}

internal sealed class SingleValueAttributeDescription<T>(Utf8String @namespace, Utf8String name) : AttributeDescription<T>(@namespace, name)
{
    public override T CreateData(CustomAttribute customAttribute)
    {
        ArgumentNullException.ThrowIfNull(customAttribute.Signature);

        var argument = customAttribute.Signature.FixedArguments.Single();
        var value = argument.ArgumentType.ElementType == ElementType.SzArray
            ? argument.Elements
            : argument.Element;

        return (T) value!;
    }
}
