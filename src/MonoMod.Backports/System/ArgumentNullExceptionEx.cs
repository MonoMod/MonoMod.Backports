#if NET6_0_OR_GREATER
#define HAS_THROW_HELPERS
#endif

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System;

public static class ArgumentNullExceptionEx
{
    extension(ArgumentNullException)
    {
        [StackTraceHidden]
        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
#if HAS_THROW_HELPERS
            ArgumentNullException.ThrowIfNull(argument, paramName);
#else
            if (argument is null)
            {
                ThrowHelper.ThrowArgumentNullException(paramName ?? nameof(argument));
            }
#endif
        }
    }
}
