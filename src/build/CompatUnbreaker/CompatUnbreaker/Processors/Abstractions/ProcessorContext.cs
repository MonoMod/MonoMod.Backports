using CompatUnbreaker.Models;

namespace CompatUnbreaker.Processors.Abstractions;

internal sealed class ProcessorContext
{
    public required ShimModel ShimModel { get; init; }
}
