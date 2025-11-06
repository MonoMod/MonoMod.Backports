namespace CompatUnbreaker.Tool.Utilities;

internal static class StringExtensions
{
    public static string TrimPrefix(this string text, string? prefix)
    {
        if (!string.IsNullOrEmpty(prefix) && text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return text[prefix.Length..];
        }

        return text;
    }
}
