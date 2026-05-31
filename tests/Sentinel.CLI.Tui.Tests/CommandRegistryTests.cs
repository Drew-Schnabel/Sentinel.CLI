using FluentAssertions;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class CommandRegistryTests
{
    [Fact]
    public void Dispatch_blank_input_is_a_silent_no_op()
    {
        var registry = new CommandRegistry(new RecordingStoreControl(), _ => { });

        var result = registry.Dispatch("   ");

        result.Success.Should().BeTrue();
        result.Message.Should().BeEmpty();
    }

    [Fact]
    public void Dispatch_unknown_verb_returns_an_error_result()
    {
        var registry = new CommandRegistry(new RecordingStoreControl(), _ => { });

        var result = registry.Dispatch("thmee");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("unknown command 'thmee'");
        result.Output.Should().Be(CommandOutput.Status);
    }

    [Fact]
    public void Dispatch_clear_invokes_the_store_control()
    {
        var store = new RecordingStoreControl();
        var registry = new CommandRegistry(store, _ => { });

        var result = registry.Dispatch("clear");

        store.ClearCount.Should().Be(1);
        result.Success.Should().BeTrue();
        result.Output.Should().Be(CommandOutput.Status);
    }

    [Fact]
    public void Dispatch_help_lists_every_registered_verb_to_the_details_pane()
    {
        var registry = new CommandRegistry(new RecordingStoreControl(), _ => { });

        var result = registry.Dispatch("help");

        result.Output.Should().Be(CommandOutput.Details);
        foreach (var command in registry.Commands)
        {
            result.Message.Should().Contain(command.Verb);
        }
    }

    [Fact]
    public void Default_registry_exposes_the_built_in_verbs()
    {
        var registry = new CommandRegistry(new RecordingStoreControl(), _ => { });

        registry.Commands.Select(c => c.Verb)
            .Should().Contain(["help", "clear", "filter", "search", "reset", "pause", "resume", "export", "theme", "capacity", "errors", "doctor"]);
    }

    [Fact]
    public void Dispatch_capacity_with_a_valid_number_resizes_the_store()
    {
        var store = new RecordingStoreControl();
        var registry = new CommandRegistry(store, _ => { });

        var result = registry.Dispatch("capacity 1000");

        store.TraceCapacity.Should().Be(1000);
        result.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("capacity")]            // no arg
    [InlineData("capacity abc")]        // non-numeric
    [InlineData("capacity 0")]          // below min
    [InlineData("capacity 999999999")]  // above max
    public void Dispatch_capacity_rejects_bad_input_without_resizing(string input)
    {
        var store = new RecordingStoreControl();
        var registry = new CommandRegistry(store, _ => { });

        var result = registry.Dispatch(input);

        result.Success.Should().BeFalse();
        store.TraceCapacity.Should().Be(500); // unchanged
    }

    [Fact]
    public void Dispatch_doctor_delegates_to_the_host_diagnose()
    {
        var called = 0;
        var registry = new CommandRegistry(
            new RecordingStoreControl(), _ => { }, diagnose: () => { called++; return CommandResult.Ok("health", CommandOutput.Details); });

        var result = registry.Dispatch("doctor");

        called.Should().Be(1);
        result.Output.Should().Be(CommandOutput.Details);
    }

    [Fact]
    public void Dispatch_errors_applies_a_status_error_filter()
    {
        TraceFilter? captured = null;
        var registry = new CommandRegistry(new RecordingStoreControl(), f => captured = f);

        var result = registry.Dispatch("errors");

        captured.Should().NotBeNull();
        captured!.Expression.Should().Be("status=error");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Dispatch_theme_with_a_known_name_applies_it()
    {
        Theme? applied = null;
        var registry = new CommandRegistry(
            new RecordingStoreControl(), _ => { }, setTheme: t => applied = t);

        var result = registry.Dispatch("theme light");

        applied.Should().NotBeNull();
        applied!.Name.Should().Be(ThemeName.Light);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Dispatch_theme_with_an_unknown_name_errors_and_applies_nothing()
    {
        var applied = 0;
        var registry = new CommandRegistry(
            new RecordingStoreControl(), _ => { }, setTheme: _ => applied++);

        var result = registry.Dispatch("theme solarized");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("unknown theme 'solarized'");
        applied.Should().Be(0);
    }

    [Fact]
    public void Dispatch_export_without_a_path_errors_and_does_not_invoke_export()
    {
        var calls = 0;
        var registry = new CommandRegistry(
            new RecordingStoreControl(), _ => { }, _ => { calls++; return CommandResult.Ok("x"); });

        var result = registry.Dispatch("export");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("usage");
        calls.Should().Be(0);
    }

    [Fact]
    public void Dispatch_export_with_a_path_delegates_to_the_export_host()
    {
        string? captured = null;
        var registry = new CommandRegistry(
            new RecordingStoreControl(), _ => { }, p => { captured = p; return CommandResult.Ok($"exported {p}"); });

        var result = registry.Dispatch("export ./trace.json");

        captured.Should().Be("./trace.json");
        result.Message.Should().Contain("exported ./trace.json");
    }

    [Fact]
    public void Dispatch_pause_and_resume_toggle_the_store_without_clearing_it()
    {
        var store = new RecordingStoreControl();
        var registry = new CommandRegistry(store, _ => { });

        registry.Dispatch("pause").Success.Should().BeTrue();
        store.IsPaused.Should().BeTrue();

        registry.Dispatch("resume").Success.Should().BeTrue();
        store.IsPaused.Should().BeFalse();

        store.ClearCount.Should().Be(0); // pausing never drops telemetry
    }

    [Fact]
    public void Dispatch_reset_clears_the_filter_without_touching_the_store()
    {
        var store = new RecordingStoreControl();
        var captured = TraceFilter.Create("svc", null, []).Filter; // start non-null
        captured.Should().NotBeNull();
        var called = 0;
        var registry = new CommandRegistry(store, f => { captured = f; called++; });

        var result = registry.Dispatch("reset");

        called.Should().Be(1);
        captured.Should().BeNull();       // filter cleared
        store.ClearCount.Should().Be(0);  // telemetry untouched (unlike :clear)
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Dispatch_filter_with_options_and_text_sets_a_filter()
    {
        TraceFilter? captured = null;
        var called = 0;
        var registry = new CommandRegistry(new RecordingStoreControl(), f => { captured = f; called++; });

        var result = registry.Dispatch("filter service=orders-api status=error checkout");

        called.Should().Be(1);
        captured.Should().NotBeNull();
        captured!.Expression.Should().Be("service=orders-api status=error checkout");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Dispatch_filter_with_no_args_clears_the_filter()
    {
        TraceFilter? captured = null;
        var called = 0;
        var registry = new CommandRegistry(new RecordingStoreControl(), f => { captured = f; called++; });

        var result = registry.Dispatch("filter");

        called.Should().Be(1);     // the setter was invoked…
        captured.Should().BeNull(); // …with null, i.e. cleared
        result.Message.Should().Contain("cleared");
    }

    [Fact]
    public void Dispatch_filter_with_invalid_status_errors_and_does_not_set_a_filter()
    {
        var called = 0;
        var registry = new CommandRegistry(new RecordingStoreControl(), _ => called++);

        var result = registry.Dispatch("filter status=bogus");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("unknown status 'bogus'");
        called.Should().Be(0); // filter left unchanged
    }

    [Fact]
    public void Dispatch_search_sets_a_free_text_filter()
    {
        TraceFilter? captured = null;
        var registry = new CommandRegistry(new RecordingStoreControl(), f => captured = f);

        registry.Dispatch("search timeout");

        captured.Should().NotBeNull();
        captured!.Expression.Should().Be("timeout");
    }

    [Fact]
    public void Dispatch_search_folds_kv_tokens_into_text_rather_than_clearing()
    {
        TraceFilter? captured = null;
        var registry = new CommandRegistry(new RecordingStoreControl(), f => captured = f);

        registry.Dispatch("search service=x"); // parser sees an option, but :search treats it as text

        captured.Should().NotBeNull(); // not cleared
        captured!.Expression.Should().Be("service=x");
    }

    private sealed class RecordingStoreControl : IStoreControl
    {
        public int ClearCount { get; private set; }

        public void Clear() => ClearCount++;
        public void SetPaused(bool paused) => IsPaused = paused;
        public bool IsPaused { get; private set; }
        public int TraceCapacity { get; private set; } = 500;
        public void SetTraceCapacity(int max) => TraceCapacity = max;
    }
}
