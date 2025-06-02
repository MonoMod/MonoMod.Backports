// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// =+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// PartitionerStatic.cs
//
// A class of default partitioners for Partitioner<TSource>
//
// =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace System.Collections.Concurrent
{
    // The static class Partitioners implements 3 default partitioning strategies:
    // 1. dynamic load balance partitioning for indexable data source (IList and arrays)
    // 2. static partitioning for indexable data source (IList and arrays)
    // 3. dynamic load balance partitioning for enumerables. Enumerables have indexes, which are the natural order
    //    of elements, but enumerators are not indexable
    // - data source of type IList/arrays have both dynamic and static partitioning, as 1 and 3.
    //   We assume that the source data of IList/Array is not changing concurrently.
    // - data source of type IEnumerable can only be partitioned dynamically (load-balance)
    // - Dynamic partitioning methods 1 and 3 are same, both being dynamic and load-balance. But the
    //   implementation is different for data source of IList/Array vs. IEnumerable:
    //   * When the source collection is IList/Arrays, we use Interlocked on the shared index;
    //   * When the source collection is IEnumerable, we use Monitor to wrap around the access to the source
    //     enumerator.

    /// <summary>
    /// Provides common partitioning strategies for arrays, lists, and enumerables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The static methods on <see cref="Partitioner"/> are all thread-safe and may be used concurrently
    /// from multiple threads. However, while a created partitioner is in use, the underlying data source
    /// should not be modified, whether from the same thread that's using a partitioner or from a separate
    /// thread.
    /// </para>
    /// </remarks>
    public static class Partitioner
    {
        // How many chunks do we want to divide the range into?  If this is 1, then the
        // answer is "one chunk per core".  Generally, though, you'll achieve better
        // load balancing on a busy system if you make it higher than 1.
        private const int CoreOversubscriptionRate = 3;

        /// <summary>
        /// Creates an orderable partitioner from an <see cref="System.Collections.Generic.IList{T}"/>
        /// instance.
        /// </summary>
        /// <typeparam name="TSource">Type of the elements in source list.</typeparam>
        /// <param name="list">The list to be partitioned.</param>
        /// <param name="loadBalance">
        /// A Boolean value that indicates whether the created partitioner should dynamically
        /// load balance between partitions rather than statically partition.
        /// </param>
        /// <returns>
        /// An orderable partitioner based on the input list.
        /// </returns>
        public static OrderablePartitioner<TSource> Create<TSource>(IList<TSource> list, bool loadBalance)
        {
            if (list is null)
                ThrowHelper.ThrowArgumentNullException(nameof(list));
            if (loadBalance)
            {
                return (new DynamicPartitionerForIList<TSource>(list));
            }
            else
            {
                return (new StaticIndexRangePartitionerForIList<TSource>(list));
            }
        }

        /// <summary>
        /// Creates an orderable partitioner from a <see cref="System.Array"/> instance.
        /// </summary>
        /// <typeparam name="TSource">Type of the elements in source array.</typeparam>
        /// <param name="array">The array to be partitioned.</param>
        /// <param name="loadBalance">
        /// A Boolean value that indicates whether the created partitioner should dynamically load balance
        /// between partitions rather than statically partition.
        /// </param>
        /// <returns>
        /// An orderable partitioner based on the input array.
        /// </returns>
        public static OrderablePartitioner<TSource> Create<TSource>(TSource[] array, bool loadBalance)
        {
            if (array is null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);

            // This implementation uses 'ldelem' instructions for element retrieval, rather than using a
            // method call.

            if (loadBalance)
            {
                return (new DynamicPartitionerForArray<TSource>(array));
            }
            else
            {
                return (new StaticIndexRangePartitionerForArray<TSource>(array));
            }
        }

        /// <summary>
        /// Creates an orderable partitioner from a <see cref="System.Collections.Generic.IEnumerable{TSource}"/> instance.
        /// </summary>
        /// <typeparam name="TSource">Type of the elements in source enumerable.</typeparam>
        /// <param name="source">The enumerable to be partitioned.</param>
        /// <returns>
        /// An orderable partitioner based on the input array.
        /// </returns>
        /// <remarks>
        /// The ordering used in the created partitioner is determined by the natural order of the elements
        /// as retrieved from the source enumerable.
        /// </remarks>
        public static OrderablePartitioner<TSource> Create<TSource>(IEnumerable<TSource> source)
        {
            return Create<TSource>(source, EnumerablePartitionerOptions.None);
        }

        /// <summary>
        /// Creates an orderable partitioner from a <see cref="System.Collections.Generic.IEnumerable{TSource}"/> instance.
        /// </summary>
        /// <typeparam name="TSource">Type of the elements in source enumerable.</typeparam>
        /// <param name="source">The enumerable to be partitioned.</param>
        /// <param name="partitionerOptions">Options to control the buffering behavior of the partitioner.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// The <paramref name="partitionerOptions"/> argument specifies an invalid value for <see
        /// cref="System.Collections.Concurrent.EnumerablePartitionerOptions"/>.
        /// </exception>
        /// <returns>
        /// An orderable partitioner based on the input array.
        /// </returns>
        /// <remarks>
        /// The ordering used in the created partitioner is determined by the natural order of the elements
        /// as retrieved from the source enumerable.
        /// </remarks>
        internal static OrderablePartitioner<TSource> Create<TSource>(IEnumerable<TSource> source, EnumerablePartitionerOptions partitionerOptions)
        {
            if (source is null)
                ThrowHelper.ThrowArgumentNullException(nameof(source));
            if ((partitionerOptions & (~EnumerablePartitionerOptions.NoBuffering)) != 0)
                throw new ArgumentOutOfRangeException(nameof(partitionerOptions));

            return (new DynamicPartitionerForIEnumerable<TSource>(source, partitionerOptions));
        }

        /// <summary>Creates a partitioner that chunks the user-specified range.</summary>
        /// <param name="fromInclusive">The lower, inclusive bound of the range.</param>
        /// <param name="toExclusive">The upper, exclusive bound of the range.</param>
        /// <returns>A partitioner.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"> The <paramref name="toExclusive"/> argument is
        /// less than or equal to the <paramref name="fromInclusive"/> argument.</exception>
        /// <remarks>if ProccessorCount == 1, for correct rangeSize calculation the const CoreOversubscriptionRate must be > 1 (avoid division by 1)</remarks>
        public static OrderablePartitioner<Tuple<long, long>> Create(long fromInclusive, long toExclusive)
        {
            if (toExclusive <= fromInclusive)
                throw new ArgumentOutOfRangeException(nameof(toExclusive));
            decimal range = (decimal)toExclusive - fromInclusive;
            long rangeSize = (long)(range / (Environment.ProcessorCount * CoreOversubscriptionRate));
            if (rangeSize == 0)
                rangeSize = 1;
            return Partitioner.Create(CreateRanges(fromInclusive, toExclusive, rangeSize), EnumerablePartitionerOptions.NoBuffering); // chunk one range at a time
        }

        /// <summary>Creates a partitioner that chunks the user-specified range.</summary>
        /// <param name="fromInclusive">The lower, inclusive bound of the range.</param>
        /// <param name="toExclusive">The upper, exclusive bound of the range.</param>
        /// <param name="rangeSize">The size of each subrange.</param>
        /// <returns>A partitioner.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"> The <paramref name="toExclusive"/> argument is
        /// less than or equal to the <paramref name="fromInclusive"/> argument.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException"> The <paramref name="rangeSize"/> argument is
        /// less than or equal to 0.</exception>
        public static OrderablePartitioner<Tuple<long, long>> Create(long fromInclusive, long toExclusive, long rangeSize)
        {
            if (toExclusive <= fromInclusive)
                throw new ArgumentOutOfRangeException(nameof(toExclusive));
            if (rangeSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(rangeSize));
            return Partitioner.Create(CreateRanges(fromInclusive, toExclusive, rangeSize), EnumerablePartitionerOptions.NoBuffering); // chunk one range at a time
        }

        // Private method to parcel out range tuples.
        private static IEnumerable<Tuple<long, long>> CreateRanges(long fromInclusive, long toExclusive, long rangeSize)
        {
            // Enumerate all of the ranges
            long from, to;
            bool shouldQuit = false;

            for (long i = fromInclusive; (i < toExclusive) && !shouldQuit; i = unchecked(i + rangeSize))
            {
                from = i;
                try { checked { to = i + rangeSize; } }
                catch (OverflowException)
                {
                    to = toExclusive;
                    shouldQuit = true;
                }
                if (to > toExclusive)
                    to = toExclusive;
                yield return new Tuple<long, long>(from, to);
            }
        }

        /// <summary>Creates a partitioner that chunks the user-specified range.</summary>
        /// <param name="fromInclusive">The lower, inclusive bound of the range.</param>
        /// <param name="toExclusive">The upper, exclusive bound of the range.</param>
        /// <returns>A partitioner.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"> The <paramref name="toExclusive"/> argument is
        /// less than or equal to the <paramref name="fromInclusive"/> argument.</exception>
        /// <remarks>if ProccessorCount == 1, for correct rangeSize calculation the const CoreOversubscriptionRate must be > 1 (avoid division by 1),
        /// and the same issue could occur with rangeSize == -1 when fromInclusive = int.MinValue and toExclusive = int.MaxValue.</remarks>
        public static OrderablePartitioner<Tuple<int, int>> Create(int fromInclusive, int toExclusive)
        {
            if (toExclusive <= fromInclusive)
                throw new ArgumentOutOfRangeException(nameof(toExclusive));
            long range = (long)toExclusive - fromInclusive;
            int rangeSize = (int)(range / (Environment.ProcessorCount * CoreOversubscriptionRate));
            if (rangeSize == 0)
                rangeSize = 1;
            return Partitioner.Create(CreateRanges(fromInclusive, toExclusive, rangeSize), EnumerablePartitionerOptions.NoBuffering); // chunk one range at a time
        }

        /// <summary>Creates a partitioner that chunks the user-specified range.</summary>
        /// <param name="fromInclusive">The lower, inclusive bound of the range.</param>
        /// <param name="toExclusive">The upper, exclusive bound of the range.</param>
        /// <param name="rangeSize">The size of each subrange.</param>
        /// <returns>A partitioner.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"> The <paramref name="toExclusive"/> argument is
        /// less than or equal to the <paramref name="fromInclusive"/> argument.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException"> The <paramref name="rangeSize"/> argument is
        /// less than or equal to 0.</exception>
        public static OrderablePartitioner<Tuple<int, int>> Create(int fromInclusive, int toExclusive, int rangeSize)
        {
            if (toExclusive <= fromInclusive)
                throw new ArgumentOutOfRangeException(nameof(toExclusive));
            if (rangeSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(rangeSize));
            return Partitioner.Create(CreateRanges(fromInclusive, toExclusive, rangeSize), EnumerablePartitionerOptions.NoBuffering); // chunk one range at a time
        }

        // Private method to parcel out range tuples.
        private static IEnumerable<Tuple<int, int>> CreateRanges(int fromInclusive, int toExclusive, int rangeSize)
        {
            // Enumerate all of the ranges
            int from, to;
            bool shouldQuit = false;

            for (int i = fromInclusive; (i < toExclusive) && !shouldQuit; i = unchecked(i + rangeSize))
            {
                from = i;
                try { checked { to = i + rangeSize; } }
                catch (OverflowException)
                {
                    to = toExclusive;
                    shouldQuit = true;
                }
                if (to > toExclusive)
                    to = toExclusive;
                yield return new Tuple<int, int>(from, to);
            }
        }

        #region Dynamic Partitioner for source data of IndexRange types (IList<> and Array<>)
        /// <summary>
        /// Dynamic load-balance partitioner. This class is abstract and to be derived from by
        /// the customized partitioner classes for IList, Array, and IEnumerable
        /// </summary>
        /// <typeparam name="TSource">Type of the elements in the source data</typeparam>
        /// <typeparam name="TCollection"> Type of the source data collection</typeparam>
        private abstract class DynamicPartitionerForIndexRange_Abstract<TSource, TCollection> : OrderablePartitioner<TSource>
        {
            // TCollection can be: IList<TSource>, TSource[] and IEnumerable<TSource>
            // Derived classes specify TCollection, and implement the abstract method GetOrderableDynamicPartitions_Factory accordingly
            private readonly TCollection _data;

            /// <summary>
            /// Constructs a new orderable partitioner
            /// </summary>
            /// <param name="data">source data collection</param>
            protected DynamicPartitionerForIndexRange_Abstract(TCollection data)
                : base(true, false, true)
            {
                _data = data;
            }

            /// <summary>
            /// Partition the source data and create an enumerable over the resulting partitions.
            /// </summary>
            /// <param name="data">the source data collection</param>
            /// <returns>an enumerable of partitions of </returns>
            protected abstract IEnumerable<KeyValuePair<long, TSource>> GetOrderableDynamicPartitions_Factory(TCollection data);

            /// <summary>
            /// Overrides OrderablePartitioner.GetOrderablePartitions.
            /// Partitions the underlying collection into the given number of orderable partitions.
            /// </summary>
            /// <param name="partitionCount">number of partitions requested</param>
            /// <returns>A list containing <paramref name="partitionCount"/> enumerators.</returns>
            public override IList<IEnumerator<KeyValuePair<long, TSource>>> GetOrderablePartitions(int partitionCount)
            {
                if (partitionCount <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(partitionCount));
                }
                IEnumerator<KeyValuePair<long, TSource>>[] partitions
                    = new IEnumerator<KeyValuePair<long, TSource>>[partitionCount];
                IEnumerable<KeyValuePair<long, TSource>> partitionEnumerable = GetOrderableDynamicPartitions_Factory(_data);
                for (int i = 0; i < partitionCount; i++)
                {
                    partitions[i] = partitionEnumerable.GetEnumerator();
                }
                return partitions;
            }

            /// <summary>
            /// Overrides OrderablePartitioner.GetOrderableDynamicPartitions
            /// </summary>
            /// <returns>a enumerable collection of orderable partitions</returns>
            public override IEnumerable<KeyValuePair<long, TSource>> GetOrderableDynamicPartitions()
            {
                return GetOrderableDynamicPartitions_Factory(_data);
            }

            /// <summary>
            /// Whether additional partitions can be created dynamically.
            /// </summary>
            public override bool SupportsDynamicPartitions
            {
                get { return true; }
            }
        }

        /// <summary>
        /// Defines dynamic partition for source data of IList and Array.
        /// This class inherits DynamicPartitionEnumerator_Abstract
        ///   - implements GrabNextChunk, HasNoElementsLeft, and Dispose methods for IList and Array
        ///   - Current property still remains abstract, implementation is different for IList and Array
        ///   - introduces another abstract method SourceCount, which returns the number of elements in
        ///     the source data. Implementation differs for IList and Array
        /// </summary>
        /// <typeparam name="TSource">Type of the elements in the data source</typeparam>
        /// <typeparam name="TSourceReader">Type of the reader on the source data</typeparam>
        private abstract class DynamicPartitionEnumeratorForIndexRange_Abstract<TSource, TSourceReader> : DynamicPartitionEnumerator_Abstract<TSource, TSourceReader>
        {
            //fields
            protected int _startIndex; //initially zero

            //constructor
            protected DynamicPartitionEnumeratorForIndexRange_Abstract(TSourceReader sharedReader, SharedLong sharedIndex)
                : base(sharedReader, sharedIndex)
            {
            }

            //abstract methods
            //the Current property is still abstract, and will be implemented by derived classes
            //we add another abstract method SourceCount to get the number of elements from the source reader

            /// <summary>
            /// Get the number of elements from the source reader.
            /// It calls IList.Count or Array.Length
            /// </summary>
            protected abstract int SourceCount { get; }

            //overriding methods

            /// <summary>
            /// Reserves a contiguous range of elements from source data
            /// </summary>
            /// <param name="requestedChunkSize">specified number of elements requested</param>
            /// <returns>
            /// true if we successfully reserved at least one element (up to #=requestedChunkSize)
            /// false if all elements in the source collection have been reserved.
            /// </returns>
            protected override bool GrabNextChunk(int requestedChunkSize)
            {
                Debug.Assert(requestedChunkSize > 0);

                while (!HasNoElementsLeft)
                {
                    Debug.Assert(_sharedIndex != null);
                    // use the new Volatile.Read method because it is cheaper than Interlocked.Read on AMD64 architecture
                    long oldSharedIndex = Volatile.Read(ref _sharedIndex!.Value);

                    if (HasNoElementsLeft)
                    {
                        //HasNoElementsLeft situation changed from false to true immediately
                        //and oldSharedIndex becomes stale
                        return false;
                    }

                    //there won't be overflow, because the index of IList/array is int, and we
                    //have casted it to long.
                    long newSharedIndex = Math.Min(SourceCount - 1, oldSharedIndex + requestedChunkSize);


                    //the following CAS, if successful, reserves a chunk of elements [oldSharedIndex+1, newSharedIndex]
                    //inclusive in the source collection
                    if (Interlocked.CompareExchange(ref _sharedIndex.Value, newSharedIndex, oldSharedIndex)
                        == oldSharedIndex)
                    {
                        //set up local indexes.
                        //_currentChunkSize is always set to requestedChunkSize when source data had
                        //enough elements of what we requested
                        _currentChunkSize!.Value = (int)(newSharedIndex - oldSharedIndex);
                        _localOffset!.Value = -1;
                        _startIndex = (int)(oldSharedIndex + 1);
                        return true;
                    }
                }
                //didn't get any element, return false;
                return false;
            }

            /// <summary>
            /// Returns whether or not the shared reader has already read the last
            /// element of the source data
            /// </summary>
            protected override bool HasNoElementsLeft
            {
                get
                {
                    Debug.Assert(_sharedIndex != null);
                    // use the new Volatile.Read method because it is cheaper than Interlocked.Read on AMD64 architecture
                    return Volatile.Read(ref _sharedIndex!.Value) >= SourceCount - 1;
                }
            }

            /// <summary>
            /// For source data type IList and Array, the type of the shared reader is just the data itself.
            /// We don't do anything in Dispose method for IList and Array.
            /// </summary>
            public override void Dispose() { }
        }


        /// <summary>
        /// Inherits from DynamicPartitioners
        /// Provides customized implementation of GetOrderableDynamicPartitions_Factory method, to return an instance
        /// of EnumerableOfPartitionsForIList defined internally
        /// </summary>
        /// <typeparam name="TSource">Type of elements in the source data</typeparam>
        private sealed class DynamicPartitionerForIList<TSource> : DynamicPartitionerForIndexRange_Abstract<TSource, IList<TSource>>
        {
            //constructor
            internal DynamicPartitionerForIList(IList<TSource> source)
                : base(source) { }

            //override methods
            protected override IEnumerable<KeyValuePair<long, TSource>> GetOrderableDynamicPartitions_Factory(IList<TSource> _data)
            {
                //_data itself serves as shared reader
                return new InternalPartitionEnumerable(_data);
            }

            /// <summary>
            /// Inherits from PartitionList_Abstract
            /// Provides customized implementation for source data of IList
            /// </summary>
            private sealed class InternalPartitionEnumerable : IEnumerable<KeyValuePair<long, TSource>>
            {
                //reader through which we access the source data
                private readonly IList<TSource> _sharedReader;
                private readonly SharedLong _sharedIndex;

                internal InternalPartitionEnumerable(IList<TSource> sharedReader)
                {
                    _sharedReader = sharedReader;
                    _sharedIndex = new SharedLong(-1);
                }

                public IEnumerator<KeyValuePair<long, TSource>> GetEnumerator()
                {
                    return new InternalPartitionEnumerator(_sharedReader, _sharedIndex);
                }

                IEnumerator IEnumerable.GetEnumerator()
                {
                    return ((InternalPartitionEnumerable)this).GetEnumerator();
                }
            }

            /// <summary>
            /// Inherits from DynamicPartitionEnumeratorForIndexRange_Abstract
            /// Provides customized implementation of SourceCount property and Current property for IList
            /// </summary>
            private sealed class InternalPartitionEnumerator : DynamicPartitionEnumeratorForIndexRange_Abstract<TSource, IList<TSource>>
            {
                //constructor
                internal InternalPartitionEnumerator(IList<TSource> sharedReader, SharedLong sharedIndex)
                    : base(sharedReader, sharedIndex) { }

                //overriding methods
                protected override int SourceCount
                {
                    get { return _sharedReader.Count; }
                }
                /// <summary>
                /// return a KeyValuePair of the current element and its key
                /// </summary>
                public override KeyValuePair<long, TSource> Current
                {
                    get
                    {
                        //verify that MoveNext is at least called once before Current is called
                        if (_currentChunkSize == null)
                        {
                            throw new InvalidOperationException("Current called before MoveNext");
                        }

                        Debug.Assert(_localOffset!.Value >= 0 && _localOffset.Value < _currentChunkSize.Value);
                        return new KeyValuePair<long, TSource>(_startIndex + _localOffset.Value,
                            _sharedReader[_startIndex + _localOffset.Value]);
                    }
                }
            }
        }



        /// <summary>
        /// Inherits from DynamicPartitioners
        /// Provides customized implementation of GetOrderableDynamicPartitions_Factory method, to return an instance
        /// of EnumerableOfPartitionsForArray defined internally
        /// </summary>
        /// <typeparam name="TSource">Type of elements in the source data</typeparam>
        private sealed class DynamicPartitionerForArray<TSource> : DynamicPartitionerForIndexRange_Abstract<TSource, TSource[]>
        {
            //constructor
            internal DynamicPartitionerForArray(TSource[] source)
                : base(source) { }

            //override methods
            protected override IEnumerable<KeyValuePair<long, TSource>> GetOrderableDynamicPartitions_Factory(TSource[] _data)
            {
                return new InternalPartitionEnumerable(_data);
            }

            /// <summary>
            /// Inherits from PartitionList_Abstract
            /// Provides customized implementation for source data of Array
            /// </summary>
            private sealed class InternalPartitionEnumerable : IEnumerable<KeyValuePair<long, TSource>>
            {
                //reader through which we access the source data
                private readonly TSource[] _sharedReader;
                private readonly SharedLong _sharedIndex;

                internal InternalPartitionEnumerable(TSource[] sharedReader)
                {
                    _sharedReader = sharedReader;
                    _sharedIndex = new SharedLong(-1);
                }

                IEnumerator IEnumerable.GetEnumerator()
                {
                    return ((InternalPartitionEnumerable)this).GetEnumerator();
                }


                public IEnumerator<KeyValuePair<long, TSource>> GetEnumerator()
                {
                    return new InternalPartitionEnumerator(_sharedReader, _sharedIndex);
                }
            }

            /// <summary>
            /// Inherits from DynamicPartitionEnumeratorForIndexRange_Abstract
            /// Provides customized implementation of SourceCount property and Current property for Array
            /// </summary>
            private sealed class InternalPartitionEnumerator : DynamicPartitionEnumeratorForIndexRange_Abstract<TSource, TSource[]>
            {
                //constructor
                internal InternalPartitionEnumerator(TSource[] sharedReader, SharedLong sharedIndex)
                    : base(sharedReader, sharedIndex) { }

                //overriding methods
                protected override int SourceCount
                {
                    get { return _sharedReader.Length; }
                }

                public override KeyValuePair<long, TSource> Current
                {
                    get
                    {
                        //verify that MoveNext is at least called once before Current is called
                        if (_currentChunkSize == null)
                        {
                            throw new InvalidOperationException("Current called before MoveNext");
                        }

                        Debug.Assert(_localOffset!.Value >= 0 && _localOffset.Value < _currentChunkSize.Value);
                        return new KeyValuePair<long, TSource>(_startIndex + _localOffset.Value,
                            _sharedReader[_startIndex + _localOffset.Value]);
                    }
                }
            }
        }
        #endregion

        #region Static partitioning for IList and Array, abstract classes
        /// <summary>
        /// Static partitioning over IList.
        /// - dynamic and load-balance
        /// - Keys are ordered within each partition
        /// - Keys are ordered across partitions
        /// - Keys are normalized
        /// - Number of partitions is fixed once specified, and the elements of the source data are
        /// distributed to each partition as evenly as possible.
        /// </summary>
        /// <typeparam name="TSource">type of the elements</typeparam>
        /// <typeparam name="TCollection">Type of the source data collection</typeparam>
        private abstract class StaticIndexRangePartitioner<TSource, TCollection> : OrderablePartitioner<TSource>
        {
            protected StaticIndexRangePartitioner()
                : base(true, true, true) { }

            /// <summary>
            /// Abstract method to return the number of elements in the source data
            /// </summary>
            protected abstract int SourceCount { get; }

            /// <summary>
            /// Abstract method to create a partition that covers a range over source data,
            /// starting from "startIndex", ending at "endIndex"
            /// </summary>
            /// <param name="startIndex">start index of the current partition on the source data</param>
            /// <param name="endIndex">end index of the current partition on the source data</param>
            /// <returns>a partition enumerator over the specified range</returns>
            // The partitioning algorithm is implemented in GetOrderablePartitions method
            // This method delegates according to source data type IList/Array
            protected abstract IEnumerator<KeyValuePair<long, TSource>> CreatePartition(int startIndex, int endIndex);

            /// <summary>
            /// Overrides OrderablePartitioner.GetOrderablePartitions
            /// Return a list of partitions, each of which enumerate a fixed part of the source data
            /// The elements of the source data are distributed to each partition as evenly as possible.
            /// Specifically, if the total number of elements is N, and number of partitions is x, and N = a*x +b,
            /// where a is the quotient, and b is the remainder. Then the first b partitions each has a + 1 elements,
            /// and the last x-b partitions each has a elements.
            /// For example, if N=10, x =3, then
            ///    partition 0 ranges [0,3],
            ///    partition 1 ranges [4,6],
            ///    partition 2 ranges [7,9].
            /// This also takes care of the situation of (x&gt;N), the last x-N partitions are empty enumerators.
            /// An empty enumerator is indicated by
            ///      (_startIndex == list.Count &amp;&amp; _endIndex == list.Count -1)
            /// </summary>
            /// <param name="partitionCount">specified number of partitions</param>
            /// <returns>a list of partitions</returns>
            public override IList<IEnumerator<KeyValuePair<long, TSource>>> GetOrderablePartitions(int partitionCount)
            {
                if (partitionCount <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(partitionCount));
                }

                int quotient, remainder;
                quotient = SourceCount / partitionCount;
                remainder = SourceCount % partitionCount;

                IEnumerator<KeyValuePair<long, TSource>>[] partitions = new IEnumerator<KeyValuePair<long, TSource>>[partitionCount];
                int lastEndIndex = -1;
                for (int i = 0; i < partitionCount; i++)
                {
                    int startIndex = lastEndIndex + 1;

                    if (i < remainder)
                    {
                        lastEndIndex = startIndex + quotient;
                    }
                    else
                    {
                        lastEndIndex = startIndex + quotient - 1;
                    }
                    partitions[i] = CreatePartition(startIndex, lastEndIndex);
                }
                return partitions;
            }
        }

        /// <summary>
        /// Static Partition for IList/Array.
        /// This class implements all methods required by IEnumerator interface, except for the Current property.
        /// Current Property is different for IList and Array. Arrays calls 'ldelem' instructions for faster element
        /// retrieval.
        /// </summary>
        //We assume the source collection is not being updated concurrently. Otherwise it will break the
        //static partitioning, since each partition operates on the source collection directly, it does
        //not have a local cache of the elements assigned to them.
        private abstract class StaticIndexRangePartition<TSource> : IEnumerator<KeyValuePair<long, TSource>>
        {
            //the start and end position in the source collection for the current partition
            //the partition is empty if and only if
            // (_startIndex == _data.Count && _endIndex == _data.Count-1)
            protected readonly int _startIndex;
            protected readonly int _endIndex;

            //the current index of the current partition while enumerating on the source collection
            protected volatile int _offset;

            /// <summary>
            /// Constructs an instance of StaticIndexRangePartition
            /// </summary>
            /// <param name="startIndex">the start index in the source collection for the current partition </param>
            /// <param name="endIndex">the end index in the source collection for the current partition</param>
            protected StaticIndexRangePartition(int startIndex, int endIndex)
            {
                _startIndex = startIndex;
                _endIndex = endIndex;
                _offset = startIndex - 1;
            }

            /// <summary>
            /// Current Property is different for IList and Array. Arrays calls 'ldelem' instructions for faster
            /// element retrieval.
            /// </summary>
            public abstract KeyValuePair<long, TSource> Current { get; }

            /// <summary>
            /// We don't dispose the source for IList and array
            /// </summary>
            public void Dispose() { }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Moves to the next item
            /// Before the first MoveNext is called: _offset == _startIndex-1;
            /// </summary>
            /// <returns>true if successful, false if there is no item left</returns>
            public bool MoveNext()
            {
                if (_offset < _endIndex)
                {
                    _offset++;
                    return true;
                }
                else
                {
                    //After we have enumerated over all elements, we set _offset to _endIndex +1.
                    //The reason we do this is, for an empty enumerator, we need to tell the Current
                    //property whether MoveNext has been called or not.
                    //For an empty enumerator, it starts with (_offset == _startIndex-1 == _endIndex),
                    //and we don't set a new value to _offset, then the above condition will always be
                    //true, and the Current property will mistakenly assume MoveNext is never called.
                    _offset = _endIndex + 1;
                    return false;
                }
            }

            object? IEnumerator.Current
            {
                get
                {
                    return ((StaticIndexRangePartition<TSource>)this).Current;
                }
            }
        }
        #endregion

        #region Static partitioning for IList
        /// <summary>
        /// Inherits from StaticIndexRangePartitioner
        /// Provides customized implementation of SourceCount and CreatePartition
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        private sealed class StaticIndexRangePartitionerForIList<TSource> : StaticIndexRangePartitioner<TSource, IList<TSource>>
        {
            private readonly IList<TSource> _list;
            internal StaticIndexRangePartitionerForIList(IList<TSource> list)
                : base()
            {
                Debug.Assert(list != null);
                _list = list!;
            }
            protected override int SourceCount
            {
                get { return _list.Count; }
            }
            protected override IEnumerator<KeyValuePair<long, TSource>> CreatePartition(int startIndex, int endIndex)
            {
                return new StaticIndexRangePartitionForIList<TSource>(_list, startIndex, endIndex);
            }
        }

        /// <summary>
        /// Inherits from StaticIndexRangePartition
        /// Provides customized implementation of Current property
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        private sealed class StaticIndexRangePartitionForIList<TSource> : StaticIndexRangePartition<TSource>
        {
            //the source collection shared by all partitions
            private readonly IList<TSource> _list;

            internal StaticIndexRangePartitionForIList(IList<TSource> list, int startIndex, int endIndex)
                : base(startIndex, endIndex)
            {
                Debug.Assert(startIndex >= 0 && endIndex <= list.Count - 1);
                _list = list;
            }

            public override KeyValuePair<long, TSource> Current
            {
                get
                {
                    //verify that MoveNext is at least called once before Current is called
                    if (_offset < _startIndex)
                    {
                        throw new InvalidOperationException("Current called before MoveNext");
                    }

                    Debug.Assert(_offset >= _startIndex && _offset <= _endIndex);
                    return (new KeyValuePair<long, TSource>(_offset, _list[_offset]));
                }
            }
        }
        #endregion

        #region static partitioning for Arrays
        /// <summary>
        /// Inherits from StaticIndexRangePartitioner
        /// Provides customized implementation of SourceCount and CreatePartition for Array
        /// </summary>
        private sealed class StaticIndexRangePartitionerForArray<TSource> : StaticIndexRangePartitioner<TSource, TSource[]>
        {
            private readonly TSource[] _array;
            internal StaticIndexRangePartitionerForArray(TSource[] array)
                : base()
            {
                Debug.Assert(array != null);
                _array = array!;
            }
            protected override int SourceCount
            {
                get { return _array.Length; }
            }
            protected override IEnumerator<KeyValuePair<long, TSource>> CreatePartition(int startIndex, int endIndex)
            {
                return new StaticIndexRangePartitionForArray<TSource>(_array, startIndex, endIndex);
            }
        }

        /// <summary>
        /// Inherits from StaticIndexRangePartitioner
        /// Provides customized implementation of SourceCount and CreatePartition
        /// </summary>
        private sealed class StaticIndexRangePartitionForArray<TSource> : StaticIndexRangePartition<TSource>
        {
            //the source collection shared by all partitions
            private readonly TSource[] _array;

            internal StaticIndexRangePartitionForArray(TSource[] array, int startIndex, int endIndex)
                : base(startIndex, endIndex)
            {
                Debug.Assert(startIndex >= 0 && endIndex <= array.Length - 1);
                _array = array;
            }

            public override KeyValuePair<long, TSource> Current
            {
                get
                {
                    //verify that MoveNext is at least called once before Current is called
                    if (_offset < _startIndex)
                    {
                        throw new InvalidOperationException("Current called before MoveNext");
                    }

                    Debug.Assert(_offset >= _startIndex && _offset <= _endIndex);
                    return (new KeyValuePair<long, TSource>(_offset, _array[_offset]));
                }
            }
        }
        #endregion

    }
}