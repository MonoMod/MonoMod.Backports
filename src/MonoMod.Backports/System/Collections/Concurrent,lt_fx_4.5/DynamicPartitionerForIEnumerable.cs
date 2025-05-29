using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace System.Collections.Concurrent
{
    /// <summary>
    /// Inherits from DynamicPartitioners
    /// Provides customized implementation of GetOrderableDynamicPartitions_Factory method, to return an instance
    /// of EnumerableOfPartitionsForIEnumerator defined internally
    /// </summary>
    /// <typeparam name="TSource">Type of elements in the source data</typeparam>
    internal sealed class DynamicPartitionerForIEnumerable<TSource> : OrderablePartitioner<TSource>
    {
        private readonly IEnumerable<TSource> _source;
        private readonly bool _useSingleChunking;

        //constructor
        internal DynamicPartitionerForIEnumerable(IEnumerable<TSource> source, EnumerablePartitionerOptions partitionerOptions)
            : base(true, false, true)
        {
            _source = source;
            _useSingleChunking = ((partitionerOptions & EnumerablePartitionerOptions.NoBuffering) != 0);
        }

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

#pragma warning disable CA2000 // Dispose objects before losing scope
            // This is the BCL implementation of this method.
            IEnumerable<KeyValuePair<long, TSource>> partitionEnumerable = new InternalPartitionEnumerable(_source.GetEnumerator(), _useSingleChunking, true);
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
            return new InternalPartitionEnumerable(_source.GetEnumerator(), _useSingleChunking, false);
        }

        /// <summary>
        /// Whether additional partitions can be created dynamically.
        /// </summary>
        public override bool SupportsDynamicPartitions
        {
            get { return true; }
        }

        #region Internal classes:  InternalPartitionEnumerable, InternalPartitionEnumerator
        /// <summary>
        /// Provides customized implementation for source data of IEnumerable
        /// Different from the counterpart for IList/Array, this enumerable maintains several additional fields
        /// shared by the partitions it owns, including a boolean "_hasNoElementsLef", a shared lock, and a
        /// shared count "_activePartitionCount" used to track active partitions when they were created statically
        /// </summary>
        private sealed class InternalPartitionEnumerable : IEnumerable<KeyValuePair<long, TSource>>, IDisposable
        {
            //reader through which we access the source data
            private readonly IEnumerator<TSource> _sharedReader;
            private readonly SharedLong _sharedIndex; //initial value -1

            private volatile KeyValuePair<long, TSource>[]? _fillBuffer; // intermediate buffer to reduce locking
            private volatile int _fillBufferSize;            // actual number of elements in _FillBuffer. Will start
                                                             // at _FillBuffer.Length, and might be reduced during the last refill
            private volatile int _fillBufferCurrentPosition; //shared value to be accessed by Interlock.Increment only
            private volatile int _activeCopiers;             //number of active copiers

            //fields shared by all partitions that this Enumerable owns, their allocation is deferred
            private readonly SharedBool _hasNoElementsLeft; // no elements left at all.
            private readonly SharedBool _sourceDepleted;    // no elements left in the enumerator, but there may be elements in the Fill Buffer

            //shared synchronization lock, created by this Enumerable
            private readonly object _sharedLock; //deferring allocation by enumerator

            private bool _disposed;

            // If dynamic partitioning, then _activePartitionCount == null
            // If static partitioning, then it keeps track of active partition count
            private readonly SharedInt? _activePartitionCount;

            // records whether or not the user has requested single-chunking behavior
            private readonly bool _useSingleChunking;

            internal InternalPartitionEnumerable(IEnumerator<TSource> sharedReader, bool useSingleChunking, bool isStaticPartitioning)
            {
                _sharedReader = sharedReader;
                _sharedIndex = new SharedLong(-1);
                _hasNoElementsLeft = new SharedBool(false);
                _sourceDepleted = new SharedBool(false);
                _sharedLock = new object();
                _useSingleChunking = useSingleChunking;

                // Only allocate the fill-buffer if single-chunking is not in effect
                if (!_useSingleChunking)
                {
                    // Time to allocate the fill buffer which is used to reduce the contention on the shared lock.
                    // First pick the buffer size multiplier. We use 4 for when there are more than 4 cores, and just 1 for below. This is based on empirical evidence.
                    int fillBufferMultiplier = (Environment.ProcessorCount > 4) ? 4 : 1;

                    // and allocate the fill buffer using these two numbers
                    _fillBuffer = new KeyValuePair<long, TSource>[fillBufferMultiplier * PartitionerUtilities.GetDefaultChunkSize<TSource>()];
                }

                if (isStaticPartitioning)
                {
                    // If this object is created for static partitioning (ie. via GetPartitions(int partitionCount),
                    // GetOrderablePartitions(int partitionCount)), we track the active partitions, in order to dispose
                    // this object when all the partitions have been disposed.
                    _activePartitionCount = new SharedInt(0);
                }
                else
                {
                    // Otherwise this object is created for dynamic partitioning (ie, via GetDynamicPartitions(),
                    // GetOrderableDynamicPartitions()), we do not need tracking. This object must be disposed
                    // explicitly
                    _activePartitionCount = null;
                }
            }

            public IEnumerator<KeyValuePair<long, TSource>> GetEnumerator()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException("Cannot call GetEnumerator after source has been disposed");
                }
                else
                {
                    return new InternalPartitionEnumerator(_sharedReader, _sharedIndex,
                        _hasNoElementsLeft, _activePartitionCount, this, _useSingleChunking);
                }
            }


            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((InternalPartitionEnumerable)this).GetEnumerator();
            }

            ///////////////////
            //
            // Used by GrabChunk_Buffered()
            private void TryCopyFromFillBuffer(KeyValuePair<long, TSource>[] destArray,
                                              int requestedChunkSize,
                                              ref int actualNumElementsGrabbed)
            {
                actualNumElementsGrabbed = 0;


                // making a local defensive copy of the fill buffer reference, just in case it gets nulled out
                KeyValuePair<long, TSource>[]? fillBufferLocalRef = _fillBuffer;
                if (fillBufferLocalRef == null)
                    return;

                // first do a quick check, and give up if the current position is at the end
                // so that we don't do an unnecessary pair of Interlocked.Increment / Decrement calls
                if (_fillBufferCurrentPosition >= _fillBufferSize)
                {
                    return; // no elements in the buffer to copy from
                }

                // We might have a chance to grab elements from the buffer. We will know for sure
                // when we do the Interlocked.Add below.
                // But first we must register as a potential copier in order to make sure
                // the elements we're copying from don't get overwritten by another thread
                // that starts refilling the buffer right after our Interlocked.Add.
                Interlocked.Increment(ref _activeCopiers);

                int endPos = Interlocked.Add(ref _fillBufferCurrentPosition, requestedChunkSize);
                int beginPos = endPos - requestedChunkSize;

                if (beginPos < _fillBufferSize)
                {
                    // adjust index and do the actual copy
                    actualNumElementsGrabbed = (endPos < _fillBufferSize) ? endPos : _fillBufferSize - beginPos;
                    Array.Copy(fillBufferLocalRef, beginPos, destArray, 0, actualNumElementsGrabbed);
                }

                // let the record show we are no longer accessing the buffer
                Interlocked.Decrement(ref _activeCopiers);
            }

            /// <summary>
            /// This is the common entry point for consuming items from the source enumerable
            /// </summary>
            /// <returns>
            /// true if we successfully reserved at least one element
            /// false if all elements in the source collection have been reserved.
            /// </returns>
            internal bool GrabChunk(KeyValuePair<long, TSource>[] destArray, int requestedChunkSize, ref int actualNumElementsGrabbed)
            {
                actualNumElementsGrabbed = 0;

                if (_hasNoElementsLeft.Value)
                {
                    return false;
                }

                if (_useSingleChunking)
                {
                    return GrabChunk_Single(destArray, requestedChunkSize, ref actualNumElementsGrabbed);
                }
                else
                {
                    return GrabChunk_Buffered(destArray, requestedChunkSize, ref actualNumElementsGrabbed);
                }
            }

            /// <summary>
            /// Version of GrabChunk that grabs a single element at a time from the source enumerable
            /// </summary>
            /// <returns>
            /// true if we successfully reserved an element
            /// false if all elements in the source collection have been reserved.
            /// </returns>
            internal bool GrabChunk_Single(KeyValuePair<long, TSource>[] destArray, int requestedChunkSize, ref int actualNumElementsGrabbed)
            {
                Debug.Assert(_useSingleChunking, "Expected _useSingleChecking to be true");
                Debug.Assert(requestedChunkSize == 1, $"Got requested chunk size of {requestedChunkSize} when single-chunking was on");
                Debug.Assert(actualNumElementsGrabbed == 0, $"Expected actualNumElementsGrabbed == 0, instead it is {actualNumElementsGrabbed}");
                Debug.Assert(destArray.Length == 1, $"Expected destArray to be of length 1, instead its length is {destArray.Length}");

                lock (_sharedLock)
                {
                    if (_hasNoElementsLeft.Value)
                        return false;

                    try
                    {
                        if (_sharedReader.MoveNext())
                        {
                            _sharedIndex.Value = checked(_sharedIndex.Value + 1);
                            destArray[0]
                                = new KeyValuePair<long, TSource>(_sharedIndex.Value,
                                                                    _sharedReader.Current);
                            actualNumElementsGrabbed = 1;
                            return true;
                        }
                        else
                        {
                            //if MoveNext() return false, we set the flag to inform other partitions
                            _sourceDepleted.Value = true;
                            _hasNoElementsLeft.Value = true;
                            return false;
                        }
                    }
                    catch
                    {
                        // On an exception, make sure that no additional items are hereafter enumerated
                        _sourceDepleted.Value = true;
                        _hasNoElementsLeft.Value = true;
                        throw;
                    }
                }
            }



            /// <summary>
            /// Version of GrabChunk that uses buffering scheme to grab items out of source enumerable
            /// </summary>
            /// <returns>
            /// true if we successfully reserved at least one element (up to #=requestedChunkSize)
            /// false if all elements in the source collection have been reserved.
            /// </returns>
            internal bool GrabChunk_Buffered(KeyValuePair<long, TSource>[] destArray, int requestedChunkSize, ref int actualNumElementsGrabbed)
            {
                Debug.Assert(requestedChunkSize > 0);
                Debug.Assert(!_useSingleChunking, "Did not expect to be in single-chunking mode");

                TryCopyFromFillBuffer(destArray, requestedChunkSize, ref actualNumElementsGrabbed);

                if (actualNumElementsGrabbed == requestedChunkSize)
                {
                    // that was easy.
                    return true;
                }
                else if (_sourceDepleted.Value)
                {
                    // looks like we both reached the end of the fill buffer, and the source was depleted previously
                    // this means no more work to do for any other worker
                    _hasNoElementsLeft.Value = true;
                    _fillBuffer = null;
                    return (actualNumElementsGrabbed > 0);
                }


                //
                //  now's the time to take the shared lock and enumerate
                //
                lock (_sharedLock)
                {
                    if (_sourceDepleted.Value)
                    {
                        return (actualNumElementsGrabbed > 0);
                    }

                    try
                    {
                        // we need to make sure all array copiers are finished
                        if (_activeCopiers > 0)
                        {
                            SpinWait sw = default;
                            while (_activeCopiers > 0)
                                sw.SpinOnce();
                        }

                        Debug.Assert(_sharedIndex != null); //already been allocated in MoveNext() before calling GrabNextChunk

                        // Now's the time to actually enumerate the source

                        // We first fill up the requested # of elements in the caller's array
                        // continue from the where TryCopyFromFillBuffer() left off
                        for (; actualNumElementsGrabbed < requestedChunkSize; actualNumElementsGrabbed++)
                        {
                            if (_sharedReader.MoveNext())
                            {
                                _sharedIndex!.Value = checked(_sharedIndex.Value + 1);
                                destArray[actualNumElementsGrabbed]
                                    = new KeyValuePair<long, TSource>(_sharedIndex.Value,
                                                                      _sharedReader.Current);
                            }
                            else
                            {
                                //if MoveNext() return false, we set the flag to inform other partitions
                                _sourceDepleted.Value = true;
                                break;
                            }
                        }

                        // taking a local snapshot of _FillBuffer in case some other thread decides to null out _FillBuffer
                        // in the entry of this method after observing _sourceCompleted = true
                        var localFillBufferRef = _fillBuffer;

                        // If the big buffer seems to be depleted, we will also fill that up while we are under the lock
                        // Note that this is the only place that _FillBufferCurrentPosition can be reset
                        if (_sourceDepleted.Value == false && localFillBufferRef != null &&
                            _fillBufferCurrentPosition >= localFillBufferRef.Length)
                        {
                            for (int i = 0; i < localFillBufferRef.Length; i++)
                            {
                                if (_sharedReader.MoveNext())
                                {
                                    _sharedIndex!.Value = checked(_sharedIndex.Value + 1);
                                    localFillBufferRef[i]
                                        = new KeyValuePair<long, TSource>(_sharedIndex.Value,
                                                                          _sharedReader.Current);
                                }
                                else
                                {
                                    // No more elements left in the enumerator.
                                    // Record this, so that the next request can skip the lock
                                    _sourceDepleted.Value = true;

                                    // also record the current count in _FillBufferSize
                                    _fillBufferSize = i;

                                    // and exit the for loop so that we don't keep incrementing _FillBufferSize
                                    break;
                                }
                            }

                            _fillBufferCurrentPosition = 0;
                        }
                    }
                    catch
                    {
                        // If an exception occurs, don't let the other enumerators try to enumerate.
                        // NOTE: this could instead throw an InvalidOperationException, but that would be unexpected
                        // and not helpful to the end user.  We know the root cause is being communicated already.)
                        _sourceDepleted.Value = true;
                        _hasNoElementsLeft.Value = true;
                        throw;
                    }
                }

                return (actualNumElementsGrabbed > 0);
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _sharedReader.Dispose();
                }
            }
        }

        /// <summary>
        /// Inherits from DynamicPartitionEnumerator_Abstract directly
        /// Provides customized implementation for: GrabNextChunk, HasNoElementsLeft, Current, Dispose
        /// </summary>
        private sealed class InternalPartitionEnumerator : DynamicPartitionEnumerator_Abstract<TSource, IEnumerator<TSource>>
        {
            //---- fields ----
            //cached local copy of the current chunk
            private KeyValuePair<long, TSource>[]? _localList; //defer allocating to avoid false sharing

            // the values of the following two fields are passed in from
            // outside(already initialized) by the constructor,
            private readonly SharedBool _hasNoElementsLeft;
            private readonly SharedInt? _activePartitionCount;
            private readonly InternalPartitionEnumerable _enumerable;

            //constructor
            internal InternalPartitionEnumerator(
                IEnumerator<TSource> sharedReader,
                SharedLong sharedIndex,
                SharedBool hasNoElementsLeft,
                SharedInt? activePartitionCount,
                InternalPartitionEnumerable enumerable,
                bool useSingleChunking)
                : base(sharedReader, sharedIndex, useSingleChunking)
            {
                _hasNoElementsLeft = hasNoElementsLeft;
                _enumerable = enumerable;
                _activePartitionCount = activePartitionCount;

                if (_activePartitionCount != null)
                {
                    // If static partitioning, we need to increase the active partition count.
                    Interlocked.Increment(ref _activePartitionCount.Value);
                }
            }

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

                if (HasNoElementsLeft)
                {
                    return false;
                }

                // defer allocation to avoid false sharing
                if (_localList == null)
                {
                    _localList = new KeyValuePair<long, TSource>[_maxChunkSize];
                }

                // make the actual call to the enumerable that grabs a chunk
                return _enumerable.GrabChunk(_localList, requestedChunkSize, ref _currentChunkSize!.Value);
            }

            /// <summary>
            /// Returns whether or not the shared reader has already read the last
            /// element of the source data
            /// </summary>
            /// <remarks>
            /// We cannot call _sharedReader.MoveNext(), to see if it hits the last element
            /// or not, because we can't undo MoveNext(). Thus we need to maintain a shared
            /// boolean value _hasNoElementsLeft across all partitions
            /// </remarks>
            protected override bool HasNoElementsLeft
            {
                get { return _hasNoElementsLeft.Value; }
            }

            public override KeyValuePair<long, TSource> Current
            {
                get
                {
                    //verify that MoveNext is at least called once before Current is called
                    if (_currentChunkSize == null)
                    {
                        throw new InvalidOperationException("Current called before ModeNext");
                    }
                    Debug.Assert(_localList != null);
                    Debug.Assert(_localOffset!.Value >= 0 && _localOffset.Value < _currentChunkSize.Value);
                    return (_localList![_localOffset.Value]);
                }
            }

            public override void Dispose()
            {
                // If this is static partitioning, i.e. _activePartitionCount != null, since the current partition
                // is disposed, we decrement the number of active partitions for the shared reader.
                if (_activePartitionCount != null && Interlocked.Decrement(ref _activePartitionCount.Value) == 0)
                {
                    // If the number of active partitions becomes 0, we need to dispose the shared
                    // reader we created in the _enumerable object.
                    _enumerable.Dispose();
                }
                // If this is dynamic partitioning, i.e. _activePartitionCount != null, then _enumerable needs to
                // be disposed explicitly by the user, and we do not need to anything here
            }
        }
        #endregion
    }
}
