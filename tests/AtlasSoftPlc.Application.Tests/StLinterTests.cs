using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Application.Logic;
using Xunit;
using FluentAssertions;

namespace AtlasSoftPlc.Application.Tests.Logic;

public class StLinterTests
{
    [Fact]
    public void Lint_ValidCode_ShouldPass()
    {
        var code = @"
VAR
    Start : BOOL;
    Motor : BOOL;
END_VAR

Motor := Start;";

        var linter = new StLinter();
        var result = linter.Lint(code);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Lint_MissingSemicolon_ShouldFail()
    {
        var code = @"
VAR
    Start : BOOL;
END_VAR

Motor := Start"; // Falta ;

        var linter = new StLinter();
        var result = linter.Lint(code);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("punto y coma"));
    }

    [Fact]
    public void Lint_UnbalancedParentheses_ShouldFail()
    {
        var code = @"
VAR
    Start : BOOL;
END_VAR

Motor := (Start;"; // Paréntesis no cerrado

        var linter = new StLinter();
        var result = linter.Lint(code);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Paréntesis no balanceados"));
    }

    [Fact]
    public void Lint_MissingBlocks_ShouldFail()
    {
        var code = @"Motor := Start;";

        var linter = new StLinter();
        var result = linter.Lint(code);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("VAR/END_VAR"));
    }
}
