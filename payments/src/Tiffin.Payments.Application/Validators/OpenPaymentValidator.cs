using FluentValidation;
using Tiffin.Payments.Application.Commands;

namespace Tiffin.Payments.Application.Validators;

/// <summary>Runs before <c>OpenPaymentHandler</c>.</summary>
public sealed class OpenPaymentValidator : AbstractValidator<OpenPayment>
{
    public OpenPaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.City).NotEmpty().MaximumLength(64);
        RuleFor(x => x.CustomerId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PaymentToken).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
