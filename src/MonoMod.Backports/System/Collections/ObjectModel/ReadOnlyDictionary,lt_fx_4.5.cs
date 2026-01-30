using System.Collections.Generic;

namespace System.Collections.ObjectModel
{
    public class ReadOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary where TKey : notnull
    {
        private readonly IDictionary<TKey, TValue> m_dictionary; // Do not rename (binary serialization)

        private KeyCollection? _keys;
        private ValueCollection? _values;

        public ReadOnlyDictionary(IDictionary<TKey, TValue> dictionary)
        {
            ArgumentNullException.ThrowIfNull(dictionary);

            m_dictionary = dictionary;
        }

        /// <summary>Gets an empty <see cref="ReadOnlyDictionary{TKey, TValue}"/>.</summary>
        /// <value>An empty <see cref="ReadOnlyDictionary{TKey, TValue}"/>.</value>
        /// <remarks>The returned instance is immutable and will always be empty.</remarks>
        public static ReadOnlyDictionary<TKey, TValue> Empty { get; } = new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>());

        protected IDictionary<TKey, TValue> Dictionary => m_dictionary;

        public KeyCollection Keys => _keys ??= new KeyCollection(m_dictionary.Keys);

        public ValueCollection Values => _values ??= new ValueCollection(m_dictionary.Values);

        public bool ContainsKey(TKey key) => m_dictionary.ContainsKey(key);

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;

