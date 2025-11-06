namespace CompatUnbreaker.Tests.TestFiles;

public static class ReservedTests
{
    public static extern dynamic Dynamic(dynamic a, dynamic[] b, (dynamic, object, int, dynamic) c);

    public static extern ref readonly int ReadOnly();

    public static extern void RequiredLocation(ref readonly int a);

    public static extern void IsUnmanaged<T>() where T : unmanaged;

    public static extern (int A, ((int E, int F) C, int D) B) TupleElementNames();

    public static extern object? Nullable<T>(object? a, object b, object? c, object d, T? e, object?[][]? f);

    public static extern void Extension(this object o);

    public ref struct IsByRefLike;

    public class Generics<A, B, C, D>
        where A : notnull
        where B : class
        where C : class?
    {
        public required A a;
    }
}
