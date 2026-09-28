using FluentValidation;
using Tiffin.Kitchen.Application.Commands;

namespace Tiffin.Kitchen.Application.Validators;

/// <summary>Runs before the handler of <c>AcceptTicket</c>. Whether the promise is within reason is rule K2, the aggregate's.</summary>
public sealed class AcceptTicketValidator : AbstractValidator<AcceptTicket>
{
    public AcceptTicketValidator() => RuleFor(x => x.OrderId).NotEmpty();
}

/// <summary>Runs before the handler of <c>RejectTicket</c>.</summary>
public sealed class RejectTicketValidator : AbstractValidator<RejectTicket>
{
    public RejectTicketValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
    }
}
