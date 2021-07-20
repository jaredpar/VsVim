#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022 || VS_SPECIFIC_MAC
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Vim;
using Vim.Extensions;

namespace Vim.UI.Wpf.Implementation.WordCompletion.Async
{
    /// <summary>
    /// This type is responsible for providing word completion sessions over a given ITextView
    /// instance and given set of words.
    /// 
    /// Properly integrating with the IntelliSense stack here is a bit tricky.  In order to participate
    /// in any completion session you must provide an ICompletionSource for the lifetime of the 
    /// ITextView.  Ideally we don't want to provide any completion information unless we are actually
    /// starting a word completion session
    /// </summary>
    internal sealed class WordAsyncCompletionSessionFactory
    {
        private readonly IAsyncCompletionBroker _asyncCompletionBroker;
#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022
        private readonly IVsEditorAdaptersFactoryService _vsEditorAdaptersFactoryService;
        internal WordAsyncCompletionSessionFactory(
            IAsyncCompletionBroker asyncCompletionBroker,
            IVsEditorAdaptersFactoryService vsEditorAdaptersFactoryService = null)
        {
            _asyncCompletionBroker = asyncCompletionBroker;
            _vsEditorAdaptersFactoryService = vsEditorAdaptersFactoryService;
        }
#elif VS_SPECIFIC_MAC
        internal WordAsyncCompletionSessionFactory(
            IAsyncCompletionBroker asyncCompletionBroker)
        {
            _asyncCompletionBroker = asyncCompletionBroker;
        }
#endif
        internal FSharpOption<IWordCompletionSession> CreateWordCompletionSession(ITextView textView, SnapshotSpan wordSpan, IEnumerable<string> wordCollection, bool isForward)
        {
            // Dismiss any active ICompletionSession instances.  It's possible and possibly common for 
            // normal intellisense to be active when the user invokes word completion.  We want only word
            // completion displayed at this point 
            _asyncCompletionBroker.GetSession(textView)?.Dismiss();

            // Store the WordCompletionData inside the ITextView. The IAsyncCompletionSource implementation will 
            // asked to provide data for the creation of the IAsyncCompletionSession. Hence we must share through
            // ITextView
            var wordCompletionData = new VimWordCompletionData(
                wordSpan,
                new ReadOnlyCollection<string>(wordCollection.ToList()));
            textView.SetWordCompletionData(wordCompletionData);

            // Create a completion session at the start of the word.  The actual session information will 
            // take care of mapping it to a specific span
            var completionTrigger = new CompletionTrigger(CompletionTriggerReason.Insertion, wordSpan.Snapshot);
            var asyncCompletionSession = _asyncCompletionBroker.TriggerCompletion(
                textView,
                completionTrigger,
                wordSpan.Start,
                CancellationToken.None);

            // It's possible for the Start method to dismiss the ICompletionSession.  This happens when there
            // is an initialization error such as being unable to find a CompletionSet.  If this occurs we
            // just return the equivalent IWordCompletionSession (one which is dismissed)
            if (asyncCompletionSession is null || asyncCompletionSession.IsDismissed)
            {
                return FSharpOption<IWordCompletionSession>.None;
            }

            asyncCompletionSession.OpenOrUpdate(completionTrigger, wordSpan.Start, CancellationToken.None);
#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022
            return new WordAsyncCompletionSession(asyncCompletionSession, _vsEditorAdaptersFactoryService);
#elif VS_SPECIFIC_MAC
            return new WordAsyncCompletionSession(asyncCompletionSession);
#endif
        }
    }
}

#elif VS_SPECIFIC_2017
// Nothing to do
#else
#error Unsupported configuration
#endif
