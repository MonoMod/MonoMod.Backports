#if NET9_0_OR_GREATER
#define RUNTIME_SUPPORTS_BY_REF_LIKE_GENERICS
#endif

#if !NETCOREAPP
// See docs/RuntimeIssueNotes.md. Until 2015, Mono returned incorrect values for the sizeof opcode when applied to a type parameter.
// To deal with this, we need to compute type size in another way, and return it as appropriate.
#define POSSIBLY_BROKEN_SIZEOF
#endif

using System.Diagnostics.CodeAnalysis;
using static InlineIL.IL;
using static InlineIL.IL.Emit;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Contains generic, low-level functionality for manipulating pointers.
    /// </summary>
    public static unsafe class Unsafe
    {
        /// <summary>
        /// Returns a pointer to the given by-ref parameter.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void* AsPointer<T>(ref T value)
        {
            Ldarg(nameof(value));
            Conv_U();
            return ReturnPointer();
        }

#if POSSIBLY_BROKEN_SIZEOF
        private static class PerTypeValues<T>
        {
            public static readonly nint TypeSize = ComputeTypeSize();

            private static nint ComputeTypeSize()
            {
                var array = new T[2];
                return ByteOffset(ref array[0], ref array[1]);
            }
        }
#endif

        /// <summary>
        /// Returns the size of an object of the given type parameter.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static int SizeOf<T>()
        {
#if POSSIBLY_BROKEN_SIZEOF
            return (int)PerTypeValues<T>.TypeSize;
#else
            Sizeof<T>();
            return Return<int>();
#endif
        }

        /// <summary>
        /// Casts the given object to the specified type, performs no dynamic type checking.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        [return: NotNullIfNotNull(nameof(o))]
        public static T As<T>(object? o) where T : class?
        {
            Ldarg(nameof(o));
            return Return<T>();
        }

        /// <summary>
        /// Reinterprets the given reference as a reference to a value of type <typeparamref name="TTo"/>.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref TTo As<TFrom, TTo>(ref TFrom source)
        {
            Ldarg(nameof(source));
            return ref ReturnRef<TTo>();
        }

        /// <summary>
        /// Adds an element offset to the given reference.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T Add<T>(ref T source, int elementOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(elementOffset));
#if POSSIBLY_BROKEN_SIZEOF
            Push(SizeOf<T>());
#else
            Sizeof<T>();
#endif
            Conv_I();
            Mul();
            Emit.Add();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Adds an element offset to the given reference.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T Add<T>(ref T source, IntPtr elementOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(elementOffset));
#if POSSIBLY_BROKEN_SIZEOF
            Push(SizeOf<T>());
#else
            Sizeof<T>();
#endif
            Mul();
            Emit.Add();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Adds an element offset to the given pointer.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void* Add<T>(void* source, int elementOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(elementOffset));
#if POSSIBLY_BROKEN_SIZEOF
            Push(SizeOf<T>());
#else
            Sizeof<T>();
#endif
            Conv_I();
            Mul();
            Emit.Add();
            return ReturnPointer();
        }

        /// <summary>
        /// Adds an element offset to the given reference.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T Add<T>(ref T source, nuint elementOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(elementOffset));
#if POSSIBLY_BROKEN_SIZEOF
            Push(SizeOf<T>());
#else
            Sizeof<T>();
