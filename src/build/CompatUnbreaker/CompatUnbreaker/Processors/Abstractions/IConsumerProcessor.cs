using AsmResolver.DotNet;

namespace CompatUnbreaker.Processors.Abstractions;

internal interface IConsumerProcessor
{
    void Process(ProcessorContext context, AssemblyDefinition consumerAssembly);
}
