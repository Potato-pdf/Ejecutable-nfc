# Documentación de Integración NFC (C#)

Esta documentación describe cómo la aplicación de escritorio en C# debe comunicarse con el Backend NFC para validar lecturas de tarjetas o llaveros NFC.

## Arquitectura

- **C# Application**: Responsable de leer el hardware NFC y enviar el UID capturado al servidor.
- **Backend (Bun + Drizzle + Postgres)**: Valida el UID contra la base de datos de usuarios permitidos y registra todos los intentos (exitosos o denegados) en una tabla de logs.

---

## Endpoint de Validación

### `POST /api/scan`

Evalúa un UID capturado por el lector NFC, registra el intento y devuelve si el acceso está permitido o denegado.

- **URL:** `http://<IP-DEL-SERVIDOR>:3000/api/scan`
- **Method:** `POST`
- **Headers:** 
  - `Content-Type: application/json`

#### Body Request (JSON)

El backend espera recibir únicamente el `uid` (identificador único) capturado del dispositivo NFC.

```json
{
  "uid": "12:34:56:78"
}
```

#### Respuestas Esperadas

**1. Acceso Permitido (HTTP Status `200 OK`)**

Ocurre cuando el `uid` existe en la base de datos de claves permitidas.

```json
{
  "success": true,
  "message": "Acceso permitido para UID: 12:34:56:78",
  "nfcKey": {
    "id": "e8e12345-1234-5678-abcd-e8e123456789",
    "uid": "12:34:56:78",
    "fechaCreacion": "2026-05-27T00:00:00.000Z"
  }
}
```

**2. Acceso Denegado (HTTP Status `403 Forbidden`)**

Ocurre cuando el `uid` se envía correctamente, pero no está registrado en el sistema.

```json
{
  "success": false,
  "message": "Acceso denegado para UID: AA:BB:CC:DD",
  "nfcKey": null
}
```

**3. Solicitud Incorrecta (HTTP Status `400 Bad Request`)**

Ocurre si el payload enviado está malformado o le falta el campo `uid`.

```json
{
  "error": "UID es requerido"
}
```

---

### `POST /api/register`

Registra un nuevo UID (llave, tarjeta o llavero NFC) en la base de datos para que sea considerado como válido en los escaneos futuros.

- **URL:** `http://<IP-DEL-SERVIDOR>:3000/api/register`
- **Method:** `POST`
- **Headers:** 
  - `Content-Type: application/json`

#### Body Request (JSON)

```json
{
  "uid": "NUEVO-UID-1234"
}
```

#### Respuestas Esperadas

**1. Registro Exitoso (HTTP Status `201 Created`)**

```json
{
  "success": true,
  "message": "UID NUEVO-UID-1234 registrado exitosamente."
}
```

**2. Conflicto - Ya Registrado (HTTP Status `409 Conflict`)**

```json
{
  "success": false,
  "message": "El UID NUEVO-UID-1234 ya está registrado."
}
```

---

## Ejemplos de Prueba (cURL)

Puedes probar que el servidor está arriba y respondiendo correctamente ejecutando estos comandos desde cualquier terminal:

**Probar un acceso denegado (UID no registrado):**
```bash
curl -X POST https://backend-nfc-lo1t.onrender.com/api/scan \
  -H "Content-Type: application/json" \
  -d '{"uid": "test-uid-falso"}'
```
*Respuesta esperada:* `{"success":false,"message":"Acceso denegado para UID: test-uid-falso","user":null}`

**Probar un acceso válido:**
*(Asegúrate de cambiar "CLAVE-REAL" por un UID que hayas insertado en la base de datos)*
```bash
curl -X POST https://backend-nfc-lo1t.onrender.com/api/scan \
  -H "Content-Type: application/json" \
  -d '{"uid": "CLAVE-REAL"}'
```

**Registrar una nueva tarjeta:**
```bash
curl -X POST https://backend-nfc-lo1t.onrender.com/api/register \
  -H "Content-Type: application/json" \
  -d '{"uid": "NUEVO-LLAVERO-XYZ"}'
```

---

## Ejemplo de Consumo en C# (HttpClient)

Aquí tienes un ejemplo de cómo implementar la llamada en tu aplicación C# utilizando `HttpClient`:

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class NfcService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl = "https://backend-nfc-lo1t.onrender.com/api/scan";

    public NfcService()
    {
        _httpClient = new HttpClient();
    }

    public async Task<bool> ValidateNfcUidAsync(string uid)
    {
        var payload = new { uid = uid };
        string json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_apiUrl, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✅ ACCESO PERMITIDO: " + responseBody);
                return true; // Puerta/Torniquete puede abrirse
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                Console.WriteLine("❌ ACCESO DENEGADO: " + responseBody);
                return false;
            }
            else
            {
                Console.WriteLine("⚠️ ERROR DE SOLICITUD: " + responseBody);
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("🛑 Error de conexión con el backend: " + ex.Message);
            return false;
        }
    }
}
```

## Estructura de Base de Datos

El backend guarda y verifica automáticamente la información en PostgreSQL utilizando las siguientes estructuras (gestionadas por el Backend):

- **Tabla `nfc_keys`**: Almacena `id` (UUID), `uid` (string único del NFC) y `fecha_creacion`.
- **Tabla `logs`**: Almacena `id` (UUID), `mensaje` (string describiendo el evento de escaneo) y `fecha_creacion`. Todo escaneo a `/api/scan` genera un registro aquí, sea válido o no.
