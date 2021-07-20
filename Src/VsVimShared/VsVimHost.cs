using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using EnvDTE;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Vim;
using Vim.Extensions;
using Vim.UI.Wpf;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.OLE.Interop;
using EnvDTE80;
using System.Windows.Threading;
using System.Diagnostics;
using Vim.Interpreter;

#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022
using Microsoft.VisualStudio.Platform.WindowManagement;
using Microsoft.VisualStudio.PlatformUI.Shell;
#elif VS_SPECIFIC_2017
#else
#error Unsupported configuration
#endif

namespace Vim.VisualStudio
{
    /// <summary>
    /// Implement the IVimHost interface for Visual Studio functionality.  It's not responsible for 
    /// the relationship with the IVimBuffer, merely implementing the host functionality
    /// </summary>
    [Export(typeof(IVimHost))]
    [Export(typeof(IWpfTextViewCreationListener))]
    [Export(typeof(VsVimHost))]
    [ContentType(VimConstants.ContentType)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class VsVimHost : VimHost, IVsSelectionEvents, IVsRunningDocTableEvents3
    {
        #region SettingsSource

        /// <summary>
        /// This class provides the ability to control our host specific settings using the familiar
        /// :set syntax in a vim file.  It is just proxying them to the real IVimApplicationSettings
        /// </summary>
        internal sealed class SettingsSource : IVimCustomSettingSource
        {
            private const string UseEditorIndentName = "vsvim_useeditorindent";
            private const string UseEditorDefaultsName = "vsvim_useeditordefaults";
            private const string UseEditorTabAndBackspaceName = "vsvim_useeditortab";
            private const string UseEditorCommandMarginName = "vsvim_useeditorcommandmargin";
            private const string CleanMacrosName = "vsvim_cleanmacros";
            private const string HideMarksName = "vsvim_hidemarks";

            private readonly IVimApplicationSettings _vimApplicationSettings;

            private SettingsSource(IVimApplicationSettings vimApplicationSettings)
            {
                _vimApplicationSettings = vimApplicationSettings;
            }

            internal static void Initialize(IVimGlobalSettings globalSettings, IVimApplicationSettings vimApplicationSettings)
            {
                var settingsSource = new SettingsSource(vimApplicationSettings);
                globalSettings.AddCustomSetting(UseEditorIndentName, UseEditorIndentName, settingsSource);
                globalSettings.AddCustomSetting(UseEditorDefaultsName, UseEditorDefaultsName, settingsSource);
                globalSettings.AddCustomSetting(UseEditorTabAndBackspaceName, UseEditorTabAndBackspaceName, settingsSource);
                globalSettings.AddCustomSetting(UseEditorCommandMarginName, UseEditorCommandMarginName, settingsSource);
                globalSettings.AddCustomSetting(CleanMacrosName, CleanMacrosName, settingsSource);
                globalSettings.AddCustomSetting(HideMarksName, HideMarksName, settingsSource);
            }

            SettingValue IVimCustomSettingSource.GetDefaultSettingValue(string name)
            {
                switch (name)
                {
                    case UseEditorIndentName:
                    case UseEditorDefaultsName:
                    case UseEditorTabAndBackspaceName:
                    case UseEditorCommandMarginName:
                    case CleanMacrosName:
                        return SettingValue.NewToggle(false);
                    case HideMarksName:
                        return SettingValue.NewString("");
                    default:
                        Debug.Assert(false);
                        return SettingValue.NewToggle(false);
                }
            }

            SettingValue IVimCustomSettingSource.GetSettingValue(string name)
            {
                switch (name)
                {
                    case UseEditorIndentName:
                        return SettingValue.NewToggle(_vimApplicationSettings.UseEditorIndent);
                    case UseEditorDefaultsName:
                        return SettingValue.NewToggle(_vimApplicationSettings.UseEditorDefaults);
                    case UseEditorTabAndBackspaceName:
                        return SettingValue.NewToggle(_vimApplicationSettings.UseEditorTabAndBackspace);
                    case UseEditorCommandMarginName:
                        return SettingValue.NewToggle(_vimApplicationSettings.UseEditorCommandMargin);
                    case CleanMacrosName:
                        return SettingValue.NewToggle(_vimApplicationSettings.CleanMacros);
                    case HideMarksName:
                        return SettingValue.NewString(_vimApplicationSettings.HideMarks);
                    default:
                        Debug.Assert(false);
                        return SettingValue.NewToggle(false);
                }
            }

            void IVimCustomSettingSource.SetSettingValue(string name, SettingValue settingValue)
            {
                void setBool(Action<bool> action)
                {
                    if (!settingValue.IsToggle)
                    {
                        return;
                    }

                    var value = ((SettingValue.Toggle)settingValue).Toggle;
                    action(value);
                }

                void setString(Action<string> action)
                {
                    if (!settingValue.IsString)
                    {
                        return;
                    }

                    var value = ((SettingValue.String)settingValue).String;
                    action(value);
                }

                switch (name)
                {
                    case UseEditorIndentName:
                        setBool(v => _vimApplicationSettings.UseEditorIndent = v);
                        break;
                    case UseEditorDefaultsName:
                        setBool(v => _vimApplicationSettings.UseEditorDefaults = v);
                        break;
                    case UseEditorTabAndBackspaceName:
                        setBool(v => _vimApplicationSettings.UseEditorTabAndBackspace = v);
                        break;
                    case UseEditorCommandMarginName:
                        setBool(v => _vimApplicationSettings.UseEditorCommandMargin = v);
                        break;
                    case CleanMacrosName:
                        setBool(v => _vimApplicationSettings.CleanMacros = v);
                        break;
                    case HideMarksName:
                        setString(v => _vimApplicationSettings.HideMarks = v);
                        break;
                    default:
                        Debug.Assert(false);
                        break;
                }
            }
        }

        #endregion

        #region SettingsSync

        internal sealed class SettingsSync
        {
            private bool _isSyncing;

            public IVimApplicationSettings VimApplicationSettings { get; }
            public IMarkDisplayUtil MarkDisplayUtil { get; }
            public IControlCharUtil ControlCharUtil { get; }
            public IClipboardDevice ClipboardDevice { get; }

            [ImportingConstructor]
            public SettingsSync(
                IVimApplicationSettings vimApplicationSettings,
                IMarkDisplayUtil markDisplayUtil,
                IControlCharUtil controlCharUtil,
                IClipboardDevice clipboardDevice)
            {
                VimApplicationSettings = vimApplicationSettings;
                MarkDisplayUtil = markDisplayUtil;
                ControlCharUtil = controlCharUtil;
                ClipboardDevice = clipboardDevice;

                MarkDisplayUtil.HideMarksChanged += SyncToApplicationSettings;
                ControlCharUtil.DisplayControlCharsChanged += SyncToApplicationSettings;
                VimApplicationSettings.SettingsChanged += SyncFromApplicationSettings;
            }

            /// <summary>
            /// Sync from our external sources to application settings
            /// </summary>
            internal void SyncToApplicationSettings(object sender = null, EventArgs e = null)
            {
                SyncAction(() =>
                {
                    VimApplicationSettings.HideMarks = MarkDisplayUtil.HideMarks;
                    VimApplicationSettings.DisplayControlChars = ControlCharUtil.DisplayControlChars;
                    VimApplicationSettings.ReportClipboardErrors = ClipboardDevice.ReportErrors;
                });
            }

            internal void SyncFromApplicationSettings(object sender = null, EventArgs e = null)
            {
                SyncAction(() =>
                {
                    MarkDisplayUtil.HideMarks = VimApplicationSettings.HideMarks;
                    ControlCharUtil.DisplayControlChars = VimApplicationSettings.DisplayControlChars;
                    ClipboardDevice.ReportErrors = VimApplicationSettings.ReportClipboardErrors;
                });
            }

            private void SyncAction(Action action)
            {
                if (!_isSyncing)
                {
                    try
                    {
                        _isSyncing = true;
                        action();
                    }
                    finally
                    {
                        _isSyncing = false;
                    }
                }
            }
        }

        #endregion

        internal const string CommandNameGoToDefinition = "Edit.GoToDefinition";
        internal const string CommandNamePeekDefinition = "Edit.PeekDefinition";
        internal const string CommandNameGoToDeclaration = "Edit.GoToDeclaration";

#if VS_SPECIFIC_2017
        internal const VisualStudioVersion VisualStudioVersion = global::Vim.VisualStudio.VisualStudioVersion.Vs2017;
#elif VS_SPECIFIC_2019
        internal const VisualStudioVersion VisualStudioVersion = global::Vim.VisualStudio.VisualStudioVersion.Vs2019;
#elif VS_SPECIFIC_2022
        internal const VisualStudioVersion VisualStudioVersion = global::Vim.VisualStudio.VisualStudioVersion.Vs2022;
#else
#error Unsupported configuration
#endif

        private readonly IVsAdapter _vsAdapter;
        private readonly ITextManager _textManager;
        private readonly IVsEditorAdaptersFactoryService _editorAdaptersFactoryService;
        private readonly _DTE _dte;
        private readonly IVsExtensibility _vsExtensibility;
        private readonly ICSharpScriptExecutor _csharpScriptExecutor;
        private readonly IVsMonitorSelection _vsMonitorSelection;
        private readonly IVimApplicationSettings _vimApplicationSettings;
        private readonly ISmartIndentationService _smartIndentationService;
        private readonly IExtensionAdapterBroker _extensionAdapterBroker;
        private readonly IVsRunningDocumentTable _runningDocumentTable;
        private readonly IVsShell _vsShell;
        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IProtectedOperations _protectedOperations;
        private readonly IClipboardDevice _clipboardDevice;
        private readonly SettingsSync _settingsSync;

        private IVim _vim;
        private FindEvents _findEvents;

        internal _DTE DTE
        {
            get { return _dte; }
        }

        /// <summary>
        /// Should we create IVimBuffer instances for new ITextView values
        /// </summary>
        public bool DisableVimBufferCreation
        {
            get;
            set;
        }

        /// <summary>
        /// Don't automatically synchronize settings.  The settings can't be synchronized until after Visual Studio 
        /// applies settings which happens at an uncertain time.  HostFactory handles this timing 
        /// </summary>
        public override bool AutoSynchronizeSettings
        {
            get { return false; }
        }

        public override DefaultSettings DefaultSettings
        {
            get { return _vimApplicationSettings.DefaultSettings; }
        }

        public override bool IsUndoRedoExpected
        {
            get { return _extensionAdapterBroker.IsUndoRedoExpected ?? base.IsUndoRedoExpected; }
        }

        public override int TabCount
        {
            get { return GetWindowFrameState().WindowFrameCount; }
        }

        public override bool UseDefaultCaret
        {
            get { return _extensionAdapterBroker.UseDefaultCaret ?? base.UseDefaultCaret; }
        }

        [ImportingConstructor]
        internal VsVimHost(
            IVsAdapter adapter,
            ITextBufferFactoryService textBufferFactoryService,
            ITextEditorFactoryService textEditorFactoryService,
            ITextDocumentFactoryService textDocumentFactoryService,
            ITextBufferUndoManagerProvider undoManagerProvider,
            IVsEditorAdaptersFactoryService editorAdaptersFactoryService,
            IEditorOperationsFactoryService editorOperationsFactoryService,
            ISmartIndentationService smartIndentationService,
            ITextManager textManager,
            ICSharpScriptExecutor csharpScriptExecutor,
            IVimApplicationSettings vimApplicationSettings,
            IExtensionAdapterBroker extensionAdapterBroker,
            IProtectedOperations protectedOperations,
            IMarkDisplayUtil markDisplayUtil,
            IControlCharUtil controlCharUtil,
            ICommandDispatcher commandDispatcher,
            SVsServiceProvider serviceProvider,
            IClipboardDevice clipboardDevice) :
            base(
                  protectedOperations,
                  textBufferFactoryService,
                  textEditorFactoryService,
                  textDocumentFactoryService,
                  editorOperationsFactoryService)
        {
            _vsAdapter = adapter;
            _editorAdaptersFactoryService = editorAdaptersFactoryService;
            _dte = (_DTE)serviceProvider.GetService(typeof(_DTE));
            _vsExtensibility = (IVsExtensibility)serviceProvider.GetService(typeof(IVsExtensibility));
            _textManager = textManager;
            _csharpScriptExecutor = csharpScriptExecutor;
            _vsMonitorSelection = serviceProvider.GetService<SVsShellMonitorSelection, IVsMonitorSelection>();
            _vimApplicationSettings = vimApplicationSettings;
            _smartIndentationService = smartIndentationService;
            _extensionAdapterBroker = extensionAdapterBroker;
            _runningDocumentTable = serviceProvider.GetService<SVsRunningDocumentTable, IVsRunningDocumentTable>();
            _vsShell = (IVsShell)serviceProvider.GetService(typeof(SVsShell));
            _protectedOperations = protectedOperations;
            _commandDispatcher = commandDispatcher;
            _clipboardDevice = clipboardDevice;

            _vsMonitorSelection.AdviseSelectionEvents(this, out uint selectionCookie);
            _runningDocumentTable.AdviseRunningDocTableEvents(this, out uint runningDocumentTableCookie);

            InitOutputPane();

            _settingsSync = new SettingsSync(vimApplicationSettings, markDisplayUtil, controlCharUtil, _clipboardDevice);
            _settingsSync.SyncFromApplicationSettings();
        }

        /// <summary>
        /// Hookup the output window to the vim trace data when it's requested by the developer
        /// </summary>
        private void InitOutputPane()
        {
            // The output window is not guaraneed to be accessible on startup. On certain configurations of VS2015
            // it can throw an exception. Delaying the creation of the Window until after startup has likely 
            // completed. Additionally using IProtectedOperations to guard against exceptions 
            // https://github.com/VsVim/VsVim/issues/2249

            _protectedOperations.BeginInvoke(initOutputPaneCore, DispatcherPriority.ApplicationIdle);

            void initOutputPaneCore()
            {
                if (!(_dte is DTE2 dte2))
                {
                    return;
                }

                var outputWindow = dte2.ToolWindows.OutputWindow;
                var outputPane = outputWindow.OutputWindowPanes.Add("VsVim");

                VimTrace.Trace += (_, e) =>
                {
                    if (e.TraceKind == VimTraceKind.Error ||
                        _vimApplicationSettings.EnableOutputWindow)
                    {
                        outputPane.OutputString(e.Message + Environment.NewLine);
                    }
                };
            }
        }

        public override void EnsurePackageLoaded()
        {
            var guid = VsVimConstants.PackageGuid;
            _vsShell.LoadPackage(ref guid, out IVsPackage package);
        }

        public override void CloseAllOtherTabs(ITextView textView)
        {
            RunHostCommand(textView, "File.CloseAllButThis", string.Empty);
        }

        public override void CloseAllOtherWindows(ITextView textView)
        {
            CloseAllOtherTabs(textView); // At least for now, :only == :tabonly
        }

        private bool SafeExecuteCommand(ITextView textView, string command, string args = "")
        {
            bool postCommand = false;
            if (textView != null && textView.TextBuffer.ContentType.IsCPlusPlus())
            {
                if (command.Equals(CommandNameGoToDefinition, StringComparison.OrdinalIgnoreCase) ||
                    command.Equals(CommandNameGoToDeclaration, StringComparison.OrdinalIgnoreCase))
                {
                    // C++ commands like 'Edit.GoToDefinition' need to be
                    // posted instead of executed and they need to have a null
                    // argument in order to work like it does when bound to a
                    // keyboard shortcut like 'F12'. Reported in issue #2535.
                    postCommand = true;
                    args = null;
                }
            }

            try
            {
                return _commandDispatcher.ExecuteCommand(textView, command, args, postCommand);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Treat a bulk operation just like a macro replay.  They have similar semantics like we
        /// don't want intellisense to be displayed during the operation.  
        /// </summary>
        public override void BeginBulkOperation()
        {
            try
            {
                _vsExtensibility.EnterAutomationFunction();
            }
            catch
            {
                // If automation support isn't present it's not an issue
            }
        }

        public override void EndBulkOperation()
        {
            try
            {
                _vsExtensibility.ExitAutomationFunction();
            }
            catch
            {
                // If automation support isn't present it's not an issue
            }
        }

        /// <summary>
        /// Perform the 'find in files' operation using the specified parameters
        /// </summary>
        /// <param name="pattern">BCL regular expression pattern</param>
        /// <param name="matchCase">whether to match case</param>
        /// <param name="filesOfType">which files to search</param>
        /// <param name="flags">flags controlling the find operation</param>
        /// <param name="action">action to perform when the operation completes</param>
        public override void FindInFiles(
            string pattern,
            bool matchCase,
            string filesOfType,
            VimGrepFlags flags,
            FSharpFunc<Unit, Unit> action)
        {
            // Perform the action when the find operation completes.
            void onFindDone(vsFindResult result, bool cancelled)
            {
                // Unsubscribe.
                _findEvents.FindDone -= onFindDone;
                _findEvents = null;

                // Perform the action.
                var protectedAction =
                    _protectedOperations.GetProtectedAction(() => action.Invoke(null));
                protectedAction();
            }

            try
            {
                if (_dte.Find is Find2 find)
                {
                    // Configure the find operation.
                    find.Action = vsFindAction.vsFindActionFindAll;
                    find.FindWhat = pattern;
                    find.Target = vsFindTarget.vsFindTargetSolution;
                    find.MatchCase = matchCase;
                    find.MatchWholeWord = false;
                    find.PatternSyntax = vsFindPatternSyntax.vsFindPatternSyntaxRegExpr;
                    find.FilesOfType = filesOfType;
                    find.ResultsLocation = vsFindResultsLocation.vsFindResults1;
                    find.WaitForFindToComplete = false;

                    // Register the callback.
                    _findEvents = _dte.Events.FindEvents;
                    _findEvents.FindDone += onFindDone;

                    // Start the find operation.
                    find.Execute();
                }
            }
            catch (Exception ex)
            {
                _protectedOperations.Report(ex);
            }
        }

        /// <summary>
        /// Format the specified line range.  There is no inherent operation to do this
        /// in Visual Studio.  Instead we leverage the FormatSelection command.  Need to be careful
        /// to reset the selection after a format
        /// </summary>
        public override void FormatLines(ITextView textView, SnapshotLineRange range)
        {
            var startedWithSelection = !textView.Selection.IsEmpty;
            textView.Selection.Clear();
            textView.Selection.Select(range.ExtentIncludingLineBreak, false);
            SafeExecuteCommand(textView, "Edit.FormatSelection");

            if (!startedWithSelection)
            {
                textView.Selection.Clear();
            }
        }

        public override bool GoToDefinition()
        {
            return SafeExecuteCommand(_textManager.ActiveTextViewOptional, CommandNameGoToDefinition);
        }

        public override bool PeekDefinition()
        {
            return SafeExecuteCommand(_textManager.ActiveTextViewOptional, CommandNamePeekDefinition);
        }

        /// <summary>
        /// In a perfect world this would replace the contents of the existing ITextView
        /// with those of the specified file.  Unfortunately this causes problems in 
        /// Visual Studio when the file is of a different content type.  Instead we 
        /// mimic the behavior by opening the document in a new window and closing the
        /// existing one
        /// </summary>
        public override bool LoadFileIntoExistingWindow(string filePath, ITextView textView)
        {
            try
            {
                // Open the document before closing the other.  That way any error which occurs
                // during an open will cause the method to abandon and produce a user error 
                // message
                VsShellUtilities.OpenDocument(_vsAdapter.ServiceProvider, filePath);
                _textManager.CloseView(textView);
                return true;
            }
            catch (Exception e)
            {
                _vim.ActiveStatusUtil.OnError(e.Message);
                return false;
            }
        }

        /// <summary>
        /// Open up a new document window with the specified file
        /// </summary>
        public override FSharpOption<ITextView> LoadFileIntoNewWindow(string filePath, FSharpOption<int> line, FSharpOption<int> column)
        {
            try
            {
                // Open the document in a window.
                VsShellUtilities.OpenDocument(_vsAdapter.ServiceProvider, filePath, VSConstants.LOGVIEWID_Primary,
                    out IVsUIHierarchy hierarchy, out uint itemID, out IVsWindowFrame windowFrame);

                // Get the VS text view for the window.
                var vsTextView = VsShellUtilities.GetTextView(windowFrame);

                // Get the WPF text view for the VS text view.
                var wpfTextView = _editorAdaptersFactoryService.GetWpfTextView(vsTextView);

                if (line.IsSome())
                {
                    // Move the caret to its initial position.
                    var snapshotLine = wpfTextView.TextSnapshot.GetLineFromLineNumber(line.Value);
                    var point = snapshotLine.Start;
                    if (column.IsSome())
                    {
                        point = point.Add(column.Value);
                        wpfTextView.Caret.MoveTo(point);
                    }
                    else
                    {
                        // Default column implies moving to the first non-blank.
                        wpfTextView.Caret.MoveTo(point);
                        var editorOperations = EditorOperationsFactoryService.GetEditorOperations(wpfTextView);
                        editorOperations.MoveToStartOfLineAfterWhiteSpace(false);
                    }
                }

                return FSharpOption.Create<ITextView>(wpfTextView);
            }
            catch (Exception e)
            {
                _vim.ActiveStatusUtil.OnError(e.Message);
                return FSharpOption<ITextView>.None;
            }
        }

        public override bool NavigateTo(VirtualSnapshotPoint point)
        {
            return _textManager.NavigateTo(point);
        }

        public override string GetName(ITextBuffer buffer)
        {
            var vsTextLines = _editorAdaptersFactoryService.GetBufferAdapter(buffer) as IVsTextLines;
            if (vsTextLines == null)
            {
                return string.Empty;
            }
            return vsTextLines.GetFileName();
        }

        public override bool Save(ITextBuffer textBuffer)
        {
            // The best way to save a buffer from an extensbility stand point is to use the DTE command 
            // system.  This means save goes through IOleCommandTarget and hits the maximum number of 
            // places other extension could be listening for save events.
            //
            // This only works though when we are saving the buffer which currently has focus.  If it's 
            // not in focus then we need to resort to saving via the ITextDocument.
            var activeSave = SaveActiveTextView(textBuffer);
            if (activeSave != null)
            {
                return activeSave.Value;
            }

            return _textManager.Save(textBuffer).IsSuccess;
        }

        /// <summary>
        /// Do a save operation using the <see cref="IOleCommandTarget"/> approach if this is a buffer
        /// for the active text view.  Returns null when this operation couldn't be performed and a 
        /// non-null value when the operation was actually executed.
        /// </summary>
        private bool? SaveActiveTextView(ITextBuffer textBuffer)
        {
            IWpfTextView activeTextView;
            if (!_vsAdapter.TryGetActiveTextView(out activeTextView) ||
                !TextBufferUtil.GetSourceBuffersRecursive(activeTextView.TextBuffer).Contains(textBuffer))
            {
                return null;
            }

            return SafeExecuteCommand(activeTextView, "File.SaveSelectedItems");
        }

        public override bool SaveTextAs(string text, string fileName)
        {
            try
            {
                File.WriteAllText(fileName, text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override void Close(ITextView textView)
        {
            _textManager.CloseView(textView);
        }

        public override bool IsReadOnly(ITextBuffer textBuffer)
        {
            return _vsAdapter.IsReadOnly(textBuffer);
        }

        public override bool IsVisible(ITextView textView)
        {
            if (textView is IWpfTextView wpfTextView)
            {
                if (!wpfTextView.VisualElement.IsVisible)
                {
                    return false;
                }

                // Certain types of windows (e.g. aspx documents) always report
                // that they are visible. Use the "is on screen" predicate of
                // the window's frame to rule them out. Reported in issue
                // #2435.
                var frameResult = _vsAdapter.GetContainingWindowFrame(wpfTextView);
                if (frameResult.TryGetValue(out IVsWindowFrame frame))
                {
                    if (frame.IsOnScreen(out int isOnScreen) == VSConstants.S_OK)
                    {
                        if (isOnScreen == 0)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Custom process the insert command if possible.  This is handled by VsCommandTarget
        /// </summary>
        public override bool TryCustomProcess(ITextView textView, InsertCommand command)
        {
            if (VsCommandTarget.TryGet(textView, out VsCommandTarget vsCommandTarget))
            {
                return vsCommandTarget.TryCustomProcess(command);
            }

            return false;
        }

        public override int GetTabIndex(ITextView textView)
        {
            // TODO: Should look for the actual index instead of assuming this is called on the 
            // active ITextView.  They may not actually be equal
            var windowFrameState = GetWindowFrameState();
            return windowFrameState.ActiveWindowFrameIndex;
        }

#if VS_SPECIFIC_2019 || VS_SPECIFIC_2022

        /// <summary>
        /// Get the state of the active tab group in Visual Studio
        /// </summary>
        public override void GoToTab(int index)
        {
            GetActiveViews()[index].ShowInFront();
        }

        internal WindowFrameState GetWindowFrameState()
        {
            var activeView = ViewManager.Instance.ActiveView;
            if (activeView == null)
            {
                return WindowFrameState.Default;
            }

            var list = GetActiveViews();
            var index = list.IndexOf(activeView);
            if (index < 0)
            {
                return WindowFrameState.Default;
            }

            return new WindowFrameState(index, list.Count);
        }

        /// <summary>
        /// Get the list of View's in the current ViewManager DocumentGroup
        /// </summary>
        private static List<View> GetActiveViews()
        {
            var activeView = ViewManager.Instance.ActiveView;
            if (activeView == null)
            {
                return new List<View>();
            }

            var group = activeView.Parent as DocumentGroup;
            if (group == null)
            {
                return new List<View>();
            }

            return group.VisibleChildren.OfType<View>().ToList();
        }

        /// <summary>
        /// Is this the active IVsWindow frame which has focus?  This method is used during macro
        /// running and hence must account for view changes which occur during a macro run.  Say by the
        /// macro containing the 'gt' command.  Unfortunately these don't fully process through Visual
        /// Studio until the next UI thread pump so we instead have to go straight to the view controller
        /// </summary>
        internal bool IsActiveWindowFrame(IVsWindowFrame vsWindowFrame)
        {
            var frame = vsWindowFrame as WindowFrame;
            return frame != null && frame.FrameView == ViewManager.Instance.ActiveView;
        }

#elif VS_SPECIFIC_2017
        internal WindowFrameState GetWindowFrameState() => WindowFrameState.Default;

        internal bool IsActiveWindowFrame(IVsWindowFrame vsWindowFrame) => false;

        public override void GoToTab(int index)
        {
        }
#else
#error Unsupported configuration
#endif

        /// <summary>
        /// Open the window for the specified list
        /// </summary>
        /// <param name="listKind">the kind of list</param>
        public override void OpenListWindow(ListKind listKind)
        {
            switch (listKind)
            {
                case ListKind.Error:
                    SafeExecuteCommand(null, "View.ErrorList");
                    break;
                case ListKind.Location:
                    SafeExecuteCommand(null, "View.FindResults1");
                    break;
                default:
                    Contract.Assert(false);
                    break;
            }
        }

        /// <summary>
        /// Navigate to the specified list item in the specified list
        /// </summary>
        /// <param name="listKind">the kind of list</param>
        /// <param name="navigationKind">the kind of navigation</param>
        /// <param name="argumentOption">an optional argument for the navigation</param>
        /// <param name="hasBang">whether the bang command format was used</param>
        /// <returns>the list item navigated to</returns>
        public override FSharpOption<ListItem> NavigateToListItem(
            ListKind listKind,
            NavigationKind navigationKind,
            FSharpOption<int> argumentOption,
            bool hasBang)
        {
            var argument = argumentOption.IsSome() ? new int?(argumentOption.Value) : null;
            switch (listKind)
            {
                case ListKind.Error:
                    return NavigateToError(navigationKind, argument, hasBang);
                case ListKind.Location:
                    return NavigateToLocation(navigationKind, argument, hasBang);
                default:
                    Contract.Assert(false);
                    return FSharpOption<ListItem>.None;
            }
        }

        /// <summary>
        /// Navigate to the specified error
        /// </summary>
        /// <param name="navigationKind">the kind of navigation</param>
        /// <param name="argument">an optional argument for the navigation</param>
        /// <param name="hasBang">whether the bang command format was used</param>
        /// <returns>the list item for the error navigated to</returns>
        private FSharpOption<ListItem> NavigateToError(NavigationKind navigationKind, int? argument, bool hasBang)
        {
            try
            {
                // Use the 'IErrorList' interface to manipulate the error list.
                if (_dte is DTE2 dte2 && dte2.ToolWindows.ErrorList is IErrorList errorList)
                {
                    var tableControl = errorList.TableControl;
                    var entries = tableControl.Entries.ToList();
                    var selectedEntry = tableControl.SelectedEntry;
                    var indexOf = entries.IndexOf(selectedEntry);
                    var current = indexOf != -1 ? new int?(indexOf) : null;
                    var length = entries.Count;

                    // Now that we know the current item (if any) and the list
                    // length, convert the navigation kind and its argument
                    // into the index of the desired list item.
                    var indexResult = GetListItemIndex(navigationKind, argument, current, length);
                    if (indexResult.HasValue)
                    {
                        var index = indexResult.Value;
                        var desiredEntry = entries[index];
                        tableControl.SelectedEntries = new[] { desiredEntry };
                        desiredEntry.NavigateTo(false);

                        // Get the error text from the appropriate table
                        // column.
                        var message = "";
                        if (desiredEntry.TryGetValue("text", out object content) && content is string text)
                        {
                            message = text;
                        }

                        // Item number is one-based.
                        return new ListItem(index + 1, length, message);
                    }
                }
            }
            catch (Exception ex)
            {
                _protectedOperations.Report(ex);
            }
            return FSharpOption<ListItem>.None;
        }

        /// <summary>
        /// Navigate to the specified find result
        /// </summary>
        /// <param name="navigationKind">what kind of navigation</param>
        /// <param name="argument">optional argument for the navigation</param>
        /// <param name="hasBang">whether the bang format was used</param>
        /// <returns>the list item for the find result navigated to</returns>
        private FSharpOption<ListItem> NavigateToLocation(NavigationKind navigationKind, int? argument, bool hasBang)
        {
            try
            {
                // Use the text contents of the 'Find Results 1' window to
                // manipulate the location list.
                var windowGuid = EnvDTE.Constants.vsWindowKindFindResults1;
                var findWindow = _dte.Windows.Item(windowGuid);
                if (findWindow != null && findWindow.Selection is EnvDTE.TextSelection textSelection)
                {
                    // Note that the text document and text selection APIs are
                    // one-based but 'GetListItemIndex' returns a zero-based
                    // value.
                    var textDocument = textSelection.Parent;
                    var startOffset = 1;
                    var endOffset = 1;
                    var rawLength = textDocument.EndPoint.Line - 1;
                    var length = rawLength - startOffset - endOffset;
                    var currentLine = textSelection.CurrentLine;
                    var current = new int?();
                    if (currentLine >= 1 + startOffset && currentLine <= rawLength - endOffset)
                    {
                        current = currentLine - startOffset - 1;
                        if (current == 0 && navigationKind == NavigationKind.Next && length > 0)
                        {
                            // If we are on the first line, we can't tell
                            // whether we've naviated to the first list item
                            // yet. To handle this, we use automation to go to
                            // the next search result. If the line number
                            // doesn't change, we haven't yet performed the
                            // first navigation.
                            if (SafeExecuteCommand(null, "Edit.GoToFindResults1NextLocation"))
                            {
                                if (textSelection.CurrentLine == currentLine)
                                {
                                    current = null;
                                }
                            }
                        }
                    }

                    // Now that we know the current item (if any) and the list
                    // length, convert the navigation kind and its argument
                    // into the index of the desired list item.
                    var indexResult = GetListItemIndex(navigationKind, argument, current, length);
                    if (indexResult.HasValue)
                    {
                        var index = indexResult.Value;
                        var adjustedLine = index + startOffset + 1;
                        textSelection.MoveToLineAndOffset(adjustedLine, 1);
                        textSelection.SelectLine();
                        var message = textSelection.Text;
                        textSelection.MoveToLineAndOffset(adjustedLine, 1);
                        if (SafeExecuteCommand(null, "Edit.GoToFindResults1Location"))
                        {
                            // Try to extract just the matching portion of
                            // the line.
                            message = message.Trim();
                            var start = message.Length >= 2 && message[1] == ':' ? 2 : 0;
                            var colon = message.IndexOf(':', start);
                            if (colon != -1)
                            {
                                message = message.Substring(colon + 1).Trim();
                            }

                            return new ListItem(index + 1, length, message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _protectedOperations.Report(ex);
            }
            return FSharpOption<ListItem>.None;
        }

        /// <summary>
        /// Convert the specified navigation instructions into an index for the
        /// new list item
        /// </summary>
        /// <param name="navigationKind">the kind of navigation</param>
        /// <param name="argument">an optional argument for the navigation</param>
        /// <param name="current">the zero-based index of the current list item</param>
        /// <param name="length">the length of the list</param>
        /// <returns>a zero-based index into the list</returns>
        private static int? GetListItemIndex(NavigationKind navigationKind, int? argument, int? current, int length)
        {
            var argumentOffset = argument.HasValue ? argument.Value : 1;
            var currentIndex = current.HasValue ? current.Value : -1;
            var newIndex = -1;

            // The 'first' and 'last' navigation kinds are one-based.
            switch (navigationKind)
            {
                case NavigationKind.First:
                    newIndex = argument.HasValue ? argument.Value - 1 : 0;
                    break;
                case NavigationKind.Last:
                    newIndex = argument.HasValue ? argument.Value - 1 : length - 1;
                    break;
                case NavigationKind.Next:
                    newIndex = currentIndex + argumentOffset;
                    break;
                case NavigationKind.Previous:
                    newIndex = currentIndex - argumentOffset;
                    break;
                default:
                    Contract.Assert(false);
                    break;
            }

            if (newIndex >= 0 && newIndex < length)
            {
                return newIndex;
            }
            return null;
        }

        public override void Make(bool buildSolution, string arguments)
        {
            if (buildSolution)
            {
                SafeExecuteCommand(null, "Build.BuildSolution");
            }
            else
            {
                SafeExecuteCommand(null, "Build.BuildOnlyProject");
            }
        }

        public override bool TryGetFocusedTextView(out ITextView textView)
        {
            var result = _vsAdapter.GetWindowFrames();
            if (result.IsError)
            {
                textView = null;
                return false;
            }

            var activeWindowFrame = result.Value.FirstOrDefault(IsActiveWindowFrame);
            if (activeWindowFrame == null)
            {
                textView = null;
                return false;
            }

            // TODO: Should try and pick the ITextView which is actually focussed as 
            // there could be several in a split screen
            try
            {
                textView = activeWindowFrame.GetCodeWindow().Value.GetPrimaryTextView(_editorAdaptersFactoryService).Value;
                return textView != null;
            }
            catch
            {
                textView = null;
                return false;
            }
        }

        public override void Quit()
        {
            _dte.Quit();
        }

        public override void RunCSharpScript(IVimBuffer vimBuffer, CallInfo callInfo, bool createEachTime)
        {
            _csharpScriptExecutor.Execute(vimBuffer, callInfo, createEachTime);
        }

        public override void RunHostCommand(ITextView textView, string command, string argument)
        {
            SafeExecuteCommand(textView, command, argument);
        }

        /// <summary>
        /// Perform a horizontal window split 
        /// </summary>
        public override void SplitViewHorizontally(ITextView textView)
        {
            _textManager.SplitView(textView);
        }

        /// <summary>
        /// Perform a vertical buffer split, which is essentially just another window in a different tab group.
        /// </summary>
        public override void SplitViewVertically(ITextView value)
        {
            try
            {
                _dte.ExecuteCommand("Window.NewWindow");
                _dte.ExecuteCommand("Window.NewVerticalTabGroup");
            }
            catch (Exception e)
            {
                _vim.ActiveStatusUtil.OnError(e.Message);
            }
        }

        /// <summary>
        /// Get the point at the middle of the caret in screen coordinates
        /// </summary>
        /// <param name="textView"></param>
        /// <returns></returns>
        private Point GetScreenPoint(IWpfTextView textView)
        {
            var element = textView.VisualElement;
            var caret = textView.Caret;
            var caretX = (caret.Left + caret.Right) / 2 - textView.ViewportLeft;
            var caretY = (caret.Top + caret.Bottom) / 2 - textView.ViewportTop;
            return element.PointToScreen(new Point(caretX, caretY));
        }

        /// <summary>
        /// Get the rectangle of the window in screen coordinates including any associated
        /// elements like margins, scroll bars, etc.
        /// </summary>
        /// <param name="textView"></param>
        /// <returns></returns>
        private Rect GetScreenRect(IWpfTextView textView)
        {
            var element = textView.VisualElement;
            var parent = VisualTreeHelper.GetParent(element);
            if (parent is FrameworkElement parentElement)
            {
                // The parent is a grid that contains the text view and all its margins.
                // Unfortunately, this does not include the navigation bar, so a horizontal
                // line from the caret in a window without one might intersect the navigation
                // bar and then we would not consider it as a candidate for horizontal motion.
                // The user can work around this by moving the caret down a couple of lines.
                element = parentElement;
            }
            var size = element.RenderSize;
            var upperLeft = new Point(0, 0);
            var lowerRight = new Point(size.Width, size.Height);
            return new Rect(element.PointToScreen(upperLeft), element.PointToScreen(lowerRight));
        }

        private IEnumerable<Tuple<IWpfTextView, Rect>> GetWindowPairs()
        {
            // Build a list of all visible windows and their screen coordinates.
            return _vim.VimBuffers
                .Select(vimBuffer => vimBuffer.TextView as IWpfTextView)
                .Where(textView => textView != null)
                .Where(textView => IsVisible(textView))
                .Where(textView => textView.ViewportWidth != 0)
                .Select(textView =>
                    Tuple.Create(textView, GetScreenRect(textView)));
        }

        private bool GoToWindowVertically(IWpfTextView currentTextView, int delta)
        {
            // Find those windows that overlap a vertical line
            // passing through the caret of the current window,
            // sorted by increasing vertical position on the screen.
            var caretPoint = GetScreenPoint(currentTextView);
            var pairs = GetWindowPairs()
                .Where(pair => pair.Item2.Left <= caretPoint.X && caretPoint.X <= pair.Item2.Right)
                .OrderBy(pair => pair.Item2.Y);

            return GoToWindowCore(currentTextView, delta, false, pairs);
        }

        private bool GoToWindowHorizontally(IWpfTextView currentTextView, int delta)
        {
            // Find those windows that overlap a horizontal line
            // passing through the caret of the current window,
            // sorted by increasing horizontal position on the screen.
            var caretPoint = GetScreenPoint(currentTextView);
            var pairs = GetWindowPairs()
                .Where(pair => pair.Item2.Top <= caretPoint.Y && caretPoint.Y <= pair.Item2.Bottom)
                .OrderBy(pair => pair.Item2.X);

            return GoToWindowCore(currentTextView, delta, false, pairs);
        }

        private bool GoToWindowNext(IWpfTextView currentTextView, int delta, bool wrap)
        {
            // Sort the windows into row/column order.
            var pairs = GetWindowPairs()
                .OrderBy(pair => pair.Item2.X)
                .ThenBy(pair => pair.Item2.Y);

            return GoToWindowCore(currentTextView, delta, wrap, pairs);
        }

        private bool GoToWindowRecent(IWpfTextView currentTextView)
        {
            // Get the list of visible windows.
            var windows = GetWindowPairs().Select(pair => pair.Item1).ToList();

            // Find a recent buffer that is visible.
            var i = 1;
            while (TryGetRecentWindow(i, out IWpfTextView textView))
            {
                if (windows.Contains(textView))
                {
                    textView.VisualElement.Focus();
                    return true;
                }
                ++i;
            }

            return false;
        }

        private bool TryGetRecentWindow(int n, out IWpfTextView textView)
        {
            textView = null;
            var vimBufferOption = _vim.TryGetRecentBuffer(n);
            if (vimBufferOption.IsSome() && vimBufferOption.Value.TextView is IWpfTextView wpfTextView)
            {
                textView = wpfTextView;
            }
            return false;
        }

        public bool GoToWindowCore(IWpfTextView currentTextView, int delta, bool wrap,
            IEnumerable<Tuple<IWpfTextView, Rect>> rawPairs)
        {
            var pairs = rawPairs.ToList();

            // Find the position of the current window in that list.
            var currentIndex = pairs.FindIndex(pair => pair.Item1 == currentTextView);
            if (currentIndex == -1)
            {
                return false;
            }

            var newIndex = currentIndex + delta;
            if (wrap)
            {
                // Wrap around to a valid index.
                newIndex = (newIndex % pairs.Count + pairs.Count) % pairs.Count;
            }
            else
            {
                // Move as far as possible in the specified direction.
                newIndex = Math.Max(0, newIndex);
                newIndex = Math.Min(newIndex, pairs.Count - 1);
            }

            // Go to the resulting window.
            pairs[newIndex].Item1.VisualElement.Focus();
            return true;
        }

        public override void GoToWindow(ITextView textView, WindowKind windowKind, int count)
        {
            const int maxCount = 1000;
            var currentTextView = textView as IWpfTextView;
            if (currentTextView == null)
            {
                return;
            }

            bool result;
            switch (windowKind)
            {
                case WindowKind.Up:
                    result = GoToWindowVertically(currentTextView, -count);
                    break;
                case WindowKind.Down:
                    result = GoToWindowVertically(currentTextView, count);
                    break;
                case WindowKind.Left:
                    result = GoToWindowHorizontally(currentTextView, -count);
                    break;
                case WindowKind.Right:
                    result = GoToWindowHorizontally(currentTextView, count);
                    break;

                case WindowKind.FarUp:
                    result = GoToWindowVertically(currentTextView, -maxCount);
                    break;
                case WindowKind.FarDown:
                    result = GoToWindowVertically(currentTextView, maxCount);
                    break;
                case WindowKind.FarLeft:
                    result = GoToWindowHorizontally(currentTextView, -maxCount);
                    break;
                case WindowKind.FarRight:
                    result = GoToWindowHorizontally(currentTextView, maxCount);
                    break;

                case WindowKind.Previous:
                    result = GoToWindowNext(currentTextView, -count, true);
                    break;
                case WindowKind.Next:
                    result = GoToWindowNext(currentTextView, count, true);
                    break;

                case WindowKind.Top:
                    result = GoToWindowNext(currentTextView, -maxCount, false);
                    break;
                case WindowKind.Bottom:
                    result = GoToWindowNext(currentTextView, maxCount, false);
                    break;

                case WindowKind.Recent:
                    result = GoToWindowRecent(currentTextView);
                    break;

                default:
                    throw Contract.GetInvalidEnumException(windowKind);
            }

            if (!result)
            {
                _vim.ActiveStatusUtil.OnError("Can't move focus");
            }
        }

        public override WordWrapStyles GetWordWrapStyle(ITextView textView)
        {
            var style = WordWrapStyles.WordWrap;
            switch (_vimApplicationSettings.WordWrapDisplay)
            {
                case WordWrapDisplay.All:
                    style |= (WordWrapStyles.AutoIndent | WordWrapStyles.VisibleGlyphs);
                    break;
                case WordWrapDisplay.Glyph:
                    style |= WordWrapStyles.VisibleGlyphs;
                    break;
                case WordWrapDisplay.AutoIndent:
                    style |= WordWrapStyles.AutoIndent;
                    break;
                default:
                    Contract.Assert(false);
                    break;
            }

            return style;
        }

        public override FSharpOption<int> GetNewLineIndent(ITextView textView, ITextSnapshotLine contextLine, ITextSnapshotLine newLine, IVimLocalSettings localSettings)
        {
            if (_vimApplicationSettings.UseEditorIndent)
            {
                var indent = _smartIndentationService.GetDesiredIndentation(textView, newLine);
                if (indent.HasValue)
                {
                    return FSharpOption.Create(indent.Value);
                }
                else
                {
                    // If the user wanted editor indentation but the editor doesn't support indentation
                    // even though it proffers an indentation service then fall back to what auto
                    // indent would do if it were enabled (don't care if it actually is)
                    //
                    // Several editors like XAML offer the indentation service but don't actually 
                    // provide information.  User clearly wants indent there since the editor indent
                    // is enabled.  Do a best effort and us Vim style indenting
                    return FSharpOption.Create(EditUtil.GetAutoIndent(contextLine, localSettings.TabStop));
                }
            }

            return FSharpOption<int>.None;
        }

        public override bool GoToGlobalDeclaration(ITextView textView, string target)
        {
            // The difference between global and local declarations in vim is a
            // heuristic one that is irrelevant when using a language service
            // that precisely understands the semantics of the program being
            // edited.
            //
            // At the semantic level, local variables have local declarations
            // and global variables have global declarations, and so it is
            // never ambiguous whether the given variable or function is local
            // or global. It is only at the syntactic level that ambiguity
            // could arise.
            return GoToDeclaration(textView, target);
        }

        public override bool GoToLocalDeclaration(ITextView textView, string target)
        {
            return GoToDeclaration(textView, target);
        }

        private bool GoToDeclaration(ITextView textView, string target)
        {
            // The 'Edit.GoToDeclaration' is not widely implemented (for
            // example, C# does not implement it), and so we use
            // 'Edit.GoToDefinition' unless we are sure the language service
            // supports declarations.
            if (textView.TextBuffer.ContentType.IsCPlusPlus())
            {
                return SafeExecuteCommand(textView, CommandNameGoToDeclaration, target);
            }
            else
            {
                return SafeExecuteCommand(textView, CommandNameGoToDefinition, target);
            }
        }

        public override void VimCreated(IVim vim)
        {
            _vim = vim;
            SettingsSource.Initialize(vim.GlobalSettings, _vimApplicationSettings);
        }

        public override void VimRcLoaded(VimRcState vimRcState, IVimLocalSettings localSettings, IVimWindowSettings windowSettings)
        {
            if (vimRcState.IsLoadFailed)
            {
                // If we failed to load a vimrc file then we should add a couple of sanity 
                // settings.  Otherwise the Visual Studio experience wont't be what users expect
                localSettings.AutoIndent = true;
            }
        }

        public override bool ShouldCreateVimBuffer(ITextView textView)
        {
            if (textView.IsPeekView())
            {
                return true;
            }

            if (_vsAdapter.IsWatchWindowView(textView))
            {
                return false;
            }

            if (!_vsAdapter.IsTextEditorView(textView))
            {
                return false;
            }

            var result = _extensionAdapterBroker.ShouldCreateVimBuffer(textView);
            if (result.HasValue)
            {
                return result.Value;
            }

            if (!base.ShouldCreateVimBuffer(textView))
            {
                return false;
            }

            return !DisableVimBufferCreation;
        }

        public override bool ShouldIncludeRcFile(VimRcPath vimRcPath)
        {
            switch (_vimApplicationSettings.VimRcLoadSetting)
            {
                case VimRcLoadSetting.None:
                    return false;
                case VimRcLoadSetting.VimRc:
                    return vimRcPath.VimRcKind == VimRcKind.VimRc;
                case VimRcLoadSetting.VsVimRc:
                    return vimRcPath.VimRcKind == VimRcKind.VsVimRc;
                case VimRcLoadSetting.Both:
                    return true;
                default:
                    Contract.Assert(false);
                    return base.ShouldIncludeRcFile(vimRcPath);
            }
        }

#region IVsSelectionEvents

        int IVsSelectionEvents.OnCmdUIContextChanged(uint dwCmdUICookie, int fActive)
        {
            return VSConstants.S_OK;
        }

        int IVsSelectionEvents.OnElementValueChanged(uint elementid, object varValueOld, object varValueNew)
        {
            var id = (VSConstants.VSSELELEMID)elementid;
            if (id == VSConstants.VSSELELEMID.SEID_WindowFrame)
            {
                ITextView getTextView(object obj)
                {
                    var vsWindowFrame = obj as IVsWindowFrame;
                    if (vsWindowFrame == null)
                    {
                        return null;
                    }

                    var vsCodeWindow = vsWindowFrame.GetCodeWindow();
                    if (vsCodeWindow.IsError)
                    {
                        return null;
                    }

                    var lastActiveTextView = vsCodeWindow.Value.GetLastActiveView(_vsAdapter.EditorAdapter);
                    if (lastActiveTextView.IsError)
                    {
                        return null;
                    }

                    return lastActiveTextView.Value;
                }

                ITextView oldView = getTextView(varValueOld);
                ITextView newView = null;
                if (ErrorHandler.Succeeded(_vsMonitorSelection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out object value)))
                {
                    newView = getTextView(value);
                }

                RaiseActiveTextViewChanged(
                    oldView == null ? FSharpOption<ITextView>.None : FSharpOption.Create<ITextView>(oldView),
                    newView == null ? FSharpOption<ITextView>.None : FSharpOption.Create<ITextView>(newView));
            }

            return VSConstants.S_OK;
        }

        int IVsSelectionEvents.OnSelectionChanged(IVsHierarchy pHierOld, uint itemidOld, IVsMultiItemSelect pMISOld, ISelectionContainer pSCOld, IVsHierarchy pHierNew, uint itemidNew, IVsMultiItemSelect pMISNew, ISelectionContainer pSCNew)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeSave(uint docCookie)
        {
            if (_vsAdapter.GetTextBufferForDocCookie(docCookie).TryGetValue(out ITextBuffer buffer))
            {
                RaiseBeforeSave(buffer);
            }
            return VSConstants.S_OK;
        }

#endregion
    }
}
