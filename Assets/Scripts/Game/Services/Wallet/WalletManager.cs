using System;
using System.Collections.Generic;
using PopupSystem.Game.Domain.Wallet;

namespace PopupSystem.Game.Services.Wallet
{
    /// <summary>Local currency balances. Every change is validated as a whole before anything moves, and
    /// <see cref="BalancesChanged"/> is raised afterwards with each subscriber isolated, so a throwing observer
    /// cannot interrupt or fail the change that notified it.</summary>
    public sealed class WalletManager
    {
        /// <summary>The highest balance a currency can reach. A credit that would cross it is refused as a whole
        /// (<see cref="BalanceLimitExceededException"/>) rather than wrapping into a negative balance.</summary>
        public const int MaxBalance = int.MaxValue;

        private readonly Dictionary<string, int> _balances = new();
        private readonly Dictionary<string, long> _deltaBuffer = new();
        private readonly StateChangeNotifier _balancesChanged;

        public WalletManager()
        {
            _balancesChanged = new StateChangeNotifier(() => BalancesChanged);
        }

        public event Action BalancesChanged;

        public void SetWallet(WalletSnapshot snapshot)
        {
            _balances.Clear();

            if (snapshot?.Balances != null)
            {
                for (var i = 0; i < snapshot.Balances.Count; i++)
                {
                    var balance = snapshot.Balances[i];
                    _balances[balance.CurrencyId] = balance.Amount;
                }
            }

            _balancesChanged.Raise();
        }

        /// <summary>Credits every amount, or throws and changes nothing: an amount below zero is an
        /// <see cref="ArgumentOutOfRangeException"/> (a debit goes through <see cref="Spend"/>), a balance past
        /// <see cref="MaxBalance"/> a <see cref="BalanceLimitExceededException"/>.</summary>
        public void AddCurrencies(IReadOnlyList<CurrencyAmount> currencies)
        {
            Exchange(currencies, null);
        }

        public bool CanAfford(CurrencyAmount price)
        {
            return price == null || price.Amount <= 0 || GetBalance(price.CurrencyId) >= price.Amount;
        }

        /// <summary>Debits the price, or throws <see cref="InsufficientFundsException"/> and changes nothing.
        /// A null or zero price is a no-op and raises no event.</summary>
        public void Spend(CurrencyAmount price)
        {
            Exchange(null, price);
        }

        public int GetBalance(string currencyId)
        {
            return _balances.TryGetValue(currencyId, out var amount) ? amount : 0;
        }

        public IReadOnlyList<CurrencyAmount> GetSnapshot()
        {
            var balances = new List<CurrencyAmount>(_balances.Count);

            foreach (var pair in _balances)
            {
                balances.Add(new CurrencyAmount(pair.Key, pair.Value));
            }

            return balances;
        }

        /// <summary>Throws exactly what <see cref="Exchange"/> would, without changing anything.</summary>
        internal void EnsureCanExchange(IReadOnlyList<CurrencyAmount> credits, CurrencyAmount debit)
        {
            BuildDeltas(credits, debit);
        }

        /// <summary>Credits and a debit as one change: the net result per currency is checked first (no balance
        /// below zero, none above <see cref="MaxBalance"/>), then applied, then observed once.</summary>
        internal void Exchange(IReadOnlyList<CurrencyAmount> credits, CurrencyAmount debit)
        {
            BuildDeltas(credits, debit);

            if (_deltaBuffer.Count == 0)
            {
                return;
            }

            foreach (var pair in _deltaBuffer)
            {
                _balances[pair.Key] = (int)(GetBalance(pair.Key) + pair.Value);
            }

            _deltaBuffer.Clear();
            _balancesChanged.Raise();
        }

        /// <summary>Holds <see cref="BalancesChanged"/> back until the scope ends; see
        /// <c>RewardGrantService.Grant</c>.</summary>
        internal StateChangeNotifier.Scope DeferNotifications()
        {
            return _balancesChanged.Defer();
        }

        // Fills _deltaBuffer with the validated net change per currency (zero-sum currencies left out), or throws
        // with the buffer cleared. long, because the sum of two valid ints is exactly what must not wrap.
        private void BuildDeltas(IReadOnlyList<CurrencyAmount> credits, CurrencyAmount debit)
        {
            _deltaBuffer.Clear();

            try
            {
                if (credits != null)
                {
                    for (var i = 0; i < credits.Count; i++)
                    {
                        var credit = credits[i];
                        if (credit.Amount < 0)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(credits), credit.Amount, $"A credit of {credit.CurrencyId} cannot be negative.");
                        }

                        AddDelta(credit.CurrencyId, credit.Amount);
                    }
                }

                if (debit != null && debit.Amount > 0)
                {
                    AddDelta(debit.CurrencyId, -(long)debit.Amount);
                }

                foreach (var pair in _deltaBuffer)
                {
                    var balance = GetBalance(pair.Key);
                    var result = balance + pair.Value;

                    // Only a net debit can be refused for funds: a credit to a balance the server delivered below
                    // zero still applies.
                    if (pair.Value < 0 && result < 0)
                    {
                        throw new InsufficientFundsException(debit, balance);
                    }

                    if (result > MaxBalance)
                    {
                        throw new BalanceLimitExceededException(pair.Key, balance, pair.Value);
                    }
                }

                RemoveZeroDeltas();
            }
            catch
            {
                _deltaBuffer.Clear();
                throw;
            }
        }

        private void AddDelta(string currencyId, long amount)
        {
            _deltaBuffer.TryGetValue(currencyId, out var current);
            _deltaBuffer[currencyId] = current + amount;
        }

        private void RemoveZeroDeltas()
        {
            List<string> zero = null;

            foreach (var pair in _deltaBuffer)
            {
                if (pair.Value == 0)
                {
                    (zero ??= new List<string>()).Add(pair.Key);
                }
            }

            if (zero == null)
            {
                return;
            }

            for (var i = 0; i < zero.Count; i++)
            {
                _deltaBuffer.Remove(zero[i]);
            }
        }
    }
}
