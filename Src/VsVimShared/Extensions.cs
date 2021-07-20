using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media;
using EnvDTE;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Vim;
using Vim.Extensions;
using DteCommand = EnvDTE.Command;

namespace Vim.VisualStudio
{
    public static class Extensions
    {
        #region Command

        public static bool TryGetCommandId(this DteCommand command, out CommandId commandId)
        {
            try
            {
                var group = Guid.Parse(command.Guid);
                var id = unchecked((uint)command.ID);
                commandId = new CommandId(group, id);
                return true;
            }
            catch
            {
                commandId = default;
                return false;
            }
        }

        /// <summary>
        /// Get the binding strings for this Command.  Digs through the various ways a 
        /// binding string can be stored and returns a uniform result
        /// </summary>
        public static IEnumerable<string> GetBindings(this DteCommand command, out Exception ex)
        {
            if (null == command)
            {
                throw new ArgumentException(nameof(command));
            }

            ex = null;
            object bindings;
            try
            {
                bindings = command.Bindings;
            }
            catch (Exception ex2)
            {
                // Several user reports indicate the above call can throw.  Most commonly
                // this throws an OutOfMemoryException.  Either way we don't care what the
                // error is.  We just can't get bindings for this element
                ex = ex2;
                return Enumerable.Empty<string>();
            }

            if (bindings is object[] bindingsArray)
            {
                return bindingsArray
                    .Where(x => x is string)
                    .Cast<string>()
                    .Where(x => !string.IsNullOrEmpty(x));
            }

            if (bindings is string singleBinding)
            {
                return Enumerable.Repeat(singleBinding, 1);
            }

            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Get the binding strings for this Command.  Digs through the various ways a 
        /// binding string can be stored and returns a uniform result
        /// </summary>
        public static IEnumerable<string> GetBindings(this DteCommand command)
        {
            return GetBindings(command, out Exception unused);
        }

        /// <summary>
        /// Get the binding strings in the form of CommandKeyBinding instances
        /// </summary>
        public static IEnumerable<CommandKeyBinding> GetCommandKeyBindings(this DteCommand command)
        {
            if (null == command)
            {
                throw new ArgumentNullException("command");
            }

            // Need a helper method here so that the argument checking is prompt
            return GetCommandKeyBindingsHelper(command);
        }

        private static IEnumerable<CommandKeyBinding> GetCommandKeyBindingsHelper(DteCommand command)
        {
            if (!command.TryGetCommandId(out CommandId commandId))
            {
                yield break;
            }

            foreach (var cur in command.GetBindings())
            {
                if (KeyBinding.TryParse(cur, out KeyBinding binding))
                {
                    var name = command.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        name = $"<Unnamed> {commandId.Id}";
                    }

                    yield return new CommandKeyBinding(commandId, name, binding);
                }
            }
        }

        public static IEnumerable<KeyBinding> GetKeyBindings(this DteCommand command)
        {
            return GetCommandKeyBindings(command).Select(x => x.KeyBinding);
        }

        /// <summary>
        /// Does the Command have the provided KeyBinding as a valid binding
        /// </summary>
        public static bool HasKeyBinding(this DteCommand command, KeyBinding binding)
        {
            return GetCommandKeyBindings(command).Any(x => x.KeyBinding == binding);
        }

        /// <summary>
        /// Remove all bindings on the provided Command value
        /// </summary>
        /// <param name="command"></param>
        public static void SafeResetBindings(this DteCommand command)
        {
            try
            {
                command.Bindings = new object[] { };
            }
            catch (COMException)
            {
                // Several implementations, Transact SQL in particular, return E_FAIL for this
                // operation.  Simply ignore the failure and continue
            }
        }

        /// <summary>
        /// Safely reset the bindings on this Command to the provided KeyBinding value
        /// </summary>
        public static void SafeSetBindings(this DteCommand command, KeyBinding binding)
        {
            SafeSetBindings(command, new[] { binding.CommandString });
        }

