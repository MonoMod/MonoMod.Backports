#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER
#define HAS_STRING_COMPARISON
#endif

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace System
{
    public static class StringExtensions
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Replace(this string self, string oldValue, string newValue, StringComparison comparison)
        {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
            ThrowHelper.ThrowIfArgumentNull(oldValue, ExceptionArgument.oldValue);
            ThrowHelper.ThrowIfArgumentNull(newValue, ExceptionArgument.newValue);

#if HAS_STRING_COMPARISON
            return self.Replace(oldValue, newValue, comparison);
#else
            // we're gonna do a bit of tomfoolery
            var ish = new DefaultInterpolatedStringHandler(oldValue.Length, 0);
            var from = self.AsSpan();
            var old = oldValue.AsSpan();
            while (true)
            {
                var idx = from.IndexOf(old, comparison);
                if (idx < 0)
                {
                    ish.AppendFormatted(from);
                    break;
                }
                ish.AppendFormatted(from.Slice(0, idx));
                ish.AppendLiteral(newValue);
                from = from.Slice(idx + old.Length);
            }
            return ish.ToStringAndClear();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this string self, string value, StringComparison comparison)
        {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
            ThrowHelper.ThrowIfArgumentNull(value, ExceptionArgument.value);
#if HAS_STRING_COMPARISON
            return self.Contains(value, comparison);
#else
            return self.IndexOf(value, comparison) >= 0;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this string self, char value, StringComparison comparison)
        {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
#if HAS_STRING_COMPARISON
            return self.Contains(value, comparison);
#else
            return self.IndexOf(value, comparison) >= 0;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHashCode(this string self, StringComparison comparison)
        {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
#if HAS_STRING_COMPARISON
            return self.GetHashCode(comparison);
#else

            return StringComparerEx.FromComparison(comparison).GetHashCode(self);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf(this string self, char value, StringComparison comparison)
        {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
#if HAS_STRING_COMPARISON
            return self.IndexOf(value, comparison);
#else
            return self.IndexOf(new string(value, 1), comparison);
#endif
        }

        public static void CopyTo(this string self, Span<char> destination)
        {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
            self.AsSpan().CopyTo(destination);
        }

        public static bool TryCopyTo(this string self, Span<char> destination)
        {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
            return self.AsSpan().TryCopyTo(destination);
        }

        private const string NewLineChars = "\n\r\f\u0085\u2028\u2029";

        public static string ReplaceLineEndings(this string self) => self.ReplaceLineEndings(Environment.NewLine);
        
        public static string ReplaceLineEndings(this string self, string replacementText)
        {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
            int idxOfFirstNewlineChar = IndexOfNewlineChar(self, replacementText, out int stride);
            if (idxOfFirstNewlineChar < 0)
            {
                return self;
            }

            var firstSegment = self.AsSpan(0, idxOfFirstNewlineChar);
            var remaining = self.AsSpan(idxOfFirstNewlineChar + stride);

            var builder = new StringBuilder();
            while (true)
            {
                var idx = IndexOfNewlineChar(remaining, replacementText, out stride);
                if (idx < 0) { break; }
                builder.Append(replacementText);
                builder.Append(remaining.Slice(0, idx));
                remaining = remaining.Slice(idx + stride);
            }

            return string.Concat(firstSegment, builder.ToString(), replacementText, remaining);
        }
        
        private static int IndexOfNewlineChar(ReadOnlySpan<char> text, string replacementText, out int stride)
        {
            stride = default;
            int offset = 0;

            while (true)
            {
                int idx = text.IndexOfAny(NewLineChars);

                if ((uint)idx >= (uint)text.Length)
                {
                    return -1;
                }

                offset += idx;
                stride = 1; // needle found

                // Did we match CR? If so, and if it's followed by LF, then we need
                // to consume both chars as a single newline function match.

                if (text[idx] == '\r')
                {
                    int nextCharIdx = idx + 1;
                    if ((uint)nextCharIdx < (uint)text.Length && text[nextCharIdx] == '\n')
                    {
                        stride = 2;

                        if (replacementText != "\r\n")
                        {
                            return offset;
                        }
                    }
                    else if (replacementText != "\r")
                    {
                        return offset;
                    }
                }
                else if (replacementText.Length != 1 || replacementText[0] != text[idx])
                {
                    return offset;
                }

                offset += stride;
                text = text.Slice(idx + stride);
            }
        }

        extension(string)
        {
            /// <summary>Creates a new string by using the specified provider to control the formatting of the specified interpolated string.</summary>
            /// <param name="provider">An object that supplies culture-specific formatting information.</param>
            /// <param name="handler">The interpolated string.</param>
            /// <returns>The string that results for formatting the interpolated string using the specified format provider.</returns>
            public static string Create(IFormatProvider? provider, [InterpolatedStringHandlerArgument(nameof(provider))] ref DefaultInterpolatedStringHandler handler) =>
                handler.ToStringAndClear();

            /// <summary>Creates a new string by using the specified provider to control the formatting of the specified interpolated string.</summary>
            /// <param name="provider">An object that supplies culture-specific formatting information.</param>
            /// <param name="initialBuffer">The initial buffer that may be used as temporary space as part of the formatting operation. The contents of this buffer may be overwritten.</param>
            /// <param name="handler">The interpolated string.</param>
            /// <returns>The string that results for formatting the interpolated string using the specified format provider.</returns>
            public static string Create(IFormatProvider? provider, Span<char> initialBuffer, [InterpolatedStringHandlerArgument(nameof(provider), nameof(initialBuffer))] ref DefaultInterpolatedStringHandler handler) =>
                handler.ToStringAndClear();

            public static string Create<TState>(int length, TState state, SpanAction<char, TState> action)
            {
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                return string.Create(length, state, action);
#else
                ThrowHelper.ThrowIfArgumentNull(action, ExceptionArgument.action);
                if (length <= 0)
                {
                    if (length == 0)
                    {
                        return string.Empty;
                    }
                    
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);
                }
                var str = new string('\0', length);
                unsafe
                {
                    fixed (char* p = str)
                    {
                        action(new Span<char>(p, length), state);
                    }
                }
                return str;
#endif
            }

            public static unsafe string Concat(params ReadOnlySpan<string> values)
            {
#if NET9_0_OR_GREATER
                return string.Concat(values);
#else
                var sum = 0;
                foreach (var s in values)
                {
                    sum += s.Length;
                }

                return string.Create(sum,
                    (nint)(&values),
                    (span, state) =>
                    {
                        var values = *(ReadOnlySpan<string>*)state;
                        var offset = 0;
                        foreach (var s in values)
                        {
                            s.AsSpan().CopyTo(span.Slice(offset));
                            offset += s.Length;
                        }
                    });
#endif
            }
            
            public static string Concat(params ReadOnlySpan<object?> args)
            {
#if NET9_0_OR_GREATER
                return string.Concat(args);
#else
                if (args.Length <= 1)
                {
                    return args.IsEmpty ?
                        string.Empty :
                        args[0]?.ToString() ?? string.Empty;
                }
                
                var strings = new string[args.Length];

                for (var i = 0; i < args.Length; i++)
                {
                    var value = args[i];

                    strings[i] = value?.ToString() ?? string.Empty;
                }

                return string.Concat(strings);
#endif
            }
            
            public static unsafe string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1)
            {
#if NETCOREAPP3_0_OR_GREATER
                return string.Concat(str0, str1);
#else
                var holder = new ReadOnlySpanHolder
                {
                    _1 = str0,
                    _2 = str1,
                };
                return string.Create(str0.Length + str1.Length,
                    (nint)(&holder),
                    (span, state) =>
                    {
                        var holder = (ReadOnlySpanHolder*)state;
                        holder->_1.CopyTo(span);
                        holder->_2.CopyTo(span.Slice(holder->_1.Length));
                    });
#endif
            }
            
            public static unsafe string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1, ReadOnlySpan<char> str2)
            {
#if NETCOREAPP3_0_OR_GREATER
                return string.Concat(str0, str1, str2);
#else
                var holder = new ReadOnlySpanHolder
                {
                    _1 = str0,
                    _2 = str1,
                    _3 = str2,
                };
                return string.Create(str0.Length + str1.Length + str2.Length,
                    (nint)(&holder),
                    (span, state) =>
                    {
                        var holder = (ReadOnlySpanHolder*)state;
                        holder->_1.CopyTo(span);
                        holder->_2.CopyTo(span.Slice(holder->_1.Length));
                        holder->_3.CopyTo(span.Slice(holder->_1.Length + holder->_2.Length));
                    });
#endif
            }

            public static unsafe string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1, ReadOnlySpan<char> str2, ReadOnlySpan<char> str3)
            {
#if NETCOREAPP3_0_OR_GREATER
                return string.Concat(str0, str1, str2, str3);
#else
                var holder = new ReadOnlySpanHolder
                {
                    _1 = str0,
                    _2 = str1,
                    _3 = str2,
                    _4 = str3,
                };
                return string.Create(str0.Length + str1.Length + str2.Length + str3.Length,
                    (nint)(&holder),
                    (span, state) =>
                    {
                        var holder = (ReadOnlySpanHolder*)state;
                        holder->_1.CopyTo(span);
                        holder->_2.CopyTo(span.Slice(holder->_1.Length));
                        holder->_3.CopyTo(span.Slice(holder->_1.Length + holder->_2.Length));
                        holder->_4.CopyTo(span.Slice(holder->_1.Length + holder->_2.Length + holder->_3.Length));
                    });
#endif
            }
        }

        private ref struct ReadOnlySpanHolder
        {
            public ReadOnlySpan<char> _1;
            public ReadOnlySpan<char> _2;
            public ReadOnlySpan<char> _3;
            public ReadOnlySpan<char> _4;
        }
    }
}
