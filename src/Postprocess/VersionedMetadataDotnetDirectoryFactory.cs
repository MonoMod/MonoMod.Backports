using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Code.Native;

namespace Postprocess;

internal class VersionedMetadataDotnetDirectoryFactory(string version) : DotNetDirectoryFactory
{
    public override DotNetDirectoryBuildResult CreateDotNetDirectory(
        ModuleDefinition module, INativeSymbolsProvider symbolsProvider, IErrorListener errorListener)
    {
        var result = base.CreateDotNetDirectory(module, symbolsProvider, errorListener);
        result.Directory.Metadata!.VersionString = version;
        return result;
    }
}
