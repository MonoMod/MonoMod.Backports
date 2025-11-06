namespace CompatUnbreaker.Tests.TestFiles;

public sealed class NamedTupleTests
{
    public
        (
        (object? aa1, object aa2) a1, object a2, object a3, object a4, object a5, object a6, object a7, object a8,
        object b1, (object ba1, object ba2) b2, object b3, object b4, object? b5, object b6, object b7, object b8,
        object c1, object c2, (object ca1, object ca2) c3, object c4, object c5, object c6, object c7, object c8,
        object d1, object d2, object d3, (object da1, object? da2) d4, object d5, object d6, object d7, object d8
        ) longgg;

    public (object, object) unnamed;
}
