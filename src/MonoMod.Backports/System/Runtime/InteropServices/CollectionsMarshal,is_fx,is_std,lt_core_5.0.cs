using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public static class CollectionsMarshal
    {
        public static Span<T> AsSpan<T>(List<T>? list)
        {
            if (list is null)
            {
                return Span<T>.Empty;
            }

            return Unsafe.As<T[]>(CollectionsMarshalEx.ListFieldHolder<T>.ItemsField.GetValue(list));
        }
    }
}