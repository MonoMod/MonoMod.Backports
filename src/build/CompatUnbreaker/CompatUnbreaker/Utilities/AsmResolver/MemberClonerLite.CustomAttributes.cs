using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal readonly partial struct MemberClonerLite
{
    private void CloneCustomAttributes(IHasCustomAttribute sourceProvider, IHasCustomAttribute clonedProvider)
    {
        foreach (var attribute in sourceProvider.CustomAttributes)
            clonedProvider.CustomAttributes.Add(CloneCustomAttribute(attribute));
    }

    private CustomAttribute CloneCustomAttribute(CustomAttribute attribute)
    {
        var clonedSignature = new CustomAttributeSignature();

        if (attribute.Signature is not null)
        {
            // Fixed args.
            foreach (var argument in attribute.Signature.FixedArguments)
                clonedSignature.FixedArguments.Add(CloneCustomAttributeArgument(argument));

            // Named args.
            foreach (var namedArgument in attribute.Signature.NamedArguments)
            {
                var clonedArgument = new CustomAttributeNamedArgument(
                    namedArgument.MemberType,
                    namedArgument.MemberName,
                    namedArgument.ArgumentType,
                    CloneCustomAttributeArgument(namedArgument.Argument));

                clonedSignature.NamedArguments.Add(clonedArgument);
            }
        }

        var constructor = attribute.Constructor;
        if (constructor is null)
        {
            throw new ArgumentException(
                $"Custom attribute of {attribute.Parent} does not have a constructor defined.");
        }

        return new CustomAttribute((ICustomAttributeType) constructor.ImportWith(_importer), clonedSignature);
    }

    private CustomAttributeArgument CloneCustomAttributeArgument(CustomAttributeArgument argument)
    {
        var clonedArgument = new CustomAttributeArgument(argument.ArgumentType.ImportWith(_importer))
        {
            IsNullArray = argument.IsNullArray,
        };

        // Copy all elements.
        for (var i = 0; i < argument.Elements.Count; i++)
            clonedArgument.Elements.Add(CloneElement(argument.Elements[i]));

        return clonedArgument;
    }

    private object? CloneElement(object? element)
    {
        if (element is TypeSignature type)
            return type.ImportWith(_importer);

        return element;
    }
}
