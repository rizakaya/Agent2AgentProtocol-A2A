# Faz 2: SDK ile keşif ve teklif

Bu fazda iki şey yaptık:

1. Tedarikçiyi resmi A2A .NET SDK'sı ile yeniden yazdık (`Supplier.Agent`). Aynı kod iki farklı şirket olarak çalışıyor.
2. İlk **A2A istemcimizi** yazdık (`Purchasing.Agent`). Tedarikçileri kartlarından keşfediyor, teklif istiyor ve en uygununu seçiyor.

Önemli sonuç: Satınalma Ajanı, SDK ile yazılmış iki tedarikçiyle ve Faz 1'de **elle yazdığımız** RawSupplier ile aynı kodla konuşuyor. Hangi teknolojiyle yazıldıkları onu ilgilendirmiyor. A2A'nın asıl vaadi bu.

---

## 1. Çalıştırma

**1. Tedarikçileri başlat** (üç ayrı pencere açılır):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\faz2-baslat.ps1
```

| Pencere | Adres | Ne satıyor |
| --- | --- | --- |
| Çelik A.Ş. | http://localhost:5101 | Çelik boru: 85 TL/m, 500 m ve üzeri %5 indirim, 3 gün |
| Bağlantı Ltd. | http://localhost:5102 | Çelik boru 82 TL/m, vida 0,40 TL, boya 210 TL/L, indirim yok, 7 gün |
| RawSupplier (Faz 1) | http://localhost:5100 | Çelik boru: Faz 1'deki elle yazılmış sunucu |

**2. Satınalma Ajanını çalıştır:**

```powershell
dotnet run --project src\Purchasing.Agent                    # etkileşimli
dotnet run --project src\Purchasing.Agent -- celik-boru 500   # tek seferlik
```

> Aynı projeyi (Supplier.Agent) iki farklı profille aynı anda başlatmanın en kolay yolu bu betik. Satınalma Ajanını istersen Visual Studio'dan (F5) çalıştırabilirsin.

## 2. Örnek çıktı

```
=== 1) Keşif: tedarikçilerin Agent Card'ları okunuyor ===
  ✓ Çelik A.Ş.                         http://localhost:5101/a2a    satıyor: celik-boru
  ✓ Bağlantı Ltd.                      http://localhost:5102/a2a    satıyor: boya, celik-boru, vida
  ✓ Çelik A.Ş. (elle yazılmış sürüm)   http://localhost:5100/a2a    satıyor: celik-boru

=== 3) Teklif isteniyor (500 m Çelik boru, contextId=satinalma-20261107-095148) ===
  Çelik A.Ş.                         birim      80,75 TL   toplam    40.375,00 TL   teslim 3 gün
  Bağlantı Ltd.                      birim      82,00 TL   toplam    41.000,00 TL   teslim 7 gün
  Çelik A.Ş. (elle yazılmış sürüm)   birim      80,75 TL   toplam    40.375,00 TL   teslim 3 gün

=== 4) Karar ===
  Seçilen: Çelik A.Ş., toplam 40.375,00 TL, 3 gün
