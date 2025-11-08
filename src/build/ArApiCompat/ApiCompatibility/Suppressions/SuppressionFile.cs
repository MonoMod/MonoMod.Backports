using ArApiCompat.ApiCompatibility.Comparing;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace ArApiCompat.ApiCompatibility.Suppressions;

internal sealed class SuppressionFile
{
    public sealed class Comparison
    {
        public string? Left { get; set; }
        public string? Right { get; set; }

        public List<Suppression> Suppressions { get; } = new();

        public Suppression? GetSuppressionFor(Suppression suppression)
            => Suppressions.FirstOrDefault(s => s == suppression);
    }

    public sealed record Suppression
    {
        public DifferenceType DifferenceType { get; set; }
        public string? TypeName { get; set; }
        public string? Message { get; set; }
    }

    public List<Comparison> Comparisons { get; } = new();

    public Comparison? GetComparison(string? left, string? right)
        => Comparisons.FirstOrDefault(c => c.Left == left && c.Right == right);

    public void Sort()
    {
        Comparisons.Sort((a, b)
            => StringComparer.Ordinal.Compare(a.Left, b.Left) * 2
             + StringComparer.Ordinal.Compare(a.Right, b.Right));

        foreach (var comparison in Comparisons)
        {
            comparison.Suppressions.Sort((a, b)
                => a.DifferenceType.CompareTo(b.DifferenceType) * 4
                 + StringComparer.Ordinal.Compare(a.TypeName, b.TypeName) * 2
                 + StringComparer.Ordinal.Compare(a.Message, b.Message));
        }
    }

    public SuppressionFile RemoveSuppressionsFrom(SuppressionFile other, out bool otherHasUnusedSuppressions)
    {
        var usedSuppressions = new HashSet<Suppression>(ReferenceEqualityComparer.Instance);

        var result = new SuppressionFile();
        foreach (var comparison in Comparisons)
        {
            var newComparison = new Comparison()
            {
                Left = comparison.Left,
                Right = comparison.Right,
            };

            var otherComparison = other.GetComparison(comparison.Left, comparison.Right);
            if (otherComparison is null)
            {
                // no matching comparison in suppression source, add directly
                newComparison.Suppressions.AddRange(comparison.Suppressions.Select(s => s with { }));
            }
            else
            {
                // there was a matching comparison, go suppression-by-suppression to compare
                foreach (var suppression in comparison.Suppressions)
                {
                    var matching = otherComparison.Suppressions.FirstOrDefault(c => c == suppression);
                    if (matching is null)
                    {
                        // there's no matching suppression in other, add a clone
                        newComparison.Suppressions.Add(suppression with { });
                    }
                    else
                    {
                        // there's a matching suppression in other, mark it
                        _ = usedSuppressions.Add(matching);
                    }
                }
            }

            if (newComparison.Suppressions.Count > 0)
            {
                result.Comparisons.Add(newComparison);
            }
        }

        otherHasUnusedSuppressions = other
            .Comparisons.SelectMany(c => c.Suppressions)
            .Any(s => !usedSuppressions.Contains(s));

        return result;
    }

    private static readonly XName NArCompatSuppressions = "ArCompatSuppressions";
    private static readonly XName NComparison = "Comparison";
    private static readonly XName NLeft = "Left";
    private static readonly XName NRight = "Right";
    private static readonly XName NSuppression = "Suppression";
    private static readonly XName NDifferenceType = "DifferenceType";
    private static readonly XName NTypeName = "TypeName";
    private static readonly XName NMessage = "Message";

    public XDocument Serialize()
    {
        var sortedComparisons = Comparisons
            .OrderBy(c => c.Left)
            .ThenBy(c => c.Right);

        var doc = new XDocument();
        var rootNode = new XElement(NArCompatSuppressions);
        doc.Add(rootNode);

        foreach (var comparison in sortedComparisons)
        {
            var compareNode = new XElement(NComparison);
            rootNode.Add(compareNode);
            if (comparison.Left != null)
            {
                compareNode.Add(new XAttribute(NLeft, comparison.Left)); 
            }
            if (comparison.Right != null)
            {
                compareNode.Add(new XAttribute(NRight, comparison.Right)); 
            }

            var suppressions = comparison.Suppressions
                .OrderBy(s => s.DifferenceType)
                .ThenBy(s => s.TypeName)
                .ThenBy(s => s.Message);

            foreach (var suppression in suppressions)
            {
                var suppressionNode = new XElement(NSuppression,
                    new XAttribute(NDifferenceType, suppression.DifferenceType.ToString()),
                    new XElement(NTypeName, suppression.TypeName));
                if (suppression.Message is not null)
                {
                    suppressionNode.Add(new XElement(NMessage, new XText(suppression.Message)));
                }
                compareNode.Add(suppressionNode);
            }
        }

        return doc;
    }

    public static SuppressionFile Deserialize(XDocument document)
    {
        [DoesNotReturn]
        static void Throw(string message) => throw new InvalidOperationException(message);

        var root = document.Root;
        if (root is null)
            Throw("Suppressions file must have root node");
        if (root.Name != NArCompatSuppressions)
            Throw($"Suppressions file root must be '{NArCompatSuppressions}'");

        var result = new SuppressionFile();
        foreach (var child in root.Elements())
        {
            if (child.Name != NComparison)
                Throw($"Children of root must be '{NComparison}'");

            var comparison = new Comparison();
            if (child.Attribute(NLeft) is { } attrl)
                comparison.Left = attrl.Value;
            if (child.Attribute(NRight) is { } attrr)
                comparison.Right = attrr.Value;

            foreach (var child2 in child.Elements())
            {
                if (child2.Name != NSuppression)
                    Throw($"Children of '{NComparison}' must be '{NSuppression}'");
                if (child2.Attribute(NDifferenceType) is not { } attrDiffType)
                    Throw($"'{NSuppression}' must have attribute '{NDifferenceType}'");

                var suppression = new Suppression();
                suppression.DifferenceType = Enum.Parse<DifferenceType>(attrDiffType.Value);
                if (child2.Element(NTypeName) is { } typeNameElem)
                {
                    suppression.TypeName = typeNameElem.Value;
                }
                if (child2.Element(NMessage) is { } messageElem)
                {
                    suppression.Message = messageElem.Value;
                }

                comparison.Suppressions.Add(suppression);
            }

            result.Comparisons.Add(comparison);
        }

        return result;
    }
}
