#if NET5_0_OR_GREATER
#define HAS_ASSPAN
#endif
#if NET6_0_OR_GREATER
#define HAS_GETVALUEREF
#endif
#if NET8_0_OR_GREATER
#define HAS_SETCOUNT
#endif

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

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
        
#if !HAS_GETVALUEREF
        private static class DictRelfectionHolder<TKey, TValue> where TKey : notnull
        {
            public delegate ref TValue? EntryValueFieldRefGetter(Dictionary<TKey, TValue> dict, TKey key);
            public static EntryValueFieldRefGetter GetEntryValueFieldRef;

            static DictRelfectionHolder()
            {
                var dictType = typeof(Dictionary<TKey, TValue>);

                var findValueMethod = dictType.GetMethod("FindValue", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [typeof(TKey)], null);

                if (findValueMethod is not null)
                {
                    GetEntryValueFieldRef = (EntryValueFieldRefGetter)Delegate.CreateDelegate(typeof(EntryValueFieldRefGetter), findValueMethod);
                    return;
                }
                
                var entriesField = dictType.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new NotSupportedException("Could not get dictionary entries array field");

                var entryType = entriesField.FieldType.GetElementType()!;
                var entryValueField = entryType.GetField("value",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
                
                var findEntryMethod = dictType.GetMethod("FindEntry", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new NotSupportedException("Could not get dictionary find entry method");
                
                var dm = new DynamicMethod("GetEntryValueFieldRef", typeof(TValue).MakeByRefType(), [dictType, typeof(TKey)], typeof(CollectionExtensionsEx), true);
                var il = dm.GetILGenerator();

                var entryIndex = il.DeclareLocal(typeof(int));
                var successLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Callvirt, findEntryMethod);
                il.Emit(OpCodes.Stloc, entryIndex);
                il.Emit(OpCodes.Ldloc, entryIndex);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Bge, successLabel);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_U);
                // im not taking any risks here
                il.Emit(OpCodes.Call, typeof(Unsafe).GetMethod("AsRef", [typeof(void*)])!.MakeGenericMethod(typeof(TValue)));
                il.Emit(OpCodes.Ret);
                il.MarkLabel(successLabel);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, entriesField);
                il.Emit(OpCodes.Ldloc, entryIndex);
                il.Emit(OpCodes.Ldelema, entriesField.FieldType.GetElementType()!);
                il.Emit(OpCodes.Ldflda, entryValueField);
                il.Emit(OpCodes.Ret);

                GetEntryValueFieldRef = (EntryValueFieldRefGetter)dm.CreateDelegate(typeof(EntryValueFieldRefGetter));
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
                    versionField.SetValue(list, (int)versionField.GetValue(list) + 1);
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

            public static ref TValue GetValueRefOrNullRef<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key)
                where TKey : notnull
            {
#if HAS_GETVALUEREF
                return ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
#else
                // they don't validate for null so neither will we
                return ref DictRelfectionHolder<TKey, TValue>.GetEntryValueFieldRef(dict, key)!;
#endif
            }
            
            public static ref TValue? GetValueRefOrAddDefault<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key)
                where TKey : notnull
            {
#if HAS_GETVALUEREF
                return ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key);
#else
                // they don't validate for null so neither will we
                if (dict.ContainsKey(key))
                {
                    return ref DictRelfectionHolder<TKey, TValue>.GetEntryValueFieldRef(dict, key);
                }

                dict.Add(key, default!);
                return  ref DictRelfectionHolder<TKey, TValue>.GetEntryValueFieldRef(dict, key);
#endif
            }
        }
    }
}