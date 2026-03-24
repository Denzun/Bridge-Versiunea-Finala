using POSBridge.Abstractions.Enums;
using POSBridge.Abstractions.Models;

namespace POSBridge.Abstractions;

/// <summary>
/// Interfață comună pentru toate dispozitivele fiscale
/// Definește "limba comună" pe care toate casele trebuie să o vorbească
/// </summary>
public interface IFiscalDevice
{
    // ==================== DEVICE IDENTITY ====================
    
    /// <summary>
    /// Nume producător (ex: Datecs, Tremol, Elcom)
    /// </summary>
    string VendorName { get; }
    
    /// <summary>
    /// Model dispozitiv (ex: DP-25, FP-700X)
    /// </summary>
    string ModelName { get; }
    
    /// <summary>
    /// Capabilitățile dispozitivului (ce poate face)
    /// </summary>
    DeviceCapabilities Capabilities { get; }
    
    
    // ==================== CONNECTION MANAGEMENT ====================
    
    /// <summary>
    /// Conectare la dispozitiv
    /// </summary>
    /// <param name="settings">Setări conexiune (port, baud rate, IP, etc.)</param>
    /// <returns>True dacă conectarea a reușit</returns>
    Task<bool> ConnectAsync(ConnectionSettings settings);
    
    /// <summary>
    /// Deconectare de la dispozitiv
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Este conectat la dispozitiv?
    /// </summary>
    bool IsConnected { get; }
    
    
    // ==================== RECEIPT OPERATIONS ====================
    
    /// <summary>
    /// Deschide bon fiscal
    /// </summary>
    /// <param name="operatorCode">Cod operator (1-20)</param>
    /// <param name="password">Parolă operator</param>
    Task<ReceiptResult> OpenReceiptAsync(int operatorCode, string password);
    
    /// <summary>
    /// Adaugă articol pe bon (vânzare)
    /// </summary>
    /// <param name="name">Denumire articol</param>
    /// <param name="price">Preț unitar (lei)</param>
    /// <param name="quantity">Cantitate</param>
    /// <param name="vatGroup">Grupă TVA (1=19%, 2=9%, etc.)</param>
    /// <param name="department">Departament (1-99)</param>
    Task<SaleResult> AddSaleAsync(string name, decimal price, decimal quantity, int vatGroup, int department = 1);
    
    /// <summary>
    /// Subtotal bon (calculează și opțional printează)
    /// CRITICAL: Returnează valoarea subtotalului pentru validări
    /// </summary>
    /// <param name="print">Tipărește subtotal pe bon?</param>
    /// <param name="display">Afișează pe display casă?</param>
    /// <returns>Rezultat cu suma subtotalului</returns>
    Task<SubtotalResult> SubtotalAsync(bool print = true, bool display = true);
    
    /// <summary>
    /// Adaugă discount sau adaos
    /// </summary>
    /// <param name="valueOrPercent">Valoare (lei) sau procent</param>
    /// <param name="isPercent">Este procent? (false = valoare absolută)</param>
    Task<DiscountResult> AddDiscountAsync(decimal valueOrPercent, bool isPercent);
    
    /// <summary>
    /// Adaugă plată pe bon
    /// </summary>
    /// <param name="type">Tip plată (Cash, Card, etc.)</param>
    /// <param name="amount">Sumă plătită (lei)</param>
    Task<PaymentResult> AddPaymentAsync(PaymentType type, decimal amount);
    
    /// <summary>
    /// Închide bonul fiscal
    /// </summary>
    Task<CloseResult> CloseReceiptAsync();
    
    /// <summary>
    /// Anulează bonul fiscal curent
    /// </summary>
    Task CancelReceiptAsync();
    
    
    // ==================== CRITICAL METHODS (din analiza FiscalNet) ====================
    
    /// <summary>
    /// Citește informații despre bonul curent
    /// CRITICAL: Previne bonuri blocate (problema #1)
    /// Permite verificare automată dacă un bon este deschis
    /// </summary>
    Task<ReceiptInfo> ReadCurrentReceiptInfoAsync();
    
    /// <summary>
    /// Tipărește duplicat ultimul bon fiscal
    /// CRITICAL: UX îmbunătățit - duplicat instant (3sec vs 2min)
    /// </summary>
    Task PrintLastReceiptDuplicateAsync();
    
    
    // ==================== CASH MANAGEMENT ====================
    
    /// <summary>
    /// Introducere numerar în casă
    /// </summary>
    /// <param name="amount">Sumă introdusă (lei)</param>
    /// <param name="description">Descriere (opțional) - pentru audit trail</param>
    Task CashInAsync(decimal amount, string description = "");
    
    /// <summary>
    /// Scoatere numerar din casă
    /// </summary>
    /// <param name="amount">Sumă scoasă (lei)</param>
    /// <param name="description">Descriere (opțional) - pentru audit trail</param>
    Task CashOutAsync(decimal amount, string description = "");
    
    /// <summary>
    /// Citește sumele zilnice disponibile în casă
    /// CRITICAL: Reconciliere cash automată (problema #2)
    /// Permite comparare cash socotit manual vs cash fiscal
    /// </summary>
    Task<DailyAmounts> ReadDailyAvailableAmountsAsync();
    
    
    // ==================== REPORTS ====================
    
    /// <summary>
    /// Tipărește raport zilnic (X sau Z)
    /// </summary>
    /// <param name="type">"X" = raport informativ, "Z" = raport cu închidere zi</param>
    Task PrintDailyReportAsync(string type);
    
    /// <summary>
    /// Tipărește raport memorie fiscală pe interval date
    /// </summary>
    /// <param name="startDate">Data început</param>
    /// <param name="endDate">Data sfârșit</param>
    Task PrintFiscalMemoryByDateAsync(DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Tipărește raport operatori
    /// </summary>
    Task PrintOperatorsReportAsync();
    
    /// <summary>
    /// Tipărește raport departamente
    /// </summary>
    Task PrintDepartmentsReportAsync();
    
    
    // ==================== DEVICE INFO & STATUS ====================
    
    /// <summary>
    /// Obține informații despre dispozitiv
    /// </summary>
    Task<DeviceInfo> GetDeviceInfoAsync();
    
    /// <summary>
    /// Citește status dispozitiv (pentru diagnostic)
    /// </summary>
    Task<string> GetStatusAsync();
    
    
    // ==================== DISPLAY & OTHER ====================
    
    /// <summary>
    /// Afișează text pe display-ul casei
    /// </summary>
    /// <param name="text1">Linie 1</param>
    /// <param name="text2">Linie 2 (opțional)</param>
    Task DisplayTextAsync(string text1, string text2 = "");
    
    /// <summary>
    /// Deschide sertarul de bani
    /// </summary>
    Task OpenCashDrawerAsync();
    
    /// <summary>
    /// Tipărește text non-fiscal
    /// </summary>
    /// <param name="text">Text de tipărit</param>
    Task PrintNonFiscalTextAsync(string text);
}
