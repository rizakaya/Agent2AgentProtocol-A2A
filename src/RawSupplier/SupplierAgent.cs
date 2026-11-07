using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Factory.Shared;
using RawSupplier.Protocol;

namespace RawSupplier;

/// <summary>
/// Tedarikçinin "beyni". A2A protokolünden bağımsızdır:
/// gelen mesajı anlar, fiyat teklifini hesaplar ve cevap mesajını hazırlar.
///
/// Dikkat: Bu sınıfta HTTP veya JSON-RPC ile ilgili hiçbir şey yok.
/// Protokol işi Program.cs'te, iş mantığı burada. Faz 2'de SDK'ya geçerken
/// bu ayrım sayesinde sadece Program.cs tarafı değişecek.
/// </summary>
public sealed partial class SupplierAgent
{
    public const string SupplierName = "Çelik A.Ş. (elle yazılmış sürüm)";

    private const int DeliveryDays = 3;
    private const decimal BulkThreshold = 500m;      // Bu miktar ve üstüne indirim
    private const decimal BulkDiscount = 0.05m;      // %5

    /// <summary>Bu tedarikçinin sattığı malzemeler ve birim fiyatları (TL).</summary>
    private static readonly Dictionary<string, decimal> PriceList = new()
    {
        [Materials.SteelPipe.Code] = 85.00m,
    };

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Agent Card: bu ajanı dış dünyaya tanıtan kart.</summary>
    public static AgentCard CreateAgentCard(string baseUrl) => new()
    {
        Name = SupplierName,
        Description = "Çelik boru satan tedarikçi ajanı. Malzeme ve miktar verildiğinde " +
                      "birim fiyat, toplam tutar ve teslim süresiyle fiyat teklifi verir.",
        Version = "1.0.0",
        Provider = new AgentProvider { Organization = "Çelik A.Ş.", Url = "https://example.com/celik-as" },
        SupportedInterfaces =
        [
            new AgentInterface
            {
                Url = $"{baseUrl.TrimEnd('/')}/a2a",   // JSON-RPC isteklerinin gideceği adres
                ProtocolBinding = "JSONRPC",
                ProtocolVersion = "1.0",
            },
        ],
        Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false },
        DefaultInputModes = ["text/plain", "application/json"],
        DefaultOutputModes = ["text/plain", "application/json"],
        Skills =
        [
            new AgentSkill
            {
                Id = "fiyat-teklifi",
                Name = "Fiyat teklifi",
                Description = "Çelik boru için fiyat teklifi verir. Girdi: metin (\"500 m çelik boru\") " +
                              "veya veri { materialCode, quantity }. Çıktı: metin + Quote verisi.",
                Tags = ["teklif", "celik-boru", "tedarik"],
                Examples = ["500 m çelik boru için fiyat teklifi istiyorum"],
            },
        ],
    };

    /// <summary>
    /// Gelen mesajı işler ve cevap mesajını döner.
    /// Mesaj iki şekilde gelebilir:
    ///   1) data part:  { "materialCode": "celik-boru", "quantity": 500 }   ← programlar için
    ///   2) text part:  "500 m çelik boru için teklif istiyorum"            ← insanlar / LLM'ler için
    /// </summary>
    public Message Handle(Message incoming)
    {
        // Konuşma bağlamı: istemci verdiyse aynısını kullan, vermediyse yenisini üret.
        var contextId = incoming.ContextId ?? NewId();

        var request = TryReadDataPart(incoming) ?? TryReadTextPart(incoming);
        if (request is null)
        {
            return Reply(contextId, Part.FromText(
                "İsteğinizi anlayamadım. Örnek: \"500 m çelik boru için teklif\" " +
                "ya da { \"materialCode\": \"celik-boru\", \"quantity\": 500 }"));
        }

        var material = Materials.FindByCode(request.MaterialCode);
        if (material is null || !PriceList.TryGetValue(material.Code, out var unitPrice))
        {
            return Reply(contextId, Part.FromText(
                $"Üzgünüz, '{material?.Name ?? request.MaterialCode}' satmıyoruz. Sadece çelik boru satıyoruz."));
        }

        if (request.Quantity <= 0)
        {
            return Reply(contextId, Part.FromText("Miktar sıfırdan büyük olmalı."));
        }

        var quote = CalculateQuote(material, request.Quantity, unitPrice);

        // Cevapta iki parça var: aynı bilginin insan için ve program için iki hali.
        return Reply(contextId,
            Part.FromText(
                $"{quote.Quantity.ToString("N0", Tr)} {quote.Unit} {material.Name.ToLower(Tr)} için teklifimiz: " +
                $"birim fiyat {quote.UnitPrice.ToString("N2", Tr)} TL, toplam {quote.TotalPrice.ToString("N2", Tr)} TL, " +
                $"teslim süresi {quote.DeliveryDays} gün."),
            Part.FromData(quote));
    }

    private static Quote CalculateQuote(Material material, decimal quantity, decimal listPrice)
    {
        var unitPrice = quantity >= BulkThreshold ? listPrice * (1 - BulkDiscount) : listPrice;
        return new Quote(
            Supplier: SupplierName,
            MaterialCode: material.Code,
            Quantity: quantity,
            Unit: material.Unit,
            UnitPrice: Math.Round(unitPrice, 2),
            TotalPrice: Math.Round(unitPrice * quantity, 2),
            Currency: "TRY",
            DeliveryDays: DeliveryDays);
    }

    /// <summary>Mesajda { materialCode, quantity } şeklinde bir data part var mı?</summary>
    private static QuoteRequest? TryReadDataPart(Message message)
    {
        foreach (var part in message.Parts)
        {
            if (part.Data is not { ValueKind: JsonValueKind.Object } data) continue;
            try
            {
                var request = data.Deserialize<QuoteRequest>(A2AJson.Options);
                if (!string.IsNullOrWhiteSpace(request?.MaterialCode)) return request;
            }
            catch (JsonException)
            {
                // Bu data part bizim beklediğimiz şekilde değil, diğer parçalara bak.
            }
        }
        return null;
    }

    /// <summary>Metin parçasından malzeme ve miktar çıkarmaya çalışır ("500 m çelik boru").</summary>
    private static QuoteRequest? TryReadTextPart(Message message)
    {
        var text = string.Join(" ", message.Parts.Where(p => p.Text is not null).Select(p => p.Text));
        var material = Materials.FindInText(text);
        var match = NumberRegex().Match(text);
        if (material is null || !match.Success) return null;

        var quantity = decimal.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return new QuoteRequest(material.Code, quantity);
    }

    private static Message Reply(string contextId, params Part[] parts) => new()
    {
        Role = Roles.Agent,
        MessageId = NewId(),
        ContextId = contextId,
        Parts = [.. parts],
    };

    private static string NewId() => Guid.NewGuid().ToString("N");

    [GeneratedRegex(@"\d+([.,]\d+)?")]
    private static partial Regex NumberRegex();
}
