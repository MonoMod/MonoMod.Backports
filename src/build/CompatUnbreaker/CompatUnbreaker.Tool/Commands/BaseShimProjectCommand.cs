using AsmResolver.DotNet;
using CompatUnbreaker.Models;
using CompatUnbreaker.Processors;
using CompatUnbreaker.Processors.Abstractions;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.ApiCompatibility.Comparing;
using CompatUnbreaker.Tool.SkeletonGeneration;
using CompatUnbreaker.Utilities.AsmResolver;
using ConsoleAppFramework;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.Build.Logging;

namespace CompatUnbreaker.Tool.Commands;

public abstract class BaseShimProjectCommand
{
    static BaseShimProjectCommand()
    {
        MSBuildLocator.RegisterDefaults();
    }

    private static (List<string> FrameworkReferences, List<string> References) BuildAndGetReferences(ref ProjectInstance project, bool build)
    {
        var loggers = new ConsoleLogger[]
        {
            new ConsoleLogger(LoggerVerbosity.Quiet),
        };

        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");
        if (!string.IsNullOrEmpty(targetFrameworks))
        {
            project = new ProjectInstance(project.FullPath, new Dictionary<string, string>
            {
                ["TargetFramework"] = targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0],
            }, null);
        }

        if (!build)
        {
            project.SetProperty("BuildProjectReferences", "false");
        }

        if (!project.Build("Restore", loggers))
        {
            throw new Exception("Failed to restore packages");
        }

        if (!project.Build(build ? "Build" : "FindReferenceAssembliesForReferences", loggers))
        {
            throw new Exception("Failed to build");
        }

        var frameworkReferences = new List<string>();
        var references = new List<string>();

        foreach (var reference in project.GetItems("ReferencePathWithRefAssemblies"))
        {
            if (reference.HasMetadata("FrameworkReferenceName") || reference.GetMetadataValue("NuGetPackageId") == "NETStandard.Library")
            {
                frameworkReferences.Add(reference.EvaluatedInclude);
            }
            else
            {
                references.Add(reference.EvaluatedInclude);
            }
        }

        return (frameworkReferences, references);
    }

    protected async Task<AssemblyMapper> LoadAsync([Argument] string projectPath, bool skipBuild = false)
    {
        var shimProject = new ProjectInstance(projectPath);

        var shimTargetAssemblyName = shimProject.GetPropertyValue("ShimTargetAssemblyName");
        var shimBaselineProjectPath = shimProject.GetPropertyValue("ShimBaselineProject");

        var shimBaselineProject = new ProjectInstance(Path.Combine(shimProject.Directory, shimBaselineProjectPath));

        var (shimFrameworkReferences, shimReferences) = BuildAndGetReferences(ref shimProject, !skipBuild);
        var (baselineFrameworkReferences, baselineReferences) = BuildAndGetReferences(ref shimBaselineProject, false);

        var shimPath = shimProject.GetPropertyValue("TargetPath");

        baselineReferences.RemoveAll(r => shimFrameworkReferences.Any(r2 => Path.GetFileName(r2) == Path.GetFileName(r)));

        var shimReferenceAssemblies = shimFrameworkReferences.Concat(shimReferences).Select(AssemblyDefinition.FromFile).ToArray();
        var baselineReferenceAssemblies = shimFrameworkReferences.Concat(baselineReferences).Select(AssemblyDefinition.FromFile).ToArray();

        var shimAssembly = new SimpleAssemblyResolver(shimReferenceAssemblies).Load(shimPath);
        var targetAssembly = shimReferenceAssemblies.Single(a => a.Name == shimTargetAssemblyName);

        var baselineResolver = new SimpleAssemblyResolver(baselineReferenceAssemblies);
        var baselineAssembly = baselineReferenceAssemblies.Single(a => a.Name == shimTargetAssemblyName);

        foreach (var assemblyReference in baselineAssembly.Modules.Concat(targetAssembly.Modules).SelectMany(m => m.AssemblyReferences))
        {
            if (assemblyReference.Resolve() == null)
            {
                throw new InvalidOperationException($"Couldn't resolve {assemblyReference}");
            }
        }

        Unbreaker.ProcessReference(shimAssembly, targetAssembly);

        var assemblyMapper = AssemblyMapper.Create(baselineAssembly, targetAssembly);

        return assemblyMapper;
    }
}
