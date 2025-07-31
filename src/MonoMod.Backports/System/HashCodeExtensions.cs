#if !NET6_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#endif

namespace System
{
    public static class HashCodeExtensions
    {
        /// <summary>Adds a span of bytes to the hash code.</summary>
        /// <param name="self">The <see cref="HashCode"/> instance to operate on.</param>
        /// <param name="value">The span.</param>
        /// <remarks>
        /// This method does not guarantee that the result of adding a span of bytes will match
        /// the result of adding the same bytes individually.
        /// </remarks>
        public static void AddBytes(this ref HashCode self, ReadOnlySpan<byte> value)
        {
#if NET6_0_OR_GREATER
            self.AddBytes(value);
#else
            // this impl would normally be in HashCode, but we can use it for all instances of HashCode because it doesn't depend on internals

            ref byte pos = ref MemoryMarshal.GetReference(value);
            ref byte end = ref Unsafe.Add(ref pos, value.Length);

            // Add four bytes at a time until the input has fewer than four bytes remaining.
            while ((nint)Unsafe.ByteOffset(ref pos, ref end) >= sizeof(int))
            {
                self.Add(Unsafe.ReadUnaligned<int>(ref pos));
                pos = ref Unsafe.Add(ref pos, sizeof(int));
            }

            // Add the remaining bytes a single byte at a time.
            while (Unsafe.IsAddressLessThan(ref pos, ref end))
            {
                self.Add((int)pos);
                pos = ref Unsafe.Add(ref pos, 1);
            }
#endif
        }
    }
}
