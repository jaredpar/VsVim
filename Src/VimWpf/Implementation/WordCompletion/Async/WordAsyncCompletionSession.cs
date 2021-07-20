#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022 || VS_SPECIFIC_MAC
using System;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Vim;
using System.Threading;
using System.Windows.Threading;
#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Editor;
#endif
using System.Reflection;

namespace Vim.UI.Wpf.Implementation.WordCompletion.Async
{
    /// <summary>
    /// Implementation of the IWordCompletionSession interface.  
    /// to provide the friendly interface the core Vim engine is expecting
    /// </summary>
    internal sealed class WordAsyncCompletionSession : IWordCompletionSession
    {
        private readonly ITextView _textView;
        private readonly IAsyncCompletionSession _asyncCompletionSession;
        private bool _isDismissed;
        private event EventHandler _dismissed;
        private readonly DispatcherTimer _tipTimer;
#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022
        private readonly IVsTextView _vsTextView;

        internal WordAsyncCompletionSession(IAsyncCompletionSession asyncCompletionSession, IVsEditorAdaptersFactoryService vsEditorAdaptersFactoryService = null)
        {
            _textView = asyncCompletionSession.TextView;
            _asyncCompletionSession = asyncCompletionSession;
            _asyncCompletionSession.Dismissed += delegate { OnDismissed(); };
            _vsTextView = vsEditorAdaptersFactoryService?.GetViewAdapter(_textView);
            if (_vsTextView is object)
            {
                _tipTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, callback: ResetTipOpacity, Dispatcher.CurrentDispatcher);
                _tipTimer.Start();
            }
        }
#endif
#if VS_SPECIFIC_MAC
        internal WordAsyncCompletionSession(IAsyncCompletionSession asyncCompletionSession)
        {
            _textView = asyncCompletionSession.TextView;
            _asyncCompletionSession = asyncCompletionSession;
            _asyncCompletionSession.Dismissed += delegate { OnDismissed(); };
        }
#endif
        /// <summary>
        /// Called when the session is dismissed.  Need to alert any consumers that we have been
        /// dismissed 
        /// </summary>
        private void OnDismissed()
        {
            _isDismissed = true;
            _textView.ClearWordCompletionData();
            _dismissed?.Invoke(this, EventArgs.Empty);
            _tipTimer?.Stop();
        }

        private bool WithOperations(Action<IAsyncCompletionSessionOperations> action)
        {
            if (_asyncCompletionSession is IAsyncCompletionSessionOperations operations)
            {
                action(operations);
                return true;
            }

            return false;
        }

        private bool MoveDown() => WithOperations(operations => operations.SelectDown());
        private bool MoveUp() => WithOperations(operations => operations.SelectUp());
        private void Commit() => _asyncCompletionSession.Commit(KeyInputUtil.EnterKey.Char, CancellationToken.None);

        /// <summary>
        /// The async completion presenter will fade out the completion menu when the control key is clicked. That 
        /// is unfortunate for VsVim as control is held down for the duration of a completion session. This ... method
        /// is used to reset the opacity to 1.0 
        /// </summary>
        private void ResetTipOpacity(object sender, EventArgs e)
        {
            try
            {
#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022

                var methodInfo = _vsTextView.GetType().BaseType.GetMethod(
                    "SetTipOpacity",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    Type.DefaultBinder,
                    types: new[] { typeof(double) },
                    modifiers: null);
                if (methodInfo is object)
                {
                    methodInfo.Invoke(_vsTextView, new object[] { (double)1.0 });
                }
#endif
            }
            catch (Exception ex)
            {
                VimTrace.TraceDebug($"Unable to set tip opacity {ex}");
            }
        }

#region IWordCompletionSession

        ITextView IWordCompletionSession.TextView => _textView;
        bool IWordCompletionSession.IsDismissed => _isDismissed;
        event EventHandler IWordCompletionSession.Dismissed
        {
            add { _dismissed += value; }
            remove { _dismissed -= value; }
        }

        void IWordCompletionSession.Dismiss()
        {
            _isDismissed = true;
            _asyncCompletionSession.Dismiss();
        }

        bool IWordCompletionSession.MoveNext() => MoveDown();
        bool IWordCompletionSession.MovePrevious() => MoveUp();
        void IWordCompletionSession.Commit() => Commit();

#endregion

#region IPropertyOwner 

        PropertyCollection IPropertyOwner.Properties => _asyncCompletionSession.Properties;

#endregion
    }
}

#elif VS_SPECIFIC_2017
// Nothing to do
#else
#error Unsupported configuration
#endif
