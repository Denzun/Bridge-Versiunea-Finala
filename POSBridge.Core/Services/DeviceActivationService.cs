using System.Net.Http;
using System.Text.Json;

namespace POSBridge.Core.Services;

/// <summary>
/// Service for checking POS device activation status via Platforma API (v13.0.0).
/// Each device is identified by its unique serial number and must be activated before use.
/// </summary>
public class DeviceActivationService
{
    private readonly HttpClient _httpClient;
    private const string API_BASE_URL = "http://0dmin.app.store.ro";
    private const string API_PATH = "/api/posdevice";

    public string FullCheckUrl(string serialNumber) =>
        $"{API_BASE_URL}{API_PATH}/check?serialNumber={Uri.EscapeDataString(serialNumber)}";

    public string FullRegisterUrl => $"{API_BASE_URL}{API_PATH}/register";

    public string BaseUrl => API_BASE_URL;

    public DeviceActivationService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Checks if the device is activated on the server.
    /// </summary>
    public async Task<ActivationResult> CheckActivationAsync(string serialNumber)
    {
        try
        {
            var url = FullCheckUrl(serialNumber);
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                return new ActivationResult
                {
                    IsActivated = false,
                    Message = $"Server error: {response.StatusCode}",
                    CanRetry = true
                };
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ActivationResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return new ActivationResult
            {
                IsActivated = result?.IsActivated ?? false,
                Status = result?.Status ?? string.Empty,
                Message = result?.Message ?? "Unknown status",
                SerialNumber = result?.SerialNumber ?? string.Empty,
                FiscalPrinterSeries = result?.FiscalPrinterSeries ?? string.Empty,
                Model = result?.Model ?? string.Empty,
                TenantCode = result?.TenantCode ?? string.Empty,
                FirstAuthenticationDate = result?.FirstAuthenticationDate,
                LastCheckIn = result?.LastCheckIn,
                CanRetry = false
            };
        }
        catch (TaskCanceledException)
        {
            return new ActivationResult
            {
                IsActivated = false,
                Message = "Connection timeout. Please check your internet connection.",
                CanRetry = true
            };
        }
        catch (Exception ex)
        {
            return new ActivationResult
            {
                IsActivated = false,
                Message = $"Error: {ex.Message}",
                CanRetry = true
            };
        }
    }

    /// <summary>
    /// Registers a new device with the server (API v13.0.0).
    /// Requires fiscalPrinterSeries and model from the connected fiscal printer.
    /// </summary>
    public async Task<ActivationResult> RegisterDeviceAsync(
        string serialNumber, string tenantCode,
        string fiscalPrinterSeries, string model)
    {
        try
        {
            var request = new
            {
                serialNumber,
                tenantCode,
                fiscalPrinterSeries,
                model
            };
            
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(FullRegisterUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return new ActivationResult
                {
                    IsActivated = false,
                    Message = $"Registration failed: {response.StatusCode} - {errorBody}",
                    CanRetry = true
                };
            }
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<RegistrationResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return new ActivationResult
            {
                IsActivated = result?.IsActivated ?? false,
                Message = result?.Message ?? "Registration completed",
                CanRetry = false
            };
        }
        catch (Exception ex)
        {
            return new ActivationResult
            {
                IsActivated = false,
                Message = $"Registration error: {ex.Message}",
                CanRetry = true
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
    public async Task SendDeviceInfoAsync(string serialNumber, string model, string fiscalPrinterSeries)
    {
        if (string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(fiscalPrinterSeries))
            return;

        try
        {
            var request = new
            {
                serialNumber,
                model = model ?? "N/A",
                fiscalPrinterSeries = fiscalPrinterSeries ?? "N/A"
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{API_BASE_URL}{API_PATH}/checkin", content);

            if (response.IsSuccessStatusCode)
            {
                // Server updated successfully
            }
        }
        catch
        {
            // Silent fail - check-in is not critical for core functionality
        }
    }
}

/// <summary>
/// API response model for activation check endpoint (v13.0.0).
/// </summary>
public class ActivationResponse
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
}

/// <summary>
/// API response model for device registration endpoint.
/// </summary>
public class RegistrationResponse
{
    public string Message { get; set; } = string.Empty;
    public bool IsActivated { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
}

/// <summary>
/// Result model for device activation operations (v13.0.0).
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
}
