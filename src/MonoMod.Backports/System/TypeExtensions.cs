#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
#define HAS_ISBYREFLIKE
#endif

using System.Runtime.CompilerServices;

namespace System
{
    public static class TypeExtensions
    {
        [OverloadResolutionPriority(-1)]
        public static bool IsByRefLike(this Type type)
        {
            ThrowHelper.ThrowIfArgumentNull(type, ExceptionArgument.type);
            if (type is null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.type);

#if HAS_ISBYREFLIKE
            return type.IsByRefLike;
#else
            // TODO: cache this information somehow
            foreach (var attr in type.GetCustomAttributes(false))
            {
                if (attr.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute")
                    return true;
            }

            return false;
#endif
        }

        // note: currently, this property is not accessible: https://github.com/dotnet/roslyn/issues/78753
        extension(Type type)
        {
            [Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
                Justification = "It must be non-static, analyzer doesn't understand extensions yet.")]
            public bool IsByRefLike => type.IsByRefLike();
        }
    }
}
