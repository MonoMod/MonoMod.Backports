using System.Diagnostics.CodeAnalysis;
using AsmResolver.DotNet;

namespace CompatUnbreaker.Models.Attributes;

internal static class AttributeDescriptionExtensions
{
    public static IEnumerable<TData> Find<TData>(this IHasCustomAttribute provider, AttributeDescription<TData> attributeDescription)
    {
        foreach (var attribute in provider.CustomAttributes)
        {
            var declaringType = attribute.Type;
            if (declaringType is null)
                continue;

            if (declaringType.Namespace == attributeDescription.Namespace && declaringType.Name == attributeDescription.Name)
            {
                yield return attributeDescription.CreateData(attribute);
            }
        }
    }

    public static bool Has<TData>(this IHasCustomAttribute provider, AttributeDescription<TData> attributeDescription)
    {
        return provider.Find(attributeDescription).Any();
    }

    public static bool TryFindSingle<TData>(this IHasCustomAttribute provider, AttributeDescription<TData> attributeDescription, [MaybeNullWhen(false)] out TData result)
    {
        return provider.Find(attributeDescription).TryGetSingle(out result);
    }

    private static bool TryGetSingle<TSource>(this IEnumerable<TSource> source, [MaybeNullWhen(false)] out TSource result)
    {
        ArgumentNullException.ThrowIfNull(source);

        using (var e = source.GetEnumerator())
        {
            if (!e.MoveNext())
            {
                result = default;
                return false;
            }

            result = e.Current;
            if (!e.MoveNext())
            {
                return true;
            }
        }

        throw new InvalidOperationException("Sequence contains more than one element");
    }
}
