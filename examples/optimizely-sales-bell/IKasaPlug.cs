namespace SalesBellSample;

/// <summary>Toggles a single TP-Link Kasa smart plug on or off.</summary>
public interface IKasaPlug
{
    Task SetAsync(bool on, CancellationToken ct = default);
}
