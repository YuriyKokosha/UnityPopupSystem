using System;
using Cysharp.Threading.Tasks;

namespace PopupSystem.Extensions
{
    /// <summary>
    /// UniTask.Preserve() only supports being safely re-awaited AFTER it has already completed -
    /// the cached result/exception is then replayed synchronously to whoever awaits next. While
    /// the task is still pending, Preserve()'s MemoizeSource just forwards each OnCompleted call
    /// straight through to the single-continuation-slot source underneath, so a second CONCURRENT
    /// await (from a different in-flight consumer, before the first one completes) throws
    /// "Already continuation registered, can not await twice".
    ///
    /// Share() fixes this for the case that actually needs it: more than one place awaiting the
    /// same in-flight operation at the same time (e.g. a popup driving its own loading state from
    /// a claim/purchase, while the code that opened the popup is also awaiting the outcome to
    /// restore its own button). It awaits the source task exactly once internally and republishes
    /// the outcome through a UniTaskCompletionSource, whose OnCompleted genuinely supports any
    /// number of concurrently registered continuations - unlike Preserve(), it does not just
    /// forward to a single-slot source.
    ///
    /// Lives under Extensions (not UI) because it is a general-purpose UniTask utility with no
    /// dependency on anything UI-specific - any layer of the project (Game, App, UI) is free to
    /// use it.
    /// </summary>
    public static class UniTaskShareExtensions
    {
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
