using System;
using Cysharp.Threading.Tasks;

namespace PopupSystem.Extensions
{
    public static class UniTaskShareExtensions
    {
        /// <summary>Republishes an in-flight UniTask so several consumers can await it concurrently.
        /// Preserve() cannot: while still pending it forwards to a single-continuation source, and a second
        /// concurrent await throws.</summary>
        public static UniTask<T> Share<T>(this UniTask<T> task)
        {
            var completionSource = new UniTaskCompletionSource<T>();
            ForwardAsync(task, completionSource).Forget();
            return completionSource.Task;
        }

        private static async UniTaskVoid ForwardAsync<T>(UniTask<T> task, UniTaskCompletionSource<T> completionSource)
        {
            try
            {
                var result = await task;
                completionSource.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        }
    }
}
