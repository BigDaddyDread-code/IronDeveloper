using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynProject = Microsoft.CodeAnalysis.Project;

namespace IronDev.Infrastructure.Services.CodeIntelligence;

public sealed class RoslynProjectSemanticIndexService : IProjectSemanticIndexService
{
    public async Task<SemanticIndex> IndexProjectAsync(
        string solutionOrProjectPath,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var projects = new List<SemanticProjectInfo>();
        var symbols = new List<SemanticSymbolInfo>();

        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
        {
            warnings.Add("No solution or project path was provided for semantic indexing.");
            return Empty(solutionOrProjectPath, warnings);
        }

        if (!File.Exists(solutionOrProjectPath))
        {
            warnings.Add($"Solution or project path does not exist: {solutionOrProjectPath}");
            return Empty(solutionOrProjectPath, warnings);
        }

        EnsureMsBuildRegistered(warnings);

        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e => warnings.Add(e.Diagnostic.Message));

        try
        {
            var solution = await LoadSolutionAsync(workspace, solutionOrProjectPath, cancellationToken);
            foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var before = symbols.Count;
                var projectSymbols = await ExtractProjectSymbolsAsync(project, cancellationToken);
                symbols.AddRange(projectSymbols);

                projects.Add(new SemanticProjectInfo
                {
                    Name = project.Name,
                    FilePath = project.FilePath ?? string.Empty,
                    SymbolCount = symbols.Count - before
                });
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Roslyn semantic index failed: {ex.GetType().Name}: {ex.Message}");
        }

        return new SemanticIndex
        {
            RootPath = solutionOrProjectPath,
            IndexedAtUtc = DateTimeOffset.UtcNow,
            Projects = projects,
            Symbols = symbols,
            LanguageQuality = BuildLanguageQuality(symbols, projects),
            Warnings = warnings
        };
    }

    private static async Task<Solution> LoadSolutionAsync(
        MSBuildWorkspace workspace,
        string solutionOrProjectPath,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(solutionOrProjectPath);
        if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(solutionOrProjectPath, cancellationToken: cancellationToken);
            return project.Solution;
        }

        return await workspace.OpenSolutionAsync(solutionOrProjectPath, cancellationToken: cancellationToken);
    }

    private static async Task<IReadOnlyList<SemanticSymbolInfo>> ExtractProjectSymbolsAsync(
        RoslynProject project,
        CancellationToken cancellationToken)
    {
        var symbols = new List<SemanticSymbolInfo>();

        foreach (var document in project.Documents.Where(d => d.SourceCodeKind == SourceCodeKind.Regular))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var model = await document.GetSemanticModelAsync(cancellationToken);
            if (root == null || model == null)
                continue;

            foreach (var node in root.DescendantNodes())
            {
                var symbol = GetDeclaredSymbol(model, node, cancellationToken);
                if (symbol == null)
                    continue;

                var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                var span = location?.GetLineSpan();

                symbols.Add(new SemanticSymbolInfo
                {
                    LanguageId = "csharp",
                    FilePath = document.FilePath ?? string.Empty,
                    Name = symbol.Name,
                    Kind = GetKind(symbol),
                    FullyQualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ContainerName = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                        ?? symbol.ContainingNamespace?.ToDisplayString(),
                    Signature = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    DocumentationComment = EmptyToNull(symbol.GetDocumentationCommentXml()),
                    StartLine = span is null ? null : span.Value.StartLinePosition.Line + 1,
                    EndLine = span is null ? null : span.Value.EndLinePosition.Line + 1,
                    Confidence = "High"
                });
            }
        }

        return symbols;
    }

    private static ISymbol? GetDeclaredSymbol(
        SemanticModel model,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        return node switch
        {
            TypeDeclarationSyntax typeDeclaration => model.GetDeclaredSymbol(typeDeclaration, cancellationToken),
            EnumDeclarationSyntax enumDeclaration => model.GetDeclaredSymbol(enumDeclaration, cancellationToken),
            DelegateDeclarationSyntax delegateDeclaration => model.GetDeclaredSymbol(delegateDeclaration, cancellationToken),
            MethodDeclarationSyntax methodDeclaration => model.GetDeclaredSymbol(methodDeclaration, cancellationToken),
            ConstructorDeclarationSyntax constructorDeclaration => model.GetDeclaredSymbol(constructorDeclaration, cancellationToken),
            PropertyDeclarationSyntax propertyDeclaration => model.GetDeclaredSymbol(propertyDeclaration, cancellationToken),
            EventDeclarationSyntax eventDeclaration => model.GetDeclaredSymbol(eventDeclaration, cancellationToken),
            FieldDeclarationSyntax fieldDeclaration => fieldDeclaration.Declaration.Variables.Count == 1
                ? model.GetDeclaredSymbol(fieldDeclaration.Declaration.Variables[0], cancellationToken)
                : null,
            _ => null
        };
    }

    private static string GetKind(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol namedType => namedType.TypeKind.ToString(),
            IMethodSymbol method => method.MethodKind == MethodKind.Constructor ? "Constructor" : "Method",
            IPropertySymbol => "Property",
            IEventSymbol => "Event",
            IFieldSymbol => "Field",
            _ => symbol.Kind.ToString()
        };
    }

    private static IReadOnlyList<LanguageContextQuality> BuildLanguageQuality(
        IReadOnlyList<SemanticSymbolInfo> symbols,
        IReadOnlyList<SemanticProjectInfo> projects)
    {
        return
        [
            new LanguageContextQuality
            {
                LanguageId = "csharp",
                Confidence = symbols.Count > 0 ? "High" : "Low",
                FileCount = symbols.Select(s => s.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                SymbolCount = symbols.Count,
                Notes = projects.Count > 0
                    ? "Roslyn MSBuild semantic index loaded C# projects."
                    : "Roslyn MSBuild semantic index did not load any C# projects."
            }
        ];
    }

    private static void EnsureMsBuildRegistered(ICollection<string> warnings)
    {
        if (MSBuildLocator.IsRegistered)
            return;

        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (Exception ex)
        {
            warnings.Add($"MSBuild registration warning: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static SemanticIndex Empty(string rootPath, IReadOnlyList<string> warnings)
        => new()
        {
            RootPath = rootPath,
            IndexedAtUtc = DateTimeOffset.UtcNow,
            Warnings = warnings
        };

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
