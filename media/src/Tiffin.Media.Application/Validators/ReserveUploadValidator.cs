using FluentValidation;
using Tiffin.Media.Application.Commands;

namespace Tiffin.Media.Application.Validators;

/// <summary>Runs before the handler of <c>ReserveUpload</c>. Which purposes, types and sizes are allowed are rules M1 to M3, the aggregate's.</summary>
public sealed class ReserveUploadValidator : AbstractValidator<ReserveUpload>
{
    public ReserveUploadValidator()
    {
        RuleFor(x => x.Purpose).NotEmpty().MaximumLength(40);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        // A name is shown and never used as a path; it still holds no separator and no control character.
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(200)
            .Must(static name => name is null || (!name.Contains('/', StringComparison.Ordinal) && !name.Contains('\\', StringComparison.Ordinal) && !name.Any(char.IsControl)))
            .WithMessage("A file name holds no path.");
    }
}
