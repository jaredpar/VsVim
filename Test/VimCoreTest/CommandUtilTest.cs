using System;
using System.Linq;
using Vim.EditorHost;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Outlining;
using Moq;
using Vim.Extensions;
using Vim.UnitTest.Mock;
using Xunit;

namespace Vim.UnitTest
{
    public abstract class CommandUtilTest : VimTestBase
    {
        private MockRepository _factory;
        private MockVimHost _vimHost;
        private IMacroRecorder _macroRecorder;
        private TestableStatusUtil _statusUtil;
        private TestableBulkOperations _bulkOperations;
        private IVimGlobalSettings _globalSettings;
        private IVimLocalSettings _localSettings;
        private IVimWindowSettings _windowSettings;
        private IMotionUtil _motionUtil;
        private IVimData _vimData;
        private IWpfTextView _textView;
        private ITextBuffer _textBuffer;
        private IVimTextBuffer _vimTextBuffer;
        private IJumpList _jumpList;
        private readonly LocalMark _localMarkA = LocalMark.NewLetter(Letter.A);
        private IOutliningManager _outliningManager;
        internal CommandUtil _commandUtil;
        private ITextBuffer _lastSaved;
        private ITextView _lastClosed;

        protected void Create(params string[] lines)
        {
            _vimHost = (MockVimHost)Vim.VimHost;
            _textView = CreateTextView(lines);
            _textBuffer = _textView.TextBuffer;
            _vimTextBuffer = CreateVimTextBufferCore(_textBuffer);
            _localSettings = _vimTextBuffer.LocalSettings;

            var foldManager = CreateFoldManager(_textView);

            _factory = new MockRepository(MockBehavior.Loose);
            _statusUtil = new TestableStatusUtil();
            _bulkOperations = new TestableBulkOperations();

            var vimBufferData = CreateVimBufferData(
                _vimTextBuffer,
                _textView,
                statusUtil: _statusUtil);
            _jumpList = vimBufferData.JumpList;
            _windowSettings = vimBufferData.WindowSettings;

            _vimData = Vim.VimData;
            _macroRecorder = Vim.MacroRecorder;
            _globalSettings = Vim.GlobalSettings;

            var operations = CreateCommonOperations(vimBufferData);
            var lineChangeTracker = new LineChangeTracker(vimBufferData);
            _motionUtil = new MotionUtil(vimBufferData, operations);
            _commandUtil = new CommandUtil(
                vimBufferData,
                _motionUtil,
                operations,
                foldManager,
                new InsertUtil(vimBufferData, _motionUtil, operations),
                _bulkOperations,
                lineChangeTracker);

            var outliningManagerService = CompositionContainer.GetExportedValue<IOutliningManagerService>();
            _outliningManager = outliningManagerService.GetOutliningManager(_textView);

            VimHost.SaveFunc = textBuffer => 
            {
                _lastSaved = textBuffer;
                return true;
            };
            VimHost.CloseFunc = textView => { _lastClosed = textView; };
        }

        protected virtual IVimTextBuffer CreateVimTextBufferCore(ITextBuffer textBuffer)
        {
            return Vim.GetOrCreateVimTextBuffer(textBuffer);
        }

        protected static string CreateLinesWithLineBreak(params string[] lines)
        {
            return lines.Aggregate((x, y) => x + Environment.NewLine + y) + Environment.NewLine;
        }

        protected void SetLastCommand(NormalCommand command, int? count = null, RegisterName name = null)
        {
            var data = VimUtil.CreateCommandData(count, name);
            var storedCommand = StoredCommand.NewNormalCommand(command, data, CommandFlags.None);
            _vimData.LastCommand = FSharpOption.Create(storedCommand);
        }

        protected void AssertInsertWithTransaction(CommandResult result)
        {
            Assert.True(result.IsCompleted);
            var modeSwitch = result.AsCompleted().ModeSwitch;
            Assert.True(modeSwitch.IsSwitchModeWithArgument);
            var data = modeSwitch.AsSwitchModeWithArgument();
            Assert.Equal(ModeKind.Insert, data.ModeKind);
            Assert.True(data.ModeArgument.IsInsertWithTransaction);
        }

        /// <summary>
        /// Helper to call GetNumberValueAtCaret and dig into the NumberValue which we care about
        /// </summary>
        internal NumberValue GetNumberValueAtCaret()
        {
            var tuple = _commandUtil.GetNumberValueAtCaret();
            Assert.True(tuple.IsSome());
            return tuple.Value.Item1;
        }

        protected virtual IFoldManager CreateFoldManager(ITextView textView)
        {
            return FoldManagerFactory.GetFoldManager(_textView);
        }

        internal virtual ICommonOperations CreateCommonOperations(IVimBufferData vimBufferData)
        {
            return CommonOperationsFactory.GetCommonOperations(vimBufferData);
        }

        protected void RunNormalCommand(NormalCommand command)
        {
            var commandData = CommandData.Default;
            _commandUtil.RunNormalCommand(command, commandData);
        }

        protected void RunVisualCommand(VisualCommand command, VisualSpan visualSpan)
        {
            var commandData = CommandData.Default;
            _commandUtil.RunVisualCommand(command, commandData, visualSpan);
        }


        public sealed class CreateRegisterValueTest : CommandUtilTest
        {
            [WpfFact]
            public void Simple()
            {
                Create("");
                var value = _commandUtil.CreateRegisterValue(StringData.NewSimple("cat"), OperationKind.LineWise);
                Assert.Equal("cat" + Environment.NewLine, value.StringValue);
            }

            /// <summary>
            /// The function should only normalize line wise values.  Character wise values don't need 
            /// any transformations
            /// </summary>
            [WpfFact]
            public void IgnoreCharacterWise()
            {
                Create("");
                var value = _commandUtil.CreateRegisterValue(StringData.NewSimple("cat"), OperationKind.CharacterWise);
                Assert.Equal("cat", value.StringValue);
            }

            [WpfFact]
            public void IgnoreBlockValues()
            {
                Create("");
                var value = _commandUtil.CreateRegisterValue(VimUtil.CreateStringDataBlock("cat", "dog"), OperationKind.LineWise);
                Assert.Equal("cat" + Environment.NewLine + "dog", value.StringValue);
            }
        }

        public sealed class DisplayCharacterBytesTest : CommandUtilTest
        {
            private void Check(string expected)
            {
                _commandUtil.DisplayCharacterBytes();
                Assert.Equal(expected, _statusUtil.LastStatus);
            }

            [WpfFact]
            public void AsciiSimple()
            {
                Create("cat");
                Check("63");
            }

            [WpfFact]
            public void ZeroFillSimple()
            {
                Create("\t");
                Check("09");
            }

            [WpfFact]
            public void SurrogatePair()
            {
                Create("𠈓");
                Check("f0 a0 88 93");
            }

            [WpfFact]
            public void SurrogatePairLowSurrogae()
            {
                Create("𠈓");
                _textView.MoveCaretTo(1);
                Check("f0 a0 88 93");
            }

            [WpfFact]
            public void EndPoint()
            {
                Create("hello");
                _textView.MoveCaretTo(_textBuffer.CurrentSnapshot.Length);
                _commandUtil.DisplayCharacterBytes();
                Assert.Equal(1, VimHost.BeepCount);
            }
        }

        /// <summary>
        /// The majority of the fold functions are just pass throughs to the IFoldManager
        /// implementation.  Make sure they are actually passed through
        /// </summary>
        public sealed class FoldFunctions : CommandUtilTest
        {
            private Mock<IFoldManager> _foldManager;

            protected override IFoldManager CreateFoldManager(ITextView textView)
            {
                _foldManager = new Mock<IFoldManager>(MockBehavior.Strict);
                return _foldManager.Object;
            }

            [WpfFact]
            public void CloseAllFolds()
            {
                Create("");
                var span = _textBuffer.GetExtent();
                _foldManager.Setup(x => x.CloseAllFolds(span)).Verifiable();
                _commandUtil.CloseAllFolds();
                _foldManager.Verify();
            }

            [WpfFact]
            public void CloseAllFoldsInSelection()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                var visualSpan = VisualSpan.CreateForSpan(range.Extent, VisualKind.Character, tabStop: 4);
                _foldManager.Setup(x => x.CloseAllFolds(visualSpan.LineRange.Extent)).Verifiable();
                _commandUtil.CloseAllFoldsInSelection(visualSpan);
                _foldManager.Verify();
            }

            [WpfFact]
            public void CloseAllFoldsUnderCaret()
            {
                Create("cat", "dog", "tree");
                var span = new SnapshotSpan(_textView.GetCaretPoint(), 0);
                _foldManager.Setup(x => x.CloseAllFolds(span)).Verifiable();
                _commandUtil.CloseAllFoldsUnderCaret();
                _foldManager.Verify();
            }

            /// <summary>
            /// Implementation of the zc command should close a fold for every line in the 
            /// visual selection
            /// </summary>
            [WpfFact]
            public void CloseFoldInSelection()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                for (var i = 0; i < 2; i++)
                {
                    var point = _textBuffer.GetPointInLine(i, 0);
                    _foldManager.Setup(x => x.CloseFold(point, 1)).Verifiable();
                }
                _commandUtil.CloseFoldInSelection(VisualSpan.CreateForSpan(range.Extent, VisualKind.Character, tabStop: 4));
                _foldManager.Verify();
            }

            [WpfFact]
            public void CloseFoldUnderCaret()
            {
                Create("cat", "dog", "tree");
                _textView.MoveCaretTo(2);
                var point = _textView.GetCaretPoint();
                _foldManager.Setup(x => x.CloseFold(point, 3)).Verifiable();
                _commandUtil.CloseFoldUnderCaret(3);
                _foldManager.Verify();
            }

            [WpfFact]
            public void DeleteFoldUnderCaret()
            {
                Create("cat", "dog");
                var point = _textView.GetCaretPoint();
                _foldManager.Setup(x => x.DeleteFold(point)).Verifiable();
                _commandUtil.DeleteFoldUnderCaret();
                _foldManager.Verify();
            }

