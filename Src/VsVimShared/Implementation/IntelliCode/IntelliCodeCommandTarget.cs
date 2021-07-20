#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Vim.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Vim;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using System.Reflection;
using System.Diagnostics;

namespace VsVimShared.Implementation.IntelliCode
{
    internal sealed class IntelliCodeCommandTarget : ICommandTarget
    {
        private object _cascadingCompletionSource;
        private bool _lookedForCascadingCompletionSource;

        internal IVimBufferCoordinator VimBufferCoordinator { get; }
        internal IAsyncCompletionBroker AsyncCompletionBroker { get; }

        internal IVimBuffer VimBuffer => VimBufferCoordinator.VimBuffer;
        internal ITextView TextView => VimBuffer.TextView;
        internal object CascadingCompletionSource
        {
            get
            {
                if (!_lookedForCascadingCompletionSource)
                {
                    _lookedForCascadingCompletionSource = true;
                    _cascadingCompletionSource = TextView
                        .Properties
                        .PropertyList
                        .Where(pair => pair.Key is Type { Name: "CascadingCompletionSource" })
                        .Select(x => x.Value)
                        .FirstOrDefault();
                }

                return _cascadingCompletionSource;
            }
        }

        internal IntelliCodeCommandTarget(
            IVimBufferCoordinator vimBufferCoordinator,
            IAsyncCompletionBroker asyncCompletionBroker)
        {
            VimBufferCoordinator = vimBufferCoordinator;
            AsyncCompletionBroker = asyncCompletionBroker;
        }

        private bool Exec(EditCommand editCommand, out Action preAction, out Action postAction)
        {
            preAction = null;
            postAction = null;
            return false;
        }

        private CommandStatus QueryStatus(EditCommand editCommand)
        {
            if (editCommand.HasKeyInput &&
                editCommand.KeyInput == KeyInputUtil.EscapeKey && 
                VimBuffer.CanProcess(editCommand.KeyInput) &&
                IsIntelliCodeLineCompletionEnabled())
            {
                VimBuffer.Process(editCommand.KeyInput);
            }

            return CommandStatus.PassOn;
        }

        private bool IsIntelliCodeLineCompletionEnabled()
        {
            if (CascadingCompletionSource is not { } source)
            {
                return false;
            }

            try
            {
                var property = source.GetType().GetProperty("ShowGrayText", BindingFlags.NonPublic | BindingFlags.Instance);
                var value = property.GetValue(source, null);
                return value is bool b && b;
            }
            catch (Exception)
            {
                Debug.Assert(false);
                return false;
            }
        }

        #region ICommandTarget

        bool ICommandTarget.Exec(EditCommand editCommand, out Action preAction, out Action postAction) =>
            Exec(editCommand, out preAction, out postAction);

        CommandStatus ICommandTarget.QueryStatus(EditCommand editCommand) =>
            QueryStatus(editCommand);

        #endregion
    }

    [Export(typeof(ICommandTargetFactory))]
    [Name("IntelliCode Command Target")]
    [Order(Before = VsVimConstants.StandardCommandTargetName)]
    internal sealed class IntelliCodeCommandTargetFactory : ICommandTargetFactory
    {
        internal IAsyncCompletionBroker AsyncCompletionBroker { get; }

        [ImportingConstructor]
        public IntelliCodeCommandTargetFactory(IAsyncCompletionBroker asyncCompletionBroker)
        {
            AsyncCompletionBroker = asyncCompletionBroker;
        }

        ICommandTarget ICommandTargetFactory.CreateCommandTarget(IOleCommandTarget nextCommandTarget, IVimBufferCoordinator vimBufferCoordinator) =>
            new IntelliCodeCommandTarget(vimBufferCoordinator, AsyncCompletionBroker);
    }

}

#elif VS_SPECIFIC_2017
#else
#error Unsupported configurationi
#endif
