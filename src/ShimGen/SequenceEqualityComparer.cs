using System.Diagnostics.CodeAnalysis;

namespace ShimGen;

internal sealed class SequenceEqualityComparer<T> : EqualityComparer<IEnumerable<T>>
    where T : IEquatable<T>
{
    public static readonly SequenceEqualityComparer<T> Instance = new();

    public override bool Equals(IEnumerable<T>? x, IEnumerable<T>? y)
    {
        if (x == y) return true;
        if (x is null || y is null) return false;

        if (x.TryGetNonEnumeratedCount(out var xct)
            && y.TryGetNonEnumeratedCount(out var yct)
            && xct != yct)
            return false;

        return x.SequenceEqual(y);
    }

    public override int GetHashCode([DisallowNull] IEnumerable<T> obj)
    {
        var hc = new HashCode();

        foreach (var val in obj)
        {
            hc.Add(val.GetHashCode());
        }

        return hc.ToHashCode();
    }
}
