using System.Text;
using Microsoft.CodeAnalysis;

namespace CompatUnbreaker.Tool.Utilities.Roslyn;

internal static class TypeSymbolExtensions
{
    public static IEnumerable<ITypeSymbol> GetAllTypes(this INamespaceOrTypeSymbol symbol)
    {
        var queue = new Queue<INamespaceOrTypeSymbol>();
        queue.Enqueue(symbol);

        while (queue.Count > 0)
        {
            var member = queue.Dequeue();

            if (member is INamespaceSymbol namespaceSymbol)
            {
                foreach (var namespaceMember in namespaceSymbol.GetMembers())
                {
                    queue.Enqueue(namespaceMember);
                }
            }
            else if (member is ITypeSymbol namedTypeSymbol)
            {
                yield return namedTypeSymbol;

                foreach (var typeMember in namedTypeSymbol.GetTypeMembers())
                {
                    queue.Enqueue(typeMember);
                }
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
    }

    public static string GetFullMetadataName(this ITypeSymbol s)
    {
        var stringBuilder = new StringBuilder(s.MetadataName);
        var current = s.ContainingSymbol;

        while (current is not INamespaceSymbol { IsGlobalNamespace: true })
        {
            if (current is ITypeSymbol)
            {
                stringBuilder.Insert(0, "+");
            }
            else if (current is INamespaceSymbol)
            {
                stringBuilder.Insert(0, ".");
            }

            stringBuilder.Insert(0, current.MetadataName);
            current = current.ContainingSymbol;
        }

        return stringBuilder.ToString();
    }
}
