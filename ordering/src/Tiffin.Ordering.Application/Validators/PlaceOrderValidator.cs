using FluentValidation;
using Tiffin.Ordering.Application.Commands;

namespace Tiffin.Ordering.Application.Validators;

/// <summary>Runs before <c>PlaceOrderHandler</c>: what can be refused without asking anybody.</summary>
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.PaymentToken).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExpectedTotal).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().Must(static lines => lines is null || lines.Count <= 50);
        RuleForEach(x => x.Lines).ChildRules(static line =>
        {
            line.RuleFor(l => l.Code).NotEmpty().MaximumLength(32);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 99);
        });
        RuleFor(x => x.DeliverTo).NotNull();
        When(static x => x.DeliverTo is not null, () =>
        {
            RuleFor(x => x.DeliverTo.Recipient).NotEmpty().MaximumLength(100);
            // The sample accepts country-neutral test values; real products can add their own
            // normalization and regional phone policy at the product boundary.
            RuleFor(x => x.DeliverTo.Phone).NotEmpty().MaximumLength(30);
            RuleFor(x => x.DeliverTo.District).NotEmpty().MaximumLength(60);
            RuleFor(x => x.DeliverTo.Line).NotEmpty().MaximumLength(300);
        });
    }
}
