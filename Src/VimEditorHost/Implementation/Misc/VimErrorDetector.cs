using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using Vim.Extensions;

namespace Vim.EditorHost.Implementation.Misc
{
    /// <summary>
    /// IVimErrorDetector MEF component.  Useful in tracking down errors which are silently
    /// swallowed by the editor infrastructure
    /// </summary>
    [Export(typeof(IExtensionErrorHandler))]
    [Export(typeof(IVimErrorDetector))]
    [Export(typeof(IWpfTextViewCreationListener))]
    [Export(typeof(IVimBufferCreationListener))]
    [ContentType("any")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    public sealed class VimErrorDetector : IVimErrorDetector, IWpfTextViewCreationListener, IVimBufferCreationListener
    {
        private readonly List<Exception> _errorList = new List<Exception>();
        private readonly List<ITextView> _activeTextViewList = new List<ITextView>();
        private readonly List<IVimBuffer> _activeVimBufferList = new List<IVimBuffer>();
        private readonly ITextBufferUndoManagerProvider _textBufferUndoManagerProvider;
        private readonly IBufferTrackingService _bufferTrackingService;

        [ImportingConstructor]
        internal VimErrorDetector(IBufferTrackingService bufferTrackingService, ITextBufferUndoManagerProvider textBufferUndoManagerProvider)
        {
            _textBufferUndoManagerProvider = textBufferUndoManagerProvider;
            _bufferTrackingService = bufferTrackingService;
        }

        private void CheckForOrphanedItems()
        {
            CheckForOrphanedLinkedUndoTransaction();
            CheckForOrphanedUndoHistory();
            CheckForOrphanedTrackingItems();
        }

        private void CheckForOrphanedLinkedUndoTransaction()
        {
            foreach (var vimBuffer in _activeVimBufferList)
            {
                CheckForOrphanedLinkedUndoTransaction(vimBuffer);
            }
        }

        /// <summary>
        /// Make sure that the IVimBuffer doesn't have an open linked undo transaction when
        /// it closes.  This leads to every Visual Studio transaction after this point being
        /// linked to a single Vim undo.
        /// </summary>
        private void CheckForOrphanedLinkedUndoTransaction(IVimBuffer vimBuffer)
        {
            if (vimBuffer.UndoRedoOperations.InLinkedUndoTransaction)
            {
                // It's expected that insert and replace mode will often have a linked
                // undo transaction open.  It's common for a command like 'cw' to transition
                // to insert mode so that the following edit is linked to the 'c' portion
                // for an undo
                if (vimBuffer.ModeKind != ModeKind.Insert && vimBuffer.ModeKind != ModeKind.Replace)
                {
                    _errorList.Add(new Exception("Failed to close a linked undo transaction"));
                }
            }
        }

        private void CheckForOrphanedUndoHistory()
        {
            foreach (var textView in _activeTextViewList)
            {
                CheckForOrphanedUndoHistory(textView);
            }
        }

        /// <summary>
        /// Make sure that on tear down we don't have a current transaction.  Having one indicates
        /// we didn't close it and hence are killing undo in the ITextBuffer
        /// </summary>
        private void CheckForOrphanedUndoHistory(ITextView textView)
        {
            var history = _textBufferUndoManagerProvider.GetTextBufferUndoManager(textView.TextBuffer).TextBufferUndoHistory;
            if (history.CurrentTransaction != null)
            {
                _errorList.Add(new Exception("Failed to close an undo transaction"));
            }
        }

        /// <summary>
        /// See if any of the active ITextBuffer instances are holding onto tracking data.  If these are
        /// incorrectly held it will lead to unchecked memory leaks and performance issues as they will 
        /// all be listening to change events on the ITextBuffer
        /// </summary>
        private void CheckForOrphanedTrackingItems()
        {
            // First go through all of the active IVimBuffer instances and give them a chance to drop
            // the marks they cache.  
            foreach (var vimBuffer in _activeVimBufferList)
            {
                vimBuffer.VimTextBuffer.Clear();
                vimBuffer.JumpList.Clear();
            }

            foreach (var vimBuffer in _activeVimBufferList)
            {
                if (_bufferTrackingService.HasTrackingItems(vimBuffer.TextBuffer))
                {
                    _errorList.Add(new Exception("Orphaned tracking item detected"));
                }
            }
        }

        #region IExtensionErrorHandler

        void IExtensionErrorHandler.HandleError(object sender, Exception exception)
        {
#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022
            // https://github.com/VsVim/VsVim/issues/2463
            // Working around several bugs thrown during core MEF composition
            if (exception.Message.Contains("Microsoft.VisualStudio.Language.CodeCleanUp.CodeCleanUpFixerRegistrationService.ProfileService") ||
                exception.Message.Contains("Microsoft.VisualStudio.Language.CodeCleanUp.CodeCleanUpFixerRegistrationService.mefRegisteredCodeCleanupProviders") ||
                exception.StackTrace?.Contains("Microsoft.VisualStudio.UI.Text.Wpf.FileHealthIndicator.Implementation.FileHealthIndicatorButton..ctor") == true)
            {
                return;
            }
#elif VS_SPECIFIC_2017
#else
#error Unsupported configuration
#endif

            _errorList.Add(exception);
        }

#endregion

#region IVimErrorDetector

        bool IVimErrorDetector.HasErrors()
        {
            CheckForOrphanedItems();
            return _errorList.Count > 0;
        }

        IEnumerable<Exception> IVimErrorDetector.GetErrors()
        {
            CheckForOrphanedItems();
            return _errorList;
        }

        void IVimErrorDetector.Clear()
        {
            _errorList.Clear();
            _activeTextViewList.Clear();
            _activeVimBufferList.Clear();
        }

#endregion

#region IWpfTextViewCreationListener

        void IWpfTextViewCreationListener.TextViewCreated(IWpfTextView textView)
        {
            _activeTextViewList.Add(textView);
            textView.Closed +=
                (sender, e) =>
                {
                    _activeTextViewList.Remove(textView);
                    CheckForOrphanedUndoHistory(textView);
                };
        }

        void IVimBufferCreationListener.VimBufferCreated(IVimBuffer vimBuffer)
        {
            _activeVimBufferList.Add(vimBuffer);
            vimBuffer.Closed +=
                (sender, e) =>
                {
                    _activeVimBufferList.Remove(vimBuffer);
                    CheckForOrphanedLinkedUndoTransaction(vimBuffer);
                };
        }

#endregion
    }
}
