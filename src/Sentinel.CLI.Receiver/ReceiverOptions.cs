using System.ComponentModel.DataAnnotations;

namespace Sentinel.CLI.Receiver;

public sealed class ReceiverOptions
{
    public const string SectionName = "Receiver";

    // OTLP/gRPC (HTTP/2) listen port.
    [Range(1, 65535)]
    public int GrpcPort { get; set; } = 4317;

    // OTLP/HTTP (HTTP/1.1, protobuf body) listen port.
    [Range(1, 65535)]
    public int HttpPort { get; set; } = 4318;
}
