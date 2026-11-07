using Factory.Shared;

namespace Supplier.Agent;

/// <summary>
/// Teklif hesaplama: tamamen iş mantığı, A2A ile hiçbir ilgisi yok.
/// Faz 1'deki SupplierAgent'ın hesaplama kısmının aynısı, sadece fiyatlar artık ayardan geliyor.
/// </summary>
public sealed class QuoteCalculator(SupplierProfile profile)
{
    public bool Sells(string materialCode) => profile.PriceList.ContainsKey(materialCode);

    public Quote Calculate(Material material, decimal quantity)
    {
        var listPrice = profile.PriceList[material.Code];
        var unitPrice = profile.BulkThreshold > 0 && quantity >= profile.BulkThreshold
            ? listPrice * (1 - profile.BulkDiscount)
            : listPrice;

        return new Quote(
            Supplier: profile.Name,
            MaterialCode: material.Code,
            Quantity: quantity,
            Unit: material.Unit,
            UnitPrice: Math.Round(unitPrice, 2),
            TotalPrice: Math.Round(unitPrice * quantity, 2),
            Currency: "TRY",
            DeliveryDays: profile.DeliveryDays);
    }
}