#endif
            Mul();
            Emit.Add();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Adds an byte offset to the given reference.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T AddByteOffset<T>(ref T source, nuint byteOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(byteOffset));
            Emit.Add();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Determines whether the specified references point to the same location.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool AreSame<T>([AllowNull] ref T left, [AllowNull] ref T right)
        {
            Ldarg(nameof(left));
            Ldarg(nameof(right));
            Ceq();
            return Return<bool>();
        }

        /// <summary>
        /// Copies a value of type T to the given location.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void Copy<T>(void* destination, ref T source)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(source));
            Ldobj<T>();
            Stobj<T>();
            Ret();
        }

        /// <summary>
        /// Copies a value of type T to the given location.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void Copy<T>(ref T destination, void* source)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(source));
            Ldobj<T>();
            Stobj<T>();
            Ret();
        }

        /// <summary>
        /// Copies bytes from the source address to the destination address.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void CopyBlock(void* destination, void* source, uint byteCount)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(source));
            Ldarg(nameof(byteCount));
            Cpblk();
            Ret();
        }

        /// <summary>
        /// Copies bytes from the source address to the destination address.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void CopyBlock(ref byte destination, ref byte source, uint byteCount)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(source));
            Ldarg(nameof(byteCount));
            Cpblk();
            Ret();
        }

        /// <summary>
        /// Copies bytes from the source address to the destination address without assuming architecture dependent alignment of the addresses.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void CopyBlockUnaligned(void* destination, void* source, uint byteCount)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(source));
            Ldarg(nameof(byteCount));
            Unaligned(1);
            Cpblk();
            Ret();
        }

        /// <summary>
        /// Copies bytes from the source address to the destination address without assuming architecture dependent alignment of the addresses.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void CopyBlockUnaligned(ref byte destination, ref byte source, uint byteCount)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(source));
            Ldarg(nameof(byteCount));
            Unaligned(1);
            Cpblk();
            Ret();
        }

        /// <summary>
        /// Determines whether the memory address referenced by <paramref name="left"/> is greater than
        /// the memory address referenced by <paramref name="right"/>.
        /// </summary>
        /// <remarks>
        /// This check is conceptually similar to "(void*)(&amp;left) &gt; (void*)(&amp;right)".
        /// </remarks>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsAddressGreaterThan<T>([AllowNull] ref readonly T left, [AllowNull] ref readonly T right)
        {
            Ldarg(nameof(left));
            Ldarg(nameof(right));
            Cgt_Un();
            return Return<bool>();
        }

        /// <summary>
        /// Determines whether the memory address referenced by <paramref name="left"/> is less than
        /// the memory address referenced by <paramref name="right"/>.
        /// </summary>
        /// <remarks>
        /// This check is conceptually similar to "(void*)(&amp;left) &lt; (void*)(&amp;right)".
        /// </remarks>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsAddressLessThan<T>([AllowNull] ref readonly T left, [AllowNull] ref readonly T right)
        {
            Ldarg(nameof(left));
            Ldarg(nameof(right));
            Clt_Un();
            return Return<bool>();
        }

        /// <summary>
        /// Initializes a block of memory at the given location with a given initial value.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void InitBlock(void* startAddress, byte value, uint byteCount)
        {
            Ldarg(nameof(startAddress));
            Ldarg(nameof(value));
            Ldarg(nameof(byteCount));
            Initblk();
            Ret();
        }

        /// <summary>
        /// Initializes a block of memory at the given location with a given initial value.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void InitBlock(ref byte startAddress, byte value, uint byteCount)
        {
            Ldarg(nameof(startAddress));
            Ldarg(nameof(value));
            Ldarg(nameof(byteCount));
            Initblk();
            Ret();
        }

        /// <summary>
        /// Initializes a block of memory at the given location with a given initial value
        /// without assuming architecture dependent alignment of the address.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void InitBlockUnaligned(void* startAddress, byte value, uint byteCount)
        {
            Ldarg(nameof(startAddress));
            Ldarg(nameof(value));
            Ldarg(nameof(byteCount));
            Unaligned(1);
            Initblk();
            Ret();
        }

        /// <summary>
        /// Initializes a block of memory at the given location with a given initial value
        /// without assuming architecture dependent alignment of the address.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void InitBlockUnaligned(ref byte startAddress, byte value, uint byteCount)
        {
            Ldarg(nameof(startAddress));
            Ldarg(nameof(value));
            Ldarg(nameof(byteCount));
            Unaligned(1);
            Initblk();
            Ret();
        }

        /// <summary>
        /// Reads a value of type <typeparamref name="T"/> from the given location.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static T ReadUnaligned<T>(void* source)
        {
            Ldarg(nameof(source));
            Unaligned(1);
            Ldobj<T>();
            return Return<T>();
        }

        /// <summary>
        /// Reads a value of type <typeparamref name="T"/> from the given location.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static T ReadUnaligned<T>(ref byte source)
        {
            Ldarg(nameof(source));
            Unaligned(1);
            Ldobj<T>();
            return Return<T>();
        }

        /// <summary>
        /// Writes a value of type <typeparamref name="T"/> to the given location.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void WriteUnaligned<T>(void* destination, T value)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(value));
            Unaligned(1);
            Stobj<T>();
            Ret();
        }

        /// <summary>
        /// Writes a value of type <typeparamref name="T"/> to the given location.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void WriteUnaligned<T>(ref byte destination, T value)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(value));
            Unaligned(1);
            Stobj<T>();
            Ret();
        }

        /// <summary>
        /// Adds an byte offset to the given reference.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T AddByteOffset<T>(ref T source, IntPtr byteOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(byteOffset));
            Emit.Add();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Reads a value of type <typeparamref name="T"/> from the given location.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static T Read<T>(void* source)
        {
            Ldarg(nameof(source));
            Ldobj<T>();
            return Return<T>();
        }

        /// <summary>
        /// Writes a value of type <typeparamref name="T"/> to the given location.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void Write<T>(void* destination, T value)
        {
            Ldarg(nameof(destination));
            Ldarg(nameof(value));
            Stobj<T>();
            Ret();
        }

        /// <summary>
        /// Reinterprets the given location as a reference to a value of type <typeparamref name="T"/>.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T AsRef<T>(void* source)
        {
#if NETCOREAPP
            // For .NET Core the roundtrip via a local is no longer needed
            Ldarg(nameof(source));
#else
            // Roundtrip via a local to avoid type mismatch on return that the JIT inliner chokes on
            DeclareLocals(init: false, typeof(int).MakeByRefType());
            Ldarg(nameof(source));
            Stloc_0();
            Ldloc_0();
#endif

            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Reinterprets the given location as a reference to a value of type <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>The lifetime of the reference will not be validated when using this API.</remarks>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T AsRef<T>(scoped ref readonly T source)
        {
            Ldarg(nameof(source));
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Determines the byte offset from origin to target from the given references.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static IntPtr ByteOffset<T>([AllowNull] ref T origin, [AllowNull] ref T target)
        {
            Ldarg(nameof(target));
            Ldarg(nameof(origin));
            Sub();
            return Return<IntPtr>();
        }

        /// <summary>
        /// Returns a by-ref to type <typeparamref name="T"/> that is a null reference.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T NullRef<T>()
        {
            Ldc_I4_0();
            Conv_U();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Returns if a given by-ref to type <typeparamref name="T"/> is a null reference.
        /// </summary>
        /// <remarks>
        /// This check is conceptually similar to "(void*)(&amp;source) == nullptr".
        /// </remarks>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsNullRef<T>(ref readonly T source)
        {
            Ldarg(nameof(source));
            Ldc_I4_0();
            Conv_U();
            Ceq();
            return Return<bool>();
        }

        /// <summary>
        /// Bypasses definite assignment rules by taking advantage of <c>out</c> semantics.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void SkipInit<T>(out T value)
        {
            Ret();
            throw Unreachable();
        }

        /// <summary>
        /// Subtracts an element offset from the given reference.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T Subtract<T>(ref T source, int elementOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(elementOffset));
#if POSSIBLY_BROKEN_SIZEOF
            Push(SizeOf<T>());
#else
            Sizeof<T>();
#endif
            Conv_I();
            Mul();
            Sub();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Subtracts an element offset from the given void pointer.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void* Subtract<T>(void* source, int elementOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(elementOffset));
#if POSSIBLY_BROKEN_SIZEOF
            Push(SizeOf<T>());
#else
            Sizeof<T>();
#endif
            Conv_I();
            Mul();
            Sub();
            return ReturnPointer();
        }

        /// <summary>
        /// Subtracts an element offset from the given reference.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T Subtract<T>(ref T source, IntPtr elementOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(elementOffset));
#if POSSIBLY_BROKEN_SIZEOF
            Push(SizeOf<T>());
#else
            Sizeof<T>();
#endif
            Mul();
            Sub();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Subtracts an element offset from the given reference.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T Subtract<T>(ref T source, nuint elementOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(elementOffset));
#if POSSIBLY_BROKEN_SIZEOF
            Push(SizeOf<T>());
#else
            Sizeof<T>();
#endif
            Mul();
            Sub();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Subtracts a byte offset from the given reference.
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T SubtractByteOffset<T>(ref T source, IntPtr byteOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(byteOffset));
            Sub();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Subtracts a byte offset from the given reference.
        /// </summary>
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T SubtractByteOffset<T>(ref T source, nuint byteOffset)
        {
            Ldarg(nameof(source));
            Ldarg(nameof(byteOffset));
            Sub();
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Returns a mutable ref to a boxed value
        /// </summary>
        [NonVersionable]
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ref T Unbox<T>(object box)
            where T : struct
        {
            Ldarg(nameof(box));
            Emit.Unbox<T>();
            return ref ReturnRef<T>();
        }
    }
}
