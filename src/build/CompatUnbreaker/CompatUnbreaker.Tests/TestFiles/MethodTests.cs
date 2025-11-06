using System.Diagnostics.CodeAnalysis;

namespace CompatUnbreaker.Tests.TestFiles;

public sealed class MethodTests
{
    public static extern void Method();

    public static extern ref readonly int AllTheRefs(ref int @ref, ref readonly int refReadonly, in int @in, out int @out, scoped ref int scopedRef, [UnscopedRef] ref int unscopedRef, scoped Span<int> scopedSpan);

    public static extern void ScopedGeneric<T>(scoped T a) where T : unmanaged, allows ref struct;

    public static extern T Generic<T>(T t) where T : class, IDisposable, new();

    public static extern void Array(int[] a, int[][] b, int[,] c);

    public static extern void Params(params int[] a);

    public static extern void ParamsSpan(params ReadOnlySpan<int> a);

    public static extern void Default(int a = 123);

    // TODO needs RequiresUnsafeModifier implemented
    // public static extern unsafe void Pointer(int* a);
    // public static extern unsafe void FunctionPointer(delegate*<int, int> a, delegate* unmanaged[Stdcall, SuppressGCTransition]<int, int> b);
}