        /// <summary>
        /// Safely reset the keyboard bindings on this Command to the provided values
        /// </summary>
        public static void SafeSetBindings(this DteCommand command, IEnumerable<string> commandBindings)
        {
            try
            {
                var bindings = commandBindings.Cast<object>().ToArray();
                command.Bindings = bindings;

                // There are certain commands in Visual Studio which simply don't want to have their
                // keyboard bindings removed.  The only way to get them to relinquish control is to
                // ask them to remove the bindings twice.  
                //
                // One example of this is SolutionExplorer.OpenFilesFilter.  It has bindings for both
                // "Ctrl-[, O" and "Ctrl-[, Ctrl-O".  Asking it to remove all bindings will remove one
                // but not both (at least until you restart Visual Studio, then both will be gone).  If
                // we ask it to remove bindings twice though then it will behave as expected.  
                if (bindings.Length == 0 && command.GetBindings().Count() != 0)
                {
                    command.Bindings = bindings;
                }
            }
            catch (Exception)
            {
                // Several implementations, Transact SQL in particular, return E_FAIL for this
                // operation.  Simply ignore the failure and continue
            }
        }

        #endregion

        #region IVsTextLines

        /// <summary>
        /// Get the file name of the presented view.  If the name cannot be discovered an empty string will be returned
        /// </summary>
        public static string GetFileName(this IVsTextLines lines)
        {
            // GUID_VsBufferMoniker
            var monikerId = VsVimConstants.VsUserDataFileNameMoniker;
            if (lines is IVsUserData userData && 
                VSConstants.S_OK == userData.GetData(ref monikerId, out object data))
            {
                var strData = data as string;
                return !string.IsNullOrEmpty(strData)
                    ? strData
                    : string.Empty;
            }

            return string.Empty;
        }

        public static Result<IVsEnumLineMarkers> GetLineMarkersEnum(this IVsTextLines lines, TextSpan span)
        {
            var hresult = lines.EnumMarkers(span.iStartLine, span.iStartIndex, span.iEndLine, span.iEndIndex, 0, (uint)ENUMMARKERFLAGS.EM_ALLTYPES, out IVsEnumLineMarkers markers);
            return Result.CreateSuccessOrError(markers, hresult);
        }

        public static List<IVsTextLineMarker> GetLineMarkers(this IVsTextLines lines, TextSpan span)
        {
            var markers = GetLineMarkersEnum(lines, span);
            return markers.IsSuccess
                ? markers.Value.GetAll()
                : new List<IVsTextLineMarker>();
        }

        #endregion

        #region IVsTextView

        public static Result<IVsTextLines> GetTextLines(this IVsTextView textView)
        {
            var hresult = textView.GetBuffer(out IVsTextLines textLines);
            return Result.CreateSuccessOrError(textLines, hresult);
        }

        public static Result<IVsWindowFrame> GetWindowFrame(this IVsTextView textView)
        {
            var textViewEx = textView as IVsTextViewEx;
            if (textViewEx == null)
            {
                return Result.Error;
            }

            return textViewEx.GetWindowFrame();
        }

        #endregion

        #region IVsTextViewEx

        public static Result<IVsWindowFrame> GetWindowFrame(this IVsTextViewEx textViewEx)
        {
            if (!ErrorHandler.Succeeded(textViewEx.GetWindowFrame(out object frame)))
            {
                return Result.Error;
            }

            var vsWindowFrame = frame as IVsWindowFrame;
            if (vsWindowFrame == null)
            {
                return Result.Error;
            }

            return Result.CreateSuccess(vsWindowFrame);
        }

        #endregion

        #region IVsEditorAdaptersFactoryService

