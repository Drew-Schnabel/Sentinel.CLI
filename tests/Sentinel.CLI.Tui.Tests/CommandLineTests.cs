using FluentAssertions;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":")]
    [InlineData("  :  ")]
    [InlineData(null)]
    public void Parse_returns_null_for_blank_input(string? input)
        => CommandLine.Parse(input).Should().BeNull();

    [Fact]
    public void Parse_reads_a_bare_verb()
    {
        var parsed = CommandLine.Parse("help");

        parsed.Should().NotBeNull();
        parsed!.Verb.Should().Be("help");
        parsed.Positionals.Should().BeEmpty();
        parsed.Options.Should().BeEmpty();
    }

    [Fact]
    public void Parse_lower_cases_the_verb_but_keeps_argument_casing()
    {
        var parsed = CommandLine.Parse("Filter Orders-API");

        parsed!.Verb.Should().Be("filter");
        parsed.Positionals.Should().ContainSingle().Which.Should().Be("Orders-API");
    }

    [Fact]
    public void Parse_tolerates_a_leading_colon_and_extra_whitespace()
    {
        var parsed = CommandLine.Parse(":  clear  ");

        parsed!.Verb.Should().Be("clear");
        parsed.Positionals.Should().BeEmpty();
    }

    [Fact]
    public void Parse_splits_positionals_and_key_value_options()
    {
        var parsed = CommandLine.Parse("filter checkout service=orders-api status=error");

        parsed!.Verb.Should().Be("filter");
        parsed.Positionals.Should().ContainSingle().Which.Should().Be("checkout");
        parsed.Options.Should().HaveCount(2);
        parsed.Options["service"].Should().Be("orders-api");
        parsed.Options["status"].Should().Be("error");
    }

    [Fact]
    public void Parse_treats_a_leading_equals_as_a_positional_not_an_option()
    {
        var parsed = CommandLine.Parse("verb =value");

        parsed!.Positionals.Should().ContainSingle().Which.Should().Be("=value");
        parsed.Options.Should().BeEmpty();
    }

    [Fact]
    public void Parse_allows_an_empty_option_value()
    {
        var parsed = CommandLine.Parse("verb key=");

        parsed!.Options.Should().ContainKey("key");
        parsed.Options["key"].Should().BeEmpty();
    }
}
