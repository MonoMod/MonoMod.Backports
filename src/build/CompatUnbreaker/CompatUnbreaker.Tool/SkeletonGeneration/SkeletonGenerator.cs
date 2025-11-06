using System.Diagnostics;
using System.Reflection;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;
using CompatUnbreaker.Tool.ApiCompatibility.Comparing;
using CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;
using CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;
using CompatUnbreaker.Tool.Utilities;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Tool.Utilities.Roslyn;
using CompatUnbreaker.Utilities.AsmResolver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Simplification;
using MonoMod.RuntimeDetour;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CompatUnbreaker.Tool.SkeletonGeneration;

internal static class SkeletonGenerator
{
    public static async Task<(MSBuildWorkspace Workspace, Project Project)> GenerateAsync(AssemblyMapper assemblyMapper, string projectPath)
    {
        var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(projectPath);

        return (workspace, await GenerateAsync(assemblyMapper, workspace, project));
    }

    public static async Task<Project> GenerateAsync(AssemblyMapper assemblyMapper, Workspace workspace, Project project)
    {
        using var hook = new Hook(
            Type.GetType("Microsoft.CodeAnalysis.CSharp.Extensions.SemanticModelExtensions, Microsoft.CodeAnalysis.CSharp.Workspaces")!
                .GetMethod("UnifiesNativeIntegers", BindingFlags.Public | BindingFlags.Static)!,
            bool (SemanticModel _) => false
        );

        var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException("Failed to get compilation");
        var shimTypes = compilation.Assembly.GlobalNamespace.GetAllTypes().ToArray();
        var shimExtensionTypes = shimTypes.Where(t =>
            t.ContainingType == null &&
            t.GetAttributes().Any(a => a.AttributeClass?.GetFullMetadataName() == "CompatUnbreaker.Attributes.UnbreakerExtensionsAttribute")
        ).SelectMany(t => t.GetTypeMembers()).ToArray();

        var topLevelTypeDifferences = new Dictionary<TypeMapper, TypeCompatDifference>();
        var memberDifferences = new Dictionary<TypeMapper, List<CompatDifference>>();

        void AddMemberDifference(TypeMapper typeMapper, CompatDifference difference)
        {
            if (!memberDifferences.TryGetValue(typeMapper, out var list))
            {
                memberDifferences.Add(typeMapper, list = []);
            }

            list.Add(difference);
        }

        var apiComparer = new ApiComparer();
        apiComparer.Compare(assemblyMapper);

        foreach (var difference in apiComparer.CompatDifferences)
        {
            Console.WriteLine(difference);

            switch (difference)
            {
                case TypeMustExistDifference typeDifference:
                {
                    if (typeDifference.Mapper.DeclaringType is { } declaringType)
                    {
                        AddMemberDifference(declaringType, typeDifference);
                        break;
                    }

                    topLevelTypeDifferences.Add(typeDifference.Mapper, typeDifference);
                    break;
                }

                case MemberMustExistDifference memberDifference:
                {
                    var typeMapper = memberDifference.Mapper.DeclaringType;
                    AddMemberDifference(typeMapper, memberDifference);
                    break;
                }

                default:
                    break;
            }
        }

        Document AddDocument(TypeDefinition type, SyntaxNode syntaxRoot)
        {
            var name = type.Name + ".cs";

            var folders = type.Namespace is null
                ? []
                : type.Namespace.Value.TrimPrefix(project.DefaultNamespace).Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (project.Documents.Any(d => d.Folders.SequenceEqual(folders) && d.Name == name))
            {
                throw new InvalidOperationException($"Duplicate document '{(folders.Length == 0 ? string.Empty : string.Join('/', folders))}{name}'");
            }

            return project.AddDocument(name, syntaxRoot, folders);
        }

        var modifiedDocuments = new HashSet<DocumentId>();

        foreach (var (mapper, difference) in topLevelTypeDifferences)
        {
            if (difference is not TypeMustExistDifference) continue;

            var (leftType, rightType) = mapper;
            if (leftType == null || rightType != null) continue;

            var syntaxGenerator = SyntaxGenerator.GetGenerator(project);

            var typeDeclaration = CreateDeclaration(syntaxGenerator, leftType);

            var syntaxRoot = CreateCompilationUnit(syntaxGenerator, leftType.Namespace, typeDeclaration);

            syntaxRoot = (CompilationUnitSyntax) MethodBodyRewriter.Instance.Visit(syntaxRoot);

            var document = AddDocument(leftType, syntaxRoot);
            modifiedDocuments.Add(document.Id);
            project = document.Project;
        }

        foreach (var (typeMapper, differences) in memberDifferences)
        {
            var (leftType, rightType) = typeMapper;
            if (leftType == null) continue;

            var missingMembers = new List<IMemberDefinition>();

            foreach (var difference in differences)
            {
                IMemberDefinition? left;
                IMemberDefinition? right;

                switch (difference)
                {
                    case TypeMustExistDifference typeMustExistDifference:
                        (left, right) = typeMustExistDifference.Mapper;
                        break;

                    case MemberMustExistDifference memberMustExistDifference:
                        (left, right) = memberMustExistDifference.Mapper;
                        break;

                    default:
                        throw new UnreachableException();
                }

                if (left == null || right != null) continue;

                missingMembers.Add(left);
            }

            var syntaxGenerator = SyntaxGenerator.GetGenerator(project);

            ITypeSymbol? existingShimType = compilation.Assembly.GetTypeByMetadataName(leftType.FullName);
            existingShimType ??= shimTypes.SingleOrDefault(t =>
                t.GetAttributes().Any(a =>
                    a.AttributeClass?.GetFullMetadataName() == "CompatUnbreaker.Attributes.UnbreakerReplaceAttribute" &&
                    ((ITypeSymbol) a.ConstructorArguments.Single().Value!).GetFullMetadataName() == leftType.FullName
                )
            );

            if (existingShimType != null)
            {
                var syntaxReference = existingShimType.DeclaringSyntaxReferences.First();
                var document = project.GetDocument(syntaxReference.SyntaxTree);
                var syntaxNode = await syntaxReference.GetSyntaxAsync();

                var editor = await DocumentEditor.CreateAsync(document);

                // TODO insert members preserving member kind order
                foreach (var member in missingMembers)
                {
                    if (!syntaxGenerator.CanBeDeclared(member)) continue;
                    editor.AddMember(syntaxNode, CreateDeclaration(syntaxGenerator, member));
                }

                document = editor.GetChangedDocument();
                modifiedDocuments.Add(document.Id);
                project = document.Project;
            }
            else
            {
                var existingShimNativeExtensionType = shimExtensionTypes.FirstOrDefault(t =>
                    t.ExtensionParameter != null &&
                    t.ExtensionParameter.Type.GetFullMetadataName() == leftType.FullName
                )?.DeclaringSyntaxReferences.First();

                var existingShimUnbreakerExtensionType = shimExtensionTypes.FirstOrDefault(t =>
                    t.GetAttributes().Any(a =>
                        a.AttributeClass?.GetFullMetadataName() == "CompatUnbreaker.Attributes.UnbreakerExtensionAttribute" &&
                        ((ITypeSymbol) a.ConstructorArguments.Single().Value!).GetFullMetadataName() == leftType.FullName
                    )
                )?.DeclaringSyntaxReferences.First();

                Document? document = null;
                SyntaxNode? extensionsType = null;

                if (existingShimNativeExtensionType != null || existingShimUnbreakerExtensionType != null)
                {
                    document = project.GetDocument((existingShimNativeExtensionType ?? existingShimUnbreakerExtensionType)!.SyntaxTree);
                    extensionsType = (await (existingShimNativeExtensionType ?? existingShimUnbreakerExtensionType)!.GetSyntaxAsync()).Parent;
                }

                var (nativeMembers, unbreakerMembers) = CreateExtensionMembers(syntaxGenerator, missingMembers);

                if (nativeMembers.Count > 0)
                {
                    await EditOrAddDocumentAsync(nativeMembers, existingShimNativeExtensionType, members =>
                    {
                        return ExtensionDeclaration()
                            .AddParameterListParameters(
                                Parameter(leftType.IsStatic() ? default : "this".ToIdentifierToken())
                                    .WithType(syntaxGenerator.TypeExpression(leftType, TypeContext.Empty))
                            )
                            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken))
                            .WithMembers(
                                List(members)
                            );
                    });
                }

                if (unbreakerMembers.Count > 0)
                {
                    await EditOrAddDocumentAsync(unbreakerMembers, existingShimUnbreakerExtensionType, members =>
                    {
                        MemberDeclarationSyntax node = ClassDeclaration($"{leftType.GetUnmangledName()}Extension")
                            .AddAttributeLists(
                                (AttributeListSyntax) syntaxGenerator.Attribute(
                                    "UnbreakerExtension",
                                    TypeOfExpression(syntaxGenerator.TypeExpression(leftType, TypeContext.Empty))
                                )
                            )
                            .WithModifiers(
                                TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                            )
                            .WithMembers(
                                List(members)
                            );

                        node = (MemberDeclarationSyntax) syntaxGenerator.WithTypeParametersAndConstraints(node, leftType.GenericParameters, TypeContext.From(leftType, leftType));

                        return node;
                    });
                }

                async Task EditOrAddDocumentAsync(List<MemberDeclarationSyntax> members, SyntaxReference? extensionType, Func<List<MemberDeclarationSyntax>, MemberDeclarationSyntax> factory)
                {
                    if (extensionType != null)
                    {
                        var syntaxNode = await extensionType.GetSyntaxAsync();

                        var editor = await DocumentEditor.CreateAsync(document);

                        // TODO insert members preserving member kind order
                        foreach (var member in members)
                        {
                            editor.AddMember(syntaxNode, member.WithSimplifyAnnotations());
                        }

                        document = editor.GetChangedDocument();
                    }
                    else if (document != null)
                    {
                        Debug.Assert(extensionsType != null);

                        var editor = await DocumentEditor.CreateAsync(document);

                        editor.AddMember(extensionsType, factory(members));

                        document = editor.GetChangedDocument();
                    }
                    else
                    {
                        if (typeMapper.DeclaringType != null)
                        {
                            throw new NotImplementedException();
                        }

                        MemberDeclarationSyntax node = ClassDeclaration($"{leftType.GetUnmangledName()}Extensions")
                            .AddAttributeLists(
                                (AttributeListSyntax) syntaxGenerator.Attribute("UnbreakerExtensions")
                            )
                            .WithModifiers(
                                TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                            )
                            .WithMembers(
                                List([factory(members)])
                            );

                        var syntaxRoot = CreateCompilationUnit(syntaxGenerator, leftType.Namespace, node);

                        document = AddDocument(leftType, syntaxRoot);
                        extensionsType = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
                    }

                    modifiedDocuments.Add(document.Id);
                    project = document.Project;
                }
            }
        }

        foreach (var documentId in modifiedDocuments)
        {
            var document = project.GetDocument(documentId)!;

            document = await SimplifyAsync(document);

            Console.WriteLine((await document.GetTextAsync()).ToString());
            project = document.Project;
        }

        Console.Write("Enter `y` to save: ");
        if (Console.ReadKey().KeyChar == 'y')
        {
            // TODO stupid
            var projectText = await File.ReadAllTextAsync(project.FilePath);

            if (!workspace.TryApplyChanges(project.Solution))
            {
                Console.WriteLine("Failed to apply changes");
            }

            await File.WriteAllTextAsync(project.FilePath, projectText);
        }

        return project;
    }

    private static async Task<Document> SimplifyAsync(Document document)
    {
        document = await ImportAdder.AddImportsAsync(document, Simplifier.AddImportsAnnotation);

        // ImportAdder adds leading trivia before the first member, but it's not cleaned up when Simplifier removes all usings, so just let Formatter handle it
        {
            var syntaxRoot = (CompilationUnitSyntax) (await document.GetSyntaxRootAsync())!;
            var firstNode = syntaxRoot.Members.First();
            document = document.WithSyntaxRoot(syntaxRoot.ReplaceNode(firstNode, firstNode.WithoutLeadingTrivia()));
        }

        document = await Simplifier.ReduceAsync(document, Simplifier.Annotation);
        document = await Formatter.FormatAsync(document, Formatter.Annotation);
        document = await Formatter.FormatAsync(document, SyntaxAnnotation.ElasticAnnotation);

        return document;
    }

    private static MemberDeclarationSyntax CreateDeclaration(SyntaxGenerator syntaxGenerator, IMemberDefinition member)
    {
        var declaration = syntaxGenerator.Declaration(member, m => m.IsVisibleOutsideOfAssembly()).WithSimplifyAnnotations();
        declaration = MethodBodyRewriter.Instance.Visit(declaration);
        return (MemberDeclarationSyntax) declaration;
    }

    private static CompilationUnitSyntax CreateCompilationUnit(SyntaxGenerator syntaxGenerator, Utf8String? @namespace, MemberDeclarationSyntax node)
    {
        if (@namespace is not null)
        {
            var namespaceSyntax = (NameSyntax) syntaxGenerator.DottedName(@namespace);
            node = FileScopedNamespaceDeclaration(namespaceSyntax)
                .AddMembers(node);
        }

        return CompilationUnit()
            .AddMembers(node)
            .WithTrailingTrivia(LineFeed)
            .WithSimplifyAnnotations();
    }

    private static TNode WithSimplifyAnnotations<TNode>(this TNode node) where TNode : SyntaxNode
    {
        return node.WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation, Formatter.Annotation);
    }

    private static (List<MemberDeclarationSyntax> NativeMembers, List<MemberDeclarationSyntax> UnbreakerMembers) CreateExtensionMembers(SyntaxGenerator syntaxGenerator, List<IMemberDefinition> members)
    {
        var nativeMembers = new List<MemberDeclarationSyntax>();
        var unbreakerMembers = new List<MemberDeclarationSyntax>();

        foreach (var member in members)
        {
            if (member is TypeDefinition type)
            {
                unbreakerMembers.Add(CreateDeclaration(syntaxGenerator, type));
            }
            else if (member is FieldDefinition field)
            {
                var fieldType = syntaxGenerator.TypeExpression(field.Signature!.FieldType, TypeContext.From(field, field)).ToString();
                var fieldName = field.Name!.Value.EscapeIdentifier();

                var body = field.IsInitOnly
                    ? " => throw new NotImplementedException();"
                    : """

                      {
                          get => throw new NotImplementedException();
                          set => throw new NotImplementedException();
                      }
                      """;

                nativeMembers.Add(ParseMemberDeclaration(
                    $"""
                     [UnbreakerField]
                     public {(field.IsStatic ? "static " : string.Empty)}{fieldType} {fieldName}{body}
                     """
                )!.WithLeadingTrivia(LineFeed).WithTrailingTrivia(LineFeed).WithSimplifyAnnotations());
            }
            else if (member is MethodDefinition method)
            {
                if (method.IsPropertyAccessor())
                {
                    continue;
                }

                if (method.IsEventAccessor() && member.IsStatic())
                {
                    continue;
                }

                if (method.IsConstructor)
                {
                    method.Name = "Create";
                    method.IsStatic = true;
                    method.Signature = MethodSignature.CreateStatic(method.DeclaringType.ToTypeSignature(), method.Signature.ParameterTypes);

                    var declaration = (BaseMethodDeclarationSyntax) CreateDeclaration(syntaxGenerator, method);

                    declaration = declaration.WithAttributeLists(declaration.AttributeLists.Insert(0, AttributeList([Attribute(IdentifierName("UnbreakerConstructor"))])));

                    unbreakerMembers.Add(declaration);
                }
                else
                {
                    var declaration = (BaseMethodDeclarationSyntax) CreateDeclaration(syntaxGenerator, method);

                    if (method.IsExtension())
                    {
                        var firstParameter = declaration.ParameterList.Parameters[0];
                        declaration = declaration.WithParameterList(declaration.ParameterList.WithParameters(
                            declaration.ParameterList.Parameters.Replace(
                                firstParameter,
                                firstParameter
                                    .WithModifiers(firstParameter.Modifiers.Remove(firstParameter.Modifiers.Single(m => m.IsKind(SyntaxKind.ThisKeyword))))
                                    .WithAttributeLists(firstParameter.AttributeLists.Insert(0, AttributeList([Attribute(IdentifierName("UnbreakerThis"))])))
                            )
                        ));
                    }

                    nativeMembers.Add(declaration);
                }
            }
            else if (member is PropertyDefinition property)
            {
                nativeMembers.Add(CreateDeclaration(syntaxGenerator, property));
            }
            else if (member is EventDefinition @event)
            {
                if (!@event.IsStatic())
                {
                    continue; // TODO
                    throw new NotImplementedException();
                }

                unbreakerMembers.Add(CreateDeclaration(syntaxGenerator, @event));
            }
            else
            {
                throw new UnreachableException();
            }
        }

        return (nativeMembers, unbreakerMembers);
    }

    // private static MemberDeclarationSyntax CreateMemberFromClassDeclaration<T>(
    //     SyntaxGenerator syntaxGenerator,
    //     T member
    // ) where T : IMemberDefinition, IHasCustomAttribute
    // {
    //     if (member.IsStatic())
    //     {
    //         throw new NotImplementedException();
    //     }
    //
    //     var targetType = syntaxGenerator.TypeExpression(GetParameterType(member.DeclaringType!), TypeContext.Empty);
    //     var propertyType = syntaxGenerator.TypeExpression(
    //         member switch
    //         {
    //             PropertyDefinition property => property.Signature!.ReturnType,
    //             FieldDefinition field => field.Signature!.FieldType,
    //             _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
    //         },
    //         TypeContext.From(member, member)
    //     );
    //
    //     var hasGetMethod = member switch
    //     {
    //         PropertyDefinition property => property.GetMethod != null,
    //         FieldDefinition => true,
    //         _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
    //     };
    //
    //     var hasSetMethod = member switch
    //     {
    //         PropertyDefinition property => property.SetMethod != null,
    //         FieldDefinition field => !field.IsInitOnly,
    //         _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
    //     };
    //
    //     var accessors = new List<SyntaxNode>();
    //
    //     var thisParameter = syntaxGenerator.ParameterDeclaration("@this", targetType);
    //
    //     if (hasGetMethod)
    //     {
    //         accessors.Add(syntaxGenerator.MethodDeclaration(
    //             accessibility: Accessibility.Public,
    //             modifiers: DeclarationModifiers.Static,
    //             returnType: propertyType,
    //             name: "Get",
    //             parameters: [thisParameter]
    //         ));
    //     }
    //
    //     if (hasSetMethod)
    //     {
    //         accessors.Add(syntaxGenerator.MethodDeclaration(
    //             accessibility: Accessibility.Public,
    //             modifiers: DeclarationModifiers.Static,
    //             name: "Set",
    //             parameters:
    //             [
    //                 thisParameter,
    //                 syntaxGenerator.ParameterDeclaration("value", propertyType),
    //             ]
    //         ));
    //     }
    //
    //     var declaration = syntaxGenerator.ClassDeclaration(
    //         member.Name!.EscapeIdentifier(),
    //         accessibility: Accessibility.Public,
    //         modifiers: DeclarationModifiers.Static,
    //         members: accessors
    //     );
    //
    //     declaration = declaration.ReplaceNodes(
    //         declaration.DescendantNodes().OfType<MethodDeclarationSyntax>(),
    //         (node, _) => node
    //             .WithBody(null)
    //             .WithExpressionBody(MethodBodyRewriter.ExpressionBody)
    //             .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
    //     );
    //
    //     return (MemberDeclarationSyntax) syntaxGenerator.AddAttributes(
    //         declaration,
    //         syntaxGenerator.Attribute(member switch
    //         {
    //             PropertyDefinition => "CompatUnbreaker.Attributes.UnbreakerPropertyAttribute",
    //             FieldDefinition => "CompatUnbreaker.Attributes.UnbreakerFieldAttribute",
    //             EventDefinition => "CompatUnbreaker.Attributes.UnbreakerEventAttribute",
    //             _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
    //         })
    //     );
    // }

    // private static void ConvertInstanceMethodToExtension(MethodDefinition method)
    // {
    //     if (method.IsStatic) return;
    //
    //     ArgumentNullException.ThrowIfNull(method.DeclaringType);
    //     ArgumentNullException.ThrowIfNull(method.Signature);
    //
    //     var module = method.Module;
    //     ArgumentNullException.ThrowIfNull(module);
    //
    //     method.CustomAttributes.Add(new CustomAttribute(
    //         new MemberReference(
    //             new TypeReference(module, module.CorLibTypeFactory.CorLibScope, "System.Runtime.CompilerServices"u8, "ExtensionAttribute"u8),
    //             ".ctor"u8,
    //             MethodSignature.CreateInstance(module.CorLibTypeFactory.Void)
    //         )
    //     ));
    //
    //     method.IsStatic = true;
    //
    //     method.Signature.HasThis = false;
    //     method.Signature.ParameterTypes.Insert(0, GetParameterType(method.DeclaringType));
    //
    //     foreach (var definition in method.ParameterDefinitions)
    //     {
    //         definition.Sequence++;
    //     }
    //
    //     method.Parameters.PullUpdatesFromMethodSignature();
    //     method.Parameters[0].GetOrCreateDefinition().Name = "this";
    // }

    // private static TypeSignature GetParameterType(TypeDefinition type)
    // {
    //     TypeSignature result;
    //     if (type.GenericParameters.Count > 0)
    //     {
    //         var genArgs = new TypeSignature[type.GenericParameters.Count];
    //         for (var i = 0; i < genArgs.Length; i++)
    //             genArgs[i] = new GenericParameterSignature(type.Module, GenericParameterType.Type, i);
    //         result = type.MakeGenericInstanceType(genArgs);
    //     }
    //     else
    //     {
    //         result = type.ToTypeSignature();
    //     }
    //
    //     if (type.IsValueType)
    //         result = result.MakeByReferenceType();
    //
    //     return result;
    // }
}
