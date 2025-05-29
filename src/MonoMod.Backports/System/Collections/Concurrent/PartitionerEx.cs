#if !NET40_OR_GREATER || NET45_OR_GREATER || NETSTANDARD || NETCOREAPP || NET
#define HAS_ENUMERABLEPARTITIONEROPTIONS
#endif

using System.Collections.Generic;

namespace System.Collections.Concurrent
{
    public static class PartitionerEx
    {
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
#if HAS_ENUMERABLEPARTITIONEROPTIONS
            return Partitioner.Create(source, partitionerOptions);
#else
            if (source is null)
                ThrowHelper.ThrowArgumentNullException(nameof(source));
            if ((partitionerOptions & (~EnumerablePartitionerOptions.NoBuffering)) != 0)
                throw new ArgumentOutOfRangeException(nameof(partitionerOptions));

            return new DynamicPartitionerForIEnumerable<TSource>(source, partitionerOptions);
#endif
        }
    }
}
