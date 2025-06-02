#if NETSTANDARD || NETCOREAPP || NET || NET40_OR_GREATER
#define HAS_OCE_CT
#endif

#if !HAS_OCE_CT
using System.Runtime.Serialization;
#endif
using System.Threading;

namespace System;

[Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Analyzer does not understand extension everything yet.")]
public static class OperationCanceledExceptionEx
{
    extension(OperationCanceledException self)
    {
        public CancellationToken CancellationToken => self.GetCancellationToken();

        // note: we expose this for older langver consumers
        [Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate",
            Justification = "We do. This is a variant provided for consumers not using C# 14.")]
        public CancellationToken GetCancellationToken()
#if HAS_OCE_CT
            => self is not null ? self.CancellationToken : default;
#else
            => self is WithCancellationToken wct ? wct.CancellationToken : default;
#endif

        // constructors which exist in new versions
        public static OperationCanceledException Create(CancellationToken token)
#if HAS_OCE_CT
        => new(token);
#else
            => new WithCancellationToken(token);
#endif

        public static OperationCanceledException Create(string? message, CancellationToken token)
#if HAS_OCE_CT
        => new(message, token);
#else
            => new WithCancellationToken(message, token);
#endif

        public static OperationCanceledException Create(string? message, Exception? innerException, CancellationToken token)
#if HAS_OCE_CT
        => new(message, innerException, token);
#else
            => new WithCancellationToken(message, innerException, token);
#endif

        // other existant constructors, available for consistency
        public static OperationCanceledException Create() => new();
        public static OperationCanceledException Create(string? message) => new(message);
        public static OperationCanceledException Create(string? message, Exception? innerException) => new(message, innerException);

    }

#if !HAS_OCE_CT
    private sealed class WithCancellationToken : OperationCanceledException
    {
        public WithCancellationToken()
        {
        }

        public WithCancellationToken(CancellationToken token)
        {
            CancellationToken = token;
        }

        public WithCancellationToken(string? message) : base(message)
        {
        }

        public WithCancellationToken(string? message, CancellationToken token) : base(message)
        {
            CancellationToken = token;
        }

        public WithCancellationToken(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public WithCancellationToken(string? message, Exception? innerException,  CancellationToken token) : base(message, innerException)
        {
            CancellationToken = token;
        }

        public WithCancellationToken(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public CancellationToken CancellationToken { get; }

    }
#endif
}
