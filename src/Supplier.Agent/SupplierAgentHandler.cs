using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using A2A;
using Factory.Shared;

namespace Supplier.Agent;

/// <summary>
/// FAZ 2: Tedarikçi ajanı, resmi A2A SDK'sı ile.
///
/// Faz 1'de yaptığımız her şeyi hatırla: JSON okumak, "jsonrpc" ve "method" kontrolü,
/// sürüm header'ı, hata kodları, cevabı { "result": { "message": ... } } şeklinde paketlemek...
/// Bunların HİÇBİRİ burada yok. SDK hepsini yapıyor ve bize sadece şunu soruyor:
///
///     "Bir mesaj geldi (context). Ne cevap vereceksin? (eventQueue'ya yaz)"
///
/// Yani IAgentHandler, ajanın "beyni"dir. Protokol işi SDK'da, iş mantığı burada.
/// </summary>
public sealed partial class SupplierAgentHandler(
    SupplierProfile profile,
    QuoteCalculator calculator,
    ILogger<SupplierAgentHandler> log) : IAgentHandler
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // Quote nesnesini data part'a çevirirken camelCase kullan: { "unitPrice": ... }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// SDK, her SendMessage isteğinde bu metodu çağırır.
    ///   context    → gelen mesaj, contextId, (varsa) taskId gibi bilgiler
    ///   eventQueue → cevabımızı yazdığımız kuyruk. SDK buradan okuyup JSON-RPC cevabını oluşturur.
    /// </summary>
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        log.LogInformation("[{Supplier}] Mesaj geldi. contextId={ContextId}, part sayısı={Count}, metin=\"{Text}\"",
            profile.Name, context.ContextId, context.Message.Parts.Count, context.UserText);

        // MessageResponder: "Task açmadan, tek bir mesajla cevap ver" demek.
        // Faz 1'deki { "result": { "message": ... } } cevabının SDK'daki karşılığı.
        // Faz 3'te bunun yerine TaskUpdater kullanacağız ve sonuç { "task": ... } olacak.
        var responder = new MessageResponder(eventQueue, context.ContextId);

        var request = TryReadDataPart(context.Message) ?? TryReadText(context.UserText);
        if (request is null)
        {
            await responder.ReplyAsync(
                "İsteğinizi anlayamadım. Örnek: \"500 m çelik boru için teklif\" " +
                "ya da { \"materialCode\": \"celik-boru\", \"quantity\": 500 }",
                cancellationToken: cancellationToken);
            return;
        }

        var material = Materials.FindByCode(request.MaterialCode);
        if (material is null || !calculator.Sells(material.Code))
        {
            var sold = string.Join(", ", profile.PriceList.Keys.Select(c => Materials.FindByCode(c)?.Name ?? c));
            await responder.ReplyAsync(
                $"Üzgünüz, '{material?.Name ?? request.MaterialCode}' satmıyoruz. Sattıklarımız: {sold}.",
                cancellationToken: cancellationToken);
            return;
        }

        if (request.Quantity <= 0)
        {
            await responder.ReplyAsync("Miktar sıfırdan büyük olmalı.", cancellationToken: cancellationToken);
            return;
        }

        var quote = calculator.Calculate(material, request.Quantity);
        log.LogInformation("[{Supplier}] Teklif: {Qty} {Unit} {Material} → {Total} TL, {Days} gün",
            profile.Name, quote.Quantity, quote.Unit, material.Code, quote.TotalPrice, quote.DeliveryDays);

        // Faz 1'deki gibi iki parça: insan için metin + program için veri.
        // Part.FromText / Part.FromData, Faz 1'de elle yazdığımız Part sınıfının SDK'daki karşılığı.
        var text = Part.FromText(
            $"{quote.Quantity.ToString("N0", Tr)} {quote.Unit} {material.Name.ToLower(Tr)} için teklifimiz: " +
            $"birim fiyat {quote.UnitPrice.ToString("N2", Tr)} TL, toplam {quote.TotalPrice.ToString("N2", Tr)} TL, " +
            $"teslim süresi {quote.DeliveryDays} gün.");
        var data = Part.FromData(JsonSerializer.SerializeToElement(quote, Json));
        data.MediaType = "application/json";

        await responder.ReplyAsync([text, data], cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Task iptal isteği (CancelTask). Bu tedarikçi henüz Task açmadığı için iptal edilecek bir şey yok.
    /// Faz 3'te sipariş Task'ları gelince burayı dolduracağız.
    /// </summary>
    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>Mesajda { materialCode, quantity } şeklinde bir data part var mı?</summary>
    private static QuoteRequest? TryReadDataPart(Message message)
    {
        foreach (var part in message.Parts)
        {
            if (part.Data is not { ValueKind: JsonValueKind.Object } data) continue;
            try
            {
                var request = data.Deserialize<QuoteRequest>(Json);
                if (!string.IsNullOrWhiteSpace(request?.MaterialCode)) return request;
            }
            catch (JsonException)
            {
                // Beklediğimiz şekilde değil, diğer parçalara bak.
            }
        }
        return null;
    }

    /// <summary>
    /// Serbest metinden malzeme ve miktar çıkarır ("500 m çelik boru").
    /// context.UserText: SDK'nın, mesajdaki tüm text part'ları birleştirip verdiği kısayol.
    /// </summary>
    private static QuoteRequest? TryReadText(string? text)
    {
        var material = Materials.FindInText(text);
        var match = NumberRegex().Match(text ?? "");
        if (material is null || !match.Success) return null;
        return new QuoteRequest(material.Code, decimal.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"\d+([.,]\d+)?")]
    private static partial Regex NumberRegex();
}
