// =====================================================================
// FAZ 2: Satınalma Ajanı (A2A İSTEMCİSİ)
//
// Akış:
//   1) KEŞİF     → appsettings.json'daki adreslerden Agent Card'ları oku
//   2) FİLTRE    → kartındaki skill etiketlerine göre, istenen malzemeyi satanları seç
//   3) TEKLİF    → seçilenlere aynı anda SendMessage gönder
//   4) KARAR     → en düşük toplam tutarı seç (eşitse daha kısa teslim süresi)
//
// Okuma sırası: Program.cs → SupplierDirectory.cs → QuoteCollector.cs
//
// Kullanım:
//   dotnet run                       → etkileşimli mod (malzeme ve miktar sorar)
//   dotnet run -- celik-boru 500     → tek seferlik
// =====================================================================

using System.Globalization;
using System.Text;
using Factory.Shared;
using Microsoft.Extensions.Configuration;
using Purchasing.Agent;

Console.OutputEncoding = Encoding.UTF8;   // Windows konsolunda Türkçe karakterler için
var tr = CultureInfo.GetCultureInfo("tr-TR");

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .Build();
var supplierUrls = config.GetSection("Suppliers").Get<string[]>() ?? [];

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

// ---------------------------------------------------------------------
// 1) KEŞİF
// ---------------------------------------------------------------------
ConsoleUi.Title("1) Keşif: tedarikçilerin Agent Card'ları okunuyor");
var suppliers = await new SupplierDirectory(supplierUrls, http).DiscoverAsync();
if (suppliers.Count == 0)
{
    ConsoleUi.Warn("Hiç tedarikçi bulunamadı. Önce tedarikçileri başlat: scripts\\faz2-baslat.ps1");
    return;
}

var collector = new QuoteCollector();

// Komut satırından parametre geldiyse tek sefer çalış, gelmediyse kullanıcıya sor.
if (args.Length >= 2)
{
    await RunPurchaseAsync(args[0], args[1]);
    return;
}

while (true)
{
    Console.WriteLine();
    Console.Write($"Malzeme kodu ({string.Join(" / ", Materials.All.Select(m => m.Code))}, çıkmak için boş bırak): ");
    var code = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(code)) break;

    Console.Write("Miktar: ");
    var qty = Console.ReadLine()?.Trim() ?? "";

    await RunPurchaseAsync(code, qty);
}

// =====================================================================
async Task RunPurchaseAsync(string materialCode, string quantityText)
{
    var material = Materials.FindByCode(materialCode);
    if (material is null || !decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
    {
        ConsoleUi.Warn("Geçersiz malzeme kodu veya miktar.");
        return;
    }

    // -----------------------------------------------------------------
    // 2) FİLTRE: kim bu malzemeyi satıyor? (Kartlardaki etiketlere göre)
    // -----------------------------------------------------------------
    ConsoleUi.Title($"2) Filtre: '{material.Code}' satan tedarikçiler");
    var candidates = suppliers.Where(s => s.Sells(material.Code)).ToList();
    foreach (var s in suppliers)
        Console.WriteLine($"  {(s.Sells(material.Code) ? "✓" : "–")} {s.Card.Name}");

    if (candidates.Count == 0)
    {
        ConsoleUi.Warn("Bu malzemeyi satan tedarikçi yok.");
        return;
    }

    // -----------------------------------------------------------------
    // 3) TEKLİF: hepsine aynı anda sor. Hepsi aynı contextId'yi taşır.
    // -----------------------------------------------------------------
    var contextId = $"satinalma-{DateTime.Now:yyyyMMdd-HHmmss}";
    ConsoleUi.Title($"3) Teklif isteniyor ({quantity} {material.Unit} {material.Name}, contextId={contextId})");
    var answers = await collector.CollectAsync(candidates, new QuoteRequest(material.Code, quantity), contextId);

    foreach (var a in answers)
    {
        if (a.Quote is { } q)
            Console.WriteLine($"  {a.Supplier.Card.Name,-34} birim {q.UnitPrice.ToString("N2", tr),10} TL   toplam {q.TotalPrice.ToString("N2", tr),12} TL   teslim {q.DeliveryDays} gün");
        else
            ConsoleUi.Warn($"  {a.Supplier.Card.Name,-34} teklif yok: {a.Text}");
    }

    // -----------------------------------------------------------------
    // 4) KARAR: Bu kısım A2A değil, saf iş mantığı.
    //    Faz 6'da bu kararı bir LLM'e verip farkı göreceğiz.
    // -----------------------------------------------------------------
    var best = answers
        .Where(a => a.Quote is not null)
        .OrderBy(a => a.Quote!.TotalPrice)
        .ThenBy(a => a.Quote!.DeliveryDays)
        .FirstOrDefault();

    ConsoleUi.Title("4) Karar");
    if (best is null)
    {
        ConsoleUi.Warn("Geçerli teklif alınamadı.");
        return;
    }

    ConsoleUi.Ok($"  Seçilen: {best.Quote!.Supplier}, toplam {best.Quote.TotalPrice.ToString("N2", tr)} TL, {best.Quote.DeliveryDays} gün");
    ConsoleUi.Info($"  Tedarikçinin kendi cümlesi: \"{best.Text}\"");
    ConsoleUi.Info("  (Sipariş verme Faz 3'te: o zaman tedarikçi bir Task açacak.)");
}
