namespace POSBridge.Abstractions.Exceptions;

/// <summary>
/// Excepție pentru erori dispozitiv fiscal
/// </summary>
public class FiscalDeviceException : Exception
{
    /// <summary>
    /// Cod eroare din dispozitiv
    /// </summary>
    public int? ErrorCode { get; set; }
    
    /// <summary>
    /// Tip eroare (Device, Communication, Timeout, etc.)
    /// </summary>
    public string? ErrorType { get; set; }
    
    public FiscalDeviceException()
    {
    }
    
    public FiscalDeviceException(string message) : base(message)
    {
    }
    
    public FiscalDeviceException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
    
    public FiscalDeviceException(string message, int errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
    
    public FiscalDeviceException(string message, int errorCode, string errorType) 
        : base(message)
    {
        ErrorCode = errorCode;
        ErrorType = errorType;
    }
}
