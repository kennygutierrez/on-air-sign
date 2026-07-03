using EPiServer.Commerce.Order;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace SalesBellSample;

/// <summary>
/// Listens for order placement via <see cref="IOrderEvents"/> and rings the sales bell.
///
/// Gotcha: SavedOrder fires on EVERY save of ANY order group — carts included, and again
/// on every later status change of an existing order. So we guard twice: only purchase
/// orders, and only the first time we see a given order.
/// </summary>
public sealed class SalesBellOrderListener
{
    private readonly IOrderEvents _orderEvents;
    private readonly ISalesBell _bell;
    private readonly ILogger<SalesBellOrderListener> _log;

    // De-dupe repeated saves of the same order (per instance).
    private static readonly MemoryCache Seen = new(new MemoryCacheOptions());

    public SalesBellOrderListener(IOrderEvents orderEvents, ISalesBell bell, ILogger<SalesBellOrderListener> log)
    {
        _orderEvents = orderEvents;
        _bell = bell;
        _log = log;
    }

    public void Subscribe() => _orderEvents.SavedOrder += OnSavedOrder;

    public void Unsubscribe() => _orderEvents.SavedOrder -= OnSavedOrder;

    private void OnSavedOrder(object? sender, OrderEventArgs e)
    {
        // Carts are order groups too — only ring for a completed purchase order.
        if (e.OrderGroup is not IPurchaseOrder po)
            return;

        // Only ring the first time we see this order.
        var id = po.OrderLink.OrderGroupId;
        if (Seen.TryGetValue(id, out _))
            return;
        Seen.Set(id, true, TimeSpan.FromHours(1));

        _log.LogInformation("Order {OrderNumber} placed — ringing the sales bell", po.OrderNumber);
        _bell.Ring();
    }
}
