using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace EjecutableNFC;

/// <summary>Result returned by any NFC backend API call.</summary>
public record ApiResult(bool Success, string Message, bool IsNetworkError = false);

public class NfcService
{
    // Single shared HttpClient — safe for concurrent calls
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── /api/scan ────────────────────────────────────────────────────────

    public async Task<ApiResult> ScanAsync(string baseUrl, string uid)
    {
        try
        {
            using var content = BuildJson(new { uid });
            var response = await _http.PostAsync($"{Sanitize(baseUrl)}/api/scan", content);
            return (int)response.StatusCode switch
            {
                200 => new ApiResult(true,  $"✅ Acceso PERMITIDO — UID: {uid}"),
                403 => new ApiResult(false, $"❌ Acceso DENEGADO — UID: {uid}"),
                400 => new ApiResult(false, $"⚠ Solicitud incorrecta (400) — UID: {uid}"),
                _   => new ApiResult(false, $"⚠ Respuesta inesperada HTTP {(int)response.StatusCode}")
            };
        }
        catch (TaskCanceledException)
        {
            return new ApiResult(false, "🛑 Timeout: El servidor no respondió.", IsNetworkError: true);
        }
        catch (Exception ex)
        {
            return new ApiResult(false, $"🛑 Error de red: {ex.Message}", IsNetworkError: true);
        }
    }

    // ── /api/register ────────────────────────────────────────────────────

    public async Task<ApiResult> RegisterAsync(string baseUrl, string uid)
    {
        try
        {
            using var content = BuildJson(new { uid });
            var response = await _http.PostAsync($"{Sanitize(baseUrl)}/api/register", content);
            return (int)response.StatusCode switch
            {
                200 or 201 => new ApiResult(true,  $"✅ UID {uid} registrado exitosamente."),
                409        => new ApiResult(false, $"⚠ El UID {uid} ya estaba registrado."),
                _          => new ApiResult(false, $"⚠ Error al registrar (HTTP {(int)response.StatusCode})")
            };
        }
        catch (TaskCanceledException)
        {
            return new ApiResult(false, "🛑 Timeout al registrar. Intenta nuevamente.", IsNetworkError: true);
        }
        catch (Exception ex)
        {
            return new ApiResult(false, $"🛑 Error de red: {ex.Message}", IsNetworkError: true);
        }
    }

    // ── Connectivity test ────────────────────────────────────────────────

    public async Task<ApiResult> TestConnectionAsync(string baseUrl)
    {
        try
        {
            using var content = BuildJson(new { uid = "_TEST_" });
            var response = await _http.PostAsync($"{Sanitize(baseUrl)}/api/scan", content);
            return new ApiResult(true, $"✅ Backend accesible (HTTP {(int)response.StatusCode})");
        }
        catch (Exception ex)
        {
            return new ApiResult(false, $"🛑 No se puede conectar: {ex.Message}", IsNetworkError: true);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static StringContent BuildJson(object payload)
    {
        string json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string Sanitize(string url) => url.TrimEnd('/');
}
