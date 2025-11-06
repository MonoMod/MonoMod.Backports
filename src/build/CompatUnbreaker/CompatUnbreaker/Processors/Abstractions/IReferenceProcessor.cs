using AsmResolver.DotNet;

namespace CompatUnbreaker.Processors.Abstractions;

internal interface IReferenceProcessor
{
    void Process(ProcessorContext context, AssemblyDefinition referenceAssembly);
}
