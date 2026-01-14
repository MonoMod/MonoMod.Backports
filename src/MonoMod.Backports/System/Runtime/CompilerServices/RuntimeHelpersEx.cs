#if NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
#define HAS_ISREFORCONTAINSREF
#endif
#if NET40_OR_GREATER || NETCOREAPP || NETSTANDARD1_3_OR_GREATER
#define HAS_ENSUREEXECSTACK
#endif
#if NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
#define HAS_TRYENSUREEXECSTACK
#endif

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Serialization;

namespace System.Runtime.CompilerServices
{
    public static class RuntimeHelpersEx
    {
#if !HAS_ISREFORCONTAINSREF
        private static readonly ConditionalWeakTable<Type, StrongBox<bool>> _isReferenceCache = new();
        private static readonly StrongBox<bool> _boxFalse = new(false);
        private static readonly StrongBox<bool> _boxTrue = new(true);

        private static StrongBox<bool> GetIsRefOrContainsRef(Type t)
        {
            if (t.IsPrimitive || t.IsEnum || t.IsPointer)
            {
                return _boxFalse;
            }

            if (!t.IsValueType)
            {
                return _boxTrue;
            }

            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (_isReferenceCache.GetValue(field.FieldType, GetIsRefOrContainsRef).Value)
                {
                    return _boxTrue;
                }
            }

            return _boxFalse;
        }
#endif
        
        extension(RuntimeHelpers)
        {
#pragma warning disable SYSLIB0050
            public static object GetUninitializedObject(
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
                Type type) => FormatterServices.GetUninitializedObject(type);
#pragma warning restore SYSLIB0050

            public static bool IsReferenceOrContainsReferences<T>() =>
#if HAS_ISREFORCONTAINSREF
                RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#else
                _isReferenceCache.GetValue(typeof(T), GetIsRefOrContainsRef).Value;
#endif

            public static void EnsureSufficientExecutionStack()
            {
#if HAS_ENSUREEXECSTACK
                RuntimeHelpers.EnsureSufficientExecutionStack();
#endif
                // we do nothing
            }
            
            public static bool TryEnsureSufficientExecutionStack()
            {
#if HAS_TRYENSUREEXECSTACK
                return RuntimeHelpers.TryEnsureSufficientExecutionStack();
#elif HAS_ENSUREEXECSTACK
                try
                {
                    RuntimeHelpers.EnsureSufficientExecutionStack();
                }
                catch (InsufficientExecutionStackException)
                {
                    return false;
                }
                return true;
#else
                // just give up
                return true;
#endif
            }

            public static T[] GetSubArray<T>(T[] array, Range range)
            {
                var (offset, length) = range.GetOffsetAndLength(array.Length);
                T[] dest;
                if (typeof(T[]) == array.GetType())
                {
                    dest = new T[length];
                }
                else
                {
                    dest = Unsafe.As<T[]>(Array.CreateInstance(array.GetType().GetElementType()!, length));
                }
                
                Array.Copy(array, offset, dest, 0, length);

                return dest;
            }
        }
    }
}
