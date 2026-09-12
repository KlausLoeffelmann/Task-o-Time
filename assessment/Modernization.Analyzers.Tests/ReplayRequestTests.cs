using System.Text.Json;
using ExternalEvaluation;
using Xunit;

namespace Modernization.Analyzers.Tests;

// Explicit operational selectors; neither exports an acceptance verdict or evaluates MSBuild.
public sealed class ReplayRequestTests
{
    [Fact]
    [Trait("Category", "ProjectInventory")]
    public void Export_trusted_inventory_without_evaluating_or_building_projects()
    {
        var root = EvaluatorConfiguration.SourceRoot;
        var roles = ProjectRoles.Read(ToolReplay.Plan);
        var projects = EvaluatorConfiguration.DiscoveryRoots.SelectMany(RepositoryTests.DiscoverProjects)
            .Concat(roles.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        ProjectRoles.ValidateInventory(projects, root, roles);
        var output = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "Reports", StagePolicy.Current.Identity);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "project-inventory.json"), JsonSerializer.Serialize(projects.Select(path =>
            new { Project = path, Role = roles.TryGetValue(path, out var role) ? role.Role : "application",
                BuildProperties = roles.GetValueOrDefault(path)?.BuildProperties }),
            new JsonSerializerOptions { WriteIndented = true }));
        Assert.NotEmpty(projects);
    }

    [Fact]
    [Trait("Category", "ReplayRequest")]
    public void Export_external_request_without_executing_submission_projects()
    {
        var plan = ToolReplay.Plan ?? throw new InvalidDataException("Set ASSESSMENT_REPLAY_PLAN.");
        var challenge = Environment.GetEnvironmentVariable("ASSESSMENT_REPLAY_CHALLENGE")
            ?? throw new InvalidDataException("Set a fresh ASSESSMENT_REPLAY_CHALLENGE.");
        Assert.True(File.Exists(ExternalReplay.WriteRequest(plan, StagePolicy.Current, challenge)));
    }

    [Fact]
    [Trait("Category", "ReferenceReviewRequest")]
    public void Export_owned_source_review_request_without_building_the_producer()
    {
        var plan = ToolReplay.Plan ?? throw new InvalidDataException("Set ASSESSMENT_REPLAY_PLAN.");
        var output = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "Reports", StagePolicy.Current.Identity);
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "reference-review-request.json");
        File.WriteAllText(path, JsonSerializer.Serialize(ReferenceReplay.CreateReviewRequest(plan),
            new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(File.Exists(path));
    }
}
