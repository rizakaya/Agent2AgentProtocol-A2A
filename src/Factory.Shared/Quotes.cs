namespace Factory.Shared;

/// <summary>
/// Satınalmanın tedarikçiden istediği teklif.
/// A2A mesajında "data" part olarak taşınır:
/// { "materialCode": "celik-boru", "quantity": 500 }
/// </summary>
public sealed record QuoteRequest(string MaterialCode, decimal Quantity);

/// <summary>
/// Tedarikçinin verdiği fiyat teklifi.
/// Cevap mesajında hem okunabilir metin hem de bu yapı ("data" part) olarak döner.
/// Böylece karşı taraf bir insan ya da LLM ise metni, bir program ise veriyi kullanır.
/// </summary>
public sealed record Quote(
    string Supplier,
    string MaterialCode,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal TotalPrice,
    string Currency,
    int DeliveryDays);
