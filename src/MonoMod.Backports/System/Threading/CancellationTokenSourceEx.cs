#if NET45_OR_GREATER || NETCOREAPP || NETSTANDARD || NET || !NET40_OR_GREATER
#define HAS_DELAY_CTOR
#endif
#if NET5_0_OR_GREATER || !NET40_OR_GREATER
#define HAS_CREATE_LTC1
#endif
#if NET9_0_OR_GREATER || !NET40_OR_GREATER
#define HAS_CREATE_LTC_SPAN
#endif

namespace System.Threading;

public static class CancellationTokenSourceEx
{
    extension(CancellationTokenSource)
    {
        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token)
        {
#if HAS_CREATE_LTC1
            return CancellationTokenSource.CreateLinkedTokenSource(token);
#else
            return CancellationTokenSource.CreateLinkedTokenSource([token]);
#endif
        }

        public static CancellationTokenSource CreateLinkedTokenSource(params ReadOnlySpan<CancellationToken> tokens)
        {
#if HAS_CREATE_LTC_SPAN
            return CancellationTokenSource.CreateLinkedTokenSource(tokens);
#else
            return CancellationTokenSource.CreateLinkedTokenSource(tokens.ToArray());
#endif
        }

        // TODO: consider implementing CancelAfter via CWT to timer or some such
    }
}
