namespace Workslip.Application.Images;

public sealed record ImageUpload(
    Stream Content,
    long Length,
    string? ContentType);

public sealed record ImageInfoResponse(
    Guid Id,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);

public sealed record ImageFileResponse(
    Stream Content,
    string ContentType,
    long SizeBytes);
