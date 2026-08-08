using Microsoft.AspNetCore.Mvc;
using Workslip.Domain;

namespace Workslip.Api.Endpoints;

public sealed class JobListQueryOptions
{
    [FromQuery(Name = "status")]
    public JobStatus[]? Statuses { get; init; }

    [FromQuery(Name = "reportNumber")]
    public string? ReportNumber { get; init; }

    [FromQuery(Name = "customerName")]
    public string? CustomerName { get; init; }

    [FromQuery(Name = "customerEmail")]
    public string? CustomerEmail { get; init; }

    [FromQuery(Name = "customerAddress")]
    public string? CustomerAddress { get; init; }

    [FromQuery(Name = "search")]
    public string? Search { get; init; }

    [FromQuery(Name = "sortBy")]
    public string? SortBy { get; init; }

    [FromQuery(Name = "sortDirection")]
    public string? SortDirection { get; init; }

    [FromQuery(Name = "limit")]
    public int? Limit { get; init; }

    [FromQuery(Name = "offset")]
    public int? Offset { get; init; }
}

public sealed class ListQueryOptions
{
    [FromQuery(Name = "limit")]
    public int? Limit { get; init; }

    [FromQuery(Name = "offset")]
    public int? Offset { get; init; }

    [FromQuery(Name = "search")]
    public string? Search { get; init; }

    [FromQuery(Name = "sortBy")]
    public string? SortBy { get; init; }

    [FromQuery(Name = "sortDirection")]
    public string? SortDirection { get; init; }
}
