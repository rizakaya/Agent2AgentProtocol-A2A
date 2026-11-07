# A2A Fabrika: Agent2Agent protokolünü .NET ile öğrenmek

Bir metal sandalye fabrikasında, hammadde stoku minimum seviyenin altına düştüğünde
satınalma ajanının tedarikçi ajanlarıyla **A2A protokolü** üzerinden anlaşmasını
adım adım kuran bir öğrenme projesi.

## Gereksinimler

- .NET 10 SDK (`dotnet --version` ile kontrol et)
- İstekleri göndermek için: Visual Studio 2022 17.13+ / Visual Studio 2026, Rider veya VS Code + REST Client eklentisi (`.http` dosyaları için)

> .NET 8 kullanmak istersen her `.csproj` dosyasındaki `net10.0` değerini `net8.0` yapman yeterli.

## Klasör yapısı

```
A2AFactory.slnx
docs/            → her fazın Türkçe notu (önce bunları oku)
src/
  Factory.Shared/  → ortak tipler: Material, QuoteRequest, Quote
  RawSupplier/     → Faz 1: SDK'sız, elle yazılmış A2A sunucusu (port 5100)
    RawSupplier.http → sunucuya istek gönderme dosyası
```

## Fazlar

| Faz | Konu | Durum |
| --- | --- | --- |
| 1 | Protokolü çıplak gözle gör (SDK'sız) | ✅ [docs/faz-1.md](docs/faz-1.md) |
| 2 | SDK ile keşif ve teklif | ⏳ |
| 3 | Sipariş Task'ı, streaming ve artifact | ⏳ |
| 4 | Pazarlık (input-required) | ⏳ |
| 5 | Stok ve Üretim zinciri | ⏳ |
| 6 | (İsteğe bağlı) LLM ile karar veren satınalma | ⏳ |

## Hızlı başlangıç (Faz 1)

```bash
dotnet run --project src/RawSupplier
```

Tarayıcıda `http://localhost:5100/.well-known/agent-card.json` adresini aç,
sonra `src/RawSupplier/RawSupplier.http` dosyasındaki istekleri gönder.
