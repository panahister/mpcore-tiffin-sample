using FluentValidation;
using Tiffin.Access.Application.Commands;

namespace Tiffin.Access.Application.Validators;

/// <summary>Runs before <c>ChangeRoleHandler</c>. Whether the role is known, and the admin's to give, are rules A1 and A2, the aggregate's.</summary>
public sealed class ChangeRoleValidator : AbstractValidator<ChangeRole>
{
    public ChangeRoleValidator()
    {
        RuleFor(x => x.PersonId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(64);
        RuleFor(x => x.City).MaximumLength(64);
    }
}
