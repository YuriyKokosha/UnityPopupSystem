using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using UnityEngine;

namespace PopupSystem.Game.Services.Inventory
{
    /// <summary>JSON file per player under <c>&lt;root&gt;/inventory/</c>. Writes go to a temp file and are
    /// swapped in, so a crash mid-write leaves the previous file intact rather than a truncated one. The root is
    /// injected so tests write into a scratch directory and never touch a developer's real save.
    /// See Docs/knowledge-base/persistence.md.</summary>
    public sealed class FileInventoryStorage : IInventoryStorage
    {
        public const int SchemaVersion = 1;

        private readonly string _directory;

        public FileInventoryStorage(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory))
            {
                throw new ArgumentException("A root directory is required.", nameof(rootDirectory));
            }

            _directory = Path.Combine(rootDirectory, "inventory");
        }

        public string GetPath(string playerId)
        {
            return Path.Combine(_directory, SanitizeFileName(playerId) + ".json");
        }

        public async UniTask<InventorySnapshot> LoadAsync(string playerId, CancellationToken cancellationToken)
        {
            var path = GetPath(playerId);

            var json = await UniTask.RunOnThreadPool(
                () => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null,
                cancellationToken: cancellationToken);

            if (json == null)
            {
                return null;
            }

            var dto = JsonUtility.FromJson<InventoryFileDto>(json);

            if (dto == null || dto.schemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Inventory file '{path}' has schema {dto?.schemaVersion.ToString() ?? "null"}, expected {SchemaVersion}.");
            }

            var stacks = new ItemStack[dto.stacks?.Length ?? 0];
            for (var i = 0; i < stacks.Length; i++)
            {
                var stack = dto.stacks[i];
                stacks[i] = new ItemStack(stack.stackId, stack.itemId, stack.count);
            }

            var snapshot = new InventorySnapshot(stacks, dto.revision, dto.nextStackId);

            if (!string.IsNullOrEmpty(dto.hash) && dto.hash != snapshot.Hash)
            {
                throw new InvalidDataException($"Inventory file '{path}' does not match its own hash.");
            }

            return snapshot;
        }

        public async UniTask SaveAsync(string playerId, InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var path = GetPath(playerId);
            var json = JsonUtility.ToJson(InventoryFileDto.From(snapshot), prettyPrint: true);

            await UniTask.RunOnThreadPool(() => WriteAtomically(path, json), cancellationToken: cancellationToken);
        }

        public async UniTask ClearAsync(string playerId, CancellationToken cancellationToken)
        {
            var path = GetPath(playerId);

            await UniTask.RunOnThreadPool(() =>
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }, cancellationToken: cancellationToken);
        }

        private static void WriteAtomically(string path, string contents)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, contents, Encoding.UTF8);

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        private static string SanitizeFileName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentException("A player id is required.", nameof(playerId));
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(playerId.Length);

            foreach (var c in playerId)
            {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class InventoryFileDto
        {
            public int schemaVersion;
            public long revision;
            public long nextStackId;
            public string hash;
            public StackDto[] stacks;

            public static InventoryFileDto From(InventorySnapshot snapshot)
            {
                var stacks = new StackDto[snapshot.Stacks.Count];
                for (var i = 0; i < stacks.Length; i++)
                {
                    var stack = snapshot.Stacks[i];
                    stacks[i] = new StackDto { stackId = stack.StackId, itemId = stack.ItemId, count = stack.Count };
                }

                return new InventoryFileDto
                {
                    schemaVersion = SchemaVersion,
                    revision = snapshot.Revision,
                    nextStackId = snapshot.NextStackId,
                    hash = snapshot.Hash,
                    stacks = stacks,
                };
            }
        }

        [Serializable]
        private sealed class StackDto
        {
            public long stackId;
            public string itemId;
            public int count;
        }
    }
}
