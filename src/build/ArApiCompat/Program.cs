using ArApiCompat.ApiCompatibility.AssemblyMapping;
using ArApiCompat.ApiCompatibility.Comparing;
using ArApiCompat.ApiCompatibility.Suppressions;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using System.Xml.Linq;

Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

if (args is not [{ } suppressionFile, { } leftAssemblyFile, { } leftRefPathFile, { } rightAssemblyFile, { } rightRefPathFile, ..var rest])
{
    Console.Error.WriteLine("Usage: ArApiCompat <suppression file> <left assembly> <left reference path file> <right assembly> <right reference path file> [--write-suppressions]");
    return 1;
}

var writeSuppression = rest is ["--write-suppressions", ..];

var (leftModule, leftUniverse) = LoadModuleInUniverse(leftAssemblyFile, File.ReadAllLines(leftRefPathFile));
var (rightModule, rightUniverse) = LoadModuleInUniverse(rightAssemblyFile, File.ReadAllLines(rightRefPathFile));

var mapping = AssemblyMapper.Create(leftModule.Assembly!, rightModule.Assembly!);
var comparer = new ApiComparer();
comparer.Compare(mapping);

var existingSuppressions = new SuppressionFile();
if (!writeSuppression && File.Exists(suppressionFile))
{
    existingSuppressions = SuppressionFile.Deserialize(XDocument.Load(suppressionFile));
}

var reportedErrorSuppressions = new SuppressionFile();
// TODO: multiple comparisons at once
var comparison = new SuppressionFile.Comparison()
{
    Left = leftAssemblyFile,
    Right = rightAssemblyFile,
};
reportedErrorSuppressions.Comparisons.Add(comparison);

var differenceMap = new Dictionary<SuppressionFile.Suppression, CompatDifference>();
foreach (var diff in comparer.CompatDifferences)
{
    var suppression = new SuppressionFile.Suppression()
    {
        DifferenceType = diff.Type,
        TypeName = diff.GetType().FullName,
        Message = diff.Message,
    };
    comparison.Suppressions.Add(suppression);
    differenceMap.Add(suppression, diff);
}

reportedErrorSuppressions.Sort();

if (!writeSuppression)
{
    // suppress things
    var unsuppressed = reportedErrorSuppressions.RemoveSuppressionsFrom(existingSuppressions, out var hasUnused);

    foreach (var c in unsuppressed.Comparisons)
    {
        foreach (var s in c.Suppressions)
        {
            Console.WriteLine(differenceMap[s]);
        }
    }

    if (hasUnused)
    {
        // TODO: report error
        Console.WriteLine("suppressions file had unused suppressions");
    }
}

if (writeSuppression)
{
    // write the suppressions
    var xdoc = reportedErrorSuppressions.Serialize();
    xdoc.Save(suppressionFile, SaveOptions.OmitDuplicateNamespaces);
}

return 0;

static (ModuleDefinition module, RuntimeContext universe) LoadModuleInUniverse(string file, string[] referencePath)
{
    var module = (SerializedModuleDefinition)ModuleDefinition.FromFile(file);
    var proxyResolver = new ForwardingAssemblyResolver();
    var universe = new RuntimeContext(module.RuntimeContext.TargetRuntime, proxyResolver);
    var assemblyResolver = new ReferencePathAsemblyResolver(new() { RuntimeContext = universe }, referencePath);
    proxyResolver.Target = assemblyResolver;
    module = (SerializedModuleDefinition)ModuleDefinition.FromFile(file, universe.DefaultReaderParameters);

    return (module, universe);
}

sealed class ReferencePathAsemblyResolver(ModuleReaderParameters mrp, string[] referencePath) : AssemblyResolverBase(mrp)
{
    protected override AssemblyDefinition? ResolveImpl(AssemblyDescriptor assembly)
    {
        foreach (var file in referencePath)
        {
            if (Path.GetFileNameWithoutExtension(file).Equals(assembly.Name?.Value.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return LoadAssemblyFromFile(file);
            }
        }

        return null;
    }

    protected override string? ProbeRuntimeDirectories(AssemblyDescriptor assembly)
    {
        throw new NotImplementedException();
    }
}

sealed class ForwardingAssemblyResolver : IAssemblyResolver
{
    public IAssemblyResolver? Target { get; set; }

    public void AddToCache(AssemblyDescriptor descriptor, AssemblyDefinition definition)
    {
        Target?.AddToCache(descriptor, definition);
    }

    public void ClearCache()
    {
        Target?.ClearCache();
    }

    public bool HasCached(AssemblyDescriptor descriptor)
    {
        return Target?.HasCached(descriptor) ?? false;
    }

    public bool RemoveFromCache(AssemblyDescriptor descriptor)
    {
        return Target?.RemoveFromCache(descriptor) ?? false;
    }

    public AssemblyDefinition? Resolve(AssemblyDescriptor assembly)
    {
        return Target?.Resolve(assembly);
    }
}