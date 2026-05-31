using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Domain.Tests.Telemetry.Common;

public class TraceIdTests
{
    [Fact]
    public void Parse_accepts_32_lowercase_hex()
    {
        var id = TraceId.Parse("4bf92f3577b34da6a3ce929d0e0e4736");

        id.Value.Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
    }

    [Theory]
    [InlineData("")]
    [InlineData("4BF92F3577B34DA6A3CE929D0E0E4736")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e473")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Parse_rejects_invalid_inputs(string value)
    {
        var act = () => TraceId.Parse(value);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Parse_rejects_all_zeros()
    {
        var act = () => TraceId.Parse("00000000000000000000000000000000");

        act.Should().Throw<FormatException>();
    }
}
