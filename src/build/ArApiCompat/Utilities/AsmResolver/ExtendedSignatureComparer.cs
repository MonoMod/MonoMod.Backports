using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using System.Diagnostics.CodeAnalysis;

namespace ArApiCompat.Utilities.AsmResolver;

// TODO upstream?

/// <summary>
/// A <see cref="SignatureComparer"/> with added support for <see cref="IMemberDescriptor"/>, <see cref="PropertyDefinition"/> and <see cref="EventDefinition"/>.
/// </summary>
[SuppressMessage("Design", "CA1061:Do not hide base class methods", Justification = "Hidden base class methods eventually get called regardless.")]
internal sealed class ExtendedSignatureComparer : SignatureComparer,
    IEqualityComparer<IMemberDescriptor>,
    IEqualityComparer<PropertyDefinition>,
    IEqualityComparer<EventDefinition>
{
    public ExtendedSignatureComparer()
    {
    }

    public ExtendedSignatureComparer(SignatureComparisonFlags flags) : base(flags)
    {
    }

    public static new ExtendedSignatureComparer Default { get; } = new();
    public static ExtendedSignatureComparer VersionAgnostic { get; } = new(SignatureComparisonFlags.VersionAgnostic);


    public bool Equals(IMemberDescriptor? x, IMemberDescriptor? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;

        return x switch
        {
            ITypeDescriptor type => base.Equals(type, y as ITypeDescriptor),
            IMethodDescriptor method => base.Equals(method, y as IMethodDescriptor),
            IFieldDescriptor field => base.Equals(field, y as IFieldDescriptor),
            PropertyDefinition property => Equals(property, y as PropertyDefinition),
            EventDefinition @event => Equals(@event, y as EventDefinition),
            _ => false,
        };
    }

    public int GetHashCode(IMemberDescriptor obj)
    {
        return obj switch
        {
            ITypeDescriptor type => base.GetHashCode(type),
            IMethodDescriptor method => base.GetHashCode(method),
            IFieldDescriptor field => base.GetHashCode(field),
            PropertyDefinition property => GetHashCode(property),
            EventDefinition @event => GetHashCode(@event),
            _ => throw new ArgumentOutOfRangeException(nameof(obj)),
        };
    }

    public bool Equals(PropertyDefinition? x, PropertyDefinition? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;

        return x.Name == y.Name && Equals(x.DeclaringType, y.DeclaringType);
    }

    public int GetHashCode(PropertyDefinition obj)
    {
        return HashCode.Combine(
            obj.Name,
            obj.DeclaringType == null ? 0 : base.GetHashCode(obj.DeclaringType),
            obj.Signature == null ? 0 : base.GetHashCode(obj.Signature)
        );
    }

    public bool Equals(EventDefinition? x, EventDefinition? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;

        return x.Name == y.Name && base.Equals(x.DeclaringType, y.DeclaringType);
    }

    public int GetHashCode(EventDefinition obj)
    {
        return HashCode.Combine(
            obj.Name,
            obj.DeclaringType == null ? 0 : base.GetHashCode(obj.DeclaringType),
            obj.EventType == null ? 0 : base.GetHashCode(obj.EventType)
        );
    }

    protected override bool SimpleTypeEquals(ITypeDescriptor x, ITypeDescriptor y)
    {
        // Check the basic properties first.
        if (!x.IsTypeOf(y.Namespace, y.Name))
            return false;

        // If scope matches, it is a perfect match.
        if (Equals(x.Scope, y.Scope))
            return true;

        // It can still be an exported type, we need to resolve the type then and check if the definitions match.
        // For our purposes, we only actually care that the name matches
        return x.Resolve() is { } definition1
               && y.Resolve() is { } definition2
               && Equals(definition1.DeclaringType, definition2.DeclaringType);
    }

    protected override int SimpleTypeHashCode(ITypeDescriptor obj)
    {
        return HashCode.Combine(
            obj.Namespace,
            obj.Name,
            obj.DeclaringType is not null ? GetHashCode(obj.DeclaringType) : 0
        );
    }

}
