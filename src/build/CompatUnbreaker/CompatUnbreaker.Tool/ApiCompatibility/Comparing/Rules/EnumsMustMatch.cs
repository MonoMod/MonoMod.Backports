using AsmResolver.DotNet;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.Utilities.AsmResolver;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public sealed class EnumTypesMustMatch(TypeMapper mapper) : TypeCompatDifference(mapper)
{
    public override string Message => $"Underlying type of enum '{Mapper.Left}' changed from '{Mapper.Left.GetEnumUnderlyingType()}' to '{Mapper.Right.GetEnumUnderlyingType()}'";

    public override DifferenceType Type => DifferenceType.Changed;
}

public sealed class EnumValuesMustMatchDifference(TypeMapper mapper, FieldDefinition leftField, FieldDefinition rightField) : TypeCompatDifference(mapper)
{
    public override string Message => $"Value of field '{Mapper.Left}' in enum '{leftField.Name}' changed from '{leftField.Constant?.InterpretData()}' to '{rightField.Constant?.InterpretData()}'";

    public override DifferenceType Type => DifferenceType.Changed;
}

public sealed class EnumsMustMatch : BaseRule
{
    public override void Run(TypeMapper mapper, IList<CompatDifference> differences)
    {
        var (left, right) = mapper;

        // Ensure that this rule only runs on enums.
        if (left == null || right == null || !left.IsEnum || !right.IsEnum)
            return;

        // Get enum's underlying type.
        if (left.GetEnumUnderlyingType() is not { } leftType || right.GetEnumUnderlyingType() is not { } rightType)
        {
            return;
        }

        // Check that the underlying types are equal and if not, emit a diagnostic.
        if (!ExtendedSignatureComparer.VersionAgnostic.Equals(leftType, rightType))
        {
            differences.Add(new EnumTypesMustMatch(mapper));
            return;
        }

        // If so, compare their fields.
        // Build a map of the enum's fields, keyed by the field names.
        var leftMembers = left.Fields
            .Where(f => f.IsStatic)
            .ToDictionary(a => a.Name!.Value);
        var rightMembers = right.Fields
            .Where(f => f.IsStatic)
            .ToDictionary(a => a.Name!.Value);

        // For each field that is present in the left and right, check that their constant values match.
        // Otherwise, emit a diagnostic.
        foreach (var lEntry in leftMembers)
        {
            if (!rightMembers.TryGetValue(lEntry.Key, out var rField))
            {
                continue;
            }

            if (lEntry.Value.Constant is not { } leftConstant || rField.Constant is not { } rightConstant || !Equals(leftConstant, rightConstant))
            {
                differences.Add(new EnumValuesMustMatchDifference(mapper, lEntry.Value, rField));
            }
        }
    }

    private static bool Equals(Constant left, Constant right)
    {
        if (left.Type != right.Type)
            return false;

        return Equals(left.InterpretData(), right.InterpretData());
    }
}
