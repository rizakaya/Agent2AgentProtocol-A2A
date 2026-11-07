using System.Globalization;

namespace Factory.Shared;

/// <summary>
/// Fabrikada kullanılan bir hammadde.
/// Code: ajanların birbirine gönderdiği makine dostu kimlik (ör. "celik-boru").
/// Name: insanlar için okunabilir ad.
/// Unit: miktarın birimi (m, adet, L).
/// </summary>
public sealed record Material(string Code, string Name, string Unit);

/// <summary>
/// Senaryodaki hammaddelerin listesi. Tüm ajanlar aynı kodları kullanır,
/// böylece "çelik boru" her yerde "celik-boru" olarak anlaşılır.
/// </summary>
public static class Materials
{
    public static readonly Material SteelPipe = new("celik-boru", "Çelik boru", "m");
    public static readonly Material Screw = new("vida", "Vida", "adet");
    public static readonly Material Paint = new("boya", "Boya", "L");

    public static IReadOnlyList<Material> All { get; } = [SteelPipe, Screw, Paint];

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Koda göre hammadde bulur ("celik-boru" → Çelik boru).</summary>
    public static Material? FindByCode(string? code) =>
        All.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Serbest metin içinde geçen hammaddeyi bulur.
    /// Örnek: "500 m çelik boru lazım" → Çelik boru.
    /// Türkçe büyük/küçük harf kuralları (I/ı, İ/i) için tr-TR kültürü kullanılır.
    /// </summary>
    public static Material? FindInText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLower(Tr);
        return All.FirstOrDefault(m =>
            lower.Contains(m.Name.ToLower(Tr)) || lower.Contains(m.Code));
    }
}
