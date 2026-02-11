#if !NET6_0_OR_GREATER
using InlineIL;
#endif
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public static unsafe class MemoryMarshalEx
    {
        extension(MemoryMarshal)
        {
            public static ref byte GetArrayDataReference(Array array)
            {
#if NET6_0_OR_GREATER
                return ref MemoryMarshal.GetArrayDataReference(array);
#else
                IL.DeclareLocals(false, new LocalVar("pinned", typeof(Array)).Pinned());
                IL.Push(array);
                IL.Emit.Stloc("pinned");
                return ref *(byte*)Marshal.UnsafeAddrOfPinnedArrayElement(array, 0);
#endif
            }

            public static ref T GetArrayDataReference<T>(T[] array)
            {
#if NET5_0_OR_GREATER
                return ref MemoryMarshal.GetArrayDataReference(array);
#else
                return ref Unsafe.As<byte, T>(ref GetArrayDataReference((Array)array));
#endif
            }

            public static ref T AsRef<T>(Span<byte> span) where T : struct
            {
#if NETCOREAPP3_0_OR_GREATER
                return ref MemoryMarshal.AsRef<T>(span);
#else
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    ThrowHelper.ThrowArgumentException_TypeContainsReferences(typeof(T));
                }
                if (span.Length < Unsafe.SizeOf<T>())
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);
                }
                return ref Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(span));
#endif
            }
            
            public static ref readonly T AsRef<T>(ReadOnlySpan<byte> span) where T : struct
            {
#if NETCOREAPP3_0_OR_GREATER
                return ref MemoryMarshal.AsRef<T>(span);
#else
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    ThrowHelper.ThrowArgumentException_TypeContainsReferences(typeof(T));
                }
                if (span.Length < Unsafe.SizeOf<T>())
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);
                }
                return ref Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(span));
#endif
            }

            public static ReadOnlySpan<byte> CreateReadOnlySpanFromNullTerminated(byte* value)
            {
#if NET6_0_OR_GREATER
                return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(value);
#else
                nint current = 0;
                while (value[current] != 0)
                {
                    current += 1;
                }

                if (current > int.MaxValue)
                {
                    ThrowHelper.ThrowArgumentException("Length would exceed int.MaxValue", nameof(value));
                }
                
                return new ReadOnlySpan<byte>(value, (int)current);
#endif
            }
            
            public static ReadOnlySpan<char> CreateReadOnlySpanFromNullTerminated(char* value)
            {
#if NET6_0_OR_GREATER
                return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(value);
#else
                nint current = 0;
                while (value[current] != '\0')
                {
                    current += 1;
                }

                if (current > int.MaxValue)
                {
                    ThrowHelper.ThrowArgumentException("Length would exceed int.MaxValue", nameof(value));
                }
                
                return new ReadOnlySpan<char>(value, (int)current);
#endif
            }
        }
    }
}
