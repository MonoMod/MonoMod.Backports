using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;

namespace Postprocess
{
    internal sealed class ReferencePathAssemblyResolver : AssemblyResolverBase
    {
        private readonly Dictionary<string, string> refPathAssemblyNames = new();

        public ReferencePathAssemblyResolver(IEnumerable<string> paths, ModuleReaderParameters mrp)
            : base(mrp)
        {
            foreach (var path in paths)
            {
                if (Path.GetExtension(path) == ".dll")
                {
                    refPathAssemblyNames[Path.GetFileNameWithoutExtension(path)] = path;
                }
            }
        }

        protected override string? ProbeRuntimeDirectories(AssemblyDescriptor assembly)
        {
            throw new NotImplementedException();
        }

        protected override AssemblyDefinition? ResolveImpl(AssemblyDescriptor assembly)
        {
            if (assembly.Name is { } name && refPathAssemblyNames.TryGetValue(name, out var filePath))
            {
                return LoadAssemblyFromFile(filePath);
            }

            return base.ResolveImpl(assembly);
        }
    }
}
