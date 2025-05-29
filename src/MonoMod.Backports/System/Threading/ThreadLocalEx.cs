#if !NET40_OR_GREATER || NET45_OR_GREATER || NETSTANDARD || NETCOREAPP || NET
#define HAS_ALLVALUES
#endif

using System.Collections.Generic;
#if !HAS_ALLVALUES
using System.Reflection.Emit;
#endif

namespace System.Threading;

public static class ThreadLocalEx
{
#if !HAS_ALLVALUES
    private sealed class ThreadLocalInfo<T>
    {
        public delegate ThreadLocal<T> CreateBool(bool trackAllValues);
        public delegate ThreadLocal<T> CreateFuncBool(Func<T> func, bool trackAllValues);
        public delegate IList<T> GetValues(ThreadLocal<T> threadLocal);

        public static readonly ThreadLocalInfo<T> Info = new();

        public readonly CreateBool? CreateBoolDel;
        public readonly CreateFuncBool? CreateFuncBoolDel;
        public readonly GetValues? GetValuesDel;

        private ThreadLocalInfo()
        {
            var ctor1 = typeof(ThreadLocal<T>).GetConstructor([typeof(bool)]);
            var ctor2 = typeof(ThreadLocal<T>).GetConstructor([typeof(Func<T>), typeof(bool)]);

            if (ctor1 is not null)
            {
                var dm = new DynamicMethod($"new {typeof(ThreadLocal<T>)}(bool)", typeof(ThreadLocal<T>), [typeof(bool)]);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Newobj, ctor1);
                il.Emit(OpCodes.Ret);
                CreateBoolDel = (CreateBool)dm.CreateDelegate(typeof(CreateBool));
            }

            if (ctor2 is not null)
            {
                var dm = new DynamicMethod($"new {typeof(ThreadLocal<T>)}(Func<T>, bool)", typeof(ThreadLocal<T>), [typeof(Func<T>), typeof(bool)]);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Newobj, ctor2);
                il.Emit(OpCodes.Ret);
                CreateFuncBoolDel = (CreateFuncBool)dm.CreateDelegate(typeof(CreateFuncBool));
            }

            if (typeof(ThreadLocal<T>).GetProperty("Values")?.GetGetMethod() is { } mi)
            {
                GetValuesDel = (GetValues)Delegate.CreateDelegate(typeof(GetValues), mi);
            }
        }
    }
    private static void ThrowTrackAllValuesNotSupported()
        => ThrowHelper.ThrowNotSupportedException("trackAllValues is not supported on this platform");
#endif
    
    public static bool SupportsAllValues
#if HAS_ALLVALUES
        => true;
#else
        => false;
#endif

    extension<T>(ThreadLocal<T>)
    {
        public static ThreadLocal<T> Create(bool trackAllValues)
        {
#if HAS_ALLVALUES
            return new(trackAllValues);
#else
            if (ThreadLocalInfo<T>.Info.CreateBoolDel is { } create)
            {
                return create(trackAllValues);
            }

            if (trackAllValues)
            {
                ThrowTrackAllValuesNotSupported();
            }

            return new();
#endif
        }

        public static ThreadLocal<T> Create(Func<T> valueFactory, bool trackAllValues)
        {
#if HAS_ALLVALUES
            return new(trackAllValues);
#else
            if (ThreadLocalInfo<T>.Info.CreateFuncBoolDel is { } create)
            {
                return create(valueFactory, trackAllValues);
            }

            if (trackAllValues)
            {
                ThrowTrackAllValuesNotSupported();
            }

            return new(valueFactory);
#endif
        }
    }

    public static IList<T> Values<T>(this ThreadLocal<T> threadLocal)
    {
        ThrowHelper.ThrowIfArgumentNull(threadLocal, ExceptionArgument.threadLocal);

#if HAS_ALLVALUES
        return threadLocal.Values;
#else
        if (ThreadLocalInfo<T>.Info.GetValuesDel is { } getValues)
        {
            return getValues(threadLocal);
        }
        else
        {
            ThrowHelper.ThrowNotSupportedException("Values is not supported on this platform");
            return null!;
        }
#endif
    }

    extension<T>(ThreadLocal<T> self)
    {
        public IList<T> Values => self.Values();
    }

    extension<T>(ThreadLocal<T>)
    {
#if false // when extension constructors actually exist
        public static ThreadLocal<T>(bool trackAllValues)
            => Create<T>(trackAllValues);
        public static ThreadLocal<T>(Func<T> valueFactory, bool trackAllValues)
            => Create<T>(valueFactory, trackAllValues);
#endif
    }
}
