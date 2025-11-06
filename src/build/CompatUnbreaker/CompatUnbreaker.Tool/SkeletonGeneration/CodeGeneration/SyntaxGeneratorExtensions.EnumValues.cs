using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Editing;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    public static SyntaxNode CreateEnumConstantValue(this SyntaxGenerator syntaxGenerator, TypeDefinition enumType, object constantValue, TypeContext typeContext)
    {
        if (enumType.HasCustomAttribute("System", "FlagsAttribute"))
        {
            return syntaxGenerator.CreateFlagsEnumConstantValue(enumType, constantValue, typeContext);
        }

        return syntaxGenerator.CreateNonFlagsEnumConstantValue(enumType, constantValue, typeContext);
    }

    private static SyntaxNode CreateNonFlagsEnumConstantValue(this SyntaxGenerator syntaxGenerator, TypeDefinition enumType, object constantValue, TypeContext typeContext)
    {
        var underlyingType = enumType.GetEnumUnderlyingType();
        var constantValueULong = ConvertUnderlyingValueToUInt64(underlyingType.ElementType, constantValue);

        foreach (var field in enumType.Fields)
        {
            if (field is { Constant: not null })
            {
                var fieldValue = ConvertUnderlyingValueToUInt64(underlyingType.ElementType, field.Constant.InterpretData());
                if (constantValueULong == fieldValue)
                    return syntaxGenerator.CreateMemberAccessExpression(field, enumType, underlyingType.ElementType, typeContext);
            }
        }

        // Otherwise, just add the enum as a literal.
        return syntaxGenerator.CreateExplicitlyCastedLiteralValue(enumType, underlyingType.ElementType, constantValue, typeContext);
    }

    private static SyntaxNode CreateMemberAccessExpression(
        this SyntaxGenerator syntaxGenerator,
        FieldDefinition field,
        TypeDefinition enumType,
        ElementType underlyingSpecialType,
        TypeContext typeContext
    )
    {
        if (SyntaxFacts.IsValidIdentifier(field.Name))
        {
            return syntaxGenerator.MemberAccessExpression(syntaxGenerator.TypeExpression(enumType, typeContext), syntaxGenerator.IdentifierName(field.Name));
        }

        return syntaxGenerator.CreateExplicitlyCastedLiteralValue(enumType, underlyingSpecialType, field.Constant.InterpretData(), typeContext);
    }

    private static SyntaxNode CreateExplicitlyCastedLiteralValue(
        this SyntaxGenerator syntaxGenerator,
        TypeDefinition enumType,
        ElementType underlyingSpecialType,
        object constantValue,
        TypeContext typeContext
    )
    {
        var expression = syntaxGenerator.LiteralExpression(constantValue);

        var constantValueULong = ConvertUnderlyingValueToUInt64(underlyingSpecialType, constantValue);
        if (constantValueULong == 0)
        {
            // 0 is always convertible to an enum type without needing a cast.
            return expression;
        }

        return syntaxGenerator.CastExpression(syntaxGenerator.TypeExpression(enumType, typeContext), expression);
    }

    private static ulong ConvertUnderlyingValueToUInt64(ElementType enumUnderlyingType, object value)
    {
        unchecked
        {
            return enumUnderlyingType switch
            {
                ElementType.I1 => (ulong) (sbyte) value,
                ElementType.I2 => (ulong) (short) value,
                ElementType.I4 => (ulong) (int) value,
                ElementType.I8 => (ulong) (long) value,
                ElementType.U1 => (byte) value,
                ElementType.U2 => (ushort) value,
                ElementType.U4 => (uint) value,
                ElementType.U8 => (ulong) value,
                _ => throw new ArgumentOutOfRangeException(nameof(enumUnderlyingType), enumUnderlyingType, null),
            };
        }
    }

    private static SyntaxNode CreateFlagsEnumConstantValue(this SyntaxGenerator syntaxGenerator, TypeDefinition enumType, object constantValue, TypeContext typeContext)
    {
        // These values are sorted by value. Don't change this.
        var allFieldsAndValues = new List<(FieldDefinition field, ulong value)>();
        GetSortedEnumFieldsAndValues(enumType, allFieldsAndValues);

        var usedFieldsAndValues = new List<(FieldDefinition field, ulong value)>();
        return syntaxGenerator.CreateFlagsEnumConstantValue(enumType, constantValue, allFieldsAndValues, usedFieldsAndValues, typeContext);
    }

    private static SyntaxNode CreateFlagsEnumConstantValue(
        this SyntaxGenerator syntaxGenerator,
        TypeDefinition enumType,
        object constantValue,
        List<(FieldDefinition field, ulong value)> allFieldsAndValues,
        List<(FieldDefinition field, ulong value)> usedFieldsAndValues,
        TypeContext typeContext
    )
    {
        var underlyingSpecialType = enumType.GetEnumUnderlyingType();
        var constantValueULong = ConvertUnderlyingValueToUInt64(underlyingSpecialType.ElementType, constantValue);

        var result = constantValueULong;

        // We will not optimize this code further to keep it maintainable. There are some
        // boundary checks that can be applied to minimize the comparisons required. This code
        // works the same for the best/worst case. In general the number of items in an enum are
        // sufficiently small and not worth the optimization.
        for (var index = allFieldsAndValues.Count - 1; index >= 0 && result != 0; index--)
        {
            var fieldAndValue = allFieldsAndValues[index];
            var valueAtIndex = fieldAndValue.value;

            if (valueAtIndex != 0 && (result & valueAtIndex) == valueAtIndex)
            {
                result -= valueAtIndex;
                usedFieldsAndValues.Add(fieldAndValue);
            }
        }

        // We were able to represent this number as a bitwise OR of valid flags.
        if (result == 0 && usedFieldsAndValues.Count > 0)
        {
            // We want to emit the fields in lower to higher value.  So we walk backward.
            SyntaxNode? finalNode = null;
            for (var i = usedFieldsAndValues.Count - 1; i >= 0; i--)
            {
                var field = usedFieldsAndValues[i];
                var node = syntaxGenerator.CreateMemberAccessExpression(field.field, enumType, underlyingSpecialType.ElementType, typeContext);
                if (finalNode == null)
                {
                    finalNode = node;
                }
                else
                {
                    finalNode = syntaxGenerator.BitwiseOrExpression(finalNode, node);
                }
            }

            return finalNode;
        }

        // We couldn't find fields to OR together to make the value.

        // If we had 0 as the value, and there's an enum value equal to 0, then use that.
        var zeroField = GetZeroField(allFieldsAndValues);
        if (constantValueULong == 0 && zeroField != null)
        {
            return syntaxGenerator.CreateMemberAccessExpression(zeroField, enumType, underlyingSpecialType.ElementType, typeContext);
        }
        else
        {
            // Add anything else in as a literal value.
            return syntaxGenerator.CreateExplicitlyCastedLiteralValue(enumType, underlyingSpecialType.ElementType, constantValue, typeContext);
        }
    }

    private static FieldDefinition? GetZeroField(List<(FieldDefinition field, ulong value)> allFieldsAndValues)
    {
        for (var i = allFieldsAndValues.Count - 1; i >= 0; i--)
        {
            var (field, value) = allFieldsAndValues[i];
            if (value == 0)
            {
                return field;
            }
        }

        return null;
    }

    private static void GetSortedEnumFieldsAndValues(
        TypeDefinition enumType,
        List<(FieldDefinition field, ulong value)> allFieldsAndValues
    )
    {
        var underlyingType = enumType.GetEnumUnderlyingType();
        foreach (var field in enumType.Fields)
        {
            if (field is { Constant: not null })
            {
                var value = ConvertUnderlyingValueToUInt64(underlyingType.ElementType, field.Constant.InterpretData());
                allFieldsAndValues.Add((field, value));
            }
        }

        allFieldsAndValues.Sort(Compare);

        static int Compare((FieldDefinition field, ulong value) x, (FieldDefinition field, ulong value) y)
        {
            unchecked
            {
                return
                    (long) x.value < (long) y.value
                        ? -1
                        : (long) x.value > (long) y.value
                            ? 1
                            : -x.field.Name.CompareTo(y.field.Name);
            }
        }
    }
}