        public bool TryGetValue(TKey key, out TValue value)
        {
            return m_dictionary.TryGetValue(key, out value!);
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;

        public TValue this[TKey key] => m_dictionary[key];

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
        {
            throw new NotSupportedException();
        }

        bool IDictionary<TKey, TValue>.Remove(TKey key)
        {
            throw new NotSupportedException();
        }

        TValue IDictionary<TKey, TValue>.this[TKey key]
        {
            get => m_dictionary[key];
            set => throw new NotSupportedException();
        }

        public int Count => m_dictionary.Count;

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            return m_dictionary.Contains(item);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            m_dictionary.CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            throw new NotSupportedException();
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Clear()
        {
            throw new NotSupportedException();
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            throw new NotSupportedException();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return m_dictionary.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)m_dictionary).GetEnumerator();
        }

        private static bool IsCompatibleKey(object key)
        {
            ArgumentNullException.ThrowIfNull(key);

            return key is TKey;
        }

        void IDictionary.Add(object key, object? value)
        {
            throw new NotSupportedException();
        }

        void IDictionary.Clear()
        {
            throw new NotSupportedException();
        }

        bool IDictionary.Contains(object key)
        {
            return IsCompatibleKey(key) && ContainsKey((TKey)key);
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            if (m_dictionary is IDictionary d)
            {
                return d.GetEnumerator();
            }
            return new DictionaryEnumerator(m_dictionary);
        }

        bool IDictionary.IsFixedSize => true;

        bool IDictionary.IsReadOnly => true;

        ICollection IDictionary.Keys => Keys;

        void IDictionary.Remove(object key)
        {
            throw new NotSupportedException();
        }

        ICollection IDictionary.Values => Values;

        object? IDictionary.this[object key]
        {
            get
            {
                if (!IsCompatibleKey(key))
                {
                    return null;
                }

                if (m_dictionary.TryGetValue((TKey)key, out TValue? value))
                {
                    return value;
                }
                else
                {
                    return null;
                }
            }
            set => throw new NotSupportedException();
        }

        void ICollection.CopyTo(Array array, int index)
        {
            CollectionHelpers.ValidateCopyToArguments(Count, array, index);

            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                m_dictionary.CopyTo(pairs, index);
            }
            else
            {
                if (array is DictionaryEntry[] dictEntryArray)
                {
                    foreach (var item in m_dictionary)
                    {
                        dictEntryArray[index++] = new DictionaryEntry(item.Key, item.Value);
                    }
                }
                else
                {
                    object[] objects = array as object[] ?? throw new ArgumentException("Incompatible array type", nameof(array));
                    try
                    {
                        foreach (var item in m_dictionary)
                        {
                            objects[index++] = new KeyValuePair<TKey, TValue>(item.Key, item.Value);
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        throw new ArgumentException("Incompatible array type", nameof(array));
                    }
                }
            }
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => (m_dictionary is ICollection coll) ? coll.SyncRoot : this;

        private readonly struct DictionaryEnumerator : IDictionaryEnumerator
        {
            private readonly IDictionary<TKey, TValue> _dictionary;
            private readonly IEnumerator<KeyValuePair<TKey, TValue>> _enumerator;

            public DictionaryEnumerator(IDictionary<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _enumerator = _dictionary.GetEnumerator();
            }

            public DictionaryEntry Entry
            {
                get => new DictionaryEntry(_enumerator.Current.Key, _enumerator.Current.Value);
            }

            public object Key => _enumerator.Current.Key;

            public object? Value => _enumerator.Current.Value;

            public object Current => Entry;

            public bool MoveNext() => _enumerator.MoveNext();

            public void Reset() => _enumerator.Reset();
        }

        public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly ICollection<TKey> _collection;

            internal KeyCollection(ICollection<TKey> collection)
            {
                ArgumentNullException.ThrowIfNull(collection);

                _collection = collection;
            }

            void ICollection<TKey>.Add(TKey item)
            {
                throw new NotSupportedException();
            }

            void ICollection<TKey>.Clear()
            {
                throw new NotSupportedException();
            }

            public bool Contains(TKey item)
            {
                return _collection.Contains(item);
            }

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                _collection.CopyTo(array, arrayIndex);
            }

            public int Count => _collection.Count;

            bool ICollection<TKey>.IsReadOnly => true;

            bool ICollection<TKey>.Remove(TKey item)
            {
                throw new NotSupportedException();
            }

            public IEnumerator<TKey> GetEnumerator() => _collection.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_collection).GetEnumerator();

            void ICollection.CopyTo(Array array, int index)
            {
                CollectionHelpers.CopyTo(_collection, array, index);
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => (_collection is ICollection coll) ? coll.SyncRoot : this;
        }

        public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
        {
            private readonly ICollection<TValue> _collection;

            internal ValueCollection(ICollection<TValue> collection)
            {
                ArgumentNullException.ThrowIfNull(collection);

                _collection = collection;
            }

            void ICollection<TValue>.Add(TValue item)
            {
                throw new NotSupportedException();
            }

            void ICollection<TValue>.Clear()
            {
                throw new NotSupportedException();
            }

            bool ICollection<TValue>.Contains(TValue item) => _collection.Contains(item);

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                _collection.CopyTo(array, arrayIndex);
            }

            public int Count => _collection.Count;

            bool ICollection<TValue>.IsReadOnly => true;

            bool ICollection<TValue>.Remove(TValue item)
            {
                throw new NotSupportedException();
            }
            public IEnumerator<TValue> GetEnumerator() => _collection.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_collection).GetEnumerator();

            void ICollection.CopyTo(Array array, int index)
            {
                CollectionHelpers.CopyTo(_collection, array, index);
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => (_collection is ICollection coll) ? coll.SyncRoot : this;
        }
    }
    
        internal static class CollectionHelpers
    {
        internal static void ValidateCopyToArguments(int sourceCount, Array array, int index)
        {
#if NET
            ArgumentNullException.ThrowIfNull(array);
#else
            ArgumentNullException.ThrowIfNull(array);
#endif
 
            if (array.Rank != 1)
            {
                throw new ArgumentException("Multidimensional arrays are not supported", nameof(array));
            }
 
            if (array.GetLowerBound(0) != 0)
            {
                throw new ArgumentException("Non-zero lower bound", nameof(array));
            }
 
#if NET
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, array.Length);
#else
            if (index < 0 || index > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
#endif
 
            if (array.Length - index < sourceCount)
            {
                throw new ArgumentException("Array is too small for the index given");
            }
        }
 
        internal static void CopyTo<T>(ICollection<T> collection, Array array, int index)
        {
            ValidateCopyToArguments(collection.Count, array, index);
 
            if (collection is ICollection nonGenericCollection)
            {
                // Easy out if the ICollection<T> implements the non-generic ICollection
                nonGenericCollection.CopyTo(array, index);
            }
            else if (array is T[] items)
            {
                collection.CopyTo(items, index);
            }
            else
            {
                // We can't cast array of value type to object[], so we don't support widening of primitive types here.
                if (array is not object?[] objects)
                {
                    throw new ArgumentException("Incompatible array type", nameof(array));
                }
 
                try
                {
                    foreach (T item in collection)
                    {
                        objects[index++] = item;
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    throw new ArgumentException("Incompatible array type", nameof(array));
                }
            }
        }
    }
}
