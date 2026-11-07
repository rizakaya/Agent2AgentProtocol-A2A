using System.Text.Json;
using A2A;
using Factory.Shared;

namespace Purchasing.Agent;

/// <summary>Bir tedarikçiden gelen cevap: ya bir teklif (Quote) ya da sadece açıklama metni.</summary>
public sealed record SupplierAnswer(DiscoveredSupplier Supplier, Quote? Quote, string Text);

/// <summary>
/// Tedarikçilere A2A mesajı gönderip teklifleri toplar.
/// </summary>
public sealed class QuoteCollector
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Tüm adaylara AYNI ANDA teklif isteği gönderir (Task.WhenAll).
    /// Hepsine aynı contextId'yi veriyoruz: hepsi "aynı satınalma sürecinin" parçası.
    /// </summary>
    public async Task<List<SupplierAnswer>> CollectAsync(
        IEnumerable<DiscoveredSupplier> suppliers, QuoteRequest request, string contextId, CancellationToken ct = default)
    {
        var answers = await Task.WhenAll(suppliers.Select(s => AskAsync(s, request, contextId, ct)));
        return [.. answers];
    }

    private static async Task<SupplierAnswer> AskAsync(
        DiscoveredSupplier supplier, QuoteRequest request, string contextId, CancellationToken ct)
    {
        var material = Materials.FindByCode(request.MaterialCode)!;

        // Mesajı iki parçayla gönderiyoruz:
        //  - text: bizim veri formatımızı bilmeyen bir tedarikçi (veya LLM) de anlasın diye
        //  - data: bizim formatımızı bilen tedarikçi kesin değerlerle çalışsın diye
        var dataPart = Part.FromData(JsonSerializer.SerializeToElement(request, Json));
        dataPart.MediaType = "application/json";

        var message = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = contextId,
            Role = Role.User,
            Parts =
            [
                Part.FromText($"{request.Quantity} {material.Unit} {material.Name} için fiyat teklifi istiyoruz."),
                dataPart,
            ],
        };

        try
        {
            // SendMessage: Faz 1'de .http dosyasında elle yazdığın JSON-RPC isteğinin aynısını SDK üretir.
            var response = await supplier.Client.SendMessageAsync(new SendMessageRequest { Message = message }, ct);

            // Cevap ya Message ya da Task olabilir. Tedarikçilerimiz şimdilik hep Message döner.
            // Faz 3'te sipariş için Task döneceğiz ve buradaki switch'in ikinci kolu çalışacak.
            return response.PayloadCase switch
            {
                SendMessageResponseCase.Message => ReadAnswer(supplier, response.Message!),
                SendMessageResponseCase.Task => new SupplierAnswer(supplier, null,
                    $"Beklenmedik şekilde Task döndü (id={response.Task!.Id}). Faz 3'te ele alacağız."),
                _ => new SupplierAnswer(supplier, null, "Boş cevap."),
            };
        }
        catch (A2AException ex)
        {
            // Protokol hatası: JSON-RPC "error" alanı ile dönen hatalar (ör. -32601).
            return new SupplierAnswer(supplier, null, $"A2A hatası: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return new SupplierAnswer(supplier, null, $"Bağlantı hatası: {ex.Message}");
        }
    }

    /// <summary>Cevaptaki data part'ı Quote olarak okumaya çalışır, yoksa sadece metni alır.</summary>
    private static SupplierAnswer ReadAnswer(DiscoveredSupplier supplier, Message reply)
    {
        var text = string.Join(" ", reply.Parts.Where(p => p.Text is not null).Select(p => p.Text));

        foreach (var part in reply.Parts)
        {
            if (part.Data is not { ValueKind: JsonValueKind.Object } data) continue;
            var quote = data.Deserialize<Quote>(Json);
            if (quote is not null && quote.TotalPrice > 0) return new SupplierAnswer(supplier, quote, text);
        }

        return new SupplierAnswer(supplier, null, text);
    }
}
