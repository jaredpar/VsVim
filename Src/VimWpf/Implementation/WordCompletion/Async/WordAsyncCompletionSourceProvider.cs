#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022 || VS_SPECIFIC_MAC
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;

namespace Vim.UI.Wpf.Implementation.WordCompletion.Async
{
    [Name("Vim Async Completion Source")]
    [ContentType(VimConstants.AnyContentType)]
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Order(Before = "Roslyn Completion Source Provider")]
    internal sealed class WordAsyncCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        [ImportingConstructor]
        internal WordAsyncCompletionSourceProvider()
        {
        }

        IAsyncCompletionSource IAsyncCompletionSourceProvider.GetOrCreate(ITextView textView) =>
            new WordAsyncCompletionSource(textView);
    }   
}

#elif VS_SPECIFIC_2017
// Nothing to do
#else
#error Unsupported configuration
#endif
