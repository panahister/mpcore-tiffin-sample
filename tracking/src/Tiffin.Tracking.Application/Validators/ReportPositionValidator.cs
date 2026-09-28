using FluentValidation;
using Tiffin.Tracking.Application.Commands;

namespace Tiffin.Tracking.Application.Validators;

/// <summary>Runs before <c>ReportPositionHandler</c>. Whether the place is on Earth is rule T3, the aggregate's.</summary>
public sealed class ReportPositionValidator : AbstractValidator<ReportPosition>
{
    public ReportPositionValidator() => RuleFor(x => x.OrderId).NotEmpty();
}
