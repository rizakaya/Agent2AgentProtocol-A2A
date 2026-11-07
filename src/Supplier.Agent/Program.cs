// =====================================================================
// FAZ 2: Tedarikçi ajanı, resmi A2A .NET SDK'sı ile.
//
// Faz 1'deki RawSupplier/Program.cs ile yan yana aç ve karşılaştır:
//   - Protocol/ klasörü yok        → AgentCard, Message, Part artık A2A paketinden geliyor
//   - HandleJsonRpc fonksiyonu yok → app.MapA2A("/a2a") tek satırda aynı işi yapıyor
//   - Hata kodları yok             → SDK, -32601, -32602, -32009 vb. hepsini kendisi dönüyor
//
// Aynı proje iki farklı şirket olarak çalışır (launch profile ile):
//   dotnet run --launch-profile CelikAS       → Çelik A.Ş.     http://localhost:5101
//   dotnet run --launch-profile BaglantiLtd   → Bağlantı Ltd.  http://localhost:5102
// =====================================================================

using A2A;
using A2A.AspNetCore;
using Factory.Shared;
using Supplier.Agent;

var builder = WebApplication.CreateBuilder(args);

// Hangi şirket olarak çalışacağımızı ayardan oku (appsettings.CelikAS.json vb.)
var profile = builder.Configuration.GetSection("Supplier").Get<SupplierProfile>()
    ?? throw new InvalidOperationException(
        "Tedarikçi ayarı bulunamadı. Bir launch profile ile çalıştırın: dotnet run --launch-profile CelikAS");

var baseUrl = (builder.Configuration["urls"] ?? "http://localhost:5101").Split(';')[0].TrimEnd('/');

builder.Services.AddSingleton(profile);
builder.Services.AddSingleton<QuoteCalculator>();

// Tek satırda: ajanımızı (IAgentHandler) ve kartını kaydet.
// SDK bununla A2AServer'ı, Task deposunu (InMemoryTaskStore) ve JSON-RPC altyapısını kurar.
builder.Services.AddA2AAgent<SupplierAgentHandler>(CreateAgentCard(profile, baseUrl));

var app = builder.Build();

// Öğrenme amaçlı: gelen ham JSON'u konsola yaz.
// Böylece SDK'nın aldığı isteğin, Faz 1'de elle yazdığın JSON ile aynı olduğunu görürsün.
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsPost(ctx.Request.Method))
    {
        ctx.Request.EnableBuffering();
        var body = await new StreamReader(ctx.Request.Body, leaveOpen: true).ReadToEndAsync();
        ctx.Request.Body.Position = 0;
        app.Logger.LogInformation("[GELEN] {Path}\n{Body}", ctx.Request.Path, body);
    }
    await next();
});

// Faz 1'deki iki endpoint'in SDK karşılıkları:
app.MapA2A("/a2a");                                                         // POST /a2a (JSON-RPC)
app.MapWellKnownAgentCard(app.Services.GetRequiredService<AgentCard>());   // GET /.well-known/agent-card.json

app.Logger.LogInformation("{Name} hazır: {Url}/.well-known/agent-card.json", profile.Name, baseUrl);
app.Run();

// ---------------------------------------------------------------------
// Agent Card: artık SDK'nın AgentCard tipini kullanıyoruz.
// Skill'lerin "tags" alanına sattığımız malzeme kodlarını koyuyoruz.
// Satınalma Ajanı keşif sırasında tam olarak buna bakacak:
// "celik-boru" etiketi olan ajan, çelik boru satıyordur.
// ---------------------------------------------------------------------
static AgentCard CreateAgentCard(SupplierProfile profile, string baseUrl)
{
    var materials = profile.PriceList.Keys
        .Select(code => Materials.FindByCode(code)?.Name ?? code)
        .ToList();

    return new AgentCard
    {
        Name = profile.Name,
        Description = profile.Description,
        Version = "1.0.0",
        Provider = new AgentProvider { Organization = profile.Organization, Url = "https://example.com" },
        SupportedInterfaces =
        [
            new AgentInterface { Url = $"{baseUrl}/a2a", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
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
                Description = $"Şu malzemeler için fiyat teklifi verir: {string.Join(", ", materials)}. " +
                              "Girdi: metin veya { materialCode, quantity } verisi. Çıktı: metin + Quote verisi.",
                Tags = ["teklif", .. profile.PriceList.Keys],
                Examples = [.. profile.PriceList.Keys
                    .Select(Materials.FindByCode)
                    .OfType<Material>()
                    .Select(m => $"500 {m.Unit} {m.Name} için fiyat teklifi")],
            },
        ],
    };
}
