using ArApiCompat.ApiCompatibility.AssemblyMapping;
using ArApiCompat.ApiCompatibility.Comparing;
using ArApiCompat.ApiCompatibility.Suppressions;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;

namespace ArApiCompat;

internal sealed record ComparisonJob(
    string LeftName,
    string LeftAssembly,
    IReadOnlyList<string> LeftReferencePath,
    string RightName,
    string RightAssembly,
    IReadOnlyList<string> RightReferencePath
    );

internal sealed class ComparisonResult
{
    public static ComparisonResult Execute(IEnumerable<ComparisonJob> jobs, SuppressionFile? suppressions, ParallelOptions? parallelization = null)
    {
        var allJobs = jobs.ToArray();

        allJobs.Sort((a, b)
            => StringComparer.Ordinal.Compare(a.LeftName, b.LeftName) * 2
             + StringComparer.Ordinal.Compare(a.RightName, b.RightName));

        var allComparers = new ApiComparer[allJobs.Length];

        Parallel.For(0, allJobs.Length, parallelization ?? new(), i =>
        {
            var job = allJobs[i];

            var (left, _) = LoadModuleInNewUniverse(job.LeftAssembly, job.LeftReferencePath);
            var (right, _) = LoadModuleInNewUniverse(job.RightAssembly, job.RightReferencePath);

            var mapper = AssemblyMapper.Create(left.Assembly!, right.Assembly!);

            var comparer = new ApiComparer();
            comparer.Compare(mapper);
            allComparers[i] = comparer;
        });

        return new(allJobs, allComparers, suppressions);
    }

    private static (ModuleDefinition module, RuntimeContext universe) LoadModuleInNewUniverse(string file, IReadOnlyList<string> referencePath)
    {
        var module = (SerializedModuleDefinition)ModuleDefinition.FromFile(file);
        var proxyResolver = new ForwardingAssemblyResolver();
        var universe = new RuntimeContext(module.RuntimeContext.TargetRuntime, proxyResolver);
        var assemblyResolver = new ReferencePathAsemblyResolver(new() { RuntimeContext = universe }, referencePath);
        proxyResolver.Target = assemblyResolver;
        module = (SerializedModuleDefinition)ModuleDefinition.FromFile(file, universe.DefaultReaderParameters);

        return (module, universe);
    }

    private sealed class ReferencePathAsemblyResolver(ModuleReaderParameters mrp, IReadOnlyList<string> referencePath) : AssemblyResolverBase(mrp)
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

    private sealed class ForwardingAssemblyResolver : IAssemblyResolver
    {
        public ReferencePathAsemblyResolver? Target { get; set; }

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

    private ComparisonResult(
        ComparisonJob[] jobs,
        ApiComparer[] comparers,
        SuppressionFile? suppressions)
    {
        this.jobs = jobs;
        this.comparers = comparers;
        this.suppressions = suppressions;
        suppressedDifferences = new IReadOnlyList<CompatDifference>[jobs.Length];

        // initialize suppressionsHasUnused with whether there are comparisons with suppressions that we don't have
        if (suppressions is not null)
        {
            suppressionsHasUnused = !suppressions.Comparisons
                .All(c => jobs.Any(j => j.LeftName == c.Left && j.RightName == c.Right));
        }
    }

    private readonly ComparisonJob[] jobs;
    private readonly ApiComparer[] comparers;
    private readonly SuppressionFile? suppressions;
    private readonly IReadOnlyList<CompatDifference>?[] suppressedDifferences;
    private bool suppressionsHasUnused;

    public int JobCount => jobs.Length;
    public IReadOnlyList<ComparisonJob> Jobs => jobs;
    public IReadOnlyList<CompatDifference> GetRawDifferences(int i)
        => comparers[i].CompatDifferences;

    public IReadOnlyList<CompatDifference> GetDifferences(int i)
    {
        var list = suppressedDifferences[i];
        if (list is null)
        {
            _ = Interlocked.CompareExchange(ref suppressedDifferences[i],
                ComputeSuppresedDifferences(i),
                null);
            list = suppressedDifferences[i]!;
        }
        return list;
    }

    private IReadOnlyList<CompatDifference> ComputeSuppresedDifferences(int i)
    {
        var job = jobs[i];
        var comparer = comparers[i];
        var suppressionJob = suppressions?.GetComparison(job.LeftName, job.RightName);
        if (suppressionJob is null || suppressionJob.Suppressions.Count == 0)
        {
            // no suppressions, just return the raw compat difference list
            return comparer.CompatDifferences;
        }

        // we have suppressions, do something about it
        var usedSuppressions = new HashSet<SuppressionFile.Suppression>(ReferenceEqualityComparer.Instance);
        var result = new List<CompatDifference>(comparer.CompatDifferences.Count);

        foreach (var difference in comparer.CompatDifferences)
        {
            var suppression = suppressionJob.Suppressions
                .FirstOrDefault(s
                    => s.DifferenceType == difference.Type
                    && s.TypeName == difference.GetType().FullName
                    && s.Message == difference.Message);

            if (suppression is not null)
            {
                // difference is suppressed, record that we used the suppression
                _ = usedSuppressions.Add(suppression);
            }
            else
            {
                // difference is not suppressed, record the difference
                result.Add(difference);
            }
        }

        // we've gone through all of the suppressions, check if any were unused
        if (!suppressionJob.Suppressions.All(usedSuppressions.Contains))
        {
            // we didn't end up using some suppressions, note that down
            suppressionsHasUnused = true;
        }

        return result;
    }

    public bool HasUnusedSuppressions
    {
        get
        {
            if (suppressionsHasUnused) return true;

            // if suppressionsHasUnused is false, we need to make sure we've computed the suppressed differences for everything
            for (var i = 0; i < JobCount; i++)
            {
                _ = GetDifferences(i);
            }

            // once that's done, that's our result
            return suppressionsHasUnused;
        }
    }

    public SuppressionFile GetSuppressionFile()
    {
        var result = new SuppressionFile();

        for (var i = 0; i < JobCount; i++)
        {
            var job = jobs[i];
            var comparer = comparers[i];

            var comparison = new SuppressionFile.Comparison()
            {
                Left = job.LeftName,
                Right = job.RightName,
            };

            foreach (var diff in comparer.CompatDifferences)
            {
                comparison.Suppressions.Add(FormatSuppression(diff));
            }

            if (comparison.Suppressions.Count > 0)
            {
                result.Comparisons.Add(comparison);
            }
        }

        result.Sort(); // need this despite keeping jobs sorted from the get-go because we want to sort compat differences too

        return result;
    }

    private static SuppressionFile.Suppression FormatSuppression(CompatDifference difference)
    {
        return new()
        {
            DifferenceType = difference.Type,
            TypeName = difference.GetType().FullName,
            Message = difference.Message
        };
    }

}
