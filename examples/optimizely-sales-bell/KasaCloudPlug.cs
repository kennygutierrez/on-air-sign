using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SalesBellSample;

/// <summary>
/// Controls a Kasa plug through TP-Link's cloud API — the same path the Kasa phone app uses.
/// Logs in once, finds the device by alias, then relays the legacy set_relay_state command
/// via the cloud "passthrough" method. Caches the session and re-logs in on failure.
/// </summary>
public sealed class KasaCloudPlug : IKasaPlug
{
    private const string LoginUrl = "https://wap.tplinkcloud.com";

    private readonly HttpClient _http;
    private readonly KasaOptions _opts;
    private readonly ILogger<KasaCloudPlug> _log;

    // cached session
    private string? _token;
    private string? _deviceId;
    private string? _appServerUrl;

    public KasaCloudPlug(HttpClient http, IOptions<KasaOptions> opts, ILogger<KasaCloudPlug> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    public async Task SetAsync(bool on, CancellationToken ct = default)
    {
        if (!_opts.Enabled)
            return;

        // TP-Link expects the legacy device command as a JSON *string* inside requestData.
        var command = $"{{\"system\":{{\"set_relay_state\":{{\"state\":{(on ? 1 : 0)}}}}}}}";

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                if (_token is null)
                    await ConnectAsync(ct);

                var body = new
                {
                    method = "passthrough",
                    @params = new { deviceId = _deviceId, requestData = command }
                };

                var result = await PostAsync($"{_appServerUrl}?token={_token}", body, ct);
                if (result.GetProperty("error_code").GetInt32() != 0)
                    throw new InvalidOperationException($"cloud error: {result.GetRawText()}");

                return; // success
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Kasa cloud attempt {Attempt} failed", attempt);
                _token = null; // force a fresh login + device lookup next time
            }
        }

        _log.LogError("Kasa plug state not updated after retry");
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var login = await PostAsync(LoginUrl, new
        {
            method = "login",
            @params = new
            {
                appType = "Kasa_Android",
                cloudUserName = _opts.Username,
                cloudPassword = _opts.Password,
                terminalUUID = Guid.NewGuid().ToString()
            }
        }, ct);

        if (login.GetProperty("error_code").GetInt32() != 0)
            throw new InvalidOperationException($"TP-Link login failed: {login.GetRawText()}");

        _token = login.GetProperty("result").GetProperty("token").GetString();

        var list = await PostAsync($"{LoginUrl}?token={_token}",
            new { method = "getDeviceList", @params = new { } }, ct);

        var device = list.GetProperty("result").GetProperty("deviceList").EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("alias").GetString() == _opts.DeviceAlias);

        if (device.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"No Kasa device with alias '{_opts.DeviceAlias}'");

        _deviceId = device.GetProperty("deviceId").GetString();
        _appServerUrl = device.GetProperty("appServerUrl").GetString();
    }

    private async Task<JsonElement> PostAsync(string url, object body, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync(url, content, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }
}
