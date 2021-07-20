using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using Vim;
using Vim.UI.Wpf;
using System.IO;

namespace Vim.VisualStudio.Implementation.Misc
{
    /// <summary>
    /// Responsible for dealing with the conflicting key bindings inside of Visual Studio
    /// </summary>
    [Export(typeof(IKeyBindingService))]
    [Export(typeof(IVimBufferCreationListener))]
    internal sealed class KeyBindingService : IKeyBindingService, IVimBufferCreationListener
    {
        private readonly _DTE _dte;
        private readonly IKeyboardOptionsProvider _keyboardOptionsProvider;
        private readonly IProtectedOperations _protectedOperations;
        private readonly IVimApplicationSettings _vimApplicationSettings;
        private readonly ScopeData _scopeData;
        private bool _includeAllScopes;
        private ConflictingKeyBindingState _state;
        private HashSet<KeyInput> _vimFirstKeyInputSet;

        // TODO: This should be renamed to VimKeyInputSet.
        internal HashSet<KeyInput> VimFirstKeyInputSet
        {
            get { return _vimFirstKeyInputSet; }
            set { _vimFirstKeyInputSet = value; }
        }

        internal bool IncludeAllScopes
        {
            get { return _includeAllScopes; }
            set
            {
                if (value != _includeAllScopes)
                {
                    _includeAllScopes = value;
                    ConflictingKeyBindingState = ConflictingKeyBindingState.HasNotChecked;
                }
            }
        }

        internal ConflictingKeyBindingState ConflictingKeyBindingState
        {
            get { return _state; }
            set
            {
                if (_state != value)
                {
                    _state = value;
                    ConflictingKeyBindingStateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        [ImportingConstructor]
        internal KeyBindingService(SVsServiceProvider serviceProvider, IKeyboardOptionsProvider keyboardOptionsProvider, IProtectedOperations protectedOperations, IVimApplicationSettings vimApplicationSettings)
            : this(serviceProvider.GetService<SDTE, _DTE>(), keyboardOptionsProvider, protectedOperations, vimApplicationSettings, new ScopeData(serviceProvider.GetService<SVsShell, IVsShell>()))
        {
        }

        internal KeyBindingService(_DTE dte, IKeyboardOptionsProvider keyboardOptionsProvider, IProtectedOperations protectedOperations, IVimApplicationSettings vimApplicationSettings, ScopeData scopeData)
        {
            _dte = dte;
            _keyboardOptionsProvider = keyboardOptionsProvider;
            _protectedOperations = protectedOperations;
            _vimApplicationSettings = vimApplicationSettings;
            _scopeData = scopeData;

            FixKeyMappingIssue();
        }

        internal event EventHandler ConflictingKeyBindingStateChanged;

        internal void RunConflictingKeyBindingStateCheck(IVimBuffer vimBuffer)
        {
            _vimFirstKeyInputSet = CreateVimKeyInputSet(vimBuffer);

            // Calculate the current conflicting state.  Can't cache the snapshot here because we don't 
            // receive any notifications when key bindings change in Visual Studio.  Have to assume they 
            // change at all times
            var snapshot = CreateCommandKeyBindingSnapshot(_vimFirstKeyInputSet);
            ConflictingKeyBindingState = snapshot.Conflicting.Any()
                ? ConflictingKeyBindingState.FoundConflicts
                : ConflictingKeyBindingState.ConflictsIgnoredOrResolved;
        }

        internal void ResetConflictingKeyBindingState()
        {
            ConflictingKeyBindingState = ConflictingKeyBindingState.HasNotChecked;
        }

        internal void ResolveAnyConflicts()
        {
            if (_vimFirstKeyInputSet == null || _state != ConflictingKeyBindingState.FoundConflicts)
            {
                return;
            }

            _keyboardOptionsProvider.ShowOptionsPage();
            ConflictingKeyBindingState = ConflictingKeyBindingState.ConflictsIgnoredOrResolved;
        }

        internal void IgnoreAnyConflicts()
        {
            ConflictingKeyBindingState = ConflictingKeyBindingState.ConflictsIgnoredOrResolved;
        }

        /// <summary>
        /// Compute the set of keys that conflict with and have been already been removed.
        /// </summary>
        internal HashSet<KeyInput> CreateVimKeyInputSet(IVimBuffer buffer)
        {
            // Get the list of all KeyInputs from all the KeyInputSets for all modes.
            var hashSet = new HashSet<KeyInput>(
                buffer.AllModes
                .Select(x => x.CommandNames)
                .SelectMany(x => x)
                .SelectMany(x => x.KeyInputs))
            {

                // Include the key used to disable VsVim
                buffer.LocalSettings.GlobalSettings.DisableAllCommand
            };

            // Need to get the custom key bindings in the list.  It's very common for users 
            // to use for example function keys (<F2>, <F3>, etc ...) in their mappings which
            // are often bound to other Visual Studio commands.
            var keyMap = buffer.Vim.GlobalKeyMap;
            foreach (var keyRemapMode in KeyRemapMode.All)
            {
                foreach (var keyMapping in keyMap.GetKeyMappings(keyRemapMode))
                {
                    keyMapping.Left.KeyInputs.ForEach(keyInput => hashSet.Add(keyInput));
                }
            }

            return hashSet;
        }

        /// <summary>
        /// Compute the set of keys that conflict with and have been already been removed.
        /// </summary>
        internal CommandKeyBindingSnapshot CreateCommandKeyBindingSnapshot(IVimBuffer vimBuffer)
        {
            var hashSet = CreateVimKeyInputSet(vimBuffer);
            return CreateCommandKeyBindingSnapshot(hashSet);
        }

        internal CommandKeyBindingSnapshot CreateCommandKeyBindingSnapshot(HashSet<KeyInput> vimFirstKeyInputSet)
        {
            var commandListSnapshot = new CommandListSnapshot(_dte);
            var conflicting = FindConflictingCommandKeyBindings(commandListSnapshot, vimFirstKeyInputSet);
            var removed = FindRemovedKeyBindings(commandListSnapshot);

            return new CommandKeyBindingSnapshot(
                commandListSnapshot,
                vimFirstKeyInputSet,
                removed,
                conflicting);
        }

        /// <summary>
        /// Find all of the Command instances (which represent Visual Studio commands) which would conflict with any
        /// VsVim commands that use the keys in neededInputs.
        /// </summary>
        internal List<CommandKeyBinding> FindConflictingCommandKeyBindings(CommandListSnapshot commandsSnapshot, HashSet<KeyInput> neededInputs)
        {
            var list = new List<CommandKeyBinding>();
            var all = commandsSnapshot.CommandKeyBindings.Where(x => !ShouldSkip(x));
            foreach (var binding in all)
            {
                var input = binding.KeyBinding.FirstKeyStroke.AggregateKeyInput;
                if (neededInputs.Contains(input))
                {
                    list.Add(binding);
                }
            }

            return list;
        }

        /// <summary>
        /// Returns the list of commands that were previously removed by the user and are no longer currently active.
        /// </summary>
        internal List<CommandKeyBinding> FindRemovedKeyBindings(CommandListSnapshot commandListSnapshot)
        {
            return _vimApplicationSettings
                .RemovedBindings
                .Where(x => !commandListSnapshot.IsActive(x))
                .ToList();
        }

        /// <summary>
        /// Should this be skipped when removing conflicting bindings?
        /// </summary>
        internal bool ShouldSkip(CommandKeyBinding binding)
        {
            var scope = binding.KeyBinding.Scope;
            if (!_includeAllScopes && (_scopeData.GetScopeKind(scope) == ScopeKind.Unknown || _scopeData.GetScopeKind(scope) == ScopeKind.SolutionExplorer))
            {
                return true;
            }

            if (!binding.KeyBinding.KeyStrokes.Any())
            {
                return true;
            }

            var first = binding.KeyBinding.FirstKeyStroke;

            // We don't want to remove any mappings which don't include a modifier key 
            // because it removes too many mappings.  Without this check we would for
            // example remove Delete in insert mode, arrow keys for intellisense and 
            // general navigation, space bar for completion, etc ...
            //
            // One exception is function keys.  They are only bound in Vim to key 
            // mappings and should win over VS commands since users explicitly 
            // want them to occur
            if (first.KeyModifiers == VimKeyModifiers.None && !first.KeyInput.IsFunctionKey)
            {
                return true;
            }

            // In Vim Ctrl+Shift+f is exactly the same command as ctrl+f.  Vim simply ignores the 
            // shift key when processing a control command with an alpha character.  Visual Studio
            // though does differentiate.  Ctrl+f is different than Ctrl+Shift+F.  So don't 
            // process any alpha commands which have both Ctrl and Shift as Vim wouldn't 
            // ever hit them
            if (char.IsLetter(first.Char) && first.KeyModifiers == (VimKeyModifiers.Shift | VimKeyModifiers.Control))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Version 1.4.0 of VsVim introduced a bug which would strip the Visual Studio mappings for the 
        /// Enter and Backspace key if the user ran the key bindings dialog.  This meant that neither key
        /// would work in places that VsVim didn't run (immediate window, command window, etc ...).  This
        /// bug is fixed so that it won't happen going forward.  But we need to fix machines that we already
        /// messed up with the next install
        /// </summary>
        internal void FixKeyMappingIssue()
        {
            if (_vimApplicationSettings.KeyMappingIssueFixed)
            {
                return;
            }

            try
            {
                // Make sure we write out the string for the localized scope name
                var globalScopeName = _scopeData.GlobalScopeName;
                foreach (var command in _dte.GetCommands())
                {
                    if (!command.TryGetCommandId(out CommandId commandId))
                    {
                        continue;
                    }

                    if (VSConstants.VSStd2K == commandId.Group)
                    {
                        switch ((VSConstants.VSStd2KCmdID)commandId.Id)
                        {
                            case VSConstants.VSStd2KCmdID.RETURN:
                                if (!command.GetBindings().Any())
                                {
                                    command.SafeSetBindings(KeyBinding.Parse(globalScopeName + "::Enter"));
                                }
                                break;
                            case VSConstants.VSStd2KCmdID.BACKSPACE:
                                if (!command.GetBindings().Any())
                                {
                                    command.SafeSetBindings(KeyBinding.Parse(globalScopeName + "::Bkspce"));
                                }
                                break;
                        }
                    }
                }
            }
            finally
            {
                _vimApplicationSettings.KeyMappingIssueFixed = true;
            }
        }

        private void DumpKeyboard(StreamWriter streamWriter)
        {
            try
            {
                foreach (var dteCommand in _dte.GetCommands())
                {
                    streamWriter.WriteLine("Command: {0}", dteCommand.Name);

                    if (!dteCommand.TryGetCommandId(out CommandId commandId))
                    {
                        streamWriter.WriteLine("Cannot get CommandId: + ", dteCommand.Name);
                        continue;
                    }

                    streamWriter.WriteLine("\tId: {0} {1}", commandId.Group, commandId.Id);

                    var bindings = dteCommand.GetBindings(out Exception bindingEx);
                    if (bindingEx != null)
                    {
                        streamWriter.WriteLine("!!!Exception!!! " + bindingEx.Message + Environment.NewLine + bindingEx.StackTrace);
                        continue;
                    }

                    foreach (var binding in bindings)
                    {
                        streamWriter.WriteLine("\tBinding: {0} ", binding);

                        if (KeyBinding.TryParse(binding, out KeyBinding keyBinding))
                        {
                            streamWriter.WriteLine("\tKey Binding: {0} {1}", keyBinding.Scope, keyBinding.CommandString);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                streamWriter.WriteLine("!!!Exception!!! " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        #region IKeyBindingService

        ConflictingKeyBindingState IKeyBindingService.ConflictingKeyBindingState
        {
            get { return ConflictingKeyBindingState; }
        }

        bool IKeyBindingService.IncludeAllScopes
        {
            get { return IncludeAllScopes; }
            set { IncludeAllScopes = value; }
        }

        event EventHandler IKeyBindingService.ConflictingKeyBindingStateChanged
        {
            add { ConflictingKeyBindingStateChanged += value; }
            remove { ConflictingKeyBindingStateChanged -= value; }
        }

        CommandKeyBindingSnapshot IKeyBindingService.CreateCommandKeyBindingSnapshot(IVimBuffer vimBuffer)
        {
            return CreateCommandKeyBindingSnapshot(vimBuffer);
        }

        void IKeyBindingService.RunConflictingKeyBindingStateCheck(IVimBuffer buffer)
        {
            RunConflictingKeyBindingStateCheck(buffer);
        }

        void IKeyBindingService.ResetConflictingKeyBindingState()
        {
            ResetConflictingKeyBindingState();
        }

        void IKeyBindingService.ResolveAnyConflicts()
        {
            ResolveAnyConflicts();
        }

        void IKeyBindingService.IgnoreAnyConflicts()
        {
            IgnoreAnyConflicts();
        }

        void IKeyBindingService.DumpKeyboard(StreamWriter streamWriter)
        {
            DumpKeyboard(streamWriter);
        }

        #endregion

        #region IVimBufferCreationListener

        void IVimBufferCreationListener.VimBufferCreated(IVimBuffer vimBuffer)
        {
            void doCheck()
            {
                if (vimBuffer.IsClosed)
                {
                    return;
                }

                if (ConflictingKeyBindingState == ConflictingKeyBindingState.HasNotChecked)
                {
                    if (_vimApplicationSettings.IgnoredConflictingKeyBinding)
                    {
                        IgnoreAnyConflicts();
                    }
                    else
                    {
                        RunConflictingKeyBindingStateCheck(vimBuffer);
                    }
                }
            };

            // Don't block startup by immediately running a key binding check.  Schedule it 
            // for the future
            _protectedOperations.BeginInvoke(doCheck);
        }

        #endregion
    }
}
