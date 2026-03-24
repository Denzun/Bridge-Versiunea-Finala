using System.Net.Http;
using System.Text.Json;

namespace POSBridge.Core.Services;

/// <summary>
/// Service for POS device licensing and registration via Conexa API.
/// Devices register automatically on first run. New devices are disabled by default.
/// </summary>
public class DeviceActivationService
{
    private readonly HttpClient _httpClient;
    private const string API_BASE_URL = "https://0dmin.app.store.ro/conexa/api/licentiere/posdevice";

    public string FullCheckUrl(string serialNumber) =>
        $"{API_BASE_URL}/check?serialNumber={Uri.EscapeDataString(serialNumber)}";

    public string FullRegisterUrl => $"{API_BASE_URL}/register";

    public string BaseUrl => API_BASE_URL;

    public DeviceActivationService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Checks device activation status on the server.
    /// Called on startup and periodically (heartbeat).
    /// </summary>
    public async Task<ActivationResult> CheckActivationAsync(string serialNumber)
    {
        try
        {
            var url = FullCheckUrl(serialNumber);
            var response = await _httpClient.GetAsync(url);
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Device not registered - will auto-register on first run
                return new ActivationResult
                {
                    IsActivated = false,
                    Status = "NOT_REGISTERED",
                    Message = "Dispozitivul nu este inregistrat. Se va inregistra automat.",
                    CanRetry = true,
                    NeedsRegistration = true
                };
            }
            
            if (!response.IsSuccessStatusCode)
            {
                return new ActivationResult
                {
                    IsActivated = false,
                    Status = "ERROR",
                    Message = $"Eroare server: {response.StatusCode}",
                    CanRetry = true
                };
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ActivationResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            // Workaround: Backend sometimes returns Enabled=false but Status="Activated"
            // Check both the boolean AND the status string
            var isActivated = result?.Enabled ?? false;
            if (!isActivated && !string.IsNullOrEmpty(result?.Status))
            {
                if (result.Status.Equals("Activated", StringComparison.OrdinalIgnoreCase) ||
                    result.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                    result.Status.Equals("ENABLED", StringComparison.OrdinalIgnoreCase))
                {
                    isActivated = true;
                }
            }
            
            return new ActivationResult
            {
                IsActivated = isActivated,
                Status = result?.Status ?? "UNKNOWN",
                Message = result?.Message ?? "Status necunoscut",
                SerialNumber = result?.SerialNumber ?? serialNumber,
                FiscalPrinterSeries = result?.FiscalPrinterSeries ?? string.Empty,
                Model = result?.Model ?? string.Empty,
                TenantCode = result?.TenantCode ?? string.Empty,
                FirstAuthenticationDate = result?.FirstAuthenticationDate,
                LastCheckIn = result?.LastCheckIn,
                CanRetry = false,
                NeedsRegistration = false
            };
        }
        catch (TaskCanceledException)
        {
            return new ActivationResult
            {
                IsActivated = false,
                Status = "TIMEOUT",
                Message = "Timeout la conexiune. Verificati conexiunea la internet.",
                CanRetry = true
            };
        }
        catch (Exception ex)
        {
            return new ActivationResult
            {
                IsActivated = false,
                Status = "ERROR",
                Message = $"Eroare: {ex.Message}",
                CanRetry = true
            };
        }
    }

    /// <summary>
    /// Registers a new device with the server (auto-registration on first run).
    /// Devices are disabled by default and need manual enabling after verification.
    /// </summary>
    public async Task<ActivationResult> RegisterDeviceAsync(
        string serialNumber, 
        string tenantCode,
        string? fiscalPrinterSeries = null, 
        string? model = null)
    {
        try
        {
            var request = new
            {
                serialNumber,
                tenantCode,
                fiscalPrinterSeries = fiscalPrinterSeries ?? string.Empty,
                model = model ?? string.Empty
            };
            
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(FullRegisterUrl, content);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return new ActivationResult
                {
                    IsActivated = false,
                    Status = "ALREADY_REGISTERED",
                    Message = "Dispozitivul este deja inregistrat.",
                    CanRetry = false,
                    NeedsRegistration = false
                };
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return new ActivationResult
                {
                    IsActivated = false,
                    Status = "ERROR",
                    Message = $"Inregistrare esuata: {response.StatusCode} - {errorBody}",
                    CanRetry = true,
                    NeedsRegistration = true
                };
            }
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<RegistrationResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return new ActivationResult
            {
                IsActivated = result?.Enabled ?? false,
                Status = result?.Status ?? "REGISTERED",
                Message = result?.Message ?? "Dispozitiv inregistrat cu succes. Asteapta activarea manuala.",
                SerialNumber = result?.SerialNumber ?? serialNumber,
                CanRetry = false,
                NeedsRegistration = false
            };
        }
        catch (Exception ex)
        {
            return new ActivationResult
            {
                IsActivated = false,
                Status = "ERROR",
                Message = $"Eroare inregistrare: {ex.Message}",
                CanRetry = true,
                NeedsRegistration = true
            };
        }
    }

    /// <summary>
    /// Performs periodic heartbeat check-in while application is running.
    /// </summary>
    public async Task SendHeartbeatAsync(string serialNumber)
    {
        try
        {
            await CheckActivationAsync(serialNumber);
        }
        catch
        {
            // Silent fail - heartbeat is not critical
        }
    }

    /// <summary>
    /// Sends device model and fiscal printer series to the server (check-in).
    /// Used when device connects to update server records.
    /// </summary>
    public async Task<(bool Success, string Message)> SendDeviceInfoAsync(string serialNumber, string model, string fiscalPrinterSeries, string tenantCode)
    {
        if (string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(fiscalPrinterSeries))
            return (false, "No data to send");

        try
        {
            var request = new
            {
                serialNumber,
                tenantCode = tenantCode ?? string.Empty,
                fiscalPrinterSeries = fiscalPrinterSeries ?? "N/A",
                model = model ?? "N/A"
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var url = $"{API_BASE_URL}/register";
            System.Diagnostics.Debug.WriteLine($"[BACKEND] Sending to {url}: {json}");

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            
            System.Diagnostics.Debug.WriteLine($"[BACKEND] Response: {(int)response.StatusCode} - {responseBody}");

            if (response.IsSuccessStatusCode)
            {
                return (true, $"Server updated: {responseBody}");
            }
            else
            {
                return (false, $"Server error {(int)response.StatusCode}: {responseBody}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BACKEND] Error: {ex.Message}");
            return (false, $"Error: {ex.Message}");
        }
    }
}

/// <summary>
/// API response model for activation check endpoint.
/// </summary>
public class ActivationResponse
{
    public bool Enabled { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string FiscalPrinterSeries { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public DateTime? FirstAuthenticationDate { get; set; }
    public DateTime? LastCheckIn { get; set; }
}

/// <summary>
/// API response model for device registration endpoint.
/// </summary>
public class RegistrationResponse
{
    public string Message { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
}

/// <summary>
/// Result model for device activation operations.
/// </summary>
public class ActivationResult
{
    public bool IsActivated { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string FiscalPrinterSeries { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public DateTime? FirstAuthenticationDate { get; set; }
    public DateTime? LastCheckIn { get; set; }
    public bool CanRetry { get; set; }
    public bool NeedsRegistration { get; set; }
}
