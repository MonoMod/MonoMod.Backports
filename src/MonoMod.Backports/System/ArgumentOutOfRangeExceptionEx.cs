#if NET7_0_OR_GREATER
#define HAS_INUMBER
#endif

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
#if HAS_INUMBER
using System.Numerics;
#endif

namespace System;

[CLSCompliant(false)]
public static class ArgumentOutOfRangeExceptionEx
{
    extension(ArgumentOutOfRangeException)
    {
        public static void ThrowIfNegativeOrZero(byte value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(sbyte value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(short value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(ushort value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(uint value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(long value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(ulong value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(nint value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(nuint value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(float value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(double value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
        public static void ThrowIfNegativeOrZero(decimal value, [CallerArgumentExpression(nameof(value))] string? paramName = null) => DoThrowIfNegativeOrZero(value, paramName);
    }

    [DoesNotReturn]
    private static void ThrowNegativeOrZero(string? paramName, object? value)
        => throw new ArgumentOutOfRangeException(paramName, value, null);

#if HAS_INUMBER
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoThrowIfNegativeOrZero<T>(T value, string? paramName)
        where T : INumberBase<T>
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName);
#else
        if (T.IsNegative(value) || T.IsZero(value)) ThrowNegativeOrZero(paramName, value);
#endif
    }
#else
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoThrowIfNegativeOrZero(int value, string? paramName)
    {
        if (value <= 0) ThrowNegativeOrZero(paramName, value);
    }

    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoThrowIfNegativeOrZero(long value, string? paramName)
    {
        if (value <= 0) ThrowNegativeOrZero(paramName, value);
    }

    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoThrowIfNegativeOrZero(uint value, string? paramName)
    {
        if (value <= 0) ThrowNegativeOrZero(paramName, value);
    }

    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoThrowIfNegativeOrZero(ulong value, string? paramName)
    {
        if (value <= 0) ThrowNegativeOrZero(paramName, value);
    }

    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoThrowIfNegativeOrZero(float value, string? paramName)
    {
        if (value <= 0) ThrowNegativeOrZero(paramName, value);
    }

    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoThrowIfNegativeOrZero(double value, string? paramName)
    {
        if (value <= 0) ThrowNegativeOrZero(paramName, value);
    }

    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoThrowIfNegativeOrZero(decimal value, string? paramName)
    {
        if (value <= 0) ThrowNegativeOrZero(paramName, value);
    }
#endif
}