```

**300 m** ile dene: bu sefer indirim devreye girmez ve Bağlantı Ltd. (24.600 TL) kazanır. **vida** iste: sadece Bağlantı Ltd. sorulur, çünkü diğerlerinin kartında vida yok.

## 3. Kodu okuma sırası

**Sunucu (Supplier.Agent)**

1. `Program.cs`: Faz 1'deki `RawSupplier/Program.cs` ile yan yana aç. Faz 1'deki yaklaşık 100 satırlık JSON-RPC işinin yerini 2 satır (`MapA2A` ve `MapWellKnownAgentCard`) aldı.
2. `SupplierAgentHandler.cs`: Ajanın beyni. `IAgentHandler.ExecuteAsync`, SDK'nın her mesajda çağırdığı tek metod.
3. `appsettings.CelikAS.json` ve `appsettings.BaglantiLtd.json`: İki şirketi birbirinden ayıran tek şey bu dosyalar.

**İstemci (Purchasing.Agent)**

1. `Program.cs`: 4 adımlı akış (keşif, filtre, teklif, karar).
2. `SupplierDirectory.cs`: Keşif. `A2ACardResolver` ile kartı okur, karttan JSON-RPC adresini bulur, `A2AClient` oluşturur.
3. `QuoteCollector.cs`: `SendMessageAsync` ile teklif ister, cevaptaki data part'ı `Quote` olarak okur.

## 4. Faz 1 ile Faz 2'nin karşılaştırması

| İş | Faz 1 (elle) | Faz 2 (SDK) |
| --- | --- | --- |
| Agent Card yayınlamak | `MapGet("/.well-known/agent-card.json", ...)` | `app.MapWellKnownAgentCard(card)` |
| JSON-RPC endpoint | `MapPost("/a2a", ...)` + `HandleJsonRpc` (≈100 satır) | `app.MapA2A("/a2a")` |
| Model sınıfları | `Protocol/A2AModels.cs` | `A2A` paketinden: `AgentCard`, `Message`, `Part` |
| Cevap mesajı | `new SendMessageResult { Message = ... }` | `new MessageResponder(...).ReplyAsync(...)` |
| Hata kodları | Elle, `ErrorCodes` sınıfı | SDK otomatik döner |
| `GetTask`, `CancelTask` | Yok (-32601) | SDK'da hazır (Faz 3'te kullanacağız) |
| İş mantığı (teklif hesabı) | `SupplierAgent.cs` | `QuoteCalculator.cs`: **değişmedi** |

Son satır önemli: protokolü değiştirdik ama iş mantığına dokunmadık. Ajanın "ne yaptığı" ile "nasıl konuştuğu" birbirinden ayrı.

## 5. Keşif nasıl çalışıyor?

Satınalma Ajanı tedarikçiler hakkında sadece şunu biliyor (`src/Purchasing.Agent/appsettings.json`):

```json
{ "Suppliers": [ "http://localhost:5101", "http://localhost:5102", "http://localhost:5100" ] }
```

Gerisini kartlardan öğreniyor:

| Sorusu | Karttaki cevap |
| --- | --- |
| Bu kim? | `name`, `provider.organization` |
| İsteği nereye göndereyim? | `supportedInterfaces[].url` (kök adresten farklı: `/a2a`) |
| Hangi protokolle? | `supportedInterfaces[].protocolBinding` = `JSONRPC` |
| Ne satıyor? | `skills[].tags` (`["teklif", "celik-boru"]`) |

Bağlantı Ltd. yarın "kaynak teli" satmaya başlasa, Satınalma Ajanı'nın kodunu değiştirmeye gerek yok. Tedarikçi kartına yeni etiketi ekler, Satınalma bir sonraki keşifte öğrenir.

> Gerçek dünyada adres listesi bir **ajan kayıt defterinden** (registry) gelebilir. A2A bunu zorunlu tutmaz, kartın formatını standartlaştırır.

## 6. contextId'nin rolü

Satınalma, aynı satınalma sürecindeki tüm tedarikçilere aynı `contextId`'yi gönderiyor (ör. `satinalma-20261107-095148`). Tedarikçi loglarında bu id'yi göreceksin. Şu an sadece bir etiket. Faz 3 ve 4'te teklifin ardından gelen sipariş ve pazarlık mesajları aynı `contextId` ile bağlanacak.

## 7. Dikkat çekici bir fark: A2A-Version header'ı

- Faz 1'deki sunucumuz header'sız isteği reddediyordu, çünkü spesifikasyona göre header yoksa istemci `0.3` sayılır ve biz sadece `1.0` konuşuyorduk.
- SDK sunucusu header'sız isteği **kabul ediyor**, çünkü hem `0.3` hem `1.0` destekliyor. `A2A-Version: 9.9` gönderirsen -32009 dönüyor.

Bunu `src/Supplier.Agent/Supplier.Agent.http` içindeki 9. istekle deneyebilirsin.

## 8. Hâlâ "API gibi" olan ne?

Dürüst olalım: tedarikçi hâlâ anında, tek mesajla cevap veriyor. Bu yüzden Faz 2 hâlâ "standart bir API" hissi veriyor. Farkı yaratacak şeyler sıradaki fazlarda:

- **Faz 3:** Sipariş anında bitmeyecek. Tedarikçi bir **Task** açacak, durumu zamanla değişecek (`submitted → working → completed`), Satınalma bunu canlı izleyecek, sonunda irsaliye bir **artifact** olarak gelecek.
- **Faz 4:** Tedarikçi stokta yeterli mal yoksa **geri soracak** ("300 m verebilirim, olur mu?").

## 9. Kendin deneyebileceklerin

- `appsettings.BaglantiLtd.json` içindeki çelik boru fiyatını 79 yap. 500 m'de kim kazanır?
- `Purchasing.Agent/appsettings.json`'a olmayan bir adres ekle (ör. `http://localhost:5199`). Keşif ne yapıyor?
- Bir tedarikçinin penceresini kapat ve Satınalma'yı tekrar çalıştır.
- Üçüncü bir tedarikçi ekle: `appsettings.YeniTedarikci.json` + `launchSettings.json`'a yeni profil + Satınalma'nın adres listesine yeni port. Satınalma'nın kodunda tek satır değişmeyecek.
