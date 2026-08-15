using FluentValidation;

namespace Workslip.Application.Documents.Validators;

public sealed class CreateDocumentRequestValidator : AbstractValidator<CreateDocumentRequest>
{
    public CreateDocumentRequestValidator()
    {
        Include(new DocumentContentValidator());
    }
}

public sealed class UpdateDocumentRequestValidator : AbstractValidator<UpdateDocumentRequest>
{
    public UpdateDocumentRequestValidator()
    {
        RuleFor(request => request.Title)
            .NotEmpty().WithMessage("Titel er påkrævet.")
            .MaximumLength(200).WithMessage("Titel må højst være 200 tegn.");

        RuleFor(request => request.Content)
            .NotNull().WithMessage("Indhold er påkrævet.")
            .MaximumLength(200_000).WithMessage("Indhold må højst være 200.000 tegn.");

        RuleFor(request => request.Tags)
            .Must(tags => tags is null || tags.Count <= 10)
            .WithMessage("Et dokument kan højst have 10 tags.");

        RuleForEach(request => request.Tags!)
            .NotEmpty().WithMessage("Tags må ikke være tomme.")
            .MaximumLength(40).WithMessage("Et tag må højst være 40 tegn.");

        RuleFor(request => request.Revision)
            .GreaterThan(0).WithMessage("Dokumentets revision er ugyldig.");
    }
}

internal sealed class DocumentContentValidator : AbstractValidator<CreateDocumentRequest>
{
    public DocumentContentValidator()
    {
        RuleFor(request => request.Title)
            .NotEmpty().WithMessage("Titel er påkrævet.")
            .MaximumLength(200).WithMessage("Titel må højst være 200 tegn.");

        RuleFor(request => request.Content)
            .NotNull().WithMessage("Indhold er påkrævet.")
            .MaximumLength(200_000).WithMessage("Indhold må højst være 200.000 tegn.");

        RuleFor(request => request.Tags)
            .Must(tags => tags is null || tags.Count <= 10)
            .WithMessage("Et dokument kan højst have 10 tags.");

        RuleForEach(request => request.Tags!)
            .NotEmpty().WithMessage("Tags må ikke være tomme.")
            .MaximumLength(40).WithMessage("Et tag må højst være 40 tegn.");
    }
}
