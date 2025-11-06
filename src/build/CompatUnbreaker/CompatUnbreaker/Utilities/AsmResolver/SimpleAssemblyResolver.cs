using System.Collections.Concurrent;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Utilities.AsmResolver;

public sealed class SimpleAssemblyResolver : IAssemblyResolver
{
    private readonly ConcurrentDictionary<AssemblyDescriptor, AssemblyDefinition> _assemblies;

    public SimpleAssemblyResolver(
        IEnumerable<AssemblyDefinition>? assemblies = null,
        SignatureComparisonFlags comparisonFlags = SignatureComparisonFlags.VersionAgnostic
    )
    {
        var signatureComparer = new SignatureComparer(comparisonFlags);

        _assemblies = new ConcurrentDictionary<AssemblyDescriptor, AssemblyDefinition>(signatureComparer);

        if (assemblies != null)
        {
            foreach (var assembly in assemblies)
            {
                Load(assembly);
            }
        }
    }

    public AssemblyDefinition? Resolve(AssemblyDescriptor assembly)
    {
        if (_assemblies.TryGetValue(assembly, out var result))
        {
            return result;
        }

        return null;
    }

    public void AddToCache(AssemblyDescriptor descriptor, AssemblyDefinition definition)
    {
        if (!_assemblies.TryAdd(descriptor, definition))
        {
            throw new ArgumentException($"An item with the same key has already been added. Key: {descriptor}");
        }
    }

    public bool RemoveFromCache(AssemblyDescriptor descriptor)
    {
        return _assemblies.TryRemove(descriptor, out _);
    }

    public bool HasCached(AssemblyDescriptor descriptor)
    {
        return _assemblies.ContainsKey(descriptor);
    }

    public void ClearCache()
    {
        _assemblies.Clear();
    }

    public void Add(AssemblyDefinition definition)
    {
        AddToCache(definition, definition);
    }

    public void Load(AssemblyDefinition definition)
    {
        foreach (var module in definition.Modules)
        {
            module.MetadataResolver = new DefaultMetadataResolver(this);
        }

        Add(definition);
    }

    public AssemblyDefinition Load(string filePath)
    {
        var assemblyDefinition = AssemblyDefinition.FromFile(filePath, new ModuleReaderParameters());
        Load(assemblyDefinition);
        return assemblyDefinition;
    }
}
