#if NET5_0_OR_GREATER
#define HAS_ASSPAN
#endif
#if NET8_0_OR_GREATER
#define HAS_SETCOUNT
#endif

using System.Collections.Generic;
using System.Reflection;

namespace System.Runtime.InteropServices
{
    public static unsafe class CollectionsMarshalEx
    {
#if !HAS_SETCOUNT
        internal static class ListFieldHolder<T>
        {
#if !HAS_ASSPAN
            public static FieldInfo ItemsField;
#endif
            public static FieldInfo CountField;
            public static FieldInfo? VersionField;

            static ListFieldHolder()
            {
                var t = typeof(List<T>);

#if !HAS_ASSPAN
                ItemsField = t.GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)
                             ?? throw new NotSupportedException("Could not get List items field");
#endif
                CountField = t.GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic)
                             ?? throw new NotSupportedException("Could not get List count field");
                VersionField = t.GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic);
            }
        }
#endif

        extension(CollectionsMarshal)
        {
            public static void SetCount<T>(List<T> list, int count)
            {
#if HAS_SETCOUNT
                CollectionsMarshal.SetCount(list, count);
#else
                ArgumentNullException.ThrowIfNull(list);
                if (count < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                // setting the version field only really needs to be best effort
                if (ListFieldHolder<T>.VersionField is { } versionField)
                {
                    versionField.SetValue(list, (int)versionField.GetValue(list)! + 1);
                }

                if (count > list.Capacity)
                {
                    // taken from List<T>.EnsureCapacity
                    var newCapacity = list.Capacity == 0 ? 4 : 2 * list.Capacity;
 
                    // Allow the list to grow to maximum possible capacity (~2G elements) before encountering overflow.
                    // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
                    if ((uint)newCapacity > Array.MaxLength)
                    {
                        newCapacity = Array.MaxLength;
                    }

                    // If the computed capacity is still less than specified, set to the original argument.
                    // Capacities exceeding Array.MaxLength will be surfaced as OutOfMemoryException by Array.Resize.
                    if (newCapacity < count)
                    {
                        newCapacity = count;
                    }

                    list.Capacity = newCapacity;
                }

                // TODO: IsReferenceOrContainsReferences
                if (count < list.Count)
                {
                    CollectionsMarshal.AsSpan(list).Slice(count + 1).Clear();
                }
                
                ListFieldHolder<T>.CountField.SetValue(list, count);
#endif
            }
        }
    }
}