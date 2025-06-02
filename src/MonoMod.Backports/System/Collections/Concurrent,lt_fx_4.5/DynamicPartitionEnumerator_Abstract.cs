
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Collections.Concurrent
{
    internal static class PartitionerUtilities
    {
        //--------------------
        // The following part calculates the default chunk size. It is copied from System.Linq.Parallel.Scheduling.
        //--------------------

        // The number of bytes we want "chunks" to be, when partitioning, etc. We choose 4 cache
        // lines worth, assuming 128b cache line.  Most (popular) architectures use 64b cache lines,
        // but choosing 128b works for 64b too whereas a multiple of 64b isn't necessarily sufficient
        // for 128b cache systems.  So 128b it is.
        private const int DEFAULT_BYTES_PER_UNIT = 128;
        private const int DEFAULT_BYTES_PER_CHUNK = DEFAULT_BYTES_PER_UNIT * 4;

        public static int GetDefaultChunkSize<TSource>()
        {
            int chunkSize;

            if (typeof(TSource).IsValueType)
            {
                // Marshal.SizeOf fails for value types that don't have explicit layouts. We
                // just fall back to some arbitrary constant in that case. Is there a better way?
                {
                    // We choose '128' because this ensures, no matter the actual size of the value type,
                    // the total bytes used will be a multiple of 128. This ensures it's cache aligned.
                    chunkSize = DEFAULT_BYTES_PER_UNIT;
                }
            }
            else
            {
                Debug.Assert((DEFAULT_BYTES_PER_CHUNK % IntPtr.Size) == 0, "bytes per chunk should be a multiple of pointer size");
                chunkSize = (DEFAULT_BYTES_PER_CHUNK / IntPtr.Size);
            }
            return chunkSize;
        }
    }

    /// <summary>
    /// A very simple primitive that allows us to share a value across multiple threads.
    /// </summary>
    internal sealed class SharedInt
    {
        internal volatile int Value;

        internal SharedInt(int value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// A very simple primitive that allows us to share a value across multiple threads.
    /// </summary>
    internal sealed class SharedBool
    {
        internal volatile bool Value;

        internal SharedBool(bool value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// A very simple primitive that allows us to share a value across multiple threads.
    /// </summary>
    internal sealed class SharedLong
    {
        internal long Value;
        internal SharedLong(long value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// DynamicPartitionEnumerator_Abstract defines the enumerator for each partition for the dynamic load-balance
    /// partitioning algorithm.
    /// - Partition is an enumerator of KeyValuePairs, each corresponding to an item in the data source:
    ///   the key is the index in the source collection; the value is the item itself.
    /// - a set of such partitions share a reader over data source. The type of the reader is specified by
    ///   TSourceReader.
    /// - each partition requests a contiguous chunk of elements at a time from the source data. The chunk
    ///   size is initially 1, and doubles every time until it reaches the maximum chunk size.
    ///   The implementation for GrabNextChunk() method has two versions: one for data source of IndexRange
    ///   types (IList and the array), one for data source of IEnumerable.
    /// - The method "Reset" is not supported for any partitioning algorithm.
    /// - The implementation for MoveNext() method is same for all dynamic partitioners, so we provide it
    ///   in this abstract class.
    /// </summary>
    /// <typeparam name="TSource">Type of the elements in the data source</typeparam>
    /// <typeparam name="TSourceReader">Type of the reader on the data source</typeparam>
    //TSourceReader is
    //  - IList<TSource>, when source data is IList<TSource>, the shared reader is source data itself
    //  - TSource[], when source data is TSource[], the shared reader is source data itself
    //  - IEnumerator<TSource>, when source data is IEnumerable<TSource>, and the shared reader is an
    //    enumerator of the source data
    internal abstract class DynamicPartitionEnumerator_Abstract<TSource, TSourceReader> : IEnumerator<KeyValuePair<long, TSource>>
    {
        //----------------- common fields and constructor for all dynamic partitioners -----------------
        //--- shared by all derived class with source data type: IList, Array, and IEnumerator
        protected readonly TSourceReader _sharedReader;

        protected static int s_defaultMaxChunkSize = PartitionerUtilities.GetDefaultChunkSize<TSource>();

        //deferred allocating in MoveNext() with initial value 0, to avoid false sharing
        //we also use the fact that: (_currentChunkSize==null) means MoveNext is never called on this enumerator
        protected StrongBox<int>? _currentChunkSize;

        //deferring allocation in MoveNext() with initial value -1, to avoid false sharing
        protected StrongBox<int>? _localOffset;

        private const int CHUNK_DOUBLING_RATE = 3; // Double the chunk size every this many grabs
        private int _doublingCountdown; // Number of grabs remaining until chunk size doubles
        protected readonly int _maxChunkSize; // s_defaultMaxChunkSize unless single-chunking is requested by the caller

        // _sharedIndex shared by this set of partitions, and particularly when _sharedReader is IEnumerable
        // it serves as tracking of the natural order of elements in _sharedReader
        // the value of this field is passed in from outside (already initialized) by the constructor,
        protected readonly SharedLong _sharedIndex;

        protected DynamicPartitionEnumerator_Abstract(TSourceReader sharedReader, SharedLong sharedIndex)
            : this(sharedReader, sharedIndex, false)
        {
        }

        protected DynamicPartitionEnumerator_Abstract(TSourceReader sharedReader, SharedLong sharedIndex, bool useSingleChunking)
        {
            _sharedReader = sharedReader;
            _sharedIndex = sharedIndex;
            _maxChunkSize = useSingleChunking ? 1 : s_defaultMaxChunkSize;
        }

        // ---------------- abstract method declarations --------------

        /// <summary>
        /// Abstract method to request a contiguous chunk of elements from the source collection
        /// </summary>
        /// <param name="requestedChunkSize">specified number of elements requested</param>
        /// <returns>
        /// true if we successfully reserved at least one element (up to #=requestedChunkSize)
        /// false if all elements in the source collection have been reserved.
        /// </returns>
        //GrabNextChunk does the following:
        //  - grab # of requestedChunkSize elements from source data through shared reader,
        //  - at the time of function returns, _currentChunkSize is updated with the number of
        //    elements actually got assigned (<=requestedChunkSize).
        //  - GrabNextChunk returns true if at least one element is assigned to this partition;
        //    false if the shared reader already hits the last element of the source data before
        //    we call GrabNextChunk
        protected abstract bool GrabNextChunk(int requestedChunkSize);

        /// <summary>
        /// Abstract property, returns whether or not the shared reader has already read the last
        /// element of the source data
        /// </summary>
        protected abstract bool HasNoElementsLeft { get; }

        /// <summary>
        /// Get the current element in the current partition. Property required by IEnumerator interface
        /// This property is abstract because the implementation is different depending on the type
        /// of the source data: IList, Array or IEnumerable
        /// </summary>
        public abstract KeyValuePair<long, TSource> Current { get; }

        /// <summary>
        /// Dispose is abstract, and depends on the type of the source data:
        /// - For source data type IList and Array, the type of the shared reader is just the data itself.
        ///   We don't do anything in Dispose method for IList and Array.
        /// - For source data type IEnumerable, the type of the shared reader is an enumerator we created.
        ///   Thus we need to dispose this shared reader enumerator, when there is no more active partitions
        ///   left.
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        /// Reset on partitions is not supported
        /// </summary>
        public void Reset()
        {
            throw new NotSupportedException();
        }


        /// <summary>
        /// Get the current element in the current partition. Property required by IEnumerator interface
        /// </summary>
        object? IEnumerator.Current
        {
            get
            {
                return ((DynamicPartitionEnumerator_Abstract<TSource, TSourceReader>)this).Current;
            }
        }

        /// <summary>
        /// Moves to the next element if any.
        /// Try current chunk first, if the current chunk do not have any elements left, then we
        /// attempt to grab a chunk from the source collection.
        /// </summary>
        /// <returns>
        /// true if successfully moving to the next position;
        /// false otherwise, if and only if there is no more elements left in the current chunk
        /// AND the source collection is exhausted.
        /// </returns>
        public bool MoveNext()
        {
            //perform deferred allocating of the local variables.
            if (_localOffset == null)
            {
                Debug.Assert(_currentChunkSize == null);
                _localOffset = new StrongBox<int>(-1);
                _currentChunkSize = new StrongBox<int>(0);
                _doublingCountdown = CHUNK_DOUBLING_RATE;
            }
            Debug.Assert(_currentChunkSize != null);

            if (_localOffset.Value < _currentChunkSize!.Value - 1)
            //attempt to grab the next element from the local chunk
            {
                _localOffset.Value++;
                return true;
            }
            else
            //otherwise it means we exhausted the local chunk
            //grab a new chunk from the source enumerator
            {
                // The second part of the || condition is necessary to handle the case when MoveNext() is called
                // after a previous MoveNext call returned false.
                Debug.Assert(_localOffset.Value == _currentChunkSize.Value - 1 || _currentChunkSize.Value == 0);

                //set the requested chunk size to a proper value
                int requestedChunkSize;
                if (_currentChunkSize.Value == 0) //first time grabbing from source enumerator
                {
                    requestedChunkSize = 1;
                }
                else if (_doublingCountdown > 0)
                {
                    requestedChunkSize = _currentChunkSize.Value;
                }
                else
                {
                    requestedChunkSize = Math.Min(_currentChunkSize.Value * 2, _maxChunkSize);
                    _doublingCountdown = CHUNK_DOUBLING_RATE; // reset
                }

                // Decrement your doubling countdown
                _doublingCountdown--;

                Debug.Assert(requestedChunkSize > 0 && requestedChunkSize <= _maxChunkSize);
                //GrabNextChunk will update the value of _currentChunkSize
                if (GrabNextChunk(requestedChunkSize))
                {
                    Debug.Assert(_currentChunkSize.Value <= requestedChunkSize && _currentChunkSize.Value > 0);
                    _localOffset.Value = 0;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
    }
}
