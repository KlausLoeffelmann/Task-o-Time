using Xunit;

namespace Modernization.Analyzers.Tests;

public sealed class RubricTests
{
    [Fact]
    public void Perfect_outcome_scores_one()
    {
        Assert.Equal(1d, RepositoryTests.FullScore([], valid: true), 6);
    }

    [Fact]
    public void Independent_business_subrules_receive_independent_credit()
    {
        var oneDefect = RepositoryTests.FullScore([Diagnostic("BUS001")], valid: true);
        var bothDefects = RepositoryTests.FullScore(
            [Diagnostic("BUS001"), Diagnostic("BUS002")], valid: true);

        Assert.Equal(1d - 14d / 95d, oneDefect, 6);
        Assert.Equal(1d - 28d / 95d, bothDefects, 6);
    }

    [Fact]
    public void Duplicate_diagnostics_do_not_change_a_subrule_weight()
    {
        var one = RepositoryTests.FullScore([Diagnostic("MOD001")], valid: true);
        var repeated = RepositoryTests.FullScore(
            [Diagnostic("MOD001"), Diagnostic("MOD001"), Diagnostic("MOD001")], valid: true);

        Assert.Equal(one, repeated, 6);
    }

    [Fact]
    public void Invalid_input_scores_zero()
    {
        Assert.Equal(0d, RepositoryTests.FullScore([], valid: false));
    }

    private static ScanDiagnostic Diagnostic(string id) =>
        new(id, "Warning", "fixture", "", 0, 0, id);
}
