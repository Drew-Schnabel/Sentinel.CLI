namespace Sentinel.CLI.Domain.Telemetry.Spans;

public enum SpanStatusCode
{
    Unset = 0,
    Ok = 1,
    Error = 2,
}

public sealed record SpanStatus(SpanStatusCode Code, string? Description = null)
{
    public static SpanStatus Unset { get; } = new(SpanStatusCode.Unset);
    public static SpanStatus Ok { get; } = new(SpanStatusCode.Ok);
    public static SpanStatus Error(string? description = null) => new(SpanStatusCode.Error, description);
}
