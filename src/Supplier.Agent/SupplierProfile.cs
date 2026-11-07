namespace Supplier.Agent;

/// <summary>
/// Bir tedarikçi şirketin ayarları. appsettings.{Profil}.json dosyasından okunur.
///
/// Aynı kodu iki farklı şirket olarak çalıştırmamızı sağlayan şey bu sınıf:
///   appsettings.CelikAS.json      → Çelik A.Ş.        (port 5101)
///   appsettings.BaglantiLtd.json  → Bağlantı Ltd.     (port 5102)
/// Gerçek hayatta bunlar farklı şirketlerin, farklı ekiplerin yazdığı ajanlar olurdu.
/// </summary>
public sealed class SupplierProfile
{
    public required string Name { get; init; }
    public required string Organization { get; init; }
    public required string Description { get; init; }

    /// <summary>Teslim süresi (gün).</summary>
    public int DeliveryDays { get; init; }

    /// <summary>Bu miktar ve üstündeki siparişlere indirim uygulanır.</summary>
    public decimal BulkThreshold { get; init; }

    /// <summary>Toplu alım indirimi (0.05 = %5).</summary>
    public decimal BulkDiscount { get; init; }

    /// <summary>Satılan malzemeler: malzeme kodu → birim fiyat (TL).</summary>
    public Dictionary<string, decimal> PriceList { get; init; } = [];
}
