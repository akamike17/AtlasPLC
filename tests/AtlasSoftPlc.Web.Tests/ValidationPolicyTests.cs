using AtlasSoftPlc.Application.Validation;

namespace AtlasSoftPlc.Web.Tests;

public sealed class ValidationPolicyTests
{
    [Fact]
    public void WarningDoesNotBlockSimulationButBlocksDeploy()
    {
        var issue = new ValidationIssue { Severity = ValidationSeverity.Warning };
        Assert.False(ValidationPolicy.For(ValidationOperation.Simulation).Blocks(issue));
        Assert.True(ValidationPolicy.For(ValidationOperation.Deploy).Blocks(issue));
    }
}
