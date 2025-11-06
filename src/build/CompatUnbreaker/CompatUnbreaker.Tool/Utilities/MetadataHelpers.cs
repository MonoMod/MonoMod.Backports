using System.Diagnostics;

namespace CompatUnbreaker.Tool.Utilities;

// Copied from https://github.com/dotnet/roslyn/blob/7c625024a1984d9f04f317940d518402f5898758/src/Compilers/Core/Portable/MetadataReader/MetadataHelpers.cs

internal static class MetadataHelpers
{
    private const char GenericTypeNameManglingChar = '`';
    private const int MaxStringLengthForParamSize = 22;

    private static short InferTypeArityFromMetadataName(ReadOnlySpan<char> emittedTypeName, out int suffixStartsAt)
    {
        Debug.Assert(!emittedTypeName.IsEmpty, "NULL actual name unexpected!!!");
        var emittedTypeNameLength = emittedTypeName.Length;

        int indexOfManglingChar;
        for (indexOfManglingChar = emittedTypeNameLength; indexOfManglingChar >= 1; indexOfManglingChar--)
        {
            if (emittedTypeName[indexOfManglingChar - 1] == GenericTypeNameManglingChar)
            {
                break;
            }
        }

        if (indexOfManglingChar < 2 ||
            emittedTypeNameLength - indexOfManglingChar == 0 ||
            emittedTypeNameLength - indexOfManglingChar > MaxStringLengthForParamSize)
        {
            suffixStartsAt = -1;
            return 0;
        }

        // Given a name corresponding to <unmangledName>`<arity>, extract the arity.
        if (TryScanArity(emittedTypeName[indexOfManglingChar..]) is not { } arity)
        {
            suffixStartsAt = -1;
            return 0;
        }

        suffixStartsAt = indexOfManglingChar - 1;
        return arity;

        static short? TryScanArity(ReadOnlySpan<char> aritySpan)
        {
            // Arity must have at least one character and must not have leading zeroes.
            // Also, in order to fit into short.MaxValue (32767), it must be at most 5 characters long.
            if (aritySpan is { Length: >= 1 and <= 5 } and not ['0', ..])
            {
                var intArity = 0;
                foreach (var digit in aritySpan)
                {
                    // Accepting integral decimal digits only
                    if (digit is < '0' or > '9')
                        return null;

                    intArity = (intArity * 10) + (digit - '0');
                }

                Debug.Assert(intArity > 0);

                if (intArity <= short.MaxValue)
                    return (short) intArity;
            }

            return null;
        }
    }

    public static ReadOnlySpan<char> InferTypeArityAndUnmangleMetadataName(ReadOnlySpan<char> emittedTypeName, out short arity)
    {
        arity = InferTypeArityFromMetadataName(emittedTypeName, out var suffixStartsAt);

        if (arity == 0)
        {
            Debug.Assert(suffixStartsAt == -1);
            return emittedTypeName;
        }

        Debug.Assert(suffixStartsAt > 0 && suffixStartsAt < emittedTypeName.Length - 1);
        return emittedTypeName[..suffixStartsAt];
    }

    public static ReadOnlySpan<char> UnmangleMetadataNameForArity(ReadOnlySpan<char> emittedTypeName, int arity)
    {
        Debug.Assert(arity > 0);

        if (arity == InferTypeArityFromMetadataName(emittedTypeName, out var suffixStartsAt))
        {
            Debug.Assert(suffixStartsAt > 0 && suffixStartsAt < emittedTypeName.Length - 1);
            return emittedTypeName[..suffixStartsAt];
        }

        return emittedTypeName;
    }
}
