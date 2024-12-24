using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Serialized;
using AsmResolver.DotNet.Signatures;
using AsmResolver.IO;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.PE.DotNet.StrongName;
using Postprocess;

if (args is not [
    var outputRefDir,
    var snkPath,
    .. var dotnetShimAssemblyPaths
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

// load baseline assemblies
var readerParams = new ModuleReaderParameters(fileservice)
{
    PEReaderParameters = new(listener)
};
/*var (context, backportsImage, backportsModule) = LoadContextForRootModule(backportsPath, readerParams);
var backportsResolver = (AssemblyResolverBase)context.AssemblyResolver;

var peImageBuilder = new ManagedPEImageBuilder(
    new VersionedMetadataDotnetDirectoryFactory(backportsImage.DotNetDirectory!.Metadata!.VersionString),
    listener);*/

_ = Directory.CreateDirectory(outputRefDir);

// Now, we have some work to do for shims. We want to check if there is an equivalent type to the shims
// defined or exported from Backports, and rewrite a new shim forwarding to Backports as appropriate.
foreach (var shimPath in dotnetShimAssemblyPaths)
{
    var bclShim = ModuleDefinition.FromFile(shimPath, readerParams);

    var peImageBuilder = new ManagedPEImageBuilder(
        new VersionedMetadataDotnetDirectoryFactory(bclShim.DotNetDirectory!.Metadata!.VersionString),
        listener);

    // set up the new shim module
    var backportsShim = new ModuleDefinition(bclShim.Name, (AssemblyReference)bclShim.CorLibTypeFactory.CorLibScope)
    {

    };
    var backportsShimAssembly = new AssemblyDefinition(bclShim.Assembly!.Name,
        new(bclShim.Assembly!.Version.Major, 9999, 9999, 9999))
    {
        Modules = { backportsShim },
        HashAlgorithm = bclShim.Assembly!.HashAlgorithm,
        PublicKey = bclShim.Assembly!.PublicKey,
        HasPublicKey = bclShim.Assembly!.HasPublicKey,
    };

    var backportsReference = 
        new AssemblyReference("MonoMod.Backports", new(1,0,0,0))
        .ImportWith(backportsShim.DefaultImporter);

    // copy attributes
    foreach (var backportsAttr in bclShim.Assembly!.CustomAttributes)
    {
        var caSig = new CustomAttributeSignature();

        foreach (var arg in backportsAttr.Signature!.FixedArguments)
        {
            caSig.FixedArguments.Add(new(
                backportsShim.DefaultImporter.ImportTypeSignature(arg.ArgumentType),
                arg.Elements));
        }
        foreach (var arg in backportsAttr.Signature!.NamedArguments)
        {
            caSig.NamedArguments.Add(new(
                arg.MemberType,
                arg.MemberName,
                backportsShim.DefaultImporter.ImportTypeSignature(arg.ArgumentType),
                new(
                    backportsShim.DefaultImporter.ImportTypeSignature(arg.Argument.ArgumentType),
                    arg.Argument.Elements)));
        }

        var ca = new CustomAttribute(
            (ICustomAttributeType?)backportsShim.DefaultImporter.ImportMethodOrNull(backportsAttr.Constructor),
            caSig);

        backportsShimAssembly.CustomAttributes.Add(ca);
    }

    // go through all public types, make sure they exist in the shim, and generate the forwarder
    foreach (var type in bclShim.TopLevelTypes)
    {
        void ExportType(TypeDefinition type, bool nested)
        {
            if (!type.IsPublic) return;

            // note: we don't do any validation here, because we'll invoke apicompat for that logic after generating everything
            backportsShim.ExportedTypes.Add(new ExportedType(backportsReference, type.Namespace, type.Name)
            {
                Attributes = nested ? 0 : TypeAttributes.Forwarder,
            });

            foreach (var nestedType in type.NestedTypes)
            {
                ExportType(nestedType, true);
            }
        }

        ExportType(type, false);
    }

    // write out the resultant shim assembly
    var newShimPath = Path.Combine(outputRefDir, Path.GetFileName(shimPath));
    backportsShim.Write(newShimPath, peImageBuilder);

    // then finalize the strong name as needed
    if (backportsShimAssembly.HasPublicKey)
    {
        var name = Convert.ToHexString(backportsShimAssembly.GetPublicKeyToken()!);
        var keyPath = Path.Combine(snkPath, name + ".snk");
        if (!File.Exists(keyPath))
        {
            Console.Error.WriteLine($"Cannot finalize strong-name for {newShimPath}!");
            Console.Error.WriteLine($"    Requires SNK file for public key token {keyPath}.");
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

return 0;
