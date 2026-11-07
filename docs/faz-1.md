# Faz 1: Protokolü çıplak gözle görmek

Bu fazda hiçbir A2A kütüphanesi kullanmadan, düz ASP.NET Core ile bir A2A sunucusu yazdık.
Amaç: Faz 2'de SDK'ya geçtiğinde, SDK'nın arka planda tam olarak ne yaptığını bilmen.

Sonuç: Elle yazdığımız bu sunucuyla **resmi A2A .NET SDK istemcisi sorunsuz konuşabiliyor**.
Yani yazdığımız şey "A2A'ya benzer" bir şey değil, gerçekten A2A.

---

## 1. Ne yaptık?

`RawSupplier` adında, çelik boru satan bir tedarikçi ajanı. İki adresi var:

| Adres | Ne işe yarar |
| --- | --- |
| `GET /.well-known/agent-card.json` | **Keşif.** Ajanın kimlik kartı. |
| `POST /a2a` | **JSON-RPC.** Tüm A2A metodları buraya gelir. |

Çalıştırmak için:

```bash
dotnet run --project src/RawSupplier
```

Sunucu `http://localhost:5100` adresinde açılır. Sonra `src/RawSupplier/RawSupplier.http` dosyasındaki istekleri tek tek gönder.

## 2. Kodu okuma sırası

1. `src/RawSupplier/Program.cs`: İsteğin karşılandığı yer. `HandleJsonRpc` fonksiyonu bir isteği 4 adımda işler.
2. `src/RawSupplier/Protocol/JsonRpc.cs`: JSON-RPC zarfı ve hata kodları.
3. `src/RawSupplier/Protocol/A2AModels.cs`: `AgentCard`, `Message`, `Part` sınıfları.
4. `src/RawSupplier/SupplierAgent.cs`: Tedarikçinin iş mantığı (teklif hesaplama). Burada HTTP veya JSON-RPC'ye dair hiçbir şey olmadığına dikkat et.
5. `src/Factory.Shared/`: Tüm ajanların ortak kullanacağı `Material`, `QuoteRequest`, `Quote` tipleri.

## 3. Bir isteğin anatomisi

### İstek

```http
POST http://localhost:5100/a2a
Content-Type: application/json
A2A-Version: 1.0

{
  "jsonrpc": "2.0",                  ← JSON-RPC sürümü, her zaman "2.0"
  "id": 1,                           ← istemcinin verdiği numara, cevapta aynen döner
  "method": "SendMessage",           ← hangi A2A metodu çağrılıyor
  "params": {
    "message": {
      "messageId": "msg-001",        ← mesajın benzersiz kimliği (gönderen üretir)
      "role": "ROLE_USER",           ← isteği gönderen taraf
      "parts": [
        { "text": "500 m çelik boru için fiyat teklifi istiyorum" }
      ]
    }
  }
}
```

