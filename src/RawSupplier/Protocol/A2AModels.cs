using System.Text.Json;
using System.Text.Json.Serialization;

namespace RawSupplier.Protocol;

// =====================================================================
// A2A v1.0 veri yapıları, SDK kullanmadan elle yazıldı.
//
// Faz 2'de bunların hepsini resmi SDK'nın "A2A" paketi hazır verecek.
// Burada amaç, kablodan geçen JSON'un tam olarak neye benzediğini görmek.
// Alan adları, resmi .NET SDK'sının ürettiği JSON ile birebir aynıdır
// (bir deneme sunucusuyla doğrulandı).
// =====================================================================

/// <summary>
/// Agent Card: ajanın kimlik kartı.
/// GET /.well-known/agent-card.json adresinden yayınlanır.
/// İstemci önce bunu okur, ajanın ne yaptığını ve nereden çağrılacağını öğrenir.
/// </summary>
public sealed class AgentCard
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }

    /// <summary>
    /// Ajana hangi adres ve hangi "binding" (JSONRPC, HTTP+JSON, GRPC) ile ulaşılacağı.
    /// Bir ajan aynı anda birden fazla binding sunabilir. Bizimki sadece JSON-RPC.
    /// </summary>
    public required List<AgentInterface> SupportedInterfaces { get; init; }

    public AgentProvider? Provider { get; init; }

    /// <summary>Streaming, push notification gibi opsiyonel yetenekler.</summary>
    public required AgentCapabilities Capabilities { get; init; }

    /// <summary>Ajanın yapabildiği işler. Keşif sırasında en önemli alan budur.</summary>
    public required List<AgentSkill> Skills { get; init; }

    /// <summary>Ajanın kabul ettiği içerik türleri (MIME type).</summary>
    public required List<string> DefaultInputModes { get; init; }

    /// <summary>Ajanın üretebildiği içerik türleri (MIME type).</summary>
    public required List<string> DefaultOutputModes { get; init; }
}

public sealed class AgentInterface
{
    public required string Url { get; init; }

    /// <summary>"JSONRPC", "HTTP+JSON" veya "GRPC".</summary>
    public required string ProtocolBinding { get; init; }

    /// <summary>Bu adresin konuştuğu A2A sürümü.</summary>
    public required string ProtocolVersion { get; init; }
}

public sealed class AgentProvider
{
    public required string Organization { get; init; }
    public required string Url { get; init; }
}

public sealed class AgentCapabilities
{
    /// <summary>SendStreamingMessage destekleniyor mu? (Faz 3'te true olacak.)</summary>
    public bool Streaming { get; init; }

    /// <summary>Webhook ile durum bildirimi destekleniyor mu?</summary>
    public bool PushNotifications { get; init; }
}

public sealed class AgentSkill
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public List<string>? Tags { get; init; }

    /// <summary>Bu beceriyi tetikleyecek örnek istekler. LLM'ler bunlardan çok faydalanır.</summary>
    public List<string>? Examples { get; init; }
}

/// <summary>
/// Message: konuşmadaki tek bir mesaj.
/// Bir mesaj birden fazla "part" (parça) içerebilir: metin, yapılandırılmış veri, dosya.
/// </summary>
public sealed class Message
{
    /// <summary>"ROLE_USER" (isteği gönderen) veya "ROLE_AGENT" (cevap veren ajan).</summary>
    public required string Role { get; init; }

    public required List<Part> Parts { get; init; }

    /// <summary>Her mesajın benzersiz kimliği. Gönderen taraf üretir.</summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Konuşma bağlamı. Aynı konuşmaya ait mesajlar aynı contextId'yi taşır.
    /// İstemci göndermezse sunucu yeni bir tane üretir.
    /// </summary>
    public string? ContextId { get; init; }

    /// <summary>Mesaj bir Task'a aitse onun kimliği. (Faz 3'te kullanacağız.)</summary>
    public string? TaskId { get; init; }
}

public static class Roles
{
    public const string User = "ROLE_USER";
    public const string Agent = "ROLE_AGENT";
}

/// <summary>
/// Part: mesajın bir parçası. v1.0'da tek bir tip vardır,
/// hangi alanın dolu olduğu parçanın türünü belirler:
///   { "text": "..." }                → düz metin
///   { "data": { ... } }              → yapılandırılmış JSON verisi
///   { "url": "https://..." }         → bir dosyanın adresi
///   { "raw": "base64..." }           → dosyanın kendisi
/// </summary>
public sealed class Part
{
    public string? Text { get; init; }
    public JsonElement? Data { get; init; }
    public string? Url { get; init; }
    public string? Raw { get; init; }
    public string? Filename { get; init; }
    public string? MediaType { get; init; }

    public static Part FromText(string text) => new() { Text = text };

    public static Part FromData<T>(T value) => new()
    {
        Data = JsonSerializer.SerializeToElement(value, A2AJson.Options),
        MediaType = "application/json",
    };
}

/// <summary>SendMessage metodunun "params" alanı.</summary>
public sealed class SendMessageParams
{
    public Message? Message { get; init; }
    public SendMessageConfiguration? Configuration { get; init; }
    public JsonElement? Metadata { get; init; }
}

public sealed class SendMessageConfiguration
{
    /// <summary>İstemcinin kabul edebileceği cevap türleri.</summary>
    public List<string>? AcceptedOutputModes { get; init; }

    /// <summary>true ise sunucu, Task bitmeden hemen cevap döner. (Faz 3.)</summary>
    public bool? ReturnImmediately { get; init; }
}

/// <summary>
/// SendMessage'ın "result" alanı. İçinde ya "message" ya da "task" bulunur, ikisi birden asla.
/// Faz 1'de sadece anında cevap veriyoruz, yani hep "message" dolu.
/// </summary>
public sealed class SendMessageResult
{
    public Message? Message { get; init; }
    // public AgentTask? Task { get; init; }   ← Faz 3'te gelecek
}

/// <summary>Projedeki tüm JSON işlemleri için ortak ayarlar.</summary>
public static class A2AJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // A2A JSON'u camelCase kullanır: "messageId", "contextId" ...
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Boş alanlar JSON'a hiç yazılmaz (ör. "taskId": null görmezsin).
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Türkçe karakterler ı gibi kaçış dizisine dönüşmesin, okunabilir kalsın.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Konsola yazdırırken girintili gösterim için.</summary>
    public static readonly JsonSerializerOptions Pretty = new(Options) { WriteIndented = true };
}