            [WpfFact]
            public void DeleteAllFoldsInBuffer()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetExtent();
                _foldManager.Setup(x => x.DeleteAllFolds(range)).Verifiable();
                _commandUtil.DeleteAllFoldsInBuffer();
                _foldManager.Verify();
            }

            [WpfFact]
            public void DeleteAllFoldsUnderCaret()
            {
                Create("cat", "dog", "tree");
                var range = new SnapshotSpan(_textView.GetCaretPoint(), 0);
                _foldManager.Setup(x => x.DeleteAllFolds(range)).Verifiable();
                _commandUtil.DeleteAllFoldsUnderCaret();
                _foldManager.Verify();
            }

            [WpfFact]
            public void DeleteAllFoldsInSelection()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                var visualSpan = VisualSpan.CreateForSpan(range.Extent, VisualKind.Character, tabStop: 4);
                _foldManager.Setup(x => x.DeleteAllFolds(visualSpan.LineRange.Extent)).Verifiable();
                _commandUtil.DeleteAllFoldInSelection(visualSpan);
                _foldManager.Verify();
            }

            [WpfFact]
            public void DeleteFoldInSelection()
            {
                Create("cat", "dog", "tree");
            }

            [WpfFact]
            public void FoldLines()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                _foldManager.Setup(x => x.CreateFold(range)).Verifiable();
                _commandUtil.FoldLines(2);
                _foldManager.Verify();
            }

            [WpfFact]
            public void FoldMotion()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                var motionResult = MotionResult.Create(range.Extent, MotionKind.CharacterWiseExclusive, isForward: true);
                _foldManager.Setup(x => x.CreateFold(range)).Verifiable();
                _commandUtil.FoldMotion(motionResult);
                _foldManager.Verify();
            }

            [WpfFact]
            public void FoldSelection()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                var visualSpan = VisualSpan.CreateForSpan(range.Extent, VisualKind.Character, tabStop: 4);
                _foldManager.Setup(x => x.CreateFold(range)).Verifiable();
                _commandUtil.FoldSelection(visualSpan);
                _foldManager.Verify();
            }
        }

        /// <summary>
        /// Test the functions that mostly just pass through to the ICommonOperations
        /// implementation
        /// </summary>
        public sealed class CommonOperationsFunctions : CommandUtilTest
        {
            private Mock<ICommonOperations> _commonOperations;

            internal override ICommonOperations CreateCommonOperations(IVimBufferData vimBufferData)
            {
                _commonOperations = _factory.Create<ICommonOperations>();
                _commonOperations.Setup(x => x.EditorOperations).Returns(_factory.Create<IEditorOperations>().Object);
                _commonOperations.Setup(x => x.EditorOptions).Returns(_factory.Create<IEditorOptions>().Object);
                return _commonOperations.Object;
            }

            [WpfFact]
            public void FormatCodeLines()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                _commonOperations.Setup(x => x.FormatCodeLines(range)).Verifiable();
                _commandUtil.FormatCodeLines(2);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void FormatCodeLinesVisual()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                var visualSpan = VisualSpan.CreateForSpan(range.Extent, VisualKind.Character, tabStop: 4);
                _commonOperations.Setup(x => x.FormatCodeLines(range)).Verifiable();
                _commandUtil.FormatCodeLinesVisual(visualSpan);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void FormatCodeLinesMotion()
            {
                Create("cat", "dog", "tree");
                var range = _textBuffer.GetLineRange(0, 1);
                var motionResult = MotionResult.Create(range.Extent, MotionKind.CharacterWiseExclusive, isForward: true);
                _commonOperations.Setup(x => x.FormatCodeLines(range)).Verifiable();
                _commandUtil.FormatCodeMotion(motionResult);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void JumpToMark_Global_NotSet()
            {
                Create("cat", "dog");
                _commandUtil.JumpToMark(Mark.NewGlobalMark(Letter.A));
                Assert.Equal(Resources.Common_MarkNotSet, _statusUtil.LastError);
            }

            [WpfFact]
            public void JumpToMark_Global_InBuffer()
            {
                Create("cat", "dog");
                Vim.MarkMap.SetGlobalMark(Letter.A, _vimTextBuffer, 0, 1);
                var point = _textBuffer.GetVirtualPoint(1);
                _commonOperations.Setup(x => x.MoveCaretToVirtualPoint(point, ViewFlags.All)).Verifiable();
                _commandUtil.JumpToMark(Mark.NewGlobalMark(Letter.A));
                _commonOperations.Verify();
            }

            [WpfFact]
            public void JumpToMark_Global_InOtherBuffer()
            {
                Create("cat", "dog");
                var otherVimBuffer = CreateVimBuffer("hello");
                Vim.MarkMap.SetGlobalMark(Letter.A, otherVimBuffer.VimTextBuffer, 0, 1);

                var point = new VirtualSnapshotPoint(otherVimBuffer.TextBuffer.GetPoint(1));
                _commonOperations.Setup(x => x.NavigateToPoint(point)).Returns(true).Verifiable();
                _commandUtil.JumpToMark(Mark.NewGlobalMark(Letter.A));
                _commonOperations.Verify();
            }

            [WpfFact]
            public void JumpToMark_Global_NotSetInOtherBuffer()
            {
                Create("cat", "dog");
                var otherVimBuffer = CreateVimBuffer("hello");
                Vim.MarkMap.SetGlobalMark(Letter.A, otherVimBuffer.VimTextBuffer, 0, 1);

                var point = new VirtualSnapshotPoint(otherVimBuffer.TextBuffer.GetPoint(1));
                _commonOperations.Setup(x => x.NavigateToPoint(point)).Returns(false).Verifiable();
                _commandUtil.JumpToMark(Mark.NewGlobalMark(Letter.A));
                _commonOperations.Verify();
                Assert.Equal(Resources.Common_MarkNotSet, _statusUtil.LastError);
            }

            [WpfFact]
            public void GoToDefinition_Succeeded()
            {
                Create("cat", "dog", "tree");
                _commonOperations.Setup(x => x.GoToDefinition()).Returns(Result.Succeeded).Verifiable();
                _commandUtil.GoToDefinition();
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToDefinition_Failed()
            {
                Create("cat", "dog", "tree");
                var msg = "This is a test";
                _commonOperations.Setup(x => x.GoToDefinition()).Returns(Result.NewFailed(msg)).Verifiable();
                _commandUtil.GoToDefinition();
                _commonOperations.Verify();
                Assert.Equal(msg, _statusUtil.LastError);
            }

            [WpfFact]
            public void GoToFileUnderCaret_NewWindow()
            {
                Create("");
                _commonOperations.Setup(x => x.GoToFileInNewWindow()).Verifiable();
                _commandUtil.GoToFileUnderCaret(true);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToFileUnderCaret_SameWindow()
            {
                Create("");
                _commonOperations.Setup(x => x.GoToFile()).Verifiable();
                _commandUtil.GoToFileUnderCaret(false);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToFileInSelection_LinewiseSpan_NewWindow()
            {
                Create("relative/file/path.js", "foo");
                _commonOperations.Setup(x => x.GoToFileInNewWindow("relative/file/path.js")).Verifiable();
                _commandUtil.GoToFileInSelectionInNewWindow(VisualSpan.NewLine(new SnapshotLineRange(_textView.GetLine(1).Snapshot, 0, 1)));
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToFileInSelection_CharacterWiseSpan_NewWindow()
            {
                Create("relative/file/path.js");
                _commonOperations.Setup(x => x.GoToFileInNewWindow("file/path.js")).Verifiable();
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 9, 12));
                _commandUtil.GoToFileInSelectionInNewWindow(visualSpan);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToFileInSelection_LinewiseSpan_SameWindow()
            {
                Create("relative/file/path.js", "foo");
                _commonOperations.Setup(x => x.GoToFile("relative/file/path.js")).Verifiable();
                var visualSpan = VisualSpan.NewLine(new SnapshotLineRange(_textView.GetLine(1).Snapshot, 0, 1));
                _commandUtil.GoToFileInSelection(visualSpan);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToFileInSelection_CharacterWiseSpan_SameWindow()
            {
                Create("relative/file/path.js");
                _commonOperations.Setup(x => x.GoToFile("file/path.js")).Verifiable();
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 9, 12));
                _commandUtil.GoToFileInSelection(visualSpan);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToNextTab_ForwardNoCount()
            {
                Create("");
                _commonOperations.Setup(x => x.GoToNextTab(SearchPath.Forward, 1)).Verifiable();
                _commandUtil.GoToNextTab(SearchPath.Forward, FSharpOption<int>.None);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToNextTab_BackwardNoCount()
            {
                Create("");
                _commonOperations.Setup(x => x.GoToNextTab(SearchPath.Backward, 1)).Verifiable();
                _commandUtil.GoToNextTab(SearchPath.Backward, FSharpOption<int>.None);
                _commonOperations.Verify();
            }

            [WpfFact]
            public void GoToNextTab_ForwardCount()
            {
                Create("");
                _commonOperations.Setup(x => x.GoToTab(2)).Verifiable();
                _commandUtil.GoToNextTab(SearchPath.Forward, FSharpOption.Create(2));
                _commonOperations.Verify();
            }

            [WpfFact]
            public void CloseWindow()
            {
                Create("");
                _commonOperations.Setup(x => x.CloseWindowUnlessDirty()).Verifiable();
                RunNormalCommand(NormalCommand.CloseWindow);
                _commonOperations.Verify();
            }
        }

        public sealed class UndoOperationsTest : CommandUtilTest
        {
            private Mock<IUndoRedoOperations> _undoRedoOperations;

            protected override IVimTextBuffer CreateVimTextBufferCore(ITextBuffer textBuffer)
            {
                _undoRedoOperations = new Mock<IUndoRedoOperations>(MockBehavior.Strict);
                var vimTextBuffer = (VimTextBuffer)Vim.GetOrCreateVimTextBuffer(textBuffer);
                vimTextBuffer._undoRedoOperations = _undoRedoOperations.Object;
                return vimTextBuffer;
            }

            /// <summary>
            /// Make sure that we dispose of the linked transaction in the case where the edit 
            /// throws an exception.  It's critical that these get disposed else they leave open
            /// giant undo transactions
            /// </summary>
            [WpfFact]
            public void EditWithLinkedChange_Throw()
            {
                Create("cat", "dog");
                var transaction = new Mock<ILinkedUndoTransaction>(MockBehavior.Strict);
                transaction.Setup(x => x.Dispose()).Verifiable();
                _undoRedoOperations
                    .Setup(x => x.CreateLinkedUndoTransactionWithFlags(It.IsAny<string>(), It.IsAny<LinkedUndoTransactionFlags>()))
                    .Returns(transaction.Object)
                    .Verifiable();
                _undoRedoOperations
                    .Setup(x => x.EditWithUndoTransaction<Unit>("Test", _textView, It.IsAny<FSharpFunc<Unit, Unit>>()))
                    .Throws(new ArgumentException())
                    .Verifiable();

                var caught = false;
                Action<Unit> actionRaw = _ => { throw new ArgumentException(); };
                var action = actionRaw.ToFSharpFunc();
                try
                {
                    _commandUtil.EditWithLinkedChange(
                        "Test",
                        FSharpFuncUtil.ToFSharpFunc<Unit>(_ => { }));
                }
                catch (ArgumentException)
                {
                    caught = true;
                }
                Assert.True(caught);
            }

            /// <summary>
            /// Make sure that we dispose of the linked transaction in the case where the edit 
            /// throws an exception.  It's critical that these get disposed else they leave open
            /// giant undo transactions
            /// </summary>
            [WpfFact]
            public void EditBlockSpanWithLinkedChange_Throw()
            {
                Create("cat", "dog");
                var blockSpan = BlockSpan.CreateForSpan(_textBuffer.GetSpan(0, 2), tabStop: 4);
                var transaction = new Mock<ILinkedUndoTransaction>(MockBehavior.Strict);
                transaction.Setup(x => x.Dispose()).Verifiable();
                _undoRedoOperations
                    .Setup(x => x.CreateLinkedUndoTransactionWithFlags(It.IsAny<string>(), It.IsAny<LinkedUndoTransactionFlags>()))
                    .Returns(transaction.Object)
                    .Verifiable();
                _undoRedoOperations
                    .Setup(x => x.EditWithUndoTransaction<Unit>("Test", _textView, It.IsAny<FSharpFunc<Unit, Unit>>()))
                    .Throws(new ArgumentException())
                    .Verifiable();

                var caught = false;
                Action<Unit> actionRaw = _ => { throw new ArgumentException(); };
                var action = actionRaw.ToFSharpFunc();
                try
                {
                    _commandUtil.EditBlockWithLinkedChange(
                        "Test",
                        blockSpan,
                        VisualInsertKind.Start,
                        FSharpFuncUtil.ToFSharpFunc<Unit>(_ => { }));
                }
                catch (ArgumentException)
                {
                    caught = true;
                }
                Assert.True(caught);
            }
        }

        public sealed class DeleteMotionTest : CommandUtilTest
        {
            /// <summary>
            /// Test the special case linewise promotion which occurs for d{motion} operations
            /// </summary>
            [WpfFact]
            public void SpecialCaseLinewisePromotion()
            {
                Create(" cat", " dog    ", "fish");
                var span = new SnapshotSpan(
                    _textBuffer.GetPoint(1),
                    _textBuffer.GetLine(1).Start.Add(5));
                var motion = VimUtil.CreateMotionResult(span, true, MotionKind.CharacterWiseExclusive);
                _commandUtil.DeleteMotion(VimUtil.MissingRegisterName, motion);
                Assert.Equal(OperationKind.LineWise, UnnamedRegister.OperationKind);
                Assert.Equal("fish", _textBuffer.GetLine(0).GetText());
            }

            /// <summary>
            /// The linewise special case scenario requires the line count be greater than 1
            /// </summary>
            [WpfFact]
            public void SpecialCaseLineWisePromotionMustBeMoreThanOneLine()
            {
                Create(" cat   ", " dog    ", "fish");
                var span = new SnapshotSpan(
                    _textBuffer.GetPoint(1),
                    _textBuffer.GetPoint(5));
                var motion = VimUtil.CreateMotionResult(span, true, MotionKind.CharacterWiseExclusive);
                _commandUtil.DeleteMotion(VimUtil.MissingRegisterName, motion);
                Assert.Equal(OperationKind.CharacterWise, UnnamedRegister.OperationKind);
                Assert.Equal(
                    new[] { "   ", " dog    ", "fish" },
                    _textBuffer.GetLines());
            }
        }

        public sealed class RepeatLastSubstituteTest : CommandUtilTest
        {
            [WpfFact]
            public void Simple()
            {
                Create("cat dog fish");
                _vimData.LastSubstituteData = FSharpOption.Create(new SubstituteData("cat", "bat", SubstituteFlags.None));
                _commandUtil.RepeatLastSubstitute(useSameFlags: false, useWholeBuffer: false);
                Assert.Equal("bat dog fish", _textBuffer.GetLine(0).GetText());
            }

            [WpfFact]
            public void NoLastData()
            {
                Create("cat dog fish");
                _vimData.LastSubstituteData = FSharpOption<SubstituteData>.None;
                _commandUtil.RepeatLastSubstitute(useSameFlags: false, useWholeBuffer: false);
                Assert.Equal(1, VimHost.BeepCount);
            }

            [WpfFact]
            public void UseSameFlagsTrue()
            {
                Create("cat cat fish");
                _vimData.LastSubstituteData = FSharpOption.Create(new SubstituteData("cat", "bat", SubstituteFlags.ReplaceAll));
                _commandUtil.RepeatLastSubstitute(useSameFlags: true, useWholeBuffer: false);
                Assert.Equal("bat bat fish", _textBuffer.GetLine(0).GetText());
            }

            [WpfFact]
            public void UseSameFlagsFalse()
            {
                Create("cat cat fish");
                _vimData.LastSubstituteData = FSharpOption.Create(new SubstituteData("cat", "bat", SubstituteFlags.ReplaceAll));
                _commandUtil.RepeatLastSubstitute(useSameFlags: false, useWholeBuffer: false);
                Assert.Equal("bat cat fish", _textBuffer.GetLine(0).GetText());
            }

            [WpfFact]
            public void UseSameFlagsTrueUseWholeBufferTrue()
            {
                // Reported in issue #1604.
                Create("cat cat", "cat cat", "fish", "");
                _vimData.LastSubstituteData = FSharpOption.Create(new SubstituteData("cat", "bat", SubstituteFlags.ReplaceAll));
                _commandUtil.RepeatLastSubstitute(useSameFlags: true, useWholeBuffer: true);
                Assert.Equal(new[] { "bat bat", "bat bat", "fish", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void AsCommand()
            {
                Create("cat dog fish");
                _vimData.LastSubstituteData = FSharpOption.Create(new SubstituteData("cat", "bat", SubstituteFlags.None));
                RunNormalCommand(NormalCommand.NewRepeatLastSubstitute(false, false));
            }
        }

        public sealed class ReplaceCharTest : CommandUtilTest
        {
            /// <summary>
            /// If the count is too big then an error needs to occur.  This is necessary to break 
            /// macro execution
            /// </summary>
            [WpfFact]
            public void CountTooBig()
            {
                Create("cat");
                var result = _commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('c'), 5);
                Assert.True(result.IsError);
                Assert.Equal(1, _vimHost.BeepCount);
            }

            [WpfFact]
            public void Simple1()
            {
                Create("foo");
                _commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('b'), 1);
                Assert.Equal("boo", _textView.TextSnapshot.GetLineFromLineNumber(0).GetText());
            }

            [WpfFact]
            public void Simple2()
            {
                Create("foo");
                _commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('b'), 2);
                Assert.Equal("bbo", _textView.TextSnapshot.GetLineFromLineNumber(0).GetText());
            }

            [WpfFact]
            public void Simple3()
            {
                Create("foo");
                _textView.MoveCaretTo(1);
                _commandUtil.ReplaceChar(KeyInputUtil.EnterKey, 1);
                var tss = _textView.TextSnapshot;
                Assert.Equal(2, tss.LineCount);
                Assert.Equal("f", tss.GetLineFromLineNumber(0).GetText());
                Assert.Equal("o", tss.GetLineFromLineNumber(1).GetText());
            }

            [WpfFact]
            public void Simple4()
            {
                Create("food");
                _textView.MoveCaretTo(1);
                _commandUtil.ReplaceChar(KeyInputUtil.EnterKey, 2);
                var tss = _textView.TextSnapshot;
                Assert.Equal(2, tss.LineCount);
                Assert.Equal("f", tss.GetLineFromLineNumber(0).GetText());
                Assert.Equal("d", tss.GetLineFromLineNumber(1).GetText());
            }

            [WpfFact]
            public void EnterShouldIndentNextLine()
            {
                Create("    the food is especially good today");
                const int betweenIsAndEspecially = 15;
                _textView.MoveCaretTo(betweenIsAndEspecially);
                _localSettings.AutoIndent = true;
                var tss = _textView.TextSnapshot;

                Assert.True(_commandUtil.ReplaceChar(KeyInputUtil.EnterKey, 1).IsCompleted);
                var withIndentedSecondLine = "    the food is" + Environment.NewLine + "    especially good today";
                Assert.Equal(_textBuffer.GetExtent().GetText(), withIndentedSecondLine);
            }

            /// <summary>
            /// Caret should not move as a result of a single ReplaceChar operation
            /// </summary>
            [WpfFact]
            public void DontMoveCaret()
            {
                Create("foo");
                Assert.True(_commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('u'), 1).IsCompleted);
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Caret should move for a multiple replace
            /// </summary>
            [WpfFact]
            public void MoveCaretForMultiple()
            {
                Create("foo");
                Assert.True(_commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('u'), 2).IsCompleted);
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Replacing a surrogate pair should replace both surrogates
            /// </summary>
            [WpfFact]
            public void SurrogatePair()
            {
                // Reported in issue #2352.
                const string alien = "\U0001F47D"; // 👽
                Create(alien);
                Assert.True(_commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('x'), 1).IsCompleted);
                Assert.Equal("x", _textView.TextSnapshot.GetLineFromLineNumber(0).GetText());
            }

            /// <summary>
            /// Multiply replacing a string with surrogate pairs should replace all surrogates
            /// </summary>
            [WpfFact]
            public void SurrogatePairs()
            {
                const string alien = "\U0001F47D"; // 👽
                Create($"{alien}o{alien}ooo");
                Assert.True(_commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('x'), 3).IsCompleted);
                Assert.Equal("xxxooo", _textView.TextSnapshot.GetLineFromLineNumber(0).GetText());
            }
        }

        public abstract class ScrollLinesTest : CommandUtilTest
        {
            private IFoldManager _foldManager;

            protected override IFoldManager CreateFoldManager(ITextView textView)
            {
                _foldManager = base.CreateFoldManager(textView);
                return _foldManager;
            }

            public sealed class ScrollLinesDownTest : ScrollLinesTest
            {
                /// <summary>
                /// Should be beeping at the last line in the ITextBuffer
                /// </summary>
                [WpfFact]
                public void BeepAtLastLine()
                {
                    Create("dog", "cat");
                    _textView.MoveCaretToLine(1);
                    _commandUtil.ScrollLines(ScrollDirection.Down, true, FSharpOption<int>.None);
                    Assert.Equal(1, _vimHost.BeepCount);
                }

                /// <summary>
                /// Make sure the scroll lines down will hit the bottom of the screen
                /// </summary>
                [WpfFact]
                public void ToBottom()
                {
                    Create("a", "b", "c", "d");
                    _textView.SetVisibleLineCount(count: 1);
                    for (var i = 0; i < 5; i++)
                    {
                        _commandUtil.ScrollLines(ScrollDirection.Down, true, FSharpOption<int>.None);
                    }
                    Assert.Equal(_textView.GetLine(3).Start, _textView.GetCaretPoint());
                }

                /// <summary>
                /// Make sure the scroll option is used if it's one of the parameters
                /// </summary>
                [WpfFact]
                public void UseScrollOption()
                {
                    Create("a", "b", "c", "d", "e");
                    _textView.SetVisibleLineCount(count: 4);
                    _windowSettings.Scroll = 3;
                    _commandUtil.ScrollLines(ScrollDirection.Down, true, FSharpOption<int>.None);
                    Assert.Equal(3, _textView.GetCaretLine().LineNumber);
                }

                /// <summary>
                /// If the scroll option is set to be consulted and an explicit count is given then that
                /// count should be used and the scroll option should be set to that value
                /// </summary>
                [WpfFact]
                public void ScrollOptionWithCount()
                {
                    Create("a", "b", "c", "d", "e");
                    _textView.SetVisibleLineCount(count: 3);
                    _windowSettings.Scroll = 3;
                    _commandUtil.ScrollLines(ScrollDirection.Down, true, FSharpOption.Create(2));
                    Assert.Equal(2, _textView.GetCaretLine().LineNumber);
                    Assert.Equal(2, _windowSettings.Scroll);
                }

                /// <summary>
                /// The amount of scroll is limited by the visible line count
                /// </summary>
                [WpfFact]
                public void ScrollLimitedByLineCount()
                {
                    // Reported in issue #1887.
                    Create("a", "b", "c", "d", "e", "f", "g", "h");
                    _textView.SetVisibleLineCount(count: 3);
                    _commandUtil.ScrollLines(ScrollDirection.Down, true, FSharpOption.Create(100));
                    Assert.Equal(3, _textView.GetCaretLine().LineNumber);
                    Assert.Equal(100, _windowSettings.Scroll);
                }

                /// <summary>
                /// Setting scroll to zero resets it to the calculated value
                /// </summary>
                [WpfFact]
                public void ResetScrollToCalculatedValue()
                {
                    // Reported in issue #1887.
                    Create("a", "b", "c", "d", "e", "f", "g", "h");
                    _textView.SetVisibleLineCount(count: 4);
                    var calculatedScroll = _windowSettings.Scroll;
                    _windowSettings.Scroll = 100;
                    Assert.Equal(100, _windowSettings.Scroll);
                    _windowSettings.Scroll = 0;
                    Assert.Equal(calculatedScroll, _windowSettings.Scroll);
                }

                /// <summary>
                /// With no scroll or count then the value of 1 should be used and the scroll option
                /// shouldn't be updated
                /// </summary>
                [WpfFact]
                public void NoScrollOrCount()
                {
                    Create("a", "b", "c", "d", "e");
                    _textView.SetVisibleLineCount(count: 1);
                    _windowSettings.Scroll = 3;
                    _commandUtil.ScrollLines(ScrollDirection.Down, false, FSharpOption<int>.None);
                    Assert.Equal(1, _textView.GetCaretLine().LineNumber);
                    Assert.Equal(3, _windowSettings.Scroll);
                }

#if !VS_SPECIFIC_2022 // https://github.com/VsVim/VsVim/issues/2916
                /// <summary>
                /// Make sure that scroll lines down handles a fold as a single line
                /// </summary>
                [WpfFact]
                public void OverFold()
                {
                    Create("a", "b", "c", "d", "e");
                    _textView.SetVisibleLineCount(count: 3);
                    _foldManager.CreateFold(_textBuffer.GetLineRange(1, 2)); // "b" and "c"
                    _commandUtil.ScrollLines(ScrollDirection.Down, false, FSharpOption.Create(2));
                    Assert.Equal(3, _textView.GetCaretLine().LineNumber);
                }
#endif
            }

            public sealed class ScrollLinesUpTest : ScrollLinesTest
            {
                /// <summary>
                /// Should be beeping at the first line in the ITextBuffer
                /// </summary>
                [WpfFact]
                public void BeepAtFirstLine()
                {
                    Create("dog", "cat");
                    _commandUtil.ScrollLines(ScrollDirection.Up, true, FSharpOption<int>.None);
                    Assert.Equal(1, _vimHost.BeepCount);
                }
            }
        }

        public sealed class ScrollPagesTest : CommandUtilTest
        {
            public ScrollPagesTest()
            {
                Create("a", "b", "c", "d", "e");
                _textView.SetTextViewLineCount(count: 2);
            }

            [WpfFact]
            public void SimpleDown()
            {
                _commandUtil.ScrollPages(ScrollDirection.Down, count: 1);
                Assert.Equal(1, _textView.GetCaretLine().LineNumber);
            }

            [WpfFact]
            public void SimpleUp()
            {
                _commandUtil.ScrollPages(ScrollDirection.Down, count: 2);
                _commandUtil.ScrollPages(ScrollDirection.Up, count: 1);
                Assert.Equal(1, _textView.GetCaretLine().LineNumber);
            }

            [WpfFact]
            public void FromCommand()
            {
                RunNormalCommand(NormalCommand.NewScrollPages(ScrollDirection.Down));
                Assert.Equal(1, _textView.GetCaretLine().LineNumber);
            }

            [WpfFact]
            public void BadDirection()
            {
                _commandUtil.ScrollPages((ScrollDirection)int.MaxValue, 1);
                Assert.Equal(1, VimHost.BeepCount);
            }
        }

        public sealed class MiscTest : CommandUtilTest
        {
            private IFoldManager _foldManager;

            protected override IFoldManager CreateFoldManager(ITextView textView)
            {
                _foldManager = base.CreateFoldManager(textView);
                return _foldManager;
            }

            /// <summary>
            /// Make sure we can do a very simple word add here
            /// </summary>
            [WpfFact]
            public void AddToWord_Decimal_Simple()
            {
                Create(" 1");
                _commandUtil.AddToWord(1);
                Assert.Equal(" 2", _textView.GetLine(0).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Add with a count
            /// </summary>
            [WpfFact]
            public void AddToWord_Decimal_WithCount()
            {
                Create(" 1");
                _commandUtil.AddToWord(3);
                Assert.Equal(" 4", _textView.GetLine(0).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// When alpha is not supported we should be jumping past words to add to the numbers
            /// </summary>
            [WpfFact]
            public void AddToWord_Decimal_PastWord()
            {
                Create("dog 1");
                _localSettings.NumberFormats = "";
                _commandUtil.AddToWord(1);
                Assert.Equal("dog 2", _textView.GetLine(0).GetText());
                Assert.Equal(4, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Arbitrary characters may appear after a decimal number
            /// </summary>
            [WpfFact]
            public void AddToWord_Decimal_MiddleOfWord()
            {
                // Reported in issue #2486
                Create("part1Price");
                _localSettings.NumberFormats = "";
                _commandUtil.AddToWord(1);
                Assert.Equal("part2Price", _textView.GetLine(0).GetText());
                Assert.Equal(4, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// When alpha is not supported we should be jumping past words to add to the numbers
            /// </summary>
            [WpfFact]
            public void AddToWord_Hex_PastWord()
            {
                Create("dog0xe");
                _localSettings.NumberFormats = "hex";
                _commandUtil.AddToWord(1);
                Assert.Equal("dog0xf", _textView.GetLine(0).GetText());
                Assert.Equal(5, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Arbitrary characters may appear after a hex number
            /// </summary>
            [WpfFact]
            public void AddToWord_Hex_MiddleOfWord()
            {
                Create("dog0xexyzzy");
                _localSettings.NumberFormats = "hex";
                _commandUtil.AddToWord(1);
                Assert.Equal("dog0xfxyzzy", _textView.GetLine(0).GetText());
                Assert.Equal(5, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Use add to word on an alpha
            /// </summary>
            [WpfFact]
            public void AddToWord_Alpha_Simple()
            {
                Create("cog");
                _localSettings.NumberFormats = "alpha";
                _commandUtil.AddToWord(1);
                Assert.Equal("dog", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void AddToWord_NonNumber()
            {
                Create("dog");
                _commandUtil.AddToWord(1);
                Assert.Equal(1, _vimHost.BeepCount);
            }

            /// <summary>
            /// Don't save the buffer on a close command even if it's dirty
            /// </summary>
            [WpfFact]
            public void CloseBuffer_Dirty()
            {
                Create("");
                _vimHost.IsDirtyFunc = _ => true;
                _commandUtil.CloseBuffer();
                Assert.Null(_lastSaved);
                Assert.Equal(_textView, _lastClosed);
            }

            /// <summary>
            /// Saving a non-dirty buffer is the same as saving a dirty one
            /// </summary>
            [WpfFact]
            public void CloseBuffer_NotDirty()
            {
                Create("");
                _vimHost.IsDirtyFunc = _ => false;
                _commandUtil.CloseBuffer();
                Assert.Null(_lastSaved);
                Assert.Equal(_textView, _lastClosed);
            }

            [WpfFact]
            public void SetMarkToCaret_StartOfBuffer()
            {
                Create("the cat chased the dog");
                _commandUtil.SetMarkToCaret('a');
                Assert.Equal(0, _vimTextBuffer.GetLocalMark(_localMarkA).Value.Position.Position);
            }

            /// <summary>
            /// Beep and pass the error message onto IStatusUtil if there is an error
            /// </summary>
            [WpfFact]
            public void SetMarkToCaret_BeepOnFailure()
            {
                Create("the cat chased the dog");
                _commandUtil.SetMarkToCaret('!');
                Assert.Equal(Resources.Common_MarkInvalid, _statusUtil.LastError);
                Assert.Equal(1, _vimHost.BeepCount);
            }

            /// <summary>
            /// Attempting to write to a read only mark should cause the host to beep
            /// </summary>
            [WpfFact]
            public void SetMarkToCaret_ReadOnlyMark()
            {
                Create("hello world");
                _commandUtil.SetMarkToCaret('<');
                Assert.Equal(1, _vimHost.BeepCount);
            }

            [WpfFact]
            public void JumpToMark_Simple()
            {
                Create("the cat chased the dog");
                _vimTextBuffer.SetLocalMark(_localMarkA, 0, 2);
                _commandUtil.JumpToMark(Mark.NewLocalMark(_localMarkA));
                Assert.Equal(2, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Pass the error message onto IStatusUtil if there is an error
            /// </summary>
            [WpfFact]
            public void JumpToMark_OnFailure()
            {
                Create("the cat chased the dog");
                _commandUtil.JumpToMark(Mark.NewLocalMark(_localMarkA));
                Assert.Equal(Resources.Common_MarkNotSet, _statusUtil.LastError);
            }

            /// <summary>
            /// If there is no command to repeat then just beep
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_NoCommandToRepeat()
            {
                Create("foo");
                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
                Assert.Equal(1, _vimHost.BeepCount);
            }

            /// <summary>
            /// Repeat a simple text insert
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_InsertText()
            {
                Create("");
                var textChange = TextChange.NewInsert("h");
                var command = InsertCommand.OfTextChange(textChange);
                _vimData.LastCommand = FSharpOption.Create(StoredCommand.NewInsertCommand(command, CommandFlags.Repeatable));
                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
                Assert.Equal("h", _textBuffer.GetLine(0).GetText());
            }

            /// <summary>
            /// Repeat a simple text insert with a new count
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_InsertTextNewCount()
            {
                Create("");
                var textChange = TextChange.NewInsert("h");
                var command = InsertCommand.OfTextChange(textChange);
                _vimData.LastCommand = FSharpOption.Create(StoredCommand.NewInsertCommand(command, CommandFlags.Repeatable));
                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData(count: 3));
                Assert.Equal("hhh", _textBuffer.GetLine(0).GetText());
            }

            /// <summary>
            /// Repeat a simple command
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_SimpleCommand()
            {
                Create("");
                var didRun = false;
                SetLastCommand(VimUtil.CreatePing(data =>
                {
                    Assert.True(data.Count.IsNone());
                    Assert.True(data.RegisterName.IsNone());
                    didRun = true;
                }));

                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
                Assert.True(didRun);
            }

            /// <summary>
            /// Repeat a simple command but give it a new count.  This should override the previous
            /// count
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_SimpleCommandNewCount()
            {
                Create("");
                var didRun = false;
                SetLastCommand(VimUtil.CreatePing(data =>
                {
                    Assert.True(data.Count.IsSome(2));
                    Assert.True(data.RegisterName.IsNone());
                    didRun = true;
                }));

                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData(count: 2));
                Assert.True(didRun);
            }

            /// <summary>
            /// Repeating a command should not clear the last command
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_DontClearPrevious()
            {
                Create("");
                var didRun = false;
                var command = VimUtil.CreatePing(data =>
                {
                    Assert.True(data.Count.IsNone());
                    Assert.True(data.RegisterName.IsNone());
                    didRun = true;
                });
                SetLastCommand(command);
                var saved = _vimData.LastCommand.Value;
                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
                Assert.Equal(saved, _vimData.LastCommand.Value);
                Assert.True(didRun);
            }

            /// <summary>
            /// Guard against the possiblitity of creating a StackOverflow by having the repeat
            /// last command recursively call itself
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_GuardAgainstStacOverflow()
            {
                Create();
                var didRun = false;
                SetLastCommand(VimUtil.CreatePing(data =>
                {
                    didRun = true;
                    _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
                }));

                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
                Assert.True(didRun);
                Assert.Equal(Resources.NormalMode_RecursiveRepeatDetected, _statusUtil.LastError);
            }

            /// <summary>
            /// When dealing with a repeat of a linked command where a new count is provided, only
            /// the first command gets the new count.  The linked command gets the original count
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_OnlyFirstCommandGetsNewCount()
            {
                Create("");
                var didRun1 = false;
                var didRun2 = false;
                var command1 = VimUtil.CreatePing(
                    data =>
                    {
                        didRun1 = true;
                        Assert.Equal(2, data.CountOrDefault);
                    });
                var command2 = VimUtil.CreatePing(
                    data =>
                    {
                        didRun2 = true;
                        Assert.Equal(1, data.CountOrDefault);
                    });
                var command = StoredCommand.NewLinkedCommand(
                    StoredCommand.NewNormalCommand(command1, VimUtil.CreateCommandData(), CommandFlags.None),
                    StoredCommand.NewNormalCommand(command2, VimUtil.CreateCommandData(), CommandFlags.None));
                _vimData.LastCommand = FSharpOption.Create(command);
                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData(count: 2));
                Assert.True(didRun1 && didRun2);
            }

            /// <summary>
            /// When repeating commands the mode should not be switched as if the last command ran
            /// but instead should remain in the current Normal mode.  This is best illustrated by 
            /// trying to repeat the 'o' command
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_DontSwitchModes()
            {
                var command1 = VimUtil.CreatePing(
                    data =>
                    {
                    });
            }

            /// <summary>
            /// Make sure the RepeatLastCommand properly registers as a bulk operation.  Ensure it also
            /// behaves correctly in the face of an exception
            /// </summary>
            [WpfFact]
            public void RepeatLastCommand_CallBulkOperations()
            {
                Create();
                SetLastCommand(VimUtil.CreatePing(
                    _ =>
                    {
                        Assert.Equal(1, _bulkOperations.BeginCount);
                        Assert.Equal(0, _bulkOperations.BeginCount);
                        throw new Exception();
                    }));
                try
                {
                    _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
                }
                catch
                {
                }
                Assert.Equal(1, _bulkOperations.BeginCount);
                Assert.Equal(1, _bulkOperations.BeginCount);
            }

            /// <summary>
            /// Pass a barrage of spans and verify they map back and forth within the same 
            /// ITextBuffer
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_CharacterBackAndForth()
            {
                Create("the dog kicked the ball", "into the tree");

                void action(SnapshotSpan span)
                {
                    var characterSpan = new CharacterSpan(span);
                    var visual = VisualSpan.NewCharacter(characterSpan);
                    var stored = StoredVisualSpan.OfVisualSpan(visual);
                    var restored = _commandUtil.CalculateVisualSpan(stored);
                    Assert.Equal(visual, restored);
                }

                action(new SnapshotSpan(_textView.TextSnapshot, 0, 3));
                action(new SnapshotSpan(_textView.TextSnapshot, 0, 4));
                action(new SnapshotSpan(_textView.GetLine(0).Start, _textView.GetLine(1).Start.Add(1)));
            }

            /// <summary>
            /// When repeating a multi-line characterwise span where the caret moves left,
            /// we need to use the caret to the end of the line on the first line
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_CharacterMultilineMoveCaretLeft()
            {
                Create("the dog", "ball");

                var span = new SnapshotSpan(_textView.GetPoint(3), _textView.GetLine(1).Start.Add(1));
                var stored = StoredVisualSpan.OfVisualSpan(VimUtil.CreateVisualSpanCharacter(span));
                _textView.MoveCaretTo(1);
                var restored = _commandUtil.CalculateVisualSpan(stored);
                var expected = new SnapshotSpan(_textView.GetPoint(1), _textView.GetLine(1).Start.Add(1));
                Assert.Equal(expected, restored.AsCharacter().CharacterSpan.Span);
            }

            /// <summary>
            /// When restoring for a single line maintain the length but do it from the caret
            /// point and not the original
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_Character_SingleLine()
            {
                Create("the dog kicked the cat", "and ball");

                var span = new SnapshotSpan(_textView.TextSnapshot, 3, 4);
                var stored = StoredVisualSpan.OfVisualSpan(VimUtil.CreateVisualSpanCharacter(span));
                _textView.MoveCaretTo(1);
                var restored = _commandUtil.CalculateVisualSpan(stored);
                var expected = new SnapshotSpan(_textView.GetPoint(1), 4);
                Assert.Equal(expected, restored.AsCharacter().CharacterSpan.Span);
            }

            /// <summary>
            /// Make sure that this can handle an empty last line
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_Character_EmptyLastLine()
            {
                Create("dog", "", "cat");
                var span = new SnapshotSpan(_textBuffer.GetPoint(0), _textBuffer.GetLine(1).Start.Add(1));
                var stored = StoredVisualSpan.OfVisualSpan(VimUtil.CreateVisualSpanCharacter(span));
                var restored = _commandUtil.CalculateVisualSpan(stored);
                Assert.Equal(_textBuffer.GetSpan(0, 7), restored.AsCharacter().CharacterSpan.Span);
            }

            /// <summary>
            /// Restore a Linewise span from the same offset
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_Linewise()
            {
                Create("a", "b", "c", "d");
                var span = VisualSpan.NewLine(_textView.GetLineRange(0, 1));
                var stored = StoredVisualSpan.OfVisualSpan(span);
                var restored = _commandUtil.CalculateVisualSpan(stored);
                Assert.Equal(span, restored);
            }

            /// <summary>
            /// Restore a Linewise span from a different offset
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_LinewiseDifferentOffset()
            {
                Create("a", "b", "c", "d");
                var span = VisualSpan.NewLine(_textView.GetLineRange(0, 1));
                var stored = StoredVisualSpan.OfVisualSpan(span);
                _textView.MoveCaretToLine(1);
                var restored = _commandUtil.CalculateVisualSpan(stored);
                Assert.Equal(_textView.GetLineRange(1, 2), restored.AsLine().LineRange);
            }

            /// <summary>
            /// Restore a Linewise span from a different offset which causes the count
            /// to be invalid
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_LinewiseCountPastEndOfBuffer()
            {
                Create("a", "b", "c", "d");
                var span = VisualSpan.NewLine(_textView.GetLineRange(0, 2));
                var stored = StoredVisualSpan.OfVisualSpan(span);
                _textView.MoveCaretToLine(3);
                var restored = _commandUtil.CalculateVisualSpan(stored);
                Assert.Equal(_textView.GetLineRange(3, 3), restored.AsLine().LineRange);
            }

            /// <summary>
            /// Restore of Block span at the same offset.  
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_Block()
            {
                Create("the", "dog", "kicked", "the", "ball");

                var blockSpan = _textBuffer.GetBlockSpan(0, 1, 0, 2, tabStop: _localSettings.TabStop);
                var visualSpan = VisualSpan.NewBlock(blockSpan);
                var stored = StoredVisualSpan.OfVisualSpan(visualSpan);
                var restored = _commandUtil.CalculateVisualSpan(stored);
                Assert.Equal(visualSpan, restored);
            }

            /// <summary>
            /// Restore of Block span at one character to the right
            /// </summary>
            [WpfFact]
            public void CalculateVisualSpan_BlockOneCharecterRight()
            {
                Create("the", "dog", "kicked", "the", "ball");

                var blockSpanData = _textView.GetBlockSpan(0, 1, 0, 2);
                var span = VisualSpan.NewBlock(blockSpanData);
                var stored = StoredVisualSpan.OfVisualSpan(span);
                _textView.MoveCaretTo(1);
                var restored = _commandUtil.CalculateVisualSpan(stored);
                Assert.Equal(
                    new[]
                {
                    _textView.GetLineSpan(0, 1, 1),
                    _textView.GetLineSpan(1, 1, 1)
                },
                    restored.AsBlock().BlockSpan.BlockSpans);
            }

            [WpfFact]
            public void DeleteCharacterAtCaret_Simple()
            {
                Create("foo", "bar");
                _commandUtil.DeleteCharacterAtCaret(1, VimUtil.MissingRegisterName);
                Assert.Equal("oo", _textView.GetLine(0).GetText());
                Assert.Equal("f", UnnamedRegister.StringValue);
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Delete several characters
            /// </summary>
            [WpfFact]
            public void DeleteCharacterAtCaret_TwoCharacters()
            {
                Create("foo", "bar");
                _commandUtil.DeleteCharacterAtCaret(2, VimUtil.MissingRegisterName);
                Assert.Equal("o", _textView.GetLine(0).GetText());
                Assert.Equal("fo", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Delete at a different offset and make sure the cursor is positioned correctly
            /// </summary>
            [WpfFact]
            public void DeleteCharacterAtCaret_NonZeroOffset()
            {
                Create("the cat", "bar");
                _textView.MoveCaretTo(1);
                _commandUtil.DeleteCharacterAtCaret(2, VimUtil.MissingRegisterName);
                Assert.Equal("t cat", _textView.GetLine(0).GetText());
                Assert.Equal("he", UnnamedRegister.StringValue);
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// When the count exceeds the length of the line it should delete to the end of the 
            /// line
            /// </summary>
            [WpfFact]
            public void DeleteCharacterAtCaret_CountExceedsLine()
            {
                Create("the cat", "bar");
                _globalSettings.VirtualEdit = "onemore";
                _textView.MoveCaretTo(1);
                _commandUtil.DeleteCharacterAtCaret(300, VimUtil.MissingRegisterName);
                Assert.Equal("t", _textView.GetLine(0).GetText());
                Assert.Equal("he cat", UnnamedRegister.StringValue);
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void DeleteCharacterBeforeCaret_Simple()
            {
                Create("foo");
                _textView.MoveCaretTo(1);
                _commandUtil.DeleteCharacterBeforeCaret(1, VimUtil.MissingRegisterName);
                Assert.Equal("oo", _textView.GetLine(0).GetText());
                Assert.Equal("f", UnnamedRegister.StringValue);
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// When the count exceeds the line just delete to the start of the line
            /// </summary>
            [WpfFact]
            public void DeleteCharacterBeforeCaret_CountExceedsLine()
            {
                Create("foo");
                _textView.MoveCaretTo(1);
                _commandUtil.DeleteCharacterBeforeCaret(300, VimUtil.MissingRegisterName);
                Assert.Equal("oo", _textView.GetLine(0).GetText());
                Assert.Equal("f", UnnamedRegister.StringValue);
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Caret should be moved to the start of the shift
            /// </summary>
            [WpfFact]
            public void ShiftLinesRightVisual_BlockShouldPutCaretAtStart()
            {
                Create("cat", "dog");
                _textView.MoveCaretToLine(1);
                var span = _textView.GetVisualSpanBlock(column: 1, length: 2, startLine: 0, lineCount: 2);
                _commandUtil.ShiftLinesRightVisual(1, span);
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Caret should be moved to the start of the shift
            /// </summary>
            [WpfFact]
            public void ShiftLinesLeftVisual_BlockShouldPutCaretAtStart()
            {
                Create("c  at", "d  og");
                _textView.MoveCaretToLine(1);
                var span = _textView.GetVisualSpanBlock(column: 1, length: 1, startLine: 0, lineCount: 2);
                _commandUtil.ShiftLinesLeftVisual(1, span);
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Block selections must take character width into consideration
            /// </summary>
            [WpfFact]
            public void DeleteSelection_WideCharacters()
            {
                Create("abcdefgh",
                       "\u3042\u3044\u3046\u3048\u304A",
                       "ijklmnop");
                _textView.MoveCaretToLine(1);
                var span = _textView.GetVisualSpanBlock(column: 2, length: 2, startLine: 0, lineCount: 3);
                _commandUtil.DeleteSelection(VimUtil.MissingRegisterName, span);
                Assert.Equal("abefgh", _textView.GetLine(0).GetText());
                Assert.Equal("\u3042\u3046\u3048\u304A", _textView.GetLine(1).GetText());
                Assert.Equal("ijmnop", _textView.GetLine(2).GetText());
            }

            /// <summary>
            /// Block deletions should change half selected wide characters to spaces
            /// </summary>
            [WpfFact]
            public void DeleteSelection_WideCharactersAreHalfRemoved()
            {
                Create("abcdefgh",
                       "\u3042\u3044\u3046\u3048\u304A",
                       "ijklmnop");
                _textView.MoveCaretToLine(1);
                var span = _textView.GetVisualSpanBlock(column: 3, length: 2, startLine: 0, lineCount: 3, tabStop: _localSettings.TabStop);
                _commandUtil.DeleteSelection(VimUtil.MissingRegisterName, span);
                Assert.Equal("abcfgh", _textView.GetLine(0).GetText());
                Assert.Equal("\u3042  \u3048\u304A", _textView.GetLine(1).GetText());
                Assert.Equal("ijknop", _textView.GetLine(2).GetText());
            }

            /// <summary>
            /// Changing a word based motion forward should not delete trailing whitespace
            /// </summary>
            [WpfFact]
            public void ChangeMotion_WordSpan()
            {
                Create("foo  bar");
                _commandUtil.ChangeMotion(
                    VimUtil.MissingRegisterName,
                    VimUtil.CreateMotionResult(
                        _textBuffer.GetSpan(0, 3),
                        isForward: true,
                        motionKind: MotionKind.CharacterWiseExclusive,
                        flags: MotionResultFlags.AnyWord));
                Assert.Equal("  bar", _textBuffer.GetLineRange(0).GetText());
                Assert.Equal("foo", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Changing a word based motion forward should not delete trailing whitespace
            /// </summary>
            [WpfFact]
            public void ChangeMotion_WordShouldSaveTrailingWhitespace()
            {
                Create("foo  bar");
                _commandUtil.ChangeMotion(
                    VimUtil.MissingRegisterName,
                    VimUtil.CreateMotionResult(
                        _textBuffer.GetSpan(0, 5),
                        isForward: true,
                        motionKind: MotionKind.LineWise,
                        flags: MotionResultFlags.AnyWord));
                Assert.Equal("  bar", _textBuffer.GetLineRange(0).GetText());
                Assert.Equal("foo" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Delete trailing whitespace in a non-word motion
            /// </summary>
            [WpfFact]
            public void ChangeMotion_NonWordShouldDeleteTrailingWhitespace()
            {
                Create("foo  bar");
                _commandUtil.ChangeMotion(
                    VimUtil.MissingRegisterName,
                    VimUtil.CreateMotionResult(
                        _textBuffer.GetSpan(0, 5),
                        isForward: true,
                        motionKind: MotionKind.LineWise));
                Assert.Equal("bar", _textBuffer.GetLineRange(0).GetText());
            }

            /// <summary>
            /// Leave whitespace in a backward word motion
            /// </summary>
            [WpfFact]
            public void ChangeMotion_LeaveWhitespaceIfBackward()
            {
                Create("cat dog tree");
                _commandUtil.ChangeMotion(
                    VimUtil.MissingRegisterName,
                    VimUtil.CreateMotionResult(
                        _textBuffer.GetSpan(4, 4),
                        false,
                        MotionKind.CharacterWiseInclusive));
                Assert.Equal("cat tree", _textBuffer.GetLineRange(0).GetText());
                Assert.Equal(4, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Caret should be positioned at the end of the first line
            /// </summary>
            [WpfFact]
            public void JoinLines_Caret()
            {
                Create("dog", "cat", "bear");
                _commandUtil.JoinLines(JoinKind.RemoveEmptySpaces, 1);
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Should beep when the count specified causes the range to exceed the 
            /// length of the ITextBuffer
            /// </summary>
            [WpfFact]
            public void JoinLines_CountExceedsBuffer()
            {
                Create("dog", "cat", "bear");
                _commandUtil.JoinLines(JoinKind.RemoveEmptySpaces, 3000);
                Assert.Equal(1, _vimHost.BeepCount);
            }

            /// <summary>
            /// A count of 2 is the same as 1 for JoinLines
            /// </summary>
            [WpfFact]
            public void JoinLines_CountOfTwoIsSameAsOne()
            {
                Create("dog", "cat", "bear");
                _commandUtil.JoinLines(JoinKind.RemoveEmptySpaces, 2);
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// The caret behavior for the 'J' family of commands is hard to follow at first
            /// but comes down to a very simple behavior.  The caret should be placed 1 past
            /// the last character in the second to last line joined
            /// </summary>
            [WpfFact]
            public void JoinLines_CaretWithBlankAtEnd()
            {
                Create("a ", "b", "c");
                _commandUtil.JoinLines(JoinKind.RemoveEmptySpaces, 3);
                Assert.Equal("a b c", _textView.GetLine(0).GetText());
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// If only a single line is selected it should extend down to 2 lines
            /// </summary>
            [WpfFact]
            public void JoinSelection_ExtendDown()
            {
                Create("cat", "dog", "tree");
                var visualSpan = VisualSpan.CreateForSpan(_textBuffer.GetLineRange(0, 0).Extent, VisualKind.Character, tabStop: 4);
                _commandUtil.JoinSelection(JoinKind.RemoveEmptySpaces, visualSpan);
                Assert.Equal(new[] { "cat dog", "tree" }, _textBuffer.GetLines());
            }

            /// <summary>
            /// Simple extend of 2 lines
            /// </summary>
            [WpfFact]
            public void JoinSelection_Simple()
            {
                Create("cat", "dog", "tree");
                var visualSpan = VisualSpan.CreateForSpan(_textBuffer.GetLineRange(0, 1).Extent, VisualKind.Character, tabStop: 4);
                _commandUtil.JoinSelection(JoinKind.RemoveEmptySpaces, visualSpan);
                Assert.Equal(new[] { "cat dog", "tree" }, _textBuffer.GetLines());
            }

            /// <summary>
            /// Can't join a single line and can't extend if you're on the last line
            /// </summary>
            [WpfFact]
            public void JoinSelection_NotPossible()
            {
                Create("cat", "dog", "tree");
                var visualSpan = VisualSpan.CreateForSpan(_textBuffer.GetLineRange(2, 2).Extent, VisualKind.Character, tabStop: 4);
                _commandUtil.JoinSelection(JoinKind.KeepEmptySpaces, visualSpan);
                Assert.Equal(1, _vimHost.BeepCount);
                Assert.Equal(new[] { "cat", "dog", "tree" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void ChangeCaseCaretPoint_Simple()
            {
                Create("bar", "baz");
                _commandUtil.ChangeCaseCaretPoint(ChangeCharacterKind.ToUpperCase, 1);
                Assert.Equal("Bar", _textView.GetLineRange(0).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void ChangeCaseCaretPoint_WithCount()
            {
                Create("bar", "baz");
                _commandUtil.ChangeCaseCaretPoint(ChangeCharacterKind.ToUpperCase, 2);
                Assert.Equal("BAr", _textView.GetLineRange(0).GetText());
                Assert.Equal(2, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// If the count exceeds the line then just do the rest of the line
            /// </summary>
            [WpfFact]
            public void ChangeCaseCaretPoint_CountExceedsLine()
            {
                Create("bar", "baz");
                _globalSettings.VirtualEdit = "onemore";
                _commandUtil.ChangeCaseCaretPoint(ChangeCharacterKind.ToUpperCase, 300);
                Assert.Equal("BAR", _textView.GetLine(0).GetText());
                Assert.Equal("baz", _textView.GetLine(1).GetText());
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void ChangeCaseCaretLine_Simple()
            {
                Create("foo", "bar");
                _textView.MoveCaretTo(1);
                _commandUtil.ChangeCaseCaretLine(ChangeCharacterKind.ToUpperCase);
                Assert.Equal("FOO", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Make sure the caret moves past the whitespace when changing case
            /// </summary>
            [WpfFact]
            public void ChangeCaseCaretLine_WhiteSpaceStart()
            {
                Create("  foo", "bar");
                _textView.MoveCaretTo(4);
                _commandUtil.ChangeCaseCaretLine(ChangeCharacterKind.ToUpperCase);
                Assert.Equal("  FOO", _textView.GetLine(0).GetText());
                Assert.Equal(2, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Don't change anything but letters
            /// </summary>
            [WpfFact]
            public void ChangeCaseCaretLine_ExcludeNumbers()
            {
                Create("foo123", "bar");
                _textView.MoveCaretTo(1);
                _commandUtil.ChangeCaseCaretLine(ChangeCharacterKind.ToUpperCase);
                Assert.Equal("FOO123", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Change the caret line with the rot13 encoding
            /// </summary>
            [WpfFact]
            public void ChangeCaseCaretLine_Rot13()
            {
                Create("hello", "bar");
                _textView.MoveCaretTo(1);
                _commandUtil.ChangeCaseCaretLine(ChangeCharacterKind.Rot13);
                Assert.Equal("uryyb", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// An invalid motion should produce an error and not call the passed in function
            /// </summary>
            [WpfFact]
            public void RunWithMotion_InvalidMotionShouldError()
            {
                Create("");
                var data = VimUtil.CreateMotionData(Motion.NewMark(_localMarkA));
                Func<MotionResult, CommandResult> func =
                    _ =>
                    {
                        throw new Exception("Should not run");
                    };
                var result = _commandUtil.RunWithMotion(data, func.ToFSharpFunc());
                Assert.True(result.IsError);
            }

            /// <summary>
            /// Do a put before operation on an empty buffer and make sure
            /// the buffer contains what we put
            /// </summary>
            [WpfFact]
            public void PutBefore_EmptyBuffer()
            {
                Create("");
                UnnamedRegister.UpdateValue("cat\n", OperationKind.LineWise);
                _commandUtil.PutBeforeCaret(VimUtil.MissingRegisterName, 1, false);
                Assert.Equal("cat", _textView.GetLine(0).GetText());
                Assert.Equal("", _textView.GetLine(1).GetText());
            }

            /// <summary>
            /// Do a put after operation on an empty buffer and make sure
            /// the buffer contains what we put with the final line break
            /// moved to the beginning of the buffer
            /// </summary>
            [WpfFact]
            public void PutAfter_EmptyBuffer()
            {
                Create("");
                UnnamedRegister.UpdateValue("cat\n", OperationKind.LineWise);
                _commandUtil.PutAfterCaret(VimUtil.MissingRegisterName, 1, false);
                Assert.Equal("", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
            }

            /// <summary>
            /// Do a put operation on an empty line and ensure we don't accidentaly move off 
            /// of the end of the line and insert the text in the middle of the line break
            /// </summary>
            [WpfFact]
            public void PutAfter_EmptyLine()
            {
                Create("", "dog");
                UnnamedRegister.UpdateValue("pig", OperationKind.CharacterWise);
                _commandUtil.PutAfterCaret(VimUtil.MissingRegisterName, 1, false);
                Assert.Equal("pig", _textView.GetLine(0).GetText());
                Assert.Equal(2, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// PutAfter with a block value should position the cursor on the first character
            /// of the first string in the block
            /// </summary>
            [WpfFact]
            public void PutAfter_Block()
            {
                Create("dog", "cat");
                UnnamedRegister.UpdateBlockValues("aa", "bb");
                _commandUtil.PutAfterCaret(VimUtil.MissingRegisterName, 1, false);
                Assert.Equal("daaog", _textView.GetLine(0).GetText());
                Assert.Equal("cbbat", _textView.GetLine(1).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// PutAfter with a block value in virtual space should fill in spaces
            /// </summary>
            [WpfFact]
            public void PutAfter_VirtualBlock()
            {
                Create("cat", "dog", "");
                UnnamedRegister.UpdateBlockValues("foo", "bar");
                _textView.MoveCaretTo(3, virtualSpaces: 1);
                _commandUtil.PutAfterCaret(VimUtil.MissingRegisterName, 1, false);
                Assert.Equal(new[] { "cat foo", "dog bar", "" }, _textBuffer.GetLines());
            }

            /// <summary>
            /// PutAfter with a block value with the moveCaret option should put the caret after
            /// the last inserted text from the last item in the block
            /// </summary>
            [WpfFact]
            public void PutAfter_Block_WithMoveCaret()
            {
                Create("dog", "cat");
                UnnamedRegister.UpdateBlockValues("aa", "bb");
                _commandUtil.PutAfterCaret(VimUtil.MissingRegisterName, 1, moveCaretAfterText: true);
                Assert.Equal("daaog", _textView.GetLine(0).GetText());
                Assert.Equal("cbbat", _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetLine(1).Start.Add(3), _textView.GetCaretPoint());
            }

            /// <summary>
            /// Do a put before operation on an empty line and ensure we don't accidentally
            /// move up to the previous line break and insert there
            /// </summary>
            [WpfFact]
            public void PutBefore_EmptyLine()
            {
                Create("dog", "", "cat");
                UnnamedRegister.UpdateValue("pig", OperationKind.CharacterWise);
                _textView.MoveCaretToLine(1);
                _commandUtil.PutBeforeCaret(VimUtil.MissingRegisterName, 1, false);
                Assert.Equal("dog", _textView.GetLine(0).GetText());
                Assert.Equal("pig", _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetLine(1).Start.Add(2), _textView.GetCaretPoint());
            }

            /// <summary>
            /// PutBefore with a block value in virtual space should fill in spaces
            /// </summary>
            [WpfFact]
            public void PutBefore_VirtualBlock()
            {
                Create("cat", "dog", "");
                UnnamedRegister.UpdateBlockValues("foo", "bar");
                _textView.MoveCaretTo(3, virtualSpaces: 1);
                _commandUtil.PutBeforeCaret(VimUtil.MissingRegisterName, 1, false);
                Assert.Equal(new[] { "cat foo", "dog bar", "" }, _textBuffer.GetLines());
            }

            /// <summary>
            /// Replace the text and put the caret at the end of the selection
            /// </summary>
            [WpfFact]
            public void PutOverSelection_Character()
            {
                Create("hello world");
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 0, 5));
                UnnamedRegister.UpdateValue("dog");
                _commandUtil.PutOverSelection(VimUtil.MissingRegisterName, 1, moveCaretAfterText: false, visualSpan: visualSpan);
                Assert.Equal("dog world", _textView.GetLine(0).GetText());
                Assert.Equal(2, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Replace the text and put the caret after the selection span
            /// </summary>
            [WpfFact]
            public void PutOverSelection_Character_WithCaretMove()
            {
                Create("hello world");
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 0, 5));
                UnnamedRegister.UpdateValue("dog");
                _commandUtil.PutOverSelection(VimUtil.MissingRegisterName, 1, moveCaretAfterText: true, visualSpan: visualSpan);
                Assert.Equal("dog world", _textView.GetLine(0).GetText());
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Put a linewise paste over a character visual span.  Make sure that we 
            /// put the appropriate text and OperationKind into the source register
            /// </summary>
            [WpfFact]
            public void PutOverSelection_Character_WithLine()
            {
                Create("dog");
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 1, 1));
                UnnamedRegister.UpdateValue("pig" + Environment.NewLine, OperationKind.LineWise);
                _commandUtil.PutOverSelection(VimUtil.MissingRegisterName, 1, moveCaretAfterText: false, visualSpan: visualSpan);
                Assert.Equal("d", _textView.GetLine(0).GetText());
                Assert.Equal("pig", _textView.GetLine(1).GetText());
                Assert.Equal("g", _textView.GetLine(2).GetText());
                Assert.Equal("o", UnnamedRegister.StringValue);
                Assert.Equal(OperationKind.CharacterWise, UnnamedRegister.OperationKind);
            }

            /// <summary>
            /// Make sure it removes both lines and inserts the text at the start 
            /// of the line range span.  Should position the caret at the start as well
            /// </summary>
            [WpfFact]
            public void PutOverSelection_Line()
            {
                Create("the cat", "chased", "the dog");
                var visualSpan = VisualSpan.NewLine(_textView.GetLineRange(0, 1));
                UnnamedRegister.UpdateValue("dog");
                _commandUtil.PutOverSelection(VimUtil.MissingRegisterName, 1, moveCaretAfterText: false, visualSpan: visualSpan);
                Assert.Equal("dog", _textView.GetLine(0).GetText());
                Assert.Equal("the dog", _textView.GetLine(1).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Caret should be moved to the start of the next line if the 'moveCaretAfterText' 
            /// option is specified
            /// </summary>
            [WpfFact]
            public void PutOverSelection_Line_WithCaretMove()
            {
                Create("the cat", "chased", "the dog");
                var visualSpan = VisualSpan.NewLine(_textView.GetLineRange(0, 1));
                UnnamedRegister.UpdateValue("dog");
                _commandUtil.PutOverSelection(VimUtil.MissingRegisterName, 1, moveCaretAfterText: true, visualSpan: visualSpan);
                Assert.Equal("dog", _textView.GetLine(0).GetText());
                Assert.Equal("the dog", _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetLine(1).Start, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Ensure that we have the correct OperationKind for a put over a line selection.  It
            /// should be LineWise even if the put source is CharacterWise
            /// </summary>
            [WpfFact]
            public void PutOverSelection_Line_WithCharacter()
            {
                Create("dog", "cat");
                var visualSpan = VisualSpan.NewLine(_textView.GetLineRange(0));
                UnnamedRegister.UpdateValue("pig");
                _commandUtil.PutOverSelection(VimUtil.MissingRegisterName, 1, moveCaretAfterText: false, visualSpan: visualSpan);
                Assert.Equal("dog" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal(OperationKind.LineWise, UnnamedRegister.OperationKind);
            }

            /// <summary>
            /// Make sure caret is positioned on the last inserted character on the first
            /// inserted line
            /// </summary>
            [WpfFact]
            public void PutOverSelection_Block()
            {
                Create("cat", "dog");
                var visualSpan = VisualSpan.NewBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                UnnamedRegister.UpdateValue("z");
                _commandUtil.PutOverSelection(VimUtil.MissingRegisterName, 1, moveCaretAfterText: false, visualSpan: visualSpan);
                Assert.Equal("czt", _textView.GetLine(0).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Should delete the entire line range encompasing the selection and position the 
            /// caret at the start of the range for undo / redo
            /// </summary>
            [WpfFact]
            public void DeleteLineSelection_Character()
            {
                Create("cat", "dog");
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 1, 1));
                _commandUtil.DeleteLineSelection(VimUtil.MissingRegisterName, visualSpan);
                Assert.Equal("cat" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal("dog", _textView.GetLine(0).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Should delete the entire line range encompasing the selection and position the 
            /// caret at the start of the range for undo / redo
            /// </summary>
            [WpfFact]
            public void DeleteLineSelection_Line()
            {
                Create("cat", "dog");
                var visualSpan = VisualSpan.NewLine(_textView.GetLineRange(0));
                _commandUtil.DeleteLineSelection(VimUtil.MissingRegisterName, visualSpan);
                Assert.Equal("cat" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal("dog", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// When deleting a block it should delete from the start of the span until the end
            /// of the line for every span.  Caret should be positioned at the start of the edit
            /// but backed off a single space due to 'virtualedit='.  This will be properly
            /// handled by the moveCaretForVirtualEdit function.  Ensure it's called
            /// </summary>
            [WpfFact]
            public void DeleteLineSelection_Block()
            {
                Create("cat", "dog", "fish");
                _globalSettings.VirtualEdit = string.Empty;
                var visualSpan = VisualSpan.NewBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                _commandUtil.DeleteLineSelection(VimUtil.MissingRegisterName, visualSpan);
                Assert.Equal("c", _textView.GetLine(0).GetText());
                Assert.Equal("d", _textView.GetLine(1).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Should delete the contents of the line excluding the break and preserve the 
            /// original line indent
            /// </summary>
            [WpfFact]
            public void ChangeLineSelection_Character()
            {
                Create("  cat", "dog");
                _localSettings.AutoIndent = true;
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 2, 2));
                _commandUtil.ChangeLineSelection(VimUtil.MissingRegisterName, visualSpan, specialCaseBlock: false);
                Assert.Equal("", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.Equal(2, _textView.GetCaretVirtualPoint().VirtualSpaces);
            }

            /// <summary>
            /// Don't preserve the original indent if the 'autoindent' flag is not set
            /// </summary>
            [WpfFact]
            public void ChangeLineSelection_Character_NoAutoIndent()
            {
                Create("  cat", "dog");
                _localSettings.AutoIndent = false;
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 2, 2));
                _commandUtil.ChangeLineSelection(VimUtil.MissingRegisterName, visualSpan, specialCaseBlock: false);
                Assert.Equal("  cat" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal("", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.False(_textView.GetCaretVirtualPoint().IsInVirtualSpace);
            }

            /// <summary>
            /// Delete everything except the line break and preserve the original indent
            /// </summary>
            [WpfFact]
            public void ChangeLineSelection_Line()
            {
                Create("  cat", " dog", "bear", "fish");
                _localSettings.AutoIndent = true;
                var visualSpan = VisualSpan.NewLine(_textView.GetLineRange(1, 2));
                _commandUtil.ChangeLineSelection(VimUtil.MissingRegisterName, visualSpan, specialCaseBlock: false);
                Assert.Equal("  cat", _textView.GetLine(0).GetText());
                Assert.Equal("", _textView.GetLine(1).GetText());
                Assert.Equal("fish", _textView.GetLine(2).GetText());
                Assert.Equal(_textView.GetLine(1).Start, _textView.GetCaretPoint());
                Assert.Equal(1, _textView.GetCaretVirtualPoint().VirtualSpaces);
            }

            /// <summary>
            /// When not special casing block this should behave like the other forms of 
            /// ChangeLineSelection
            /// </summary>
            [WpfFact]
            public void ChangeLineSelection_Block_NoSpecialCase()
            {
                Create("  cat", "  dog", "bear", "fish");
                _localSettings.AutoIndent = true;
                var visualSpan = VisualSpan.NewBlock(_textView.GetBlockSpan(2, 1, 0, 2));
                _commandUtil.ChangeLineSelection(VimUtil.MissingRegisterName, visualSpan, specialCaseBlock: false);
                Assert.Equal("", _textView.GetLine(0).GetText());
                Assert.Equal("bear", _textView.GetLine(1).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.Equal(2, _textView.GetCaretVirtualPoint().VirtualSpaces);
            }

            /// <summary>
            /// When special casing block this turns into a simple delete till the end of the line
            /// </summary>
            [WpfFact]
            public void ChangeLineSelection_Block_SpecialCase()
            {
                Create("  cat", "  dog", "bear", "fish");
                _localSettings.AutoIndent = true;
                var visualSpan = VisualSpan.NewBlock(_textView.GetBlockSpan(2, 1, 0, 2));
                _commandUtil.ChangeLineSelection(VimUtil.MissingRegisterName, visualSpan, specialCaseBlock: true);
                Assert.Equal("  ", _textView.GetLine(0).GetText());
                Assert.Equal("  ", _textView.GetLine(1).GetText());
                Assert.Equal(2, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Delete the text on the line and start insert mode.  Needs to pass a transaction onto
            /// insert mode to get the proper undo behavior
            /// </summary>
            [WpfFact]
            public void ChangeLines_OneLine()
            {
                Create("cat", "dog");
                var result = _commandUtil.ChangeLines(1, VimUtil.MissingRegisterName);
                AssertInsertWithTransaction(result);
                Assert.Equal("", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Make sure we use a transaction here to ensure the undo behavior is correct
            /// </summary>
            [WpfFact]
            public void ChangeTillEndOfLine_MiddleOfLine()
            {
                Create("cat");
                _globalSettings.VirtualEdit = string.Empty;
                _textView.MoveCaretTo(1);
                var result = _commandUtil.ChangeTillEndOfLine(1, VimUtil.MissingRegisterName);
                AssertInsertWithTransaction(result);
                Assert.Equal("c", _textView.GetLine(0).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Make sure we create a linked change for ChangeSelection
            /// </summary>
            [WpfFact]
            public void ChangeSelection_Character()
            {
                Create("the dog chased the ball");
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 1, 2));
                var result = _commandUtil.ChangeSelection(VimUtil.MissingRegisterName, visualSpan);
                AssertInsertWithTransaction(result);
                Assert.Equal("t dog chased the ball", _textView.GetLine(0).GetText());
                Assert.Equal("he", UnnamedRegister.StringValue);
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Make sure the character deletion positions the caret at the start of the span and
            /// updates the register
            /// </summary>
            [WpfFact]
            public void DeleteSelection_Character()
            {
                Create("the dog chased the ball");
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 1, 2));
                _commandUtil.DeleteSelection(VimUtil.MissingRegisterName, visualSpan);
                Assert.Equal("t dog chased the ball", _textView.GetLine(0).GetText());
                Assert.Equal("he", UnnamedRegister.StringValue);
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// When a full line is selected make sure that it doesn't include the line break
            /// in the deletion
            /// </summary>
            [WpfFact]
            public void DeleteSelection_Character_FullLine()
            {
                Create("cat", "dog");
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 0, 3));
                _commandUtil.DeleteSelection(VimUtil.MissingRegisterName, visualSpan);
                Assert.Equal("", _textView.GetLine(0).GetText());
                Assert.Equal("dog", _textView.GetLine(1).GetText());
            }

            /// <summary>
            /// When a full line is selected and the selection extents into the line break 
            /// then the deletion should include the entire line including the line break
            /// </summary>
            [WpfFact]
            public void DeleteSelection_Character_FullLineFromLineBreak()
            {
                Create("cat", "dog");
                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textView.GetLineSpan(0, 0, 4));
                _commandUtil.DeleteSelection(VimUtil.MissingRegisterName, visualSpan);
                Assert.Equal("dog", _textView.GetLine(0).GetText());
            }

            /// <summary>
            /// Simple decimal value that we have to jump over whitespace to get to
            /// </summary>
            [WpfFact]
            public void GetNumberValueAtCaret_Decimal_Simple()
            {
                Create(" 400");
                Assert.Equal(NumberValue.NewDecimal(400), GetNumberValueAtCaret());
            }

            /// <summary>
            /// Get the decimal number from the back of the value
            /// </summary>
            [WpfFact]
            public void GetNumberValueAtCaret_Decimal_FromBack()
            {
                Create(" 400");
                _textView.MoveCaretTo(3);
                Assert.Equal(NumberValue.NewDecimal(400), GetNumberValueAtCaret());
            }

            /// <summary>
            /// Get the decimal number when there are mulitple choices
            /// </summary>
            [WpfFact]
            public void GetNumberValueAtCaret_Decimal_MultipleChoices()
            {
                Create(" 400 42");
                _textView.MoveCaretTo(4);
                Assert.Equal(NumberValue.NewDecimal(42), GetNumberValueAtCaret());
            }

            /// <summary>
            /// When hex is not supported and we are at the end of the hex number then we should
            /// be looking at the trailing hex as a non-hex number
            /// </summary>
            [WpfFact]
            public void GetNumberValueAtCaret_Decimal_TrailingPortionOfHex()
            {
                Create(" 400 0x42");
                _localSettings.NumberFormats = "alpha";
                _textView.MoveCaretTo(8);
                Assert.Equal(NumberValue.NewDecimal(42), GetNumberValueAtCaret());
            }

            /// <summary>
            /// When alpha is not one of the supported formats we should be jumping past letters
            /// to get to number values
            /// </summary>
            [WpfFact]
            public void GetNumberValueAtCaret_Decimal_GoPastWord()
            {
                Create("dog13");
                _localSettings.NumberFormats = string.Empty;
                Assert.Equal(NumberValue.NewDecimal(13), GetNumberValueAtCaret());
            }

            /// <summary>
            /// Get the hex number when there are mulitple choices.  Hex should win over
            /// decimal when both are supported
            /// </summary>
            [WpfFact]
            public void GetNumberValueAtCaret_Hex_MultipleChoices()
            {
                Create(" 400 0x42");
                _localSettings.NumberFormats = "hex";
                _textView.MoveCaretTo(8);
                Assert.Equal(NumberValue.NewHex(0x42), GetNumberValueAtCaret());
            }

            /// <summary>
            /// Jump past the blanks to get the alpha value
            /// </summary>
            [WpfFact]
            public void GetNumbeValueAtCaret_Alpha_Simple()
            {
                Create(" hello");
                _localSettings.NumberFormats = "alpha";
                Assert.Equal(NumberValue.NewAlpha('h'), GetNumberValueAtCaret());
            }

            /// <summary>
            /// Make sure caret starts at the begining of the line when there is no auto-indent
            /// </summary>
            [WpfFact]
            public void InsertLineAbove_KeepCaretAtStartWithNoAutoIndent()
            {
                Create("foo");
                _commandUtil.InsertLineAbove(1);
                var point = _textView.Caret.Position.VirtualBufferPosition;
                Assert.False(point.IsInVirtualSpace);
                Assert.Equal(0, point.Position.Position);
            }

            /// <summary>
            /// Make sure the ending is placed correctly when done from the middle of the line
            /// </summary>
            [WpfFact]
            public void InsertLineAbove_MiddleOfLine()
            {
                Create("foo", "bar");
                _textView.Caret.MoveTo(_textView.TextSnapshot.GetLineFromLineNumber(1).Start);
                _commandUtil.InsertLineAbove(1);
                var point = _textView.Caret.Position.BufferPosition;
                Assert.Equal(1, point.GetContainingLine().LineNumber);
                Assert.Equal(string.Empty, point.GetContainingLine().GetText());
            }

            /// <summary>
            /// Make sure we properly handle edits in the middle of our edit.  This happens 
            /// when the language service does a format for a new line
            /// </summary>
            [WpfFact]
            public void InsertLineAbove_EditInTheMiddle()
            {
                Create("foo bar", "baz");

                var didEdit = false;

                _textView.TextBuffer.Changed += (sender, e) =>
                {
                    if (didEdit)
                    {
                        return;
                    }

                    using (var edit = _textView.TextBuffer.CreateEdit())
                    {
                        edit.Insert(0, "a ");
                        edit.Apply();
                    }

                    didEdit = true;
                };

                _commandUtil.InsertLineAbove(1);
                var buffer = _textView.TextBuffer;
                var line = buffer.CurrentSnapshot.GetLineFromLineNumber(0);
                Assert.Equal("a ", line.GetText());
            }

            /// <summary>
            /// Maintain current indent when 'autoindent' is set but do so in virtual space
            /// </summary>
            [WpfFact]
            public void InsertLineAbove_ShouldKeepIndentWhenAutoIndentSet()
            {
                Create("  cat", "dog");
                _localSettings.AutoIndent = true;
                _commandUtil.InsertLineAbove(1);
                Assert.Equal(2, _textView.Caret.Position.VirtualSpaces);
            }

            /// <summary>
            /// Insert from middle of line and enure it works out
            /// </summary>
            [WpfFact]
            public void InsertLineBelow_InMiddleOfLine()
            {
                Create("foo", "bar", "baz");
                _commandUtil.InsertLineBelow(1);
                Assert.Equal(string.Empty, _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetLine(1).Start, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Insert a new line at the end of the buffer and ensure it works.  Bit of a corner
            /// case since it won't have a line break
            /// </summary>
            [WpfFact]
            public void InsertLineBelow_AtEndOfBuffer()
            {
                Create("foo", "bar");
                _textView.Caret.MoveTo(_textView.GetLine(1).End);
                _commandUtil.InsertLineBelow(1);
                Assert.Equal("", _textView.GetLine(2).GetText());
            }

            /// <summary>
            /// Deeply verify the contents of an insert below
            /// </summary>
            [WpfFact]
            public void InsertLineBelow_Misc()
            {
                Create("foo bar", "baz");
                _commandUtil.InsertLineBelow(1);
                var buffer = _textView.TextBuffer;
                var line = buffer.CurrentSnapshot.GetLineFromLineNumber(0);
                Assert.Equal(Environment.NewLine, line.GetLineBreakText());
                Assert.Equal(2, line.LineBreakLength);
                Assert.Equal("foo bar", line.GetText());
                Assert.Equal("foo bar" + Environment.NewLine, line.GetTextIncludingLineBreak());

                line = buffer.CurrentSnapshot.GetLineFromLineNumber(1);
                Assert.Equal(Environment.NewLine, line.GetLineBreakText());
                Assert.Equal(2, line.LineBreakLength);
                Assert.Equal(string.Empty, line.GetText());
                Assert.Equal(string.Empty + Environment.NewLine, line.GetTextIncludingLineBreak());

                line = buffer.CurrentSnapshot.GetLineFromLineNumber(2);
                Assert.Equal(string.Empty, line.GetLineBreakText());
                Assert.Equal(0, line.LineBreakLength);
                Assert.Equal("baz", line.GetText());
                Assert.Equal("baz", line.GetTextIncludingLineBreak());
            }

            /// <summary>
            /// Nested edits occur when the language service formats our new line.  Make
            /// sure we can handle it.
            /// </summary>
            [WpfFact]
            public void InsertLineBelow_EditsInTheMiddle()
            {
                Create("foo bar", "baz");

                var didEdit = false;

                _textView.TextBuffer.Changed += (sender, e) =>
                {
                    if (didEdit)
                    {
                        return;
                    }

                    using (var edit = _textView.TextBuffer.CreateEdit())
                    {
                        edit.Insert(0, "a ");
                        edit.Apply();
                    }

                    didEdit = true;
                };

                _commandUtil.InsertLineBelow(1);
                var buffer = _textView.TextBuffer;
                var line = buffer.CurrentSnapshot.GetLineFromLineNumber(0);
                Assert.Equal("a foo bar", line.GetText());
            }

            /// <summary>
            /// Maintain indent when using autoindent
            /// </summary>
            [WpfFact]
            public void InsertLineBelow_KeepIndentWhenAutoIndentSet()
            {
                Create("  cat", "dog");
                _localSettings.AutoIndent = true;
                _commandUtil.InsertLineBelow(1);
                Assert.Equal("", _textView.GetLine(1).GetText());
                Assert.Equal(2, _textView.Caret.Position.VirtualBufferPosition.VirtualSpaces);
            }

            /// <summary>
            /// Make sure it beeps on a bad register name
            /// </summary>
            [WpfFact]
            public void RecordMacroStart_BadRegisterName()
            {
                Create("");
                _commandUtil.RecordMacroStart('!');
                Assert.Equal(1, _vimHost.BeepCount);
            }

            /// <summary>
            /// Upper case registers should cause an append to occur
            /// </summary>
            [WpfFact]
            public void RecordMacroStart_AppendRegisters()
            {
                Create("");
                RegisterMap.GetRegister('c').UpdateValue("d");
                _commandUtil.RecordMacroStart('C');
                Assert.True(_macroRecorder.IsRecording);
                Assert.Equal('d', _macroRecorder.CurrentRecording.Value.Single().Char);
            }

            /// <summary>
            /// Standard case where no append is needed
            /// </summary>
            [WpfFact]
            public void RecordMacroStart_NormalRegister()
            {
                Create("");
                RegisterMap.GetRegister('c').UpdateValue("d");
                _commandUtil.RecordMacroStart('c');
                Assert.True(_macroRecorder.IsRecording);
                Assert.True(_macroRecorder.CurrentRecording.Value.IsEmpty);
            }

            /// <summary>
            /// Make sure it beeps on a bad register name
            /// </summary>
            [WpfFact]
            public void RunMacro_BadRegisterName()
            {
                Create("");
                _commandUtil.RunAtCommand('!', 1);
                Assert.Equal(1, _vimHost.BeepCount);
            }

            /// <summary>
            /// When jumping from a location not in the jump list and we're not in the middle 
            /// of a traversal the location should be added to the list
            /// </summary>
            [WpfFact]
            public void JumpToOlderPosition_FromLocationNotInList()
            {
                Create("cat", "dog", "fish");
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(1, 0));
                _commandUtil.JumpToOlderPosition(1);
                Assert.Equal(2, _jumpList.Jumps.Length);
                Assert.Equal(_textView.GetPoint(0), _jumpList.Jumps.Head.Position);
                Assert.Equal(1, _jumpList.CurrentIndex.Value);
            }

            /// <summary>
            /// When jumping from a location in the jump list and it's the start of the traversal
            /// then move the location back to the head of the class
            /// </summary>
            [WpfFact]
            public void JumpToOlderPosition_FromLocationInList()
            {
                Create("cat", "dog", "fish");
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(2, 0));
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(1, 0));
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(0, 0));
                _textView.MoveCaretToLine(1);
                _commandUtil.JumpToOlderPosition(1);
                Assert.Equal(3, _jumpList.Jumps.Length);
                Assert.Equal(
                    _jumpList.Jumps.Select(x => x.Position),
                    new[]
                {
                    _textView.GetLine(1).Start,
                    _textView.GetLine(0).Start,
                    _textView.GetLine(2).Start
                });
                Assert.Equal(1, _jumpList.CurrentIndex.Value);
            }

            /// <summary>
            /// When jumping from a location not in the jump list and we in the middle of a 
            /// traversal don't add the location to the list
            /// </summary>
            [WpfFact]
            public void JumpToOlderPosition_FromLocationNotInListDuringTraversal()
            {
                Create("cat", "dog", "fish");
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(1, 0));
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(0, 0));
                _jumpList.StartTraversal();
                Assert.True(_jumpList.MoveOlder(1));
                _textView.MoveCaretToLine(2);
                _commandUtil.JumpToOlderPosition(1);
                Assert.Equal(2, _jumpList.Jumps.Length);
                Assert.Equal(_textView.GetPoint(0), _jumpList.Jumps.Head.Position);
                Assert.Equal(1, _jumpList.CurrentIndex.Value);
            }

            /// <summary>
            /// Jump to the next position should not add the current position 
            /// </summary>
            [WpfFact]
            public void JumpToNextPosition_FromMiddle()
            {
                Create("cat", "dog", "fish");
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(2, 0));
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(1, 0));
                _jumpList.Add(_textBuffer.GetVirtualPointInLine(0, 0));
                _jumpList.StartTraversal();
                _jumpList.MoveOlder(1);
                _commandUtil.JumpToNewerPosition(1);
                Assert.Equal(3, _jumpList.Jumps.Length);
                Assert.Equal(
                    _jumpList.Jumps.Select(x => x.Position),
                    new[]
                {
                    _textView.GetLine(0).Start,
                    _textView.GetLine(1).Start,
                    _textView.GetLine(2).Start
                });
                Assert.Equal(0, _jumpList.CurrentIndex.Value);
            }

            /// <summary>
            /// When alpha is not supported we should be jumping past words to subtract to the numbers
            /// </summary>
            [WpfFact]
            public void SubtractFromWord_Hex_PastWord()
            {
                Create("dog0x2");
                _localSettings.NumberFormats = "hex";
                _commandUtil.SubtractFromWord(1);
                Assert.Equal("dog0x1", _textView.GetLine(0).GetText());
                Assert.Equal(5, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Make sure that we write out the buffer and close if it's dirty
            /// </summary>
            [WpfFact]
            public void WriteBufferAndQuit_Dirty()
            {
                Create("");
                _vimHost.IsDirtyFunc = _ => true;
                _commandUtil.WriteBufferAndQuit();
                Assert.Equal(_textBuffer, _lastSaved);
                Assert.Equal(_textView, _lastClosed);
            }

            /// <summary>
            /// Make sure that we don't write out the buffer and simply close when the buffer
            /// isn't dirty
            /// </summary>
            [WpfFact]
            public void WriteBufferAndQuit_NotDirty()
            {
                Create("");
                _vimHost.IsDirtyFunc = _ => false;
                _commandUtil.WriteBufferAndQuit();
                Assert.Null(_lastSaved);
                Assert.Equal(_textView, _lastClosed);
            }

            /// <summary>
            /// Ensure that yank lines does a line wise yank of the 'count' lines
            /// from the caret
            /// </summary>
            [WpfFact]
            public void YankLines_Normal()
            {
                Create("cat", "dog", "bear");
                _commandUtil.YankLines(2, VimUtil.MissingRegisterName);
                Assert.Equal("cat" + Environment.NewLine + "dog" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal(OperationKind.LineWise, UnnamedRegister.OperationKind);
            }

#if !VS_SPECIFIC_2022 // https://github.com/VsVim/VsVim/issues/2916
            /// <summary>
            /// Ensure that yank lines operates against the visual buffer and will yank 
            /// the folded text
            /// </summary>
            [WpfFact]
            public void YankLines_StartOfFold()
            {
                Create("cat", "dog", "bear", "fish", "pig");
                _foldManager.CreateFold(_textView.GetLineRange(1, 2));
                _textView.MoveCaretToLine(1);
                _commandUtil.YankLines(2, VimUtil.MissingRegisterName);
                Assert.Equal("dog" + Environment.NewLine + "bear" + Environment.NewLine + "fish" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal(OperationKind.LineWise, UnnamedRegister.OperationKind);
            }

            /// <summary>
            /// Ensure that yanking over a fold will count the fold as one line
            /// </summary>
            [WpfFact]
            public void YankLines_OverFold()
            {
                Create("cat", "dog", "bear", "fish", "pig");
                _foldManager.CreateFold(_textView.GetLineRange(1, 2));
                _textView.ForceLayout();
                _commandUtil.YankLines(3, VimUtil.MissingRegisterName);
                var lines = new[] { "cat", "dog", "bear", "fish" };
                var text = lines.Aggregate((x, y) => x + Environment.NewLine + y) + Environment.NewLine;
                Assert.Equal(text, UnnamedRegister.StringValue);
                Assert.Equal(OperationKind.LineWise, UnnamedRegister.OperationKind);
            }
#endif
        }

        public sealed class OpenFoldInSelectionTest : CommandUtilTest
        {
            private IFoldManager _foldManager;

            protected override IFoldManager CreateFoldManager(ITextView textView)
            {
                _foldManager = base.CreateFoldManager(textView);
                return _foldManager;
            }

            [WpfFact]
            public void Simple()
            {
                Create("cat", "dog", "bear", "fish", "pig");
                _foldManager.CreateFold(_textView.GetLineRange(1, 2));
                _commandUtil.OpenFoldInSelection(VisualSpan.NewLine(_textBuffer.GetLineRange(0, 3)));
                Assert.True(_outliningManager.GetAllRegions(_textBuffer.GetExtent()).All((c) => !c.IsCollapsed));
            }

            [WpfFact]
            public void AsCommand()
            {
                Create("cat", "dog", "bear", "fish", "pig");
                _foldManager.CreateFold(_textView.GetLineRange(1, 2));
                var visualSpan = VisualSpan.NewLine(_textBuffer.GetLineRange(0, 3));
                RunVisualCommand(VisualCommand.OpenFoldInSelection, visualSpan);
                Assert.True(_outliningManager.GetAllRegions(_textBuffer.GetExtent()).All((c) => !c.IsCollapsed));
            }
        }
    }
}
