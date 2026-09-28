using Microsoft.EntityFrameworkCore;
using Tiffin.Payments.Application.Ports;
using Tiffin.Payments.Domain;

namespace Tiffin.Payments.Infrastructure.Persistence;

public sealed class PaymentRepository(AppDbContext database) : IPaymentRepository
{
    public async Task<Payment?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        await database.Set<Payment>().FirstOrDefaultAsync(p => p.Id == id && p.City == city, cancellationToken).ConfigureAwait(false);

    public async Task<Payment?> OfOrderAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        await database.Set<Payment>().FirstOrDefaultAsync(p => p.OrderId == orderId && p.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(Payment payment) => database.Set<Payment>().Add(payment);
}
