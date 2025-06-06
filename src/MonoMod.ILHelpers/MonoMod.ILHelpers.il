#include "version.h"

#ifdef NET35
#define FUNC_ASSEMBLY "System.Core"
.assembly extern FUNC_ASSEMBLY
{
  .publickeytoken = CORE_PUBKEY_TOKEN
  .ver 3:5:0:0
}
#else
#define FUNC_ASSEMBLY "CORE_ASSEMBLY"
#endif

.class private abstract auto ansi sealed beforefieldinit MonoMod.ILHelpers
       extends [CORE_ASSEMBLY]System.Object
{   
    .method public hidebysig static !!T& ObjectAsRef<T>(object obj) cil managed aggressiveinlining
    {
        .maxstack 1
        .locals (
            object pinned pin
#ifndef netcoreapp
            , !!T** refPtr
            , !!T& finalRef
#endif
        )

        // pin obj
        ldarg obj
        stloc pin

#ifdef netcoreapp
        // return ref *Unsafe.BitCast<object, T*>(pin);
        ldloc pin
        conv.u
#else
        // see docs/RuntimeIssueNotes.md - "`fixed` on strings in old Mono" for why this is necessary
        // T* ptr = *(T**)(&pin);
        ldloca pin
        conv.u
        stloc refPtr
        ldloc refPtr
        ldind.i
        // return Unsafe.AsRef<T>(ptr);
        // see the comments inside that function for why don't just immediately ret
        stloc finalRef
        ldloc finalRef
#endif

        ret
    }
}