### Cevap

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "message": {
      "role": "ROLE_AGENT",
      "messageId": "c92f40f2...",
      "contextId": "64a695d1...",
      "parts": [
        { "text": "500 m çelik boru için teklifimiz: birim fiyat 80,75 TL, toplam 40.375,00 TL, teslim süresi 3 gün." },
        {
          "data": {
            "supplier": "Çelik A.Ş. (elle yazılmış sürüm)",
            "materialCode": "celik-boru",
            "quantity": 500,
            "unit": "m",
            "unitPrice": 80.75,
            "totalPrice": 40375.00,
            "currency": "TRY",
            "deliveryDays": 3
          },
          "mediaType": "application/json"
        }
      ]
    }
  }
}
```

### Dikkat edilecek noktalar

- **Tek adres, çok metod.** REST'te her iş ayrı bir URL'dir (`POST /quotes`, `GET /tasks/5`). A2A'nın JSON-RPC binding'inde her şey `POST /a2a`'ya gider. Ne yapılacağını `method` alanı söyler.
- **`result` içinde ya `message` ya `task` olur.** Bu tedarikçi anında cevap verdiği için `message` dönüyor. Faz 3'te sipariş uzun süreceği için `task` döneceğiz.
- **Bir mesajda birden fazla part.** Cevapta aynı bilginin iki hali var: insan (veya LLM) için `text`, program için `data`. Satınalma Ajanı Faz 2'de `data` part'ı okuyacak.
- **`contextId`.** İstemci göndermezse sunucu yeni bir tane üretir. İstemci gönderirse (ör. `"satinalma-2026-001"`) aynısı geri döner. Böylece aynı satınalma sürecine ait mesajlar birbirine bağlanır.
- **`A2A-Version` header'ı.** İstemci hangi A2A sürümünü konuştuğunu söyler. Header yoksa spesifikasyona göre `0.3` sayılır. Bizim sunucu sadece `1.0` konuştuğu için header'sız isteği reddediyor (`src/RawSupplier/RawSupplier.http` içindeki 6. istek).

## 4. Hatalar

JSON-RPC'de **HTTP durum kodu her zaman 200'dür.** Hata, gövdedeki `error` alanında gelir:

```json
{ "jsonrpc": "2.0", "id": 5, "error": { "code": -32601, "message": "'GetTask' metodu bulunamadı" } }
```

| Kod | Anlamı | Ne zaman |
| --- | --- | --- |
| -32700 | Parse error | Gövde geçerli JSON değil. `id` okunamadığı için `null` döner. |
| -32600 | Invalid request | JSON geçerli ama `jsonrpc`/`method` alanları eksik. |
| -32601 | Method not found | Bilinmeyen metod. |
| -32602 | Invalid params | `params.message` eksik veya bozuk. |
| -32005 | Content type not supported | İstemcinin kabul ettiği türleri üretemiyoruz. (A2A'ya özel) |
| -32009 | Version not supported | `A2A-Version` desteklenmiyor. (A2A'ya özel) |

İlk dördü JSON-RPC 2.0 standardından gelir. -32001 ile -32009 arası A2A'nın kendi kodlarıdır. Örneğin -32001 "Task bulunamadı" demektir, Faz 3'te göreceğiz.

**Önemli ayrım:** "Vida satmıyoruz" cevabı bir hata değildir. İstek geçerli, ajan sadece işi yapamayacağını normal bir mesajla söylüyor. Protokol hatası ile iş cevabını karıştırma.

## 5. Resmi SDK ile uyumluluk testi

Bu sunucuyu resmi `A2A` NuGet paketinin istemcisiyle de test ettik:

```csharp
var card = await new A2ACardResolver(new Uri("http://localhost:5100/")).GetAgentCardAsync();
var client = new A2AClient(new Uri(card.SupportedInterfaces[0].Url));
var response = await client.SendMessageAsync(new SendMessageRequest
{
    Message = new Message { MessageId = "x1", Role = Role.User, Parts = [Part.FromText("250 m çelik boru")] }
});
// response.PayloadCase == Message
// response.Message.Parts[0].Text → "250 m çelik boru için teklifimiz: ..."
// response.Message.Parts[1].Data → { "supplier": ..., "unitPrice": 85.00, ... }
```

SDK'nın gönderdiği ham istek, sunucu loglarında şöyle görünüyordu:

```json
{"jsonrpc":"2.0","id":"4bc2de55-...","method":"SendMessage","params":{"message":{"role":"ROLE_USER","parts":[{"text":"250 m çelik boru"}],"messageId":"x1"}}}
```

Yani `.http` dosyasında elle yazdığın istekle birebir aynı. SDK'nın yaptığı iş bu JSON'u senin yerine üretmek ve okumak.

## 6. Faz 2'de ne değişecek?

| Faz 1'de elle yaptığımız | Faz 2'de SDK'nın karşılığı |
| --- | --- |
| `Protocol/A2AModels.cs` (AgentCard, Message, Part) | `A2A` paketindeki hazır tipler |
| `Protocol/JsonRpc.cs` ve `HandleJsonRpc` | `app.MapA2A("/a2a")` tek satır |
| `MapGet("/.well-known/agent-card.json")` | `app.MapWellKnownAgentCard(card)` |
| Sürüm kontrolü, hata kodları | SDK otomatik yapar |
| `SupplierAgent.Handle` | `IAgentHandler.ExecuteAsync` içine taşınır, iş mantığı aynı kalır |

## 7. Kendin deneyebileceklerin

- `SupplierAgent.cs` içindeki `PriceList`'e vida ekle, 4. isteği tekrar gönder.
- Agent Card'daki `url`'yi yanlış bir porta çevir ve SDK istemcisinin ne yaptığını düşün: istemci kartta yazan adrese gider, yani kart ile gerçek adres tutarlı olmak zorunda.
- `.http` dosyasında `role` değerini `"user"` yap. Ne olur? (İpucu: v0.3'te `"user"` idi, v1.0'da `"ROLE_USER"` oldu. Bizim sunucu bunu kontrol etmiyor ama resmi SDK sunucusu -32602 döner.)
