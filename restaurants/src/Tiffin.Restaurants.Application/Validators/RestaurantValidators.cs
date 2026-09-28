using FluentValidation;
using Tiffin.Restaurants.Application.Commands;
using Tiffin.Restaurants.Application.Queries;

namespace Tiffin.Restaurants.Application.Validators;

/// <summary>Runs before <c>RegisterRestaurantHandler</c>.</summary>
public sealed class RegisterRestaurantValidator : AbstractValidator<RegisterRestaurant>
{
    public RegisterRestaurantValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

/// <summary>Runs before the handler of <c>SetMenuItem</c>. Whether the price is positive is rule R1, the aggregate's.</summary>
public sealed class SetMenuItemValidator : AbstractValidator<SetMenuItem>
{
    public SetMenuItemValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
    }
}

/// <summary>Runs before <c>QuoteOrderHandler</c>.</summary>
public sealed class QuoteOrderValidator : AbstractValidator<QuoteOrder>
{
    public QuoteOrderValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().Must(static lines => lines is null || lines.Count <= 50);
        RuleForEach(x => x.Lines).ChildRules(static line =>
        {
            line.RuleFor(l => l.Code).NotEmpty().MaximumLength(32);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 99);
        });
        RuleFor(x => x.Lines)
            .Must(static lines => lines is null || lines.Select(static l => l.Code).Distinct(StringComparer.Ordinal).Count() == lines.Count)
            .WithMessage("An item is named once.");
    }
}
