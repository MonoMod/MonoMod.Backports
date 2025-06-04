// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;

#pragma warning disable CA1051 // The BCL declares visible instance fields.

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved for use by a compiler for tracking metadata.
    /// This attribute should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter, Inherited = false)]
    public sealed class NullableAttribute : Attribute
    {
        /// <summary>Flags specifying metadata related to nullable reference types.</summary>
        public readonly byte[] NullableFlags;

        /// <summary>Initializes the attribute.</summary>
        /// <param name="value">The flags value.</param>
        public NullableAttribute(byte value)
        {
            NullableFlags = [value];
        }

        /// <summary>Initializes the attribute.</summary>
        /// <param name="value">The flags value.</param>
        public NullableAttribute(byte[] value)
        {
            NullableFlags = value;
        }
    }

    /// <summary>
    /// Reserved for use by a compiler for tracking metadata.
    /// This attribute should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
    public sealed class NullableContextAttribute : Attribute
    {
        /// <summary>Flag specifying metadata related to nullable reference types.</summary>
        public readonly byte Flag;

        /// <summary>Initializes the attribute.</summary>
        /// <param name="value">The flag value.</param>
        public NullableContextAttribute(byte value)
        {
            Flag = value;
        }
    }

    /// <summary>
    /// Reserved for use by a compiler for tracking metadata.
    /// This attribute should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Module, Inherited = false)]
    public sealed class NullablePublicOnlyAttribute : Attribute
    {
        /// <summary>Indicates whether metadata for internal members is included.</summary>
        public readonly bool IncludesInternals;

        /// <summary>Initializes the attribute.</summary>
        /// <param name="value">Indicates whether metadata for internal members is included.</param>
        public NullablePublicOnlyAttribute(bool value)
        {
            IncludesInternals = value;
        }
    }

    /// <summary>
    /// Reserved for use by a compiler for tracking metadata.
    /// This attribute should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    public sealed class IsReadOnlyAttribute : Attribute
    {
        /// <summary>Initializes the attribute.</summary>
        public IsReadOnlyAttribute()
        {
        }
    }

    /// <summary>
    /// Reserved for use by a compiler for tracking metadata.
    /// This attribute should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class IsByRefLikeAttribute : Attribute
    {
        /// <summary>Initializes the attribute.</summary>
        public IsByRefLikeAttribute()
        {
        }
    }

    /// <summary>
    /// Reserved for use by a compiler for tracking metadata.
    /// This attribute should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.All)]
    public sealed class IsUnmanagedAttribute : Attribute
    {
        /// <summary>Initializes the attribute.</summary>
        public IsUnmanagedAttribute()
        {
        }
    }

    /// <summary>
    /// Reserved for use by a compiler for tracking metadata.
    /// This attribute should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class ScopedRefAttribute : Attribute
    {
        /// <summary>Initializes the attribute.</summary>
        public ScopedRefAttribute()
        {
        }
    }

    /// <summary>Indicates the language version of the ref safety rules used when the module was compiled.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Module, Inherited = false)]
    public sealed class RefSafetyRulesAttribute : Attribute
    {
        /// <summary>Initializes a new instance of the <see cref="RefSafetyRulesAttribute"/> class.</summary>
        /// <param name="version">The language version of the ref safety rules used when the module was compiled.</param>
        public RefSafetyRulesAttribute(int version) => Version = version;

        /// <summary>Gets the language version of the ref safety rules used when the module was compiled.</summary>
        public int Version { get; }
    }
}