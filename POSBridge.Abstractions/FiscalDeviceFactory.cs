using POSBridge.Abstractions.Enums;

namespace POSBridge.Abstractions;

/// <summary>
/// Factory for creating fiscal device instances based on device type.
/// Enables Multi-Vendor Architecture - supports Datecs, Tremol, Elcom, etc.
/// </summary>
public static class FiscalDeviceFactory
{
    /// <summary>
    /// Creates a fiscal device instance based on the specified device type.
    /// </summary>
    /// <param name="deviceType">Type of device (Datecs, Tremol, Elcom)</param>
    /// <returns>Instance of IFiscalDevice</returns>
    /// <exception cref="NotSupportedException">Thrown when device type is not supported</exception>
    public static IFiscalDevice CreateDevice(DeviceType deviceType)
    {
        return deviceType switch
        {
            DeviceType.Datecs => CreateDatecsDevice(),
            DeviceType.Incotex => CreateIncotexDevice(),
            DeviceType.Tremol => throw new NotSupportedException(
                "Tremol support coming soon! Implementation planned for Phase 2.\n" +
                "Will support WiFi/GPRS connectivity and advanced features."),
            DeviceType.Elcom => throw new NotSupportedException(
                "Elcom support coming soon! Implementation planned for Phase 3."),
            _ => throw new ArgumentException($"Unknown device type: {deviceType}", nameof(deviceType))
        };
    }

    /// <summary>
    /// Creates a Datecs fiscal device instance.
    /// Uses reflection to avoid direct dependency on POSBridge.Devices.Datecs.
    /// </summary>
    private static IFiscalDevice CreateDatecsDevice()
    {
        try
        {
            // Use reflection to create DatecsDevice without direct assembly reference
            var datecsAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "POSBridge.Devices.Datecs");

            if (datecsAssembly == null)
                throw new InvalidOperationException(
                    "POSBridge.Devices.Datecs assembly not found. " +
                    "Ensure the assembly is loaded and referenced.");

            var datecsDeviceType = datecsAssembly.GetType("POSBridge.Devices.Datecs.DatecsDevice");
            if (datecsDeviceType == null)
                throw new InvalidOperationException("DatecsDevice class not found in assembly.");

            var instance = Activator.CreateInstance(datecsDeviceType) as IFiscalDevice;
            if (instance == null)
                throw new InvalidOperationException("Failed to create DatecsDevice instance.");

            return instance;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create Datecs device: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates an Incotex fiscal device instance.
    /// Uses reflection to avoid direct dependency on POSBridge.Devices.Incotex.
    /// </summary>
    private static IFiscalDevice CreateIncotexDevice()
    {
        try
        {
            // Use reflection to create IncotexDevice without direct assembly reference
            var incotexAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "POSBridge.Devices.Incotex");

            if (incotexAssembly == null)
                throw new InvalidOperationException(
                    "POSBridge.Devices.Incotex assembly not found. " +
                    "Ensure the assembly is loaded and referenced.");

            var incotexDeviceType = incotexAssembly.GetType("POSBridge.Devices.Incotex.IncotexDevice");
            if (incotexDeviceType == null)
                throw new InvalidOperationException("IncotexDevice class not found in assembly.");

            var instance = Activator.CreateInstance(incotexDeviceType) as IFiscalDevice;
            if (instance == null)
                throw new InvalidOperationException("Failed to create IncotexDevice instance.");

            return instance;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create Incotex device: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets all supported device types.
    /// </summary>
    public static DeviceType[] GetSupportedDeviceTypes()
    {
        return new[] { DeviceType.Datecs, DeviceType.Incotex };
    }

    /// <summary>
    /// Checks if a device type is currently supported.
    /// </summary>
    public static bool IsDeviceTypeSupported(DeviceType deviceType)
    {
        return deviceType == DeviceType.Datecs || deviceType == DeviceType.Incotex;
    }

    /// <summary>
    /// Gets a human-readable name for a device type.
    /// </summary>
    public static string GetDeviceTypeName(DeviceType deviceType)
    {
        return deviceType switch
        {
            DeviceType.Datecs => "Datecs (RS232/TCP)",
            DeviceType.Incotex => "Incotex (Serial/USB)",
            DeviceType.Tremol => "Tremol (WiFi/GPRS) - Coming Soon",
            DeviceType.Elcom => "Elcom - Coming Soon",
            _ => "Unknown"
        };
    }
}
