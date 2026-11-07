using A2A;
using Factory.Shared;

namespace Purchasing.Agent;

/// <summary>Keşfedilmiş bir tedarikçi: kartı ve onunla konuşmak için hazırlanmış istemci.</summary>
public sealed record DiscoveredSupplier(Uri BaseUrl, AgentCard Card, A2AClient Client)
{
    /// <summary>
    /// Bu tedarikçi verilen malzemeyi satıyor mu?
    /// Cevabı kodda sabit yazmıyoruz, ajanın kendi kartındaki skill etiketlerinden öğreniyoruz.
    /// </summary>
    public bool Sells(string materialCode) =>
        Card.Skills.Any(s => s.Tags?.Contains(materialCode, StringComparer.OrdinalIgnoreCase) == true);
}

/// <summary>
/// KEŞİF (discovery): A2A'nın ilk adımı.
///
/// Satınalma Ajanı tedarikçiler hakkında sadece kök adreslerini biliyor (appsettings.json).
/// Ne sattıklarını, hangi adrese istek atılacağını, hangi protokolü konuştuklarını bilmiyor.
/// Bunların hepsini her ajanın /.well-known/agent-card.json adresindeki kartından öğreniyor.
///
/// REST ile farkı: Burada her tedarikçi için ayrı bir client sınıfı yazmadık.
/// Hangi şirket olursa olsun, kartını okuyan tek bir A2AClient onunla konuşabiliyor.
/// </summary>
public sealed class SupplierDirectory(IEnumerable<string> supplierBaseUrls, HttpClient http)
{
    public async Task<List<DiscoveredSupplier>> DiscoverAsync(CancellationToken ct = default)
    {
        var found = new List<DiscoveredSupplier>();

        foreach (var url in supplierBaseUrls)
        {
            var baseUrl = new Uri(url.TrimEnd('/') + "/");
            try
            {
                // 1) Kartı oku: GET {baseUrl}/.well-known/agent-card.json
                var resolver = new A2ACardResolver(baseUrl, http);
                var card = await resolver.GetAgentCardAsync(ct);

                // 2) Karttaki JSON-RPC adresini bul. Kök adres ile istek adresi farklı olabilir
                //    (bizde /a2a), istemci bunu tahmin etmez, karttan okur.
                var jsonRpc = card.SupportedInterfaces.FirstOrDefault(i => i.ProtocolBinding == "JSONRPC");
                if (jsonRpc is null)
                {
                    ConsoleUi.Warn($"  {baseUrl} → '{card.Name}' JSON-RPC desteklemiyor, atlandı.");
                    continue;
                }

                // 3) Bu adres için bir A2A istemcisi oluştur.
                var client = new A2AClient(new Uri(jsonRpc.Url), http);
                found.Add(new DiscoveredSupplier(baseUrl, card, client));

                var tags = card.Skills.SelectMany(s => s.Tags ?? []).Where(t => Materials.FindByCode(t) is not null);  // sadece malzeme kodları
                ConsoleUi.Ok($"  ✓ {card.Name,-34} {jsonRpc.Url,-28} satıyor: {string.Join(", ", tags)}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or A2AException)
            {
                // Ajan kapalıysa keşif devam eder, sadece o ajan listede olmaz.
                ConsoleUi.Warn($"  ✗ {baseUrl} ulaşılamadı ({ex.GetType().Name}). Çalışıyor mu?");
            }
        }

        return found;
    }
}
