namespace SalesBellSample;

/// <summary>
/// Configuration for the TP-Link Kasa cloud connection. Bind from the "SalesBell"
/// configuration section. Keep the password out of source control (Key Vault / DXP config).
/// </summary>
public sealed class KasaOptions
{
    /// <summary>TP-Link account email.</summary>
    public string Username { get; set; } = "";

    /// <summary>TP-Link account password.</summary>
    public string Password { get; set; } = "";

    /// <summary>The plug's exact alias as shown in the Kasa app.</summary>
    public string DeviceAlias { get; set; } = "On Air Lamp";

    /// <summary>Kill switch — set false in QA / load-test environments so orders don't ring the light.</summary>
    public bool Enabled { get; set; } = true;
}
