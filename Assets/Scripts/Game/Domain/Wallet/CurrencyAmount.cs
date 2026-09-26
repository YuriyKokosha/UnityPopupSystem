namespace PopupSystem.Game.Domain.Wallet
{
    /// <summary>A single currency balance or delta: soft/hard currency, energy. Currencies live in the wallet,
    /// never in inventory slots.</summary>
    public sealed class CurrencyAmount
    {
        public string CurrencyId { get; }
        public int Amount { get; }

        public CurrencyAmount(string currencyId, int amount)
        {
            CurrencyId = currencyId;
            Amount = amount;
        }
    }
}
