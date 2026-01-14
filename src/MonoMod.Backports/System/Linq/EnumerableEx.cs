using System.Collections.Generic;

namespace System.Linq
{
    public static class EnumerableEx
    {
        public static IEnumerable<TSource> Reverse<TSource>(
#if !NET_10_OR_GREATER
            this
#endif
                TSource[] source) => Enumerable.Reverse(source);
    }
}
