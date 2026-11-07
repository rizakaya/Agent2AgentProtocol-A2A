// =====================================================================
// FAZ 1: SDK kullanmadan, elle yazılmış bir A2A sunucusu.
//
// Bu dosya protokolün kendisidir. İki endpoint var:
//   GET  /.well-known/agent-card.json   → Keşif: "Ben kimim, ne yaparım, beni nereden çağırırsın?"
//   POST /a2a                           → JSON-RPC: "Bana bir mesaj gönder, cevap vereyim."
//
// Okuma sırası önerisi:
//   1) Bu dosya (Program.cs)           → istek nasıl karşılanıyor
//   2) Protocol/JsonRpc.cs             → JSON-RPC zarfı ve hata kodları
//   3) Protocol/A2AModels.cs           → AgentCard, Message, Part
//   4) SupplierAgent.cs                → tedarikçinin iş mantığı
// =====================================================================

using System.Text.Json;
using RawSupplier;
using RawSupplier.Protocol;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SupplierAgent>();

var app = builder.Build();
var log = app.Logger;

// Agent Card'daki adresin, uygulamanın gerçekten dinlediği adresle aynı olması gerekir.
var baseUrl = (app.Configuration["Urls"] ?? "http://localhost:5100").Split(';')[0];
var agentCard = SupplierAgent.CreateAgentCard(baseUrl);

// ---------------------------------------------------------------------
// 1) KEŞİF: Agent Card
// Bu adres A2A standardında sabittir. Bir istemci sadece sunucunun kök adresini
// bilir (http://localhost:5100), kartı bu yoldan okuyarak gerisini öğrenir.
// ---------------------------------------------------------------------
app.MapGet("/.well-known/agent-card.json", () =>
{
    log.LogInformation("[KEŞİF] Agent Card istendi");
    return Results.Json(agentCard, A2AJson.Options);
});

// ---------------------------------------------------------------------
// 2) JSON-RPC ENDPOINT
// A2A'nın JSON-RPC binding'inde tüm metodlar (SendMessage, GetTask, ...)
// TEK bir adrese POST edilir. Hangi işin yapılacağını URL değil,
// gövdedeki "method" alanı belirler. REST'ten en büyük farkı budur.
// ---------------------------------------------------------------------
app.MapPost("/a2a", async (HttpRequest http, SupplierAgent agent) =>
{
    var body = await new StreamReader(http.Body).ReadToEndAsync();
    log.LogInformation("[GELEN] İstek:\n{Body}", body);

    var response = HandleJsonRpc(body, http.Headers["A2A-Version"].ToString(), agent);

    log.LogInformation("[GİDEN] Cevap:\n{Body}", JsonSerializer.Serialize(response, A2AJson.Pretty));
    // Not: JSON-RPC'de hata olsa bile HTTP durum kodu 200'dür. Hata bilgisi gövdededir.
    return Results.Json(response, A2AJson.Options);
});

app.Run();

// =====================================================================
// JSON-RPC isteğini adım adım işleyen fonksiyon.
// =====================================================================
static JsonRpcResponse HandleJsonRpc(string body, string versionHeader, SupplierAgent agent)
{
    // --- Adım 1: Gövde geçerli JSON mu? ---
    JsonDocument doc;
    try
    {
        doc = JsonDocument.Parse(body);
    }
    catch (JsonException)
    {
        // JSON okunamadığı için id'yi de bilemiyoruz, bu yüzden id = null.
        return JsonRpcResponse.Failure(null, ErrorCodes.ParseError, "Geçersiz JSON");
    }

    using (doc)
    {
        var root = doc.RootElement;

        // id'yi en başta al ki hata cevaplarında bile geri gönderebilelim.
        // Clone(): doc dispose edildikten sonra da kullanılabilmesi için kopyalıyoruz.
        JsonElement? id = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var idEl)
            ? idEl.Clone()
            : null;

        // --- Adım 2: JSON-RPC zarfı doğru mu? ---
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("jsonrpc", out var ver) || ver.GetString() != "2.0"
            || !root.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String)
        {
            return JsonRpcResponse.Failure(id, ErrorCodes.InvalidRequest,
                "Geçerli bir JSON-RPC 2.0 isteği değil ('jsonrpc' ve 'method' alanları gerekli)");
        }

        // --- Adım 3: A2A sürümü ---
        // İstemci hangi A2A sürümünü konuştuğunu "A2A-Version" header'ı ile bildirir.
        // Spesifikasyona göre header yoksa istemci 0.3 kabul edilir. Biz sadece 1.0 konuşuyoruz.
        var version = string.IsNullOrWhiteSpace(versionHeader) ? "0.3" : versionHeader.Trim();
        if (version != "1.0")
        {
            return JsonRpcResponse.Failure(id, ErrorCodes.VersionNotSupported,
                $"A2A sürümü '{version}' desteklenmiyor. Desteklenen: 1.0 ('A2A-Version: 1.0' header'ını ekleyin)");
        }

        // --- Adım 4: Metodu çalıştır ---
        var method = methodEl.GetString();
        root.TryGetProperty("params", out var paramsEl);

        return method switch
        {
            Methods.SendMessage => HandleSendMessage(id, paramsEl, agent),
            _ => JsonRpcResponse.Failure(id, ErrorCodes.MethodNotFound, $"'{method}' metodu bulunamadı"),
        };
    }
}

// =====================================================================
// SendMessage: "Bu mesajı al, bana cevap ver."
// =====================================================================
static JsonRpcResponse HandleSendMessage(JsonElement? id, JsonElement paramsEl, SupplierAgent agent)
{
    SendMessageParams? p = null;
    try
    {
        if (paramsEl.ValueKind == JsonValueKind.Object)
            p = paramsEl.Deserialize<SendMessageParams>(A2AJson.Options);
    }
    catch (JsonException)
    {
        // Aşağıda InvalidParams dönecek.
    }

    if (p?.Message is not { } message
        || message.Parts is not { Count: > 0 }
        || string.IsNullOrWhiteSpace(message.MessageId)
        || string.IsNullOrWhiteSpace(message.Role))
    {
        return JsonRpcResponse.Failure(id, ErrorCodes.InvalidParams,
            "'params.message' içinde messageId, role ve en az bir part olmalı");
    }

    // İstemci "ben sadece şu türleri kabul ederim" dediyse ve biz o türleri üretemiyorsak hata ver.
    var accepted = p.Configuration?.AcceptedOutputModes;
    if (accepted is { Count: > 0 } && !accepted.Any(m => m is "text/plain" or "application/json"))
    {
        return JsonRpcResponse.Failure(id, ErrorCodes.ContentTypeNotSupported,
            $"Bu ajan şu türleri üretemez: {string.Join(", ", accepted)}");
    }

    var reply = agent.Handle(message);

    // Sonuç ya { "message": ... } ya da { "task": ... } olur.
    // Bu tedarikçi anında cevap verdiği için Task açmıyor, direkt Message dönüyor.
    return JsonRpcResponse.Success(id, new SendMessageResult { Message = reply });
}
