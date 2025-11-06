using System.Runtime.InteropServices;

namespace CompatUnbreaker.Tests.TestFiles;

public static class DllImportTests
{
    [DllImport("cat")]
    public static extern void Meow();

    [DllImport("cat", EntryPoint = "meow", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.FastCall, BestFitMapping = false, PreserveSig = false, ThrowOnUnmappableChar = false)]
    public static extern void MeowFull();
}
