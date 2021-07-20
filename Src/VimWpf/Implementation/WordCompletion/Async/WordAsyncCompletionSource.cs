#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022 || VS_SPECIFIC_MAC
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vim.UI.Wpf.Implementation.WordCompletion.Async
{
    internal sealed class WordAsyncCompletionSource : IAsyncCompletionSource
    {
        internal ITextView TextView { get; }

        internal WordAsyncCompletionSource(ITextView textView)
        {
            TextView = textView;
        }

        internal Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            CompletionContext context;
            if (TextView.TryGetWordCompletionData(out var wordCompletionData))
            {
                var itemsRaw = wordCompletionData.WordCollection.Select(x => new CompletionItem(x, this)).ToArray();
                var items = ImmutableArray.Create<CompletionItem>(itemsRaw);
                context = new CompletionContext(items);
            }
            else
            {
                context = CompletionContext.Empty;
            }

            return Task.FromResult(context);
        }

        internal CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            if (TextView.TryGetWordCompletionData(out var wordCompletionData))
            {
                return new CompletionStartData(CompletionParticipation.ExclusivelyProvidesItems, wordCompletionData.WordSpan);
            }

            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        #region IAsyncCompletionSource

        Task<CompletionContext> IAsyncCompletionSource.GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token) =>
            GetCompletionContextAsync(session, trigger, triggerLocation, applicableToSpan, token);

        Task<object> IAsyncCompletionSource.GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>(item.DisplayText);

        CompletionStartData IAsyncCompletionSource.InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token) => InitializeCompletion(trigger, triggerLocation, token);

        #endregion
}
}

#elif VS_SPECIFIC_2017
// Nothing to do
#else
#error Unsupported configuration
#endif
