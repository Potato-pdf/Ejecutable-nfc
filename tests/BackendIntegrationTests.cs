using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace NfcBackendTests;

/// <summary>
/// Integration tests against the PRODUCTION backend.
/// Verifies that the backend is alive and responds correctly
/// before blaming the Arduino or the Windows app.
///
/// Run with:
///   dotnet test tests\NfcBackendTests.csproj -v normal
/// </summary>
public class BackendIntegrationTests : IDisposable
{
    private const string BASE_URL   = "https://backend-nfc-lo1t.onrender.com";
    private const string SCAN_URL   = BASE_URL + "/api/scan";
    private const string REGISTER_URL = BASE_URL + "/api/register";

    // A UID that should never exist → will always get 403
    private const string FAKE_UID   = "TEST-UID-FALSO-99";

    // A UID used only for the register conflict test (already registered in a previous run)
    private const string REGISTER_TEST_UID = "TEST-REGISTER-NFC-MANAGER-1";

    private readonly HttpClient         _http;
    private readonly ITestOutputHelper  _out;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public BackendIntegrationTests(ITestOutputHelper output)
    {
        _out  = output;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public void Dispose() => _http.Dispose();

    // ─────────────────────────────────────────────────────────────────────
    // HELPER
    // ─────────────────────────────────────────────────────────────────────

    private StringContent Json(object payload) =>
        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private async Task<(HttpStatusCode status, string body, JsonDocument? doc)>
        PostAsync(string url, object? payload)
    {
        var content  = payload != null ? Json(payload) : null;
        var response = await _http.PostAsync(url, content);
        var body     = await response.Content.ReadAsStringAsync();
        _out.WriteLine($"[{(int)response.StatusCode}] {url}");
        _out.WriteLine($"  body: {body}");
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(body); } catch { /* not JSON — test will catch it */ }
        return (response.StatusCode, body, doc);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. BACKEND REACHABILITY
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Backend debe responder a cualquier peticion valida (no 5xx, no timeout).</summary>
    [Fact(DisplayName = "1. Backend esta en linea y responde")]
    public async Task Backend_IsOnlineAndResponds()
    {
        var (status, body, _) = await PostAsync(SCAN_URL, new { uid = FAKE_UID });

        Assert.True(
            (int)status < 500,
            $"El backend devolvio un error del servidor: HTTP {(int)status}\nBody: {body}"
        );
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. /api/scan — UID desconocido -> 403
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>UID no registrado debe devolver 403 con success:false.</summary>
    [Fact(DisplayName = "2. /api/scan con UID desconocido devuelve 403")]
    public async Task Scan_UnknownUid_Returns403()
    {
        var (status, body, doc) = await PostAsync(SCAN_URL, new { uid = FAKE_UID });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.NotNull(doc);

        bool success = doc!.RootElement.GetProperty("success").GetBoolean();
        Assert.False(success, $"'success' deberia ser false para UID desconocido. Body: {body}");
    }

    /// <summary>La respuesta 403 debe incluir el campo 'message'.</summary>
    [Fact(DisplayName = "3. /api/scan 403 incluye campo 'message'")]
    public async Task Scan_UnknownUid_ResponseHasMessageField()
    {
        var (_, body, doc) = await PostAsync(SCAN_URL, new { uid = FAKE_UID });

        Assert.NotNull(doc);
        Assert.True(
            doc!.RootElement.TryGetProperty("message", out _),
            $"La respuesta no tiene el campo 'message'. Body: {body}"
        );
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. /api/scan — Body malformado -> 400
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Cuerpo sin el campo 'uid' debe devolver 400.</summary>
    [Fact(DisplayName = "4. /api/scan sin campo 'uid' devuelve 400")]
    public async Task Scan_MissingUid_Returns400()
    {
        var (status, body, _) = await PostAsync(SCAN_URL, new { otrocampo = "valor" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    /// <summary>Cuerpo completamente vacio debe devolver 400.</summary>
    [Fact(DisplayName = "5. /api/scan con body vacio devuelve 400")]
    public async Task Scan_EmptyBody_Returns400()
    {
        var emptyContent = new StringContent("{}", Encoding.UTF8, "application/json");
        var response     = await _http.PostAsync(SCAN_URL, emptyContent);
        var body         = await response.Content.ReadAsStringAsync();
        _out.WriteLine($"[{(int)response.StatusCode}] body: {body}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. /api/scan — Estructura JSON de respuesta valida
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>El JSON de respuesta debe ser parseable y tener la forma documentada.</summary>
    [Fact(DisplayName = "6. /api/scan respuesta es JSON valido con campos esperados")]
    public async Task Scan_Response_IsValidJsonWithExpectedShape()
    {
        var (_, body, doc) = await PostAsync(SCAN_URL, new { uid = FAKE_UID });

        Assert.NotNull(doc);
        var root = doc!.RootElement;

        Assert.True(root.TryGetProperty("success",  out _), $"Falta 'success'. Body: {body}");
        Assert.True(root.TryGetProperty("message",  out _), $"Falta 'message'. Body: {body}");
        Assert.True(root.TryGetProperty("nfcKey",   out _), $"Falta 'nfcKey'. Body: {body}");
    }

    /// <summary>Para UID no registrado, nfcKey debe ser null.</summary>
    [Fact(DisplayName = "7. /api/scan con UID desconocido tiene nfcKey null")]
    public async Task Scan_UnknownUid_NfcKeyIsNull()
    {
        var (_, body, doc) = await PostAsync(SCAN_URL, new { uid = FAKE_UID });

        Assert.NotNull(doc);
        var nfcKey = doc!.RootElement.GetProperty("nfcKey");
        Assert.Equal(JsonValueKind.Null, nfcKey.ValueKind);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 5. /api/register — Registro de nuevo UID
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registrar un UID debe devolver 201 (nuevo) o 409 (ya existia).
    /// Ambos son respuestas validas segun la documentacion.
    /// </summary>
    [Fact(DisplayName = "8. /api/register devuelve 201 o 409 (nunca 5xx)")]
    public async Task Register_TestUid_Returns201Or409()
    {
        var (status, body, _) = await PostAsync(REGISTER_URL, new { uid = REGISTER_TEST_UID });

        bool validStatus = status == HttpStatusCode.Created ||
                           status == HttpStatusCode.OK      ||   // algunos backends usan 200
                           status == HttpStatusCode.Conflict;

        Assert.True(validStatus, $"Estado inesperado: HTTP {(int)status}. Body: {body}");
    }

    /// <summary>La respuesta de /api/register debe tener los campos 'success' y 'message'.</summary>
    [Fact(DisplayName = "9. /api/register respuesta tiene campos 'success' y 'message'")]
    public async Task Register_Response_HasSuccessAndMessage()
    {
        var (_, body, doc) = await PostAsync(REGISTER_URL, new { uid = REGISTER_TEST_UID });

        Assert.NotNull(doc);
        var root = doc!.RootElement;
        Assert.True(root.TryGetProperty("success", out _), $"Falta 'success'. Body: {body}");
        Assert.True(root.TryGetProperty("message", out _), $"Falta 'message'. Body: {body}");
    }

    /// <summary>
    /// Un segundo registro del mismo UID debe devolver 409 Conflict con success:false.
    /// Se ejecuta despues del test anterior gracias al mismo UID fijo.
    /// </summary>
    [Fact(DisplayName = "10. Registrar el mismo UID dos veces devuelve 409")]
    public async Task Register_DuplicateUid_Returns409()
    {
        // Primera llamada — puede ser 201 o 409 dependiendo si ya existia
        await PostAsync(REGISTER_URL, new { uid = REGISTER_TEST_UID });

        // Segunda llamada — siempre debe ser 409
        var (status, body, doc) = await PostAsync(REGISTER_URL, new { uid = REGISTER_TEST_UID });

        Assert.Equal(HttpStatusCode.Conflict, status);

        if (doc != null && doc.RootElement.TryGetProperty("success", out var s))
            Assert.False(s.GetBoolean(), $"'success' deberia ser false en 409. Body: {body}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 6. ROUNDTRIP — Registrar y luego escanear
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registrar un UID y luego escanearlo debe devolver success:true (200 OK).
    /// Usa un UID unico basado en timestamp para no contaminar la base de datos.
    /// </summary>
    [Fact(DisplayName = "11. Roundtrip: registrar y despues escanear devuelve 200")]
    public async Task Roundtrip_RegisterThenScan_Returns200()
    {
        string uid = $"ROUNDTRIP-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        _out.WriteLine($"UID de prueba: {uid}");

        // Registrar
        var (regStatus, regBody, _) = await PostAsync(REGISTER_URL, new { uid });
        Assert.True(
            regStatus == HttpStatusCode.Created || regStatus == HttpStatusCode.OK,
            $"El registro fallo: HTTP {(int)regStatus}. Body: {regBody}"
        );

        // Escanear
        var (scanStatus, scanBody, scanDoc) = await PostAsync(SCAN_URL, new { uid });
        Assert.Equal(HttpStatusCode.OK, scanStatus);
        Assert.NotNull(scanDoc);

        bool success = scanDoc!.RootElement.GetProperty("success").GetBoolean();
        Assert.True(success, $"Escaneo deberia ser exitoso tras registrar. Body: {scanBody}");
    }
}