        /// <summary>
        /// The GetWpftextView method can throw for a lot of reasons that aren't of 
        /// consequence to our code.  In particular if the IVsTextView isn't implemented by
        /// the editor shims.  Don't care, just want an IWpfTextView if it's available
        /// </summary>
        public static IWpfTextView GetWpfTextViewNoThrow(this IVsEditorAdaptersFactoryService editorAdapter, IVsTextView vsTextView)
        {
            try
            {
                return editorAdapter.GetWpfTextView(vsTextView);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region IVsShell

        internal static bool IsPackageInstalled(this IVsShell vsShell, Guid packageId)
        {
            return ErrorHandler.Succeeded(vsShell.IsPackageInstalled(ref packageId, out int isInstalled)) && 1 == isInstalled;
        }

        internal static bool IsInModalState(this IVsShell vsShell)
        {
            if (ErrorHandler.Failed(vsShell.GetProperty((int)__VSSPROPID4.VSSPROPID_IsModal, out object value)) ||
                !(value is bool))
            {
                return false;
            }

            return (bool)value;
        }

        #endregion

        #region IVsUIShell

        private sealed class ModelessUtil : IDisposable
        {
            private readonly IVsUIShell _vsShell;
            internal ModelessUtil(IVsUIShell vsShell)
            {
                _vsShell = vsShell;
                vsShell.EnableModeless(0);
            }
            public void Dispose()
            {
                _vsShell.EnableModeless(-1);
            }
        }

        public static IDisposable EnableModelessDialog(this IVsUIShell vsShell)
        {
            return new ModelessUtil(vsShell);
        }

        public static Result<List<IVsWindowFrame>> GetDocumentWindowFrames(this IVsUIShell vsShell)
        {
            var hr = vsShell.GetDocumentWindowEnum(out IEnumWindowFrames enumFrames);
            return ErrorHandler.Failed(hr) ? Result.CreateError(hr) : enumFrames.GetContents();
        }

        public static Result<List<IVsWindowFrame>> GetDocumentWindowFrames(this IVsUIShell4 vsShell, __WindowFrameTypeFlags flags)
        {
            var hr = vsShell.GetWindowEnum((uint)flags, out IEnumWindowFrames enumFrames);
            return ErrorHandler.Failed(hr) ? Result.CreateError(hr) : enumFrames.GetContents();
        }

        #endregion

        #region IEnumWindowFrames

        public static Result<List<IVsWindowFrame>> GetContents(this IEnumWindowFrames enumFrames)
        {
            var list = new List<IVsWindowFrame>();
            var array = new IVsWindowFrame[16];
            while (true)
            {
                var hr = enumFrames.Next((uint)array.Length, array, out uint num);
                if (ErrorHandler.Failed(hr))
                {
                    return Result.CreateError(hr);
                }

                if (0 == num)
                {
                    return list;
                }

                for (var i = 0; i < num; i++)
                {
                    list.Add(array[i]);
                }
            }
        }

        #endregion

        #region IVsCodeWindow

        /// <summary>
        /// Get the primary view of the code window.  Is actually the one on bottom
        /// </summary>
        public static Result<IVsTextView> GetPrimaryView(this IVsCodeWindow vsCodeWindow)
        {
            var hr = vsCodeWindow.GetPrimaryView(out IVsTextView vsTextView);
            if (ErrorHandler.Failed(hr))
            {
                return Result.CreateError(hr);
            }

            return Result.CreateSuccessNonNull(vsTextView);
        }

        /// <summary>
        /// Get the primary view of the code window.  Is actually the one on bottom
        /// </summary>
        public static Result<IWpfTextView> GetPrimaryTextView(this IVsCodeWindow codeWindow, IVsEditorAdaptersFactoryService factoryService)
        {
            var result = GetPrimaryView(codeWindow);
            if (result.IsError)
            {
                return Result.CreateError(result.HResult);
            }

            var textView = factoryService.GetWpfTextViewNoThrow(result.Value);
            return Result.CreateSuccessNonNull(textView);
        }

        /// <summary>
        /// Get the last active view of the code window.  
        /// </summary>
        public static Result<IVsTextView> GetLastActiveView(this IVsCodeWindow vsCodeWindow)
        {
            var hr = vsCodeWindow.GetLastActiveView(out IVsTextView vsTextView);
            if (ErrorHandler.Failed(hr))
            {
                return Result.CreateError(hr);
            }

            return Result.CreateSuccessNonNull(vsTextView);
        }

        /// <summary>
        /// Get the last active view of the code window
        /// </summary>
        public static Result<IWpfTextView> GetLastActiveView(this IVsCodeWindow codeWindow, IVsEditorAdaptersFactoryService factoryService)
        {
            var result = GetLastActiveView(codeWindow);
            if (result.IsError)
            {
                return Result.CreateError(result.HResult);
            }

            var textView = factoryService.GetWpfTextViewNoThrow(result.Value);
            return Result.CreateSuccessNonNull(textView);
        }

        /// <summary>
        /// Is this window currently in a split mode?
        /// </summary>
        public static bool IsSplit(this IVsCodeWindow vsCodeWindow)
        {
            return
                vsCodeWindow.GetPrimaryView().IsSuccess &&
                vsCodeWindow.GetSecondaryView().IsSuccess;
        }

        /// <summary>
        /// Get the secondary view of the code window.  Is actually the one on top
        /// </summary>
        public static Result<IWpfTextView> GetSecondaryTextView(this IVsCodeWindow codeWindow, IVsEditorAdaptersFactoryService factoryService)
        {
            var result = GetSecondaryView(codeWindow);
            if (result.IsError)
            {
                return Result.CreateError(result.HResult);
            }

            var textView = factoryService.GetWpfTextViewNoThrow(result.Value);
            return Result.CreateSuccessNonNull(textView);
        }

        /// <summary>
        /// Get the secondary view of the code window.  Is actually the one on top
        /// </summary>
        public static Result<IVsTextView> GetSecondaryView(this IVsCodeWindow vsCodeWindow)
        {
            var hr = vsCodeWindow.GetSecondaryView(out IVsTextView vsTextView);
            if (ErrorHandler.Failed(hr))
            {
                return Result.CreateError(hr);
            }

            return Result.CreateSuccessNonNull(vsTextView);
        }

        #endregion

        #region IVsWindowFrame

        public static Result<IVsCodeWindow> GetCodeWindow(this IVsWindowFrame vsWindowFrame)
        {
            var iid = typeof(IVsCodeWindow).GUID;
            var ptr = IntPtr.Zero;
            try
            {
                var hr = vsWindowFrame.QueryViewInterface(ref iid, out ptr);
                if (ErrorHandler.Failed(hr))
                {
                    return Result.CreateError(hr);
                }

                return Result.CreateSuccess((IVsCodeWindow)Marshal.GetObjectForIUnknown(ptr));
            }
            catch (Exception e)
            {
                // Venus will throw when querying for the code window
                return Result.CreateError(e);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.Release(ptr);
                }
            }
        }

        public static Result<IVsTextLines> GetTextLines(this IVsWindowFrame vsWindowFrame)
        {
            try
            {
                var vsCodeWindow = vsWindowFrame.GetCodeWindow().Value;

                ErrorHandler.ThrowOnFailure(vsCodeWindow.GetBuffer(out IVsTextLines vsTextLines));
                if (vsTextLines == null)
                {
                    return Result.Error;
                }

                return Result.CreateSuccess(vsTextLines);
            }
            catch (Exception ex)
            {
                return Result.CreateError(ex);
            }
        }

        public static Result<ITextBuffer> GetTextBuffer(this IVsWindowFrame vsWindowFrame, IVsEditorAdaptersFactoryService factoryService)
        {
            var result = GetTextLines(vsWindowFrame);
            if (result.IsError)
            {
                return Result.CreateError(result.HResult);
            }

            var vsTextLines = result.Value;
            var textBuffer = factoryService.GetDocumentBuffer(vsTextLines);
            if (textBuffer == null)
            {
                return Result.Error;
            }

            return Result.CreateSuccess(textBuffer);
        }

        /// <summary>
        /// IVsWindowFrame which contains this IVsWindowFrame.  They are allowed to nested arbitrarily 
        /// </summary>
        public static bool TryGetParent(this IVsWindowFrame vsWindowFrame, out IVsWindowFrame parentWindowFrame)
        {
            try
            {
                var hresult = vsWindowFrame.GetProperty((int)__VSFPROPID2.VSFPROPID_ParentFrame, out object parentObj);
                if (!ErrorHandler.Succeeded(hresult))
                {
                    parentWindowFrame = null;
                    return false;
                }

                parentWindowFrame = parentObj as IVsWindowFrame;
                return parentWindowFrame != null;
            }
            catch (Exception)
            {
                parentWindowFrame = null;
                return false;
            }
        }

        /// <summary>
        /// IVsWindowFrame instances can nest within each other.  This method will get the top most IVsWindowFrame
        /// in the nesting
        /// </summary>
        public static IVsWindowFrame GetTopMost(this IVsWindowFrame vsWindowFrame)
        {
            if (vsWindowFrame.TryGetParent(out IVsWindowFrame parent))
            {
                return GetTopMost(parent);
            }

            return vsWindowFrame;
        }

        #endregion

        #region IVsTextManager

        public static Tuple<bool, IWpfTextView> TryGetActiveTextView(this IVsTextManager vsTextManager, IVsEditorAdaptersFactoryService factoryService)
        {
            IWpfTextView textView = null;
            if (ErrorHandler.Succeeded(vsTextManager.GetActiveView(0, null, out IVsTextView vsTextView)) && vsTextView != null)
            {
                textView = factoryService.GetWpfTextViewNoThrow(vsTextView);
            }

            return Tuple.Create(textView != null, textView);
        }

        #endregion

        #region IVsEnumLineMarkers

        /// <summary>
        /// Don't be tempted to make this an IEnumerable because multiple calls would not
        /// produce multiple enumerations since the parameter would need to be reset
        /// </summary>
        public static List<IVsTextLineMarker> GetAll(this IVsEnumLineMarkers markers)
        {
            var list = new List<IVsTextLineMarker>();
            do
            {
                var hresult = markers.Next(out IVsTextLineMarker marker);
                if (ErrorHandler.Succeeded(hresult) && marker != null)
                {
                    list.Add(marker);
                }
                else
                {
                    break;
                }
            } while (true);

            return list;
        }

        #endregion

        #region IVsTextLineMarker

        public static Result<TextSpan> GetCurrentSpan(this IVsTextLineMarker marker)
        {
            var array = new TextSpan[1];
            var hresult = marker.GetCurrentSpan(array);
            return Result.CreateSuccessOrError(array[0], hresult);
        }

        public static Result<SnapshotSpan> GetCurrentSpan(this IVsTextLineMarker marker, ITextSnapshot snapshot)
        {
            var span = GetCurrentSpan(marker);
            return span.IsError ? Result.CreateError(span.HResult) : span.Value.ToSnapshotSpan(snapshot);
        }

        public static Result<MARKERTYPE> GetMarkerType(this IVsTextLineMarker marker)
        {
            var hresult = marker.GetType(out int type);
            return Result.CreateSuccessOrError((MARKERTYPE)type, hresult);
        }

        #endregion

        #region IVsSnippetManager



        #endregion

        #region IVsMonitorSelection

        public static Result<bool> IsCmdUIContextActive(this IVsMonitorSelection selection, Guid cmdId)
        {
            var hresult = selection.GetCmdUIContextCookie(ref cmdId, out uint cookie);
            if (ErrorHandler.Failed(hresult))
            {
                return Result.CreateError(hresult);
            }

            hresult = selection.IsCmdUIContextActive(cookie, out int active);
            return Result.CreateSuccessOrError(active != 0, hresult);
        }

        #endregion

        #region IServiceProvider

        public static TInterface GetService<TService, TInterface>(this System.IServiceProvider sp)
        {
            return (TInterface)sp.GetService(typeof(TService));
        }

        #endregion

        #region IContentType

        /// <summary>
        /// Does this IContentType represent C++
        /// </summary>
        public static bool IsCPlusPlus(this IContentType contentType)
        {
            return contentType.IsOfType(VsVimConstants.CPlusPlusContentType);
        }

        /// <summary>
        /// Does this IContentType represent C#
        /// </summary>
        public static bool IsCSharp(this IContentType contentType)
        {
            return contentType.IsOfType(VsVimConstants.CSharpContentType);
        }

        public static bool IsFSharp(this IContentType contentType)
        {
            return contentType.IsOfType("F#");
        }

        public static bool IsVisualBasic(this IContentType contentType)
        {
            return contentType.IsOfType("Basic");
        }

        public static bool IsJavaScript(this IContentType contentType)
        {
            return contentType.IsOfType("JavaScript");
        }

        public static bool IsResJSON(this IContentType contentType)
        {
            return contentType.IsOfType("ResJSON");
        }

        public static bool IsHTMLXProjection(this IContentType contentType)
        {
            return contentType.IsOfType("HTMLXProjection");
        }

        /// <summary>
        /// Is this IContentType of any of the specified types
        /// </summary>
        public static bool IsOfAnyType(this IContentType contentType, IEnumerable<string> types)
        {
            foreach (var type in types)
            {
                if (contentType.IsOfType(type))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region IDisplayWindowBroker

        /// <summary>
        /// Are any of the standard displays currently active?
        /// </summary>
        public static bool IsAnyDisplayActive(this IDisplayWindowBroker displayWindowBroker)
        {
            return
                displayWindowBroker.IsCompletionActive ||
                displayWindowBroker.IsQuickInfoActive ||
                displayWindowBroker.IsSignatureHelpActive;
        }

        #endregion

        #region ITaggerProvider

        /// <summary>
        /// Creating an ITagger for an ITaggerProvider can fail in a number of ways.  Wrap them
        /// all up here 
        /// </summary>
        public static Result<ITagger<T>> SafeCreateTagger<T>(this ITaggerProvider taggerProvider, ITextBuffer textbuffer)
            where T : ITag
        {
            try
            {
                var tagger = taggerProvider.CreateTagger<T>(textbuffer);
                if (tagger == null)
                {
                    return Result.Error;
                }

                return Result.CreateSuccess(tagger);
            }
            catch (Exception e)
            {
                return Result.CreateError(e);
            }
        }

        #endregion

        #region ITextView

        /// <summary>
        /// This will return the SnapshotSpan values from the EditBuffer which are actually visible
        /// on the screen.
        /// </summary>
        public static NormalizedSnapshotSpanCollection GetVisibleSnapshotSpans(this ITextView textView)
        {
            var bufferGraph = textView.BufferGraph;
            var visualSnapshot = textView.VisualSnapshot;
            var formattedSpan = textView.TextViewLines.GetFormattedSpan();
            if (formattedSpan.IsError)
            {
                return new NormalizedSnapshotSpanCollection();
            }

            var visualSpans = bufferGraph.MapUpToSnapshot(formattedSpan.Value, SpanTrackingMode.EdgeExclusive, visualSnapshot);
            if (visualSpans.Count != 1)
            {
                return visualSpans;
            }

            return bufferGraph.MapDownToSnapshot(visualSpans.Single(), SpanTrackingMode.EdgeExclusive, textView.TextSnapshot);
        }

        /// <summary>
        /// Get the set of all visible SnapshotSpan values and potentially some collapsed / invisible
        /// ones.  It's common for users to have collapsed / invisible regions on the ITextView and 
        /// hence simply getting a single SnapshotSpan for the set of visible text could return a huge
        /// span (potentially many, many thousands) of lines.
        /// 
        /// This method attempts to return the set of SnapshotSpan values which actually have visible
        /// text associated with them.
        /// </summary>
        public static NormalizedSnapshotSpanCollection GetLikelyVisibleSnapshotSpans(this ITextView textView)
        {
            // Mapping up and down is potentially expensive so we don't want to do it unless we have to.  Implement
            // a heuristic to check for large sections of invisible text and if it's not present then just return
            // the value which doesn't require allocations or deep calculations
            var result = textView.TextViewLines.GetFormattedSpan();
            if (result.IsError)
            {
                return new NormalizedSnapshotSpanCollection();
            }

            var formattedSpan = result.Value;
            var formattedLength = formattedSpan.Length;
            if (formattedLength / 2 <= textView.VisualSnapshot.Length)
            {
                return new NormalizedSnapshotSpanCollection(formattedSpan);
            }

            return GetVisibleSnapshotSpans(textView);
        }

        public static bool IsPeekView(this ITextView textView) => textView.Roles.Contains(VsVimConstants.TextViewRoleEmbeddedPeekTextView);

        /// <summary>
        /// When the provided <see cref="ITextView"/> is a peek view, return the <see cref="ITextView" /> that is hosting
        /// it.
        /// </summary>
        public static bool TryGetPeekViewHostView(this ITextView peekView, out ITextView hostView) => peekView.Properties.TryGetPropertySafe("PeekContainingTextView", out hostView);

        #endregion

        #region IWpfTextView

        /// <summary>
        /// There is no way to query for an IAdornmentLayer which returns null on a missing layer.  There is 
        /// only the throwing version.  Wrap it here for the cases where we have to probe for a layer
        ///
        /// This is wrapped with DebuggerNonUserCode to prevent the Exception Assistant from popping up
        /// while running this method
        /// </summary>
        [DebuggerNonUserCode]
        public static IAdornmentLayer GetAdornmentLayerNoThrow(this IWpfTextView textView, string name, object key)
        {
            try
            {
                if (textView.Properties.TryGetPropertySafe(key, out string found) && StringComparer.Ordinal.Equals(name, found))
                {
                    return null;
                }
                return textView.GetAdornmentLayer(name);
            }
            catch
            {
                textView.Properties.AddProperty(key, name);
                return null;
            }
        }

        #endregion

        #region ITextViewLineCollection

        /// <summary>
        /// Get the SnapshotSpan for the ITextViewLineCollection.  This can throw when the ITextView is being
        /// laid out so we wrap the try / catch here
        /// </summary>
        public static Result<SnapshotSpan> GetFormattedSpan(this ITextViewLineCollection collection)
        {
            try
            {
                return collection.FormattedSpan;
            }
            catch (Exception ex)
            {
                return Result.CreateError(ex);
            }
        }

        #endregion

        #region ITextSnapshot

        public static Result<SnapshotSpan> ToSnapshotSpan(this TextSpan span, ITextSnapshot snapshot)
        {
            try
            {
                var start = snapshot.GetLineFromLineNumber(span.iStartLine).Start.Add(span.iStartIndex);
                var end = snapshot.GetLineFromLineNumber(span.iEndLine).Start.Add(span.iEndIndex + 1);
                return new SnapshotSpan(start, end);
            }
            catch (Exception ex)
            {
                return Result.CreateError(ex);
            }
        }

        #endregion

        #region SnapshotSpan

        public static TextSpan ToTextSpan(this SnapshotSpan span)
        {
            var start = SnapshotPointUtil.GetLineNumberAndOffset(span.Start);
            var option = SnapshotSpanUtil.GetLastIncludedPoint(span);
            var end = option.IsSome()
                ? SnapshotPointUtil.GetLineNumberAndOffset(option.Value)
                : start;
            return new TextSpan
            {
                iStartLine = start.Item1,
                iStartIndex = start.Item2,
                iEndLine = end.Item1,
                iEndIndex = end.Item2
            };
        }

        public static Result<SnapshotSpan> SafeTranslateTo(this SnapshotSpan span, ITextSnapshot snapshot, SpanTrackingMode mode)
        {
            try
            {
                return span.TranslateTo(snapshot, mode);
            }
            catch (Exception ex)
            {
                return Result.CreateError(ex);
            }
        }

        #endregion

        #region SnapshotLineRange

        public static TextSpan ToTextSpan(this SnapshotLineRange range)
        {
            return range.Extent.ToTextSpan();
        }

        public static TextSpan ToTextSpanIncludingLineBreak(this SnapshotLineRange range)
        {
            return range.ExtentIncludingLineBreak.ToTextSpan();
        }

        #endregion

        #region _DTE

        public static IEnumerable<Project> GetProjects(this _DTE dte)
        {
            var list = dte.Solution.Projects;
            for (var i = 1; i <= list.Count; i++)
            {
                yield return list.Item(i);
            }
        }

        public static IEnumerable<ProjectItem> GetProjectItems(this _DTE dte, string fileName)
        {
            foreach (var cur in dte.GetProjects())
            {
                if (cur.TryGetProjectItem(fileName, out ProjectItem item))
                {
                    yield return item;
                }
            }
        }

        public static IEnumerable<DteCommand> GetCommands(this _DTE dte)
        {
            try
            {
                return GetCommandsCore();
            }
            catch (MissingMethodException)
            {
                return Enumerable.Empty<DteCommand>();
            }


            [MethodImpl(MethodImplOptions.NoInlining)]
            IEnumerable<DteCommand> GetCommandsCore() => dte.Commands.Cast<DteCommand>();
        }

        #endregion

        #region Project

        public static IEnumerable<ProjectItem> GetProjecItems(this Project project)
        {
            var items = project.ProjectItems;
            for (var i = 1; i <= items.Count; i++)
            {
                yield return items.Item(i);
            }
        }

        public static bool TryGetProjectItem(this Project project, string fileName, out ProjectItem item)
        {
            try
            {
                item = project.ProjectItems.Item(fileName);
                return true;
            }
            catch (ArgumentException)
            {
                item = null;
                return false;
            }
        }

        #endregion

        #region ObservableCollection<T>

        public static void AddRange<T>(this ObservableCollection<T> col, IEnumerable<T> enumerable)
        {
            foreach (var cur in enumerable)
            {
                col.Add(cur);
            }
        }

        #endregion

        #region IEnumerable<T>

        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> del)
        {
            foreach (var cur in enumerable)
            {
                del(cur);
            }
        }

        public static IEnumerable<T> GetValues<T>(this IEnumerable<Result<T>> enumerable)
        {
            foreach (var cur in enumerable)
            {
                if (cur.IsSuccess)
                {
                    yield return cur.Value;
                }
            }
        }

        #endregion

        #region FSharpOption<T>

        /// <summary>
        /// Is the F# option both Some and equal to the provided value?
        /// </summary>
        public static bool IsSome<T>(this FSharpOption<T> option, T value)
        {
            return option.IsSome() && EqualityComparer<T>.Default.Equals(option.Value, value);
        }

        #endregion

        #region IOleCommandTarget

        internal static int Exec(this IOleCommandTarget oleCommandTarget, KeyInput keyInput)
        {
            var oleCommandData = OleCommandData.Empty;
            try
            {
                if (!OleCommandUtil.TryConvert(keyInput, out oleCommandData))
                {
                    return VSConstants.E_FAIL;
                }

                return oleCommandTarget.Exec(oleCommandData);
            }
            finally
            {
                oleCommandData.Dispose();
            }
        }

        internal static int Exec(this IOleCommandTarget oleCommandTarget, OleCommandData oleCommandData)
        {
            Guid commandGroup = oleCommandData.Group;
            return oleCommandTarget.Exec(
                ref commandGroup,
                oleCommandData.Id,
                oleCommandData.CommandExecOpt,
                oleCommandData.VariantIn,
                oleCommandData.VariantOut);
        }

        internal static int QueryStatus(this IOleCommandTarget oleCommandTarget, OleCommandData oleCommandData)
        {
            return QueryStatus(oleCommandTarget, oleCommandData, out OLECMD command);
        }

        internal static bool QueryStatus(this IOleCommandTarget oleCommandTarget, OleCommandData oleCommandData, OLECMDF status)
        {
            OLECMD command;
            var hr = QueryStatus(oleCommandTarget, oleCommandData, out command);
            if (hr != VSConstants.S_OK)
            {
                return false;
            }

            var ret = (OLECMDF)command.cmdf;
            return status == (ret & status);
        }

        internal static int QueryStatus(this IOleCommandTarget oleCommandTarget, OleCommandData oleCommandData, out OLECMD command)
        {
            var commandGroup = oleCommandData.Group;
            var cmds = new OLECMD[1];
            cmds[0] = new OLECMD { cmdID = oleCommandData.Id };
            var result = oleCommandTarget.QueryStatus(
                ref commandGroup,
                1,
                cmds,
                oleCommandData.VariantIn);
            command = cmds[0];
            return result;
        }

        #endregion

        #region IEditorFormatMap

        public static Color GetBackgroundColor(this IEditorFormatMap map, string name, Color defaultColor)
        {
            var properties = map.GetProperties(name);
            var key = EditorFormatDefinition.BackgroundColorId;
            var color = defaultColor;
            if (properties != null && properties.Contains(key))
            {
                color = (Color)properties[key];
            }

            return color;
        }

        public static Brush GetBackgroundBrush(this IEditorFormatMap map, string name, Color defaultColor)
        {
            var color = GetBackgroundColor(map, name, defaultColor);
            return new SolidColorBrush(color);
        }

        #endregion

        #region SVsServiceProvider

        public static WritableSettingsStore GetWritableSettingsStore(this SVsServiceProvider vsServiceProvider)
        {
            var shellSettingsManager = new ShellSettingsManager(vsServiceProvider);
            return shellSettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
        }

        #endregion

        #region IVsRunningDocumentTable

        /// <summary>
        /// Get the document cookies for the documents in the running document table
        /// </summary>
        /// <remarks>
        /// This method simple asks for the cookies and hence won't force the document to be loaded
        /// if it is being loaded in a lazy fashion
        /// </remarks>
        public static List<uint> GetRunningDocumentCookies(this IVsRunningDocumentTable runningDocumentTable)
        {
            var list = new List<uint>();
            if (!ErrorHandler.Succeeded(runningDocumentTable.GetRunningDocumentsEnum(out IEnumRunningDocuments enumDocuments)))
            {
                return list;
            }

            uint[] array = new uint[1];
            uint pceltFetched = 0;
            while (ErrorHandler.Succeeded(enumDocuments.Next(1, array, out pceltFetched)) && (pceltFetched == 1))
            {
                list.Add(array[0]);
            }

            return list;
        }

        #endregion
    }
}
