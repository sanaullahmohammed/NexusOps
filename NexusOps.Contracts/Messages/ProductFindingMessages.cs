using NexusOps.Contracts.Dtos;

namespace NexusOps.Contracts.Messages;

/// <summary>Request/response pair between the fan-out coordinator and <c>NexusOps.ProductService</c>.</summary>
public sealed record RequestProductFinding(Guid CorrelationId, string[] Skus);

public sealed record ProductFindingReported(
    Guid CorrelationId,
    SourceFindingStatus Status,
    ProductDetail[] Products,
    string[] SkusNotFound);
