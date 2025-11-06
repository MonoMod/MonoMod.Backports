namespace CompatUnbreaker.Attributes;

/// <summary>
/// A substitute for the <see langword="this" /> keyword, to allow shimming extension methods.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UnbreakerThisAttribute : Attribute;
