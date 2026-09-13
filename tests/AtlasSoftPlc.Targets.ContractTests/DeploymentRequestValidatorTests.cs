using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Targets.ContractTests;

public sealed class DeploymentRequestValidatorTests
{
    private static readonly TargetIdentity Target = new() { Manufacturer = "Acme", Family = "X", Model = "1" };
    private static readonly DateTimeOffset Issued = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = "test-confirmation-key-32-bytes!!"u8.ToArray();

    private static (PlcProgramDefinition Project, DeploymentRequest Request, DeploymentRequestValidator Validator) Build()
    {
        var project = new PlcProgramDefinition { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "P", Version = 3 };
        var nonce = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var expires = Issued.Add(DeploymentRequestValidator.DefaultTtl);
        var verifier = new HmacDeploymentConfirmationVerifier(Key);
        var context = new DeploymentConfirmationContext("alice", "session-1", project.Id, CanonicalProgramHasher.ComputeHash(project), Target, nonce, expires);
        var request = new DeploymentRequest
        {
            TargetManufacturer = Target.Manufacturer, TargetFamily = Target.Family, TargetModel = Target.Model,
            ProjectId = project.Id, ProjectVersion = project.Version, ProjectHash = context.ProjectHash,
            ConfirmationToken = verifier.CreateForTests(context), ConfirmedBy = context.User, SessionId = context.SessionId,
            Nonce = nonce, IssuedUtc = Issued
        };
        return (project, request, new DeploymentRequestValidator(new InMemoryDeploymentNonceStore(), verifier));
    }

    [Fact]
    public void ValidConfirmation_Passes()
    {
        var (project, request, validator) = Build();
        Assert.True(validator.Validate(project, Target, request, Issued.AddMinutes(1)).IsValid);
    }

    [Fact]
    public void ArbitraryToken_IsRejected()
    {
        var (project, request, validator) = Build();
        request = request with { ConfirmationToken = "anything" };
        var result = validator.Validate(project, Target, request, Issued.AddMinutes(1));
        Assert.Equal("DEPLOY-TOKEN-INVALID", result.ErrorCode);
    }

    [Fact]
    public void Replay_IsRejected()
    {
        var (project, request, validator) = Build();
        Assert.True(validator.Validate(project, Target, request, Issued.AddMinutes(1)).IsValid);
        var result = validator.Validate(project, Target, request, Issued.AddMinutes(1));
        Assert.Equal("DEPLOY-REPLAY", result.ErrorCode);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("project")]
    [InlineData("hash")]
    [InlineData("expired")]
    public void Mismatch_IsRejected(string kind)
    {
        var (project, request, validator) = Build();
        var target = Target;
        var now = Issued.AddMinutes(1);
        if (kind == "target") target = new TargetIdentity { Manufacturer = "Other", Family = Target.Family, Model = Target.Model };
        if (kind == "project") request = request with { ProjectId = Guid.NewGuid() };
        if (kind == "hash") request = request with { ProjectHash = "bad" };
        if (kind == "expired") now = Issued.AddMinutes(6);
        var result = validator.Validate(project, target, request, now);
        Assert.False(result.IsValid);
    }
}
