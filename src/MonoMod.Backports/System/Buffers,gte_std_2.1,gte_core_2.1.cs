using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(IMemoryOwner<>))]
[assembly: TypeForwardedTo(typeof(IPinnable))]
[assembly: TypeForwardedTo(typeof(IBufferWriter<>))]
[assembly: TypeForwardedTo(typeof(MemoryHandle))]
[assembly: TypeForwardedTo(typeof(MemoryManager<>))]
[assembly: TypeForwardedTo(typeof(StandardFormat))]
[assembly: TypeForwardedTo(typeof(BuffersExtensions))]

[assembly: TypeForwardedTo(typeof(ReadOnlySequenceSegment<>))]
[assembly: TypeForwardedTo(typeof(ReadOnlySequence<>))]
[assembly: TypeForwardedTo(typeof(SequencePosition))]

[assembly: TypeForwardedTo(typeof(MemoryPool<>))]
[assembly: TypeForwardedTo(typeof(OperationStatus))]
[assembly: TypeForwardedTo(typeof(BinaryPrimitives))]
[assembly: TypeForwardedTo(typeof(Base64))]
[assembly: TypeForwardedTo(typeof(Utf8Formatter))]
[assembly: TypeForwardedTo(typeof(Utf8Parser))]

[assembly: TypeForwardedTo(typeof(ArrayPool<>))]
