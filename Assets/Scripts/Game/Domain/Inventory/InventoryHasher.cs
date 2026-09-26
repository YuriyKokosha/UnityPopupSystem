using System.Security.Cryptography;
using System.Text;

namespace PopupSystem.Game.Domain.Inventory
{
    /// <summary>The one place that defines the inventory hash: SHA-256 over the canonical string
    /// <c>revision;itemId:count;itemId:count;…</c> in StackId order, lower-case hex. The server must compute
    /// the same string the same way; see Docs/feature-maps/inventory.md for the contract and a test vector.</summary>
    public static class InventoryHasher
    {
        public static string Compute(InventorySnapshot snapshot)
        {
            return Compute(Canonicalize(snapshot));
        }

        public static string Canonicalize(InventorySnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.Append(snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));

            for (var i = 0; i < snapshot.Stacks.Count; i++)
            {
                var stack = snapshot.Stacks[i];
                builder.Append(';');
                builder.Append(stack.ItemId);
                builder.Append(':');
                builder.Append(stack.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string Compute(string canonical)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            var hex = new StringBuilder(bytes.Length * 2);

            for (var i = 0; i < bytes.Length; i++)
            {
                hex.Append(bytes[i].ToString("x2"));
            }

            return hex.ToString();
        }
    }
}
