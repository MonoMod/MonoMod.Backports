// https://github.com/dotnet/roslyn/blob/c8b5f306d86bc04c59a413ad17b6152663a1e744/src/Compilers/Core/Portable/Symbols/Accessibility.cs

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace CompatUnbreaker.Utilities.AsmResolver;

internal enum Accessibility
{
    /// <summary>
    /// No accessibility specified.
    /// </summary>
    NotApplicable = 0,

    // DO NOT CHANGE ORDER OF THESE ENUM VALUES
    Private = 1,

    /// <summary>
    /// Only accessible where both protected and internal members are accessible
    /// (more restrictive than <see cref="Protected"/>, <see cref="Internal"/> and <see cref="ProtectedOrInternal"/>).
    /// </summary>
    ProtectedAndInternal = 2,

    /// <summary>
    /// Only accessible where both protected and friend members are accessible
    /// (more restrictive than <see cref="Protected"/>, <see cref="Friend"/> and <see cref="ProtectedOrFriend"/>).
    /// </summary>
    ProtectedAndFriend = ProtectedAndInternal,

    Protected = 3,

    Internal = 4,
    Friend = Internal,

    /// <summary>
    /// Accessible wherever either protected or internal members are accessible
    /// (less restrictive than <see cref="Protected"/>, <see cref="Internal"/> and <see cref="ProtectedAndInternal"/>).
    /// </summary>
    ProtectedOrInternal = 5,

    /// <summary>
    /// Accessible wherever either protected or friend members are accessible
    /// (less restrictive than <see cref="Protected"/>, <see cref="Friend"/> and <see cref="ProtectedAndFriend"/>).
    /// </summary>
    ProtectedOrFriend = ProtectedOrInternal,

    Public = 6,
}
