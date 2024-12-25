﻿using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Serialized;
using AsmResolver.IO;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.PE.DotNet.StrongName;
using NuGet.Frameworks;
using Postprocess;

if (args is not [
    var outputRefDir,
    var snkPath,
    .. var dotnetOobPackagePaths
    ])
{
    Console.Error.WriteLine("Assemblies not provided.");
    Console.Error.WriteLine("Syntax: <output ref dir> <snk directory> <...oob package paths...>");
    Console.Error.WriteLine("Arguments provided: ");
    foreach (var arg in args)
    {
        Console.Error.WriteLine($"- {arg}");
    }
    return 1;
}

using var fileservice = new ByteArrayFileService();
var listener = ThrowErrorListener.Instance;

var readerParams = new ModuleReaderParameters(fileservice)
{
    PEReaderParameters = new(listener)
};

_ = Directory.CreateDirectory(outputRefDir);

// first pass: compute TFMs and collect list for each package
var pkgList = dotnetOobPackagePaths
    .Select(path
        => (path, Directory.EnumerateDirectories(Path.Combine(path, "lib"))
            .Select(path => (path, NuGetFramework.ParseFolder(Path.GetFileName(path))))
            .ToArray()))
    .ToArray();

var fwReducer = new FrameworkReducer();
var targetTfms = fwReducer.ReduceDownwards(pkgList
    .SelectMany(t 
        => t.Item2.Select(t => t.Item2)))
    .ToArray();

// Now, we have some work to do for shims. We want to check if there is an equivalent type to the shims
// defined or exported from Backports, and rewrite a new shim forwarding to Backports as appropriate.
foreach (var (oobPackagePath, tfms) in pkgList)
{
    var importedSet = new HashSet<string>();

    ManagedPEImageBuilder? peImageBuilder = null;
    ModuleDefinition? backportsShim = null;
    AssemblyDefinition? backportsShimAssembly = null;
    AssemblyReference? backportsReference = null;

    foreach (var (subdir, framework) in tfms
        .OrderBy(t => t.Item2, NuGetFrameworkSorter.Instance))
    {
        var bclShimPath = Directory.EnumerateFiles(subdir, "*.dll").FirstOrDefault();
        if (bclShimPath is null) continue;

        var bclShim = ModuleDefinition.FromFile(bclShimPath, readerParams);

        if (peImageBuilder is null || backportsShim is null || backportsShimAssembly is null || backportsReference is null)
        {
            peImageBuilder = new ManagedPEImageBuilder(
                new VersionedMetadataDotnetDirectoryFactory(bclShim.DotNetDirectory!.Metadata!.VersionString),
                listener);

            var bcl = KnownCorLibs.FromRuntimeInfo(
                DotNetRuntimeInfo.Parse(
                    targetTfms.Single()
                    .GetDotNetFrameworkName(DefaultFrameworkNameProvider.Instance)));

            // set up the new shim module
            backportsShim = new ModuleDefinition(bclShim.Name, bcl)
            {

            };
            backportsShimAssembly = new AssemblyDefinition(bclShim.Assembly!.Name,
                new(bclShim.Assembly!.Version.Major, 9999, 9999, 9999))
            {
                Modules = { backportsShim },
                HashAlgorithm = bclShim.Assembly!.HashAlgorithm,
                PublicKey = bclShim.Assembly!.PublicKey,
                HasPublicKey = bclShim.Assembly!.HasPublicKey,
            };

            backportsReference =
                new AssemblyReference("MonoMod.Backports", new(1, 0, 0, 0))
                .ImportWith(backportsShim.DefaultImporter);
        }

        // go through all public types, make sure they exist in the shim, and generate the forwarder
        foreach (var type in bclShim.TopLevelTypes)
        {
            void ExportType(TypeDefinition type, bool nested)
            {
                if (!type.IsPublic) return;

                if (importedSet.Add(type.FullName))
                {
                    // note: we don't do any validation here, because we'll invoke apicompat for that logic after generating everything
                    backportsShim.ExportedTypes.Add(new ExportedType(backportsReference, type.Namespace, type.Name)
                    {
                        Attributes = nested ? 0 : TypeAttributes.Forwarder,
                    });
                }

                foreach (var nestedType in type.NestedTypes)
                {
                    ExportType(nestedType, true);
                }
            }

            ExportType(type, false);
        }
    }

    if (backportsShim is null || peImageBuilder is null || backportsShimAssembly is null)
    {
        Console.Error.WriteLine($"ShimGen : error : No assemblies were found for package at {oobPackagePath}!");
        continue;
    }

    // write out the resultant shim assembly
    var newShimPath = Path.Combine(outputRefDir, backportsShim.Name!);
    backportsShim.Write(newShimPath, peImageBuilder);

    // then finalize the strong name as needed
    if (backportsShimAssembly.HasPublicKey)
    {
        var name = Convert.ToHexString(backportsShimAssembly.GetPublicKeyToken()!);
        var keyPath = Path.Combine(snkPath, name + ".snk");
        if (!File.Exists(keyPath))
        {
            Console.Error.WriteLine($"ShimGen : warning : Missing SNK file for key {name} (for {backportsShim.Name})");
        }
        else
        {
            var snk = StrongNamePrivateKey.FromFile(keyPath);
            var signer = new StrongNameSigner(snk);
            using var assemblyFs = File.Open(newShimPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            signer.SignImage(assemblyFs, backportsShimAssembly.HashAlgorithm);
        }
    }
}

foreach (var tfm in targetTfms)
{
    Console.WriteLine("tfm:" + tfm.GetShortFolderName());
}

return 0;
