using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal record TypeContext(
    TypeContext.GenericContext Generic,
    TypeContext.TransformContext Transform
)
{
    public static TypeContext Empty { get; } = new(GenericContext.Empty, TransformContext.Empty);

    public sealed record GenericContext(TypeDefinition? Type, MethodDefinition? Method)
    {
        public static GenericContext Empty { get; } = new(null, null);

        public GenericParameter? GetGenericParameter(GenericParameterSignature parameter)
        {
            var genericParameters = parameter.ParameterType switch
            {
                GenericParameterType.Type => Type?.GenericParameters,
                GenericParameterType.Method => Method?.GenericParameters,
                _ => throw new ArgumentOutOfRangeException(nameof(parameter)),
            };

            if (genericParameters is null)
                return null;

            if (parameter.Index >= 0 && parameter.Index < genericParameters.Count)
                return genericParameters[parameter.Index];

            return null;
        }
    }

    public sealed record TransformContext(
        NullableAnnotation? DefaultNullableTransform = null,
        NullableAnnotation[]? NullableTransforms = null,
        bool[]? DynamicTransforms = null,
        string?[]? TupleElementNames = null
    )
    {
        public static TransformContext Empty { get; } = new();

        private int _nullablePosition;
        private int _dynamicPosition;
        private int _namesPosition;

        public NullableAnnotation? TryConsumeNullableTransform()
        {
            return NullableTransforms?[_nullablePosition++] ?? DefaultNullableTransform;
        }

        public bool? TryConsumeDynamicTransform()
        {
            return DynamicTransforms?[_dynamicPosition++];
        }

        public Span<string?> ConsumeTupleElementNames(int numberOfElements)
        {
            if (TupleElementNames == null) return default;

            var result = TupleElementNames.AsSpan(_namesPosition, numberOfElements);
            _namesPosition += numberOfElements;
            return result;
        }

        public static TransformContext From(NullableAnnotation? defaultNullableTransform, IHasCustomAttribute attributeProvider)
        {
            var nullableAttribute = attributeProvider.FindCustomAttributes("System.Runtime.CompilerServices", "NullableAttribute").SingleOrDefault();
            var nullableTransforms = nullableAttribute?.Signature!.FixedArguments.Single().Elements.Cast<byte>().Cast<NullableAnnotation>().ToArray();

            var dynamicTransformsAttribute = attributeProvider.FindCustomAttributes("System.Runtime.CompilerServices", "DynamicAttribute").SingleOrDefault();
            bool[]? dynamicTransforms;
            if (dynamicTransformsAttribute != null)
            {
                var arguments = dynamicTransformsAttribute.Signature!.FixedArguments;
                dynamicTransforms = arguments.Count == 0 ? [true] : arguments.Single().Elements.Cast<bool>().ToArray();
            }
            else
            {
                dynamicTransforms = null;
            }

            var tupleElementNamesAttribute = attributeProvider.FindCustomAttributes("System.Runtime.CompilerServices", "TupleElementNamesAttribute").SingleOrDefault();
            var tupleElementNames = tupleElementNamesAttribute?.Signature!.FixedArguments.Single().Elements.Cast<Utf8String?>().Select(s => s?.Value).ToArray();

            return new TransformContext(
                defaultNullableTransform,
                nullableTransforms,
                dynamicTransforms,
                tupleElementNames
            );
        }
    }

    public TypeContext WithTransformsAttributeProvider(IHasCustomAttribute attributeProvider)
    {
        return this with { Transform = TransformContext.From(Transform.DefaultNullableTransform, attributeProvider) };
    }

    public static TypeContext From(TypeDefinition type, IHasCustomAttribute? transformsAttributeProvider)
    {
        return From(type, null, transformsAttributeProvider);
    }

    public static TypeContext From(MethodDefinition method, IHasCustomAttribute? transformsAttributeProvider)
    {
        return From(method.DeclaringType, method, transformsAttributeProvider);
    }

    public static TypeContext From(IMemberDefinition member, IHasCustomAttribute? transformsAttributeProvider)
    {
        if (member is MethodDefinition method) return From(method, transformsAttributeProvider);
        return From(member.DeclaringType, null, transformsAttributeProvider);
    }

    private static TypeContext From(TypeDefinition? type, MethodDefinition? method, IHasCustomAttribute? transformsAttributeProvider)
    {
        var defaultNullableTransform = GetDefaultNullableTransform((IMemberDefinition?) method ?? type);

        return new TypeContext(
            new GenericContext(type, method),
            transformsAttributeProvider != null
                ? TransformContext.From(defaultNullableTransform, transformsAttributeProvider)
                : new TransformContext(defaultNullableTransform)
        );
    }

    private static NullableAnnotation? GetDefaultNullableTransform(IMemberDefinition? member)
    {
        if (member == null) return null;

        var nullableContextAttribute = ((IHasCustomAttribute) member).FindCustomAttributes("System.Runtime.CompilerServices", "NullableContextAttribute").SingleOrDefault();

        if (nullableContextAttribute != null)
        {
            ArgumentNullException.ThrowIfNull(nullableContextAttribute.Signature);
            return (NullableAnnotation) (byte) nullableContextAttribute.Signature.FixedArguments.Single().Element!;
        }

        if (member.DeclaringType != null)
        {
            return GetDefaultNullableTransform(member.DeclaringType);
        }

        return null;
    }
}
