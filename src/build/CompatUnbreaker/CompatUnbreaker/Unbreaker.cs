using AsmResolver.DotNet;
using CompatUnbreaker.Models;
using CompatUnbreaker.Processors;
using CompatUnbreaker.Processors.Abstractions;

namespace CompatUnbreaker;

public static class Unbreaker
{
    public static void ProcessConsumer(AssemblyDefinition shimAssembly, AssemblyDefinition consumerAssembly)
    {
        var context = new ProcessorContext
        {
            ShimModel = ShimModel.From(shimAssembly),
        };

        new UnbreakerConsumerProcessor().Process(context, consumerAssembly);
    }

    public static void ProcessReference(AssemblyDefinition shimAssembly, AssemblyDefinition referenceAssembly)
    {
        var context = new ProcessorContext
        {
            ShimModel = ShimModel.From(shimAssembly),
        };

        new UnbreakerReferenceProcessor().Process(context, referenceAssembly);
    }
}
