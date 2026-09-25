using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Application.Logic;
using Xunit;

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

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
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

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("punto y coma"));
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

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Paréntesis no balanceados"));
    }

    [Fact]
    public void Lint_MissingBlocks_ShouldFail()
    {
        var code = @"Motor := Start;";

        var linter = new StLinter();
        var result = linter.Lint(code);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("VAR/END_VAR"));
    }
}
