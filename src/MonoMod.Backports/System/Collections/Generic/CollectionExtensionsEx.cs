#if NET8_0_OR_GREATER
#define HAS_LISTSPANMETHODS
#endif
#if NET7_0_OR_GREATER
#define HAS_ASREADONLY
#endif

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
#if !HAS_LISTSPANMETHODS
using System.Runtime.InteropServices;
#endif

namespace System.Collections.Generic
{
    [SuppressMessage("Design", "CA1002:Do not expose generic lists",
        Justification = "Replicating existing APIs")]
    public static class CollectionExtensionsEx
    {
        public static void AddRange<T>(
#if !HAS_LISTSPANMETHODS
            this
#endif
                List<T> list, params ReadOnlySpan<T> source
        )
        {
#if HAS_LISTSPANMETHODS
            list.AddRange(source);
#else
            ThrowHelper.ThrowIfArgumentNull(list, ExceptionArgument.list);
            if (source.IsEmpty)
            {
                return;
            }
            var currentCount = list.Count;
            CollectionsMarshal.SetCount(list, currentCount + source.Length);
            source.CopyTo(CollectionsMarshal.AsSpan(list).Slice(currentCount + 1));
#endif
        }
        
        public static void InsertRange<T>(
#if !HAS_LISTSPANMETHODS
            this
#endif
                List<T> list, int index, params ReadOnlySpan<T> source
        )
        {
#if HAS_LISTSPANMETHODS
            list.InsertRange(index, source);
#else
            ThrowHelper.ThrowIfArgumentNull(list, ExceptionArgument.list);
            if ((uint)index > (uint)list.Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index);
            }
            if (source.IsEmpty)
            {
                return;
            }
            var currentCount = list.Count;
            CollectionsMarshal.SetCount(list, currentCount + source.Length);
            var items = CollectionsMarshal.AsSpan(list);
            if (index < currentCount)
            {
                items.Slice(index, currentCount - index).CopyTo(items.Slice(index + source.Length));
            }
            source.CopyTo(items.Slice(index));
#endif
        }
        
        public static void CopyTo<T>(
#if !HAS_LISTSPANMETHODS
            this
#endif
                List<T> list, Span<T> destination
        )
        {
#if HAS_LISTSPANMETHODS
            list.CopyTo(destination);
#else
            ThrowHelper.ThrowIfArgumentNull(list, ExceptionArgument.list);
            CollectionsMarshal.AsSpan(list).CopyTo(destination);
#endif
        }

        public static ReadOnlyCollection<T> AsReadOnly<T>(
#if !HAS_ASREADONLY
            this
#endif
                IList<T> list
            ) => new(list);
        
        public static ReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(
#if !HAS_ASREADONLY
            this
#endif
                IDictionary<TKey, TValue> list
        ) where TKey : notnull => new(list);
    }
}
