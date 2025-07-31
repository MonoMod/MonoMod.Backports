#if NET9_0_OR_GREATER
#define RUNTIME_SUPPORTS_BY_REF_LIKE_GENERICS
#endif

using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Extensions to <see cref="Unsafe"/> providing consistent access to APIs introduced after the type.
    /// </summary>
    public static unsafe class UnsafeEx
    {
        extension(Unsafe)
        {
            /// <summary>
            /// Reinterprets the given value of type <typeparamref name="TFrom" /> as a value of type <typeparamref name="TTo" />.
            /// </summary>
            /// <exception cref="NotSupportedException">The sizes of <typeparamref name="TFrom" /> and <typeparamref name="TTo" /> are not the same
            /// or the type parameters are not <see langword="struct"/>s.</exception>
            [NonVersionable]
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public static TTo BitCast<TFrom, TTo>(TFrom source)
#if RUNTIME_SUPPORTS_BY_REF_LIKE_GENERICS
                where TFrom : allows ref struct
                where TTo : allows ref struct
#endif
            {
#if NET9_0_OR_GREATER
                return Unsafe.BitCast<TFrom, TTo>(source);
#else
                if (Unsafe.SizeOf<TFrom>() != Unsafe.SizeOf<TTo>() || default(TFrom) is null || default(TTo) is null)
                {
                    ThrowHelper.ThrowNotSupportedException();
                }

                return Unsafe.ReadUnaligned<TTo>(ref Unsafe.As<TFrom, byte>(ref source));
#endif
            }

            #region "ref readonly" overloads

            /// <inheritdoc cref="System.Runtime.CompilerServices.Unsafe.AreSame{T}"/>
            [NonVersionable]
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public static bool AreSame<T>([AllowNull] ref readonly T left, [AllowNull] ref readonly T right)
#if RUNTIME_SUPPORTS_BY_REF_LIKE_GENERICS
                where T : allows ref struct
#endif
            {
#if NET8_0_OR_GREATER
                return Unsafe.AreSame(in left, in right);
#else
                return Unsafe.AreSame(ref Unsafe.AsRef<T>(in left!), ref Unsafe.AsRef<T>(in right!));
#endif
            }

            /// <inheritdoc cref="System.Runtime.CompilerServices.Unsafe.ByteOffset{T}"/>
            [NonVersionable]
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public static IntPtr ByteOffset<T>([AllowNull] ref readonly T origin, [AllowNull] ref readonly T target)
#if RUNTIME_SUPPORTS_BY_REF_LIKE_GENERICS
                where T : allows ref struct
#endif
            {
#if NET8_0_OR_GREATER
                return Unsafe.ByteOffset(in origin, in target);
#else
                return Unsafe.ByteOffset(ref Unsafe.AsRef<T>(in origin!), ref Unsafe.AsRef<T>(in target!));
#endif
            }

            /// <inheritdoc cref="M:System.Runtime.CompilerServices.Unsafe.Copy``1(System.Void*, ``0@)"/>
            [NonVersionable]
            [CLSCompliant(false)]
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public static void Copy<T>(void* destination, ref readonly T source)
#if RUNTIME_SUPPORTS_BY_REF_LIKE_GENERICS
                where T : allows ref struct
#endif
            {
#if NET8_0_OR_GREATER
                Unsafe.Copy(destination, in source);
#else
                Unsafe.Copy(destination, ref Unsafe.AsRef(in source));
#endif
            }

            /// <inheritdoc cref="M:System.Runtime.CompilerServices.Unsafe.CopyBlock(System.Byte@, System.Byte@, System.UInt32)"/>
            [NonVersionable]
            [CLSCompliant(false)]
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public static void CopyBlock(ref byte destination, ref readonly byte source, uint byteCount)
            {
#if NET8_0_OR_GREATER
                Unsafe.CopyBlock(ref destination, in source, byteCount);
#else
                Unsafe.CopyBlock(ref destination, ref Unsafe.AsRef(in source), byteCount);
#endif
            }

            /// <inheritdoc cref="M:System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(System.Byte@, System.Byte@, System.UInt32)"/>
            [NonVersionable]
            [CLSCompliant(false)]
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public static void CopyBlockUnaligned(ref byte destination, ref readonly byte source, uint byteCount)
            {
#if NET8_0_OR_GREATER
                Unsafe.CopyBlockUnaligned(ref destination, in source, byteCount);
#else
                Unsafe.CopyBlockUnaligned(ref destination, ref Unsafe.AsRef(in source), byteCount);
#endif
            }

            /// <inheritdoc cref="M:System.Runtime.CompilerServices.Unsafe.ReadUnaligned``1(System.Byte@)"/>
            [NonVersionable]
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public static T ReadUnaligned<T>(scoped ref readonly byte source)
#if RUNTIME_SUPPORTS_BY_REF_LIKE_GENERICS
                where T : allows ref struct
#endif
            {
#if NET8_0_OR_GREATER
                return Unsafe.ReadUnaligned<T>(in source);
#else
                return Unsafe.ReadUnaligned<T>(ref Unsafe.AsRef(in source));
#endif
            }

            #endregion
        }
    }
}
