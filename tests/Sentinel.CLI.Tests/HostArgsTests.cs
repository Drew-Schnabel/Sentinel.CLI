using FluentAssertions;

namespace Sentinel.CLI.Tests;

public class HostArgsTests
{
    [Theory]
    [InlineData("--server", "--server")]
    [InlineData("--SERVER", "--server")]
    [InlineData("--Demo", "--demo")]
    public void Has_finds_a_flag_case_insensitively(string argInArray, string lookup)
        => HostArgs.Has([argInArray, "--Receiver:GrpcPort=4319"], lookup).Should().BeTrue();

    [Fact]
    public void Has_is_false_when_absent()
        => HostArgs.Has(["--Receiver:GrpcPort=4319"], "--server").Should().BeFalse();

    [Fact]
    public void WithoutModeFlags_strips_server_and_demo_but_keeps_config_args()
    {
        var result = HostArgs.WithoutModeFlags(["--server", "--Receiver:GrpcPort=4319", "--demo", "--Receiver:HttpPort=4320"]);

        result.Should().Equal("--Receiver:GrpcPort=4319", "--Receiver:HttpPort=4320");
    }

    [Fact]
    public void WithoutModeFlags_is_case_insensitive()
        => HostArgs.WithoutModeFlags(["--Server", "--DEMO", "--Receiver:GrpcPort=4319"])
            .Should().Equal("--Receiver:GrpcPort=4319");

    [Fact]
    public void WithoutModeFlags_preserves_order_and_leaves_clean_args_untouched()
        => HostArgs.WithoutModeFlags(["--Receiver:GrpcPort=4319", "--Receiver:HttpPort=4320"])
            .Should().Equal("--Receiver:GrpcPort=4319", "--Receiver:HttpPort=4320");
}
