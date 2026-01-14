using InlineIL;
using System.Runtime.CompilerServices;

#if !NET6_0_OR_GREATER
using System.Reflection;
#endif

namespace System.Runtime.InteropServices
{
    public static class MarshalEx
    {
#if !NET6_0_OR_GREATER
        private static readonly MethodInfo? Marshal_SetLastWin32Error_Meth
            = typeof(Marshal).GetMethod("SetLastPInvokeError", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? typeof(Marshal).GetMethod("SetLastWin32Error", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly Action<int>? Marshal_SetLastWin32Error = Marshal_SetLastWin32Error_Meth is null
            ? null
            : (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), Marshal_SetLastWin32Error_Meth);
#endif

        extension(Marshal)
        {
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public static int GetLastPInvokeError()
#if NET6_0_OR_GREATER
                => Marshal.GetLastPInvokeError();
#else
                => Marshal.GetLastWin32Error();
#endif

#if NET6_0_OR_GREATER
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
#endif
            public static void SetLastPInvokeError(int error)
            {
#if NET6_0_OR_GREATER
                Marshal.SetLastPInvokeError(error);
#else
                if (Marshal_SetLastWin32Error is not { } del)
                    throw new PlatformNotSupportedException("Cannot set last P/Invoke error (no method Marshal.SetLastWin32Error or Marshal.SetLastPInvokeError)");
                del(error);
#endif
            }

            public static void InitHandle(SafeHandle safeHandle, nint handle)
            {
                // this method always exists and is protected, we just need to call it
                IL.Push(safeHandle);
                IL.Push(handle);
                IL.Emit.Call(MethodRef.Method(typeof(SafeHandle), "SetHandle", typeof(nint)));
            }
        }
    }
}
