namespace NexusOps.Contracts.Dtos;

public sealed record OrderAnomaly(
    string OrderId,
    string AnomalyType,
    string Severity,
    int? DaysOverdue);
