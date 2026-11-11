using System.Reflection;
using System.Text.Json;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Tests.Core;

public sealed class CoreApiCompatibilityTests
{
    [Fact]
    public void PublicContractsRemainHostedByCoreAssembly()
    {
        const string expectedAssemblyName = "SqlDeps.Core";

        Assert.Equal(expectedAssemblyName, typeof(RefactorCandidateProjector).Assembly.GetName().Name);
        Assert.Equal(expectedAssemblyName, typeof(RefactorImpactResolver).Assembly.GetName().Name);
    }

    [Fact]
    public void AnalysisContractsRemainHostedByAnalysisAssembly()
    {
        const string expectedAssemblyName = "SqlDeps.Analysis";

        Assert.Equal(expectedAssemblyName, typeof(LintSuppressions).Assembly.GetName().Name);
    }

    [Fact]
    public void DomainContractsRemainHostedByDomainAssembly()
    {
        const string expectedAssemblyName = "SqlDeps.Domain";

        Assert.Equal(expectedAssemblyName, typeof(DatabaseObject).Assembly.GetName().Name);
        Assert.Equal(expectedAssemblyName, typeof(IndexAnalysisMetadata).Assembly.GetName().Name);
    }

    [Fact]
    public void CoreNamespacePublicSurfaceRemainsStable()
    {
        Assembly coreAssembly = typeof(RefactorCandidateProjector).Assembly;
        string[] expectedTypes =
        [
            "SqlDeps.Core.DocumentDiagnosticService",
            "SqlDeps.Core.DocumentDiagnosticResult",
            "SqlDeps.Core.DynamicSqlParameterizationService",
            "SqlDeps.Core.QueryPreview",
            "SqlDeps.Core.RefactorCandidate",
            "SqlDeps.Core.RefactorCandidateProjector",
            "SqlDeps.Core.RefactorDependencyImpact",
            "SqlDeps.Core.RefactorDependencyItem",
            "SqlDeps.Core.RefactorGraphSummary",
            "SqlDeps.Core.RefactorImpactResolver",
            "SqlDeps.Core.RefactorImpactRow",
            "SqlDeps.Core.RefactorPosition",
            "SqlDeps.Core.RefactorRange",
            "SqlDeps.Core.RefactorTextEdit",
            "SqlDeps.Core.RefactorWorkspaceEdit",
            "SqlDeps.Core.SqlFormatResult",
            "SqlDeps.Core.SqlFormatter",
            "SqlDeps.Core.SqlQueryBuilder",
            "SqlDeps.Core.SqlTestSkeleton",
            "SqlDeps.Core.SqlTestSkeletonBuilder",
        ];

        string[] actualTypes = coreAssembly
            .GetExportedTypes()
            .Where(type => string.Equals(type.Namespace, "SqlDeps.Core", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

        string[] missingTypes = expectedTypes.Except(actualTypes, StringComparer.Ordinal).ToArray();
        string[] unexpectedTypes = actualTypes.Except(expectedTypes, StringComparer.Ordinal).ToArray();

        Assert.True(
            missingTypes.Length == 0,
            $"Missing public Core types: {string.Join(", ", missingTypes)}");
        Assert.True(
            unexpectedTypes.Length == 0,
            $"Unexpected public Core types: {string.Join(", ", unexpectedTypes)}");
    }

    [Fact]
    public void ParserAndSqliteHelpersRemainInternal()
    {
        Assembly coreAssembly = typeof(IndexQueryService).Assembly;
        Assembly analysisAssembly = typeof(LintSuppressions).Assembly;
        string[] internalHelpers =
        [
            "SqlDeps.Storage.SqliteMetadataMapper",
            "SqlDeps.Storage.SqliteSchema",
        ];

        foreach (string helperName in internalHelpers)
        {
            Type helper = coreAssembly.GetType(helperName, throwOnError: true)!;

            Assert.False(helper.IsPublic, helperName);
        }

        Type dynamicSqlText = analysisAssembly.GetType("SqlDeps.DynamicSqlText", throwOnError: true)!;

        Assert.False(dynamicSqlText.IsPublic, dynamicSqlText.FullName);
    }

    [Fact]
    public void RefactorProjectorRemainsUsableThroughPublicCoreContract()
    {
        using var document = JsonDocument.Parse("""
            [{
              "title": "Expand SELECT *",
              "data": {"writesFile": false, "confidence": "high", "rule": "select-star"},
              "edit": {"changes": {"file:///query.sql": [{
                "range": {"start": {"line": 0, "character": 7}, "end": {"line": 0, "character": 8}},
                "newText": "id"
              }]}}
            }]
            """);

        var candidates = RefactorCandidateProjector.Project(document.RootElement.EnumerateArray());

        var candidate = Assert.Single(candidates);
        Assert.Equal("select-star", candidate.Rule);
        Assert.False(candidate.WritesFile);
    }

    [Fact]
    public void CoreFactoryMethodsRemainPublicAndTyped()
    {
        AssertPublicMethod(
            typeof(SqlFormatter),
            nameof(SqlFormatter.Format),
            typeof(SqlFormatResult),
            typeof(string));
        AssertPublicMethod(
            typeof(SqlQueryBuilder),
            nameof(SqlQueryBuilder.BuildSelect),
            typeof(QueryPreview),
            typeof(string),
            typeof(string),
            typeof(IEnumerable<string>));
        AssertPublicMethod(
            typeof(SqlQueryBuilder),
            nameof(SqlQueryBuilder.BuildSelectWithJoin),
            typeof(QueryPreview),
            typeof(string),
            typeof(string),
            typeof(IEnumerable<string>),
            typeof(string),
            typeof(IEnumerable<string>),
            typeof(string),
            typeof(string));
        AssertPublicMethod(
            typeof(SqlTestSkeletonBuilder),
            nameof(SqlTestSkeletonBuilder.Build),
            typeof(SqlTestSkeleton),
            typeof(string),
            typeof(IEnumerable<ObjectParameter>));
        AssertPublicMethod(
            typeof(RefactorCandidateProjector),
            nameof(RefactorCandidateProjector.Project),
            typeof(RefactorCandidate[]),
            typeof(IEnumerable<JsonElement>));
    }

    private static void AssertPublicMethod(
        Type declaringType,
        string methodName,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo? method = declaringType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            parameterTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
    }
}
