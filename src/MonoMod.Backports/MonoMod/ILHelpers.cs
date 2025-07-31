using InlineIL;
using System.Runtime.CompilerServices;
using static InlineIL.IL;
using static InlineIL.IL.Emit;

namespace MonoMod
{
    internal static class ILHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T ObjectAsRef<T>(object? obj)
        {
            DeclareLocals(init: false, [
                new LocalVar("pin", typeof(object)).Pinned(),
#if !NETCOREAPP
                new LocalVar("refPtr", typeof(T).MakePointerType().MakePointerType()),
                new LocalVar("finalRef", typeof(T).MakeByRefType()),
#endif
            ]);

            // pin obj
            Ldarg(nameof(obj));
            Stloc("pin");

#if NETCOREAPP
            // return ref *Unsafe.BitCast<object, T*>(pin);
            Ldloc("pin");
            Conv_U();
#else
            // see docs/RuntimeIssueNotes.md - "`fixed` on strings in old Mono" for why this is necessary
            // T* ptr = *(T**)(&pin);
            Ldloca("pin");
            Conv_U();
            Stloc("refPtr");
            Ldloc("refPtr");
            Ldind_I();
            // return Unsafe.AsRef<T>(ptr);
            // see the comments inside that function for why don't just immediately ret
            Stloc("finalRef");
            Ldloc("finalRef");
#endif

            return ref ReturnRef<T>();
        }
    }
}
