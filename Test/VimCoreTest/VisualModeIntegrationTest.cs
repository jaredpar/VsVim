using System;
using System.Linq;
using System.Threading;
using Vim.EditorHost;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Vim.Extensions;
using Vim.UnitTest.Mock;
using Xunit;
using Xunit.Extensions;
using System.Collections.Generic;
using Vim.UnitTest.Exports;
using System.Threading.Tasks;

namespace Vim.UnitTest
{
    public abstract class VisualModeIntegrationTest : VimTestBase
    {
        private IVimBuffer _vimBuffer;
        private IVimBufferData _vimBufferData;
        private IVimTextBuffer _vimTextBuffer;
        private IWpfTextView _textView;
        private ITextBuffer _textBuffer;
        private IRegisterMap _registerMap;
        private IVimGlobalSettings _globalSettings;
        protected MockVimHost _vimHost;
        protected TestableMouseDevice _testableMouseDevice;

        internal Register TestRegister
        {
            get { return _vimBuffer.RegisterMap.GetRegister('c'); }
        }

        protected virtual void Create(params string[] lines)
        {
            _textView = CreateTextView(lines);
            _textBuffer = _textView.TextBuffer;
            _vimBuffer = Vim.CreateVimBuffer(_textView);
            _vimBuffer.SwitchMode(ModeKind.Normal, ModeArgument.None);
            _vimBufferData = _vimBuffer.VimBufferData;
            _vimTextBuffer = _vimBuffer.VimTextBuffer;
            _registerMap = _vimBuffer.RegisterMap;
            _globalSettings = _vimBuffer.LocalSettings.GlobalSettings;

            // Need to make sure it's focused so macro recording will work
            _vimHost = (MockVimHost)_vimBuffer.Vim.VimHost;
            _vimHost.FocusedTextView = _textView;

            _testableMouseDevice = (TestableMouseDevice)MouseDevice;
            _testableMouseDevice.IsLeftButtonPressed = false;
            _testableMouseDevice.Point = null;

            // Some tests create a buffer than does not end with a newline and
            // then insert text on the last line which would add one if ':set
            // endofline' were in effect.
            if (lines.Length > 0 && lines[lines.Length - 1] != string.Empty)
            {
                _vimBufferData.LocalSettings.EndOfLine = false;
            }
        }

        public override void Dispose()
        {
            _testableMouseDevice.IsLeftButtonPressed = false;
            _testableMouseDevice.Point = null;
            base.Dispose();
        }

        protected virtual void Create(int tabStop, params string[] lines)
        {
            Create(lines);
            UpdateTabStop(_vimBuffer, tabStop);
        }

        protected void EnterMode(SnapshotSpan span)
        {
            var characterSpan = new CharacterSpan(span);
            var visualSelection = VisualSelection.NewCharacter(characterSpan, SearchPath.Forward);
            visualSelection.SelectAndMoveCaret(_textView);
            DoEvents();
        }

        protected void EnterMode(ModeKind kind, SnapshotSpan span)
        {
            EnterMode(span);
            _vimBuffer.SwitchMode(kind, ModeArgument.None);
        }

        /// <summary>
        /// Switches mode, then sets the visual selection. The order is reversed from EnterMode(ModeKind, SnapshotSpan).
        /// </summary>
        /// <param name="kind"></param>
        /// <param name="span"></param>
        protected void SwitchEnterMode(ModeKind kind, SnapshotSpan span)
        {
            _vimBuffer.SwitchMode(kind, ModeArgument.None);
            var characterSpan = new CharacterSpan(span);
            var visualSelection = VisualSelection.NewCharacter(characterSpan, SearchPath.Forward);
            visualSelection.SelectAndMoveCaret(_textView);
            // skipping check: context.IsEmpty == false
            DoEvents();
        }

        protected void EnterBlock(BlockSpan blockSpan)
        {
            var visualSpan = VisualSpan.NewBlock(blockSpan);
            var visualSelection = VisualSelection.CreateForward(visualSpan);
            visualSelection.SelectAndMoveCaret(_textView);
            DoEvents();
            _vimBuffer.SwitchMode(ModeKind.VisualBlock, ModeArgument.None);
        }

        public sealed class LeftMouseTest : VisualModeIntegrationTest
        {
            [WpfFact]
            public void ExclusiveDrag()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "exclusive";
                var startPoint = _textView.GetPointInLine(0, 4); // 'd' in 'dog'
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse>");
                Assert.Equal(startPoint.Position, _textView.GetCaretPoint().Position);
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind); // still normal
                var midPoint = _textView.GetPointInLine(0, 5); // 'o' in 'dog'
                _testableMouseDevice.Point = midPoint;
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal("d", _textView.GetSelectionSpan().GetText());
                Assert.Equal(midPoint.Position, _textView.GetCaretPoint().Position);
                var endPoint = _textView.GetPointInLine(0, 7); // ' ' after 'dog'
                _testableMouseDevice.Point = endPoint;
                _vimBuffer.ProcessNotation("<LeftRelease>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(endPoint.Position, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void InclusiveDrag()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "inclusive";
                var startPoint = _textView.GetPointInLine(0, 4); // 'd' in 'dog'
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse>");
                Assert.Equal(startPoint.Position, _textView.GetCaretPoint().Position);
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind); // still normal
                var midPoint = _textView.GetPointInLine(0, 5); // 'o' in 'dog'
                _testableMouseDevice.Point = midPoint;
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal("do", _textView.GetSelectionSpan().GetText());
                Assert.Equal(midPoint.Position, _textView.GetCaretPoint().Position);
                var endPoint = _textView.GetPointInLine(0, 6); // 'g' in 'dog'
                _testableMouseDevice.Point = endPoint;
                _vimBuffer.ProcessNotation("<LeftRelease>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(endPoint.Position, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void InsertDrag()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                _vimBuffer.SwitchMode(ModeKind.Insert, ModeArgument.None);
                var startPoint = _textView.GetPointInLine(0, 4); // 'd' in 'dog'
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse>");
                Assert.Equal(startPoint.Position, _textView.GetCaretPoint().Position);
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal(ModeKind.Insert, _vimBuffer.ModeKind); // still insert
                var midPoint = _textView.GetPointInLine(0, 5); // 'o' in 'dog'
                _testableMouseDevice.Point = midPoint;
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal("do", _textView.GetSelectionSpan().GetText());
                Assert.Equal(midPoint.Position, _textView.GetCaretPoint().Position);
                var endPoint = _textView.GetPointInLine(0, 6); // 'g' in 'dog'
                _testableMouseDevice.Point = endPoint;
                _vimBuffer.ProcessNotation("<LeftRelease>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(endPoint.Position, _textView.GetCaretPoint().Position);
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse><LeftRelease>");
                Assert.Equal(startPoint.Position, _textView.GetCaretPoint().Position);
                Assert.Equal(ModeKind.Insert, _vimBuffer.ModeKind); // back to insert
            }

            [WpfFact]
            public void ExclusiveShiftClick()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "exclusive";
                var startPoint = _textView.GetPointInLine(0, 4); // 'd' in 'dog'
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse>");
                Assert.Equal(startPoint.Position, _textView.GetCaretPoint().Position);
                var endPoint = _textView.GetPointInLine(0, 7); // ' ' after 'dog'
                _testableMouseDevice.Point = endPoint;
                _vimBuffer.ProcessNotation("<S-LeftMouse>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(endPoint.Position, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void InclusiveShiftClick()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "inclusive";
                var startPoint = _textView.GetPointInLine(0, 4); // 'd' in 'dog'
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse>");
                Assert.Equal(startPoint.Position, _textView.GetCaretPoint().Position);
                var endPoint = _textView.GetPointInLine(0, 6); // 'g' in 'dog'
                _testableMouseDevice.Point = endPoint;
                _vimBuffer.ProcessNotation("<S-LeftMouse>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(endPoint.Position, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void InsertShiftClick()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                _vimBuffer.SwitchMode(ModeKind.Insert, ModeArgument.None);
                var startPoint = _textView.GetPointInLine(0, 4); // 'd' in 'dog'
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse>");
                Assert.Equal(startPoint.Position, _textView.GetCaretPoint().Position);
                var endPoint = _textView.GetPointInLine(0, 6); // 'g' in 'dog'
                _testableMouseDevice.Point = endPoint;
                _vimBuffer.ProcessNotation("<S-LeftMouse>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(endPoint.Position, _textView.GetCaretPoint().Position);
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse>");
                Assert.Equal(startPoint.Position, _textView.GetCaretPoint().Position);
                Assert.Equal(ModeKind.Insert, _vimBuffer.ModeKind); // back to insert
            }

            [WpfFact]
            public void LinewiseShiftClick()
            {
                Create("cat dog bear", "pig horse bat", "");
                _textView.SetVisibleLineCount(2);
                _vimBuffer.ProcessNotation("V");
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                Assert.Equal(_textBuffer.GetLineRange(0).ExtentIncludingLineBreak,
                    _textView.GetSelectionSpan());
                var point = _textView.GetPointInLine(1, 5); // 'o' in 'horse'
                _testableMouseDevice.Point = point;
                _vimBuffer.ProcessNotation("<S-LeftMouse>");
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                Assert.Equal(_textBuffer.GetLineRange(0, 1).ExtentIncludingLineBreak,
                    _textView.GetSelectionSpan());
                Assert.Equal(point.Position, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void ExclusiveDoubleClick()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "exclusive";
                var point = _textView.GetPointInLine(0, 5); // 'o' in 'dog'
                _testableMouseDevice.Point = point;
                _vimBuffer.ProcessNotation("<LeftMouse><2-LeftMouse>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(7, _textView.GetCaretPoint().Position); // ' ' after 'dog'
            }

            [WpfFact]
            public void InclusiveDoubleClick()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "inclusive";
                var point = _textView.GetPointInLine(0, 5); // 'o' in 'dog'
                _testableMouseDevice.Point = point;
                _vimBuffer.ProcessNotation("<LeftMouse><2-LeftMouse>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(6, _textView.GetCaretPoint().Position); // 'g' in 'dog'
            }

            [WpfFact]
            public void ExclusiveDoubleClickAndDrag()
            {
                Create("cat dog bear bat", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "exclusive";
                _testableMouseDevice.Point = _textView.GetPointInLine(0, 5); // 'o' in 'dog'
                _vimBuffer.ProcessNotation("<LeftMouse><LeftRelease><2-LeftMouse>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(7, _textView.GetCaretPoint().Position); // ' ' after 'dog'
                _testableMouseDevice.Point = _textView.GetPointInLine(0, 9); // 'e' in 'bear'
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog bear", _textView.GetSelectionSpan().GetText());
                Assert.Equal(12, _textView.GetCaretPoint().Position); // ' ' after 'bear'
                _testableMouseDevice.Point = _textView.GetPointInLine(0, 1); // 'a' in 'cat'
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("cat ", _textView.GetSelectionSpan().GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position); // 'c' in 'cat'
                _testableMouseDevice.Point = _textView.GetPointInLine(0, 9); // 'e' in 'bear'
                _vimBuffer.ProcessNotation("<LeftRelease>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog bear", _textView.GetSelectionSpan().GetText());
                Assert.Equal(12, _textView.GetCaretPoint().Position); // ' ' after 'bear'
            }

            [WpfFact]
            public void InclusiveDoubleClickAndDrag()
            {
                Create("cat dog bear bat", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "inclusive";
                _testableMouseDevice.Point = _textView.GetPointInLine(0, 5); // 'o' in 'dog'
                _vimBuffer.ProcessNotation("<LeftMouse><LeftRelease><2-LeftMouse>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(6, _textView.GetCaretPoint().Position); // 'g' in 'dog'
                _testableMouseDevice.Point = _textView.GetPointInLine(0, 9); // 'e' in 'bear'
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog bear", _textView.GetSelectionSpan().GetText());
                Assert.Equal(11, _textView.GetCaretPoint().Position); // 'r' in 'bear'
                _testableMouseDevice.Point = _textView.GetPointInLine(0, 1); // 'a' in 'cat'
                _vimBuffer.ProcessNotation("<LeftDrag>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("cat dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position); // 'c' in 'cat'
                _testableMouseDevice.Point = _textView.GetPointInLine(0, 9); // 'e' in 'bear'
                _vimBuffer.ProcessNotation("<LeftRelease>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("dog bear", _textView.GetSelectionSpan().GetText());
                Assert.Equal(11, _textView.GetCaretPoint().Position); // 'r' in 'bear'
            }

            [WpfFact]
            public void TokenDoubleClick()
            {
                Create("cat (dog) bear", "");
                _textView.SetVisibleLineCount(2);
                _globalSettings.Selection = "inclusive";
                var point = _textView.GetPointInLine(0, 4); // open paren
                _testableMouseDevice.Point = point;
                _vimBuffer.ProcessNotation("<LeftMouse><LeftRelease><2-LeftMouse><LeftRelease>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal("(dog)", _textView.GetSelectionSpan().GetText());
                Assert.Equal(8, _textView.GetCaretPoint().Position); // close paren
            }

            [WpfFact]
            public void DirectiveDoubleClick()
            {
                Create("cat", "#if DEBUG", "xyzzy", "#endif", "dog", "");
                _textView.SetVisibleLineCount(6);
                _globalSettings.Selection = "inclusive";
                var startPoint = _textView.GetPointInLine(1, 0); // '#' in '#if'
                _testableMouseDevice.Point = startPoint;
                _vimBuffer.ProcessNotation("<LeftMouse><LeftRelease><2-LeftMouse><LeftRelease>");
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                var range = SnapshotLineRange.CreateForLineNumberRange(_textView.TextSnapshot, 1, 3);
                Assert.Equal(range.Value.ExtentIncludingLineBreak, _textView.GetSelectionSpan());
                var endPoint = _textView.GetPointInLine(3, 0); // '#' in '#endif'
                Assert.Equal(endPoint.Position, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void TripleClick()
            {
                Create("cat dog bear", "pig horse bat", "");
                _textView.SetVisibleLineCount(3);
                var point = _textView.GetPointInLine(1, 5); // 'o' in 'horse'
                _testableMouseDevice.Point = point;
                _vimBuffer.ProcessNotation("<LeftMouse><LeftRelease><2-LeftMouse><LeftRelease><3-LeftMouse><LeftRelease>");
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                Assert.Equal(_textBuffer.GetLineRange(1).ExtentIncludingLineBreak,
                    _textView.GetSelectionSpan());
                Assert.Equal(point.Position, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void QuadrupleClick()
            {
                Create("cat dog bear", "");
                _textView.SetVisibleLineCount(2);
                var point = _textView.GetPointInLine(0, 5); // 'o' in 'dog'
                _testableMouseDevice.Point = point;
                _vimBuffer.ProcessNotation("<LeftMouse><LeftRelease><2-LeftMouse><LeftRelease><3-LeftMouse><LeftRelease><4-LeftMouse><LeftRelease>");
                Assert.Equal(ModeKind.VisualBlock, _vimBuffer.ModeKind);
                Assert.Equal("o", _textView.GetSelectionSpan().GetText());
                Assert.Equal(point.Position, _textView.GetCaretPoint().Position);
            }
        }

        /// <summary>
        /// Standard block selection tests
        /// </summary>
        public abstract class BlockSelectionTest : VisualModeIntegrationTest
        {
            private int _tabStop;

            protected override void Create(params string[] lines)
            {
                base.Create(lines);
                _tabStop = _vimBuffer.LocalSettings.TabStop;
            }

            public sealed class TabTest : BlockSelectionTest
            {
                protected override void Create(params string[] lines)
                {
                    base.Create();
                    UpdateTabStop(_vimBuffer, 4);
                    _vimBuffer.TextView.SetText(lines);
                    _textView.MoveCaretTo(0);
                }

                [WpfFact]
                public void CaretInTab()
                {
                    Create("cat", "\tdog");
                    _vimBuffer.ProcessNotation("<C-Q>j");
                    Assert.Equal(
                        new[]
                        {
                            _textBuffer.GetLineSpan(0, 3),
                            _textBuffer.GetLineSpan(1, 1)
                        },
                        _textView.Selection.SelectedSpans);
                }

                [WpfFact]
                public void CaretInTabAnchorNonZero()
                {
                    Create("cat", "\tdog");
                    _vimBuffer.ProcessNotation("ll<C-Q>j");

                    Assert.Equal(
                        new[]
                        {
                            _textBuffer.GetLineSpan(0, 3),
                            _textBuffer.GetLineSpan(1, 1)
                        },
                        _textView.Selection.SelectedSpans);
                }

                /// <summary>
                /// The caret is past the tab.  Hence the selection for the first line should
                /// be correct.
                /// </summary>
                [WpfFact]
                public void CaretPastTab()
                {
                    Create("kitty", "\tdog");
                    _vimBuffer.ProcessNotation("ll<C-Q>jl");

                    // In a strict vim interpretation both '\t' and 'd' would be selected in the
                    // second line.  The Visual Studio editor won't have this selection and instead
                    // will not select the tab since it's only partially selected.  Hence only the
                    // 'd' will end up selected
                    Assert.Equal(
                        new[]
                        {
                            _textBuffer.GetLineSpan(0, 2, 3),
                            _textBuffer.GetLineSpan(1, 1, 1)
                        },
                        _textView.Selection.SelectedSpans);
                }

                /// <summary>
                /// This is an anti fact
                ///
                /// The WPF editor can't place the caret in the middle of a tab.  It can't
                /// for example put it on the 2 of the 4th space a tab occupies.
                /// </summary>
                [WpfFact]
                public void MiddleOfTab()
                {
                    Create("cat", "d\tog");
                    _vimBuffer.LocalSettings.TabStop = 4;
                    _vimBuffer.ProcessNotation("ll<C-q>jl");
                    var textView = _vimBuffer.TextView;
                    Assert.Equal('t', textView.Selection.Start.Position.GetChar());
                    Assert.Equal('g', textView.Selection.End.Position.GetChar());
                }
            }

            public sealed class MiscTest : BlockSelectionTest
            {
                /// <summary>
                /// Make sure the CTRL-Q command causes the block selection to start out as a single width
                /// column
                /// </summary>
                [WpfFact]
                public void InitialState()
                {
                    Create("hello world");
                    _vimBuffer.ProcessNotation("<C-Q>");
                    Assert.Equal(ModeKind.VisualBlock, _vimBuffer.ModeKind);
                    var blockSpan = new BlockSpan(_textBuffer.GetPoint(0), tabStop: _tabStop, spaces: 1, height: 1);
                    Assert.Equal(blockSpan, _vimBuffer.GetSelectionBlockSpan());
                }

                /// <summary>
                /// Make sure the CTRL-Q command causes the block selection to start out as a single width
                /// column from places other than the start of the document
                /// </summary>
                [WpfFact]
                public void InitialNonStartPoint()
                {
                    Create("big cats", "big dogs", "big trees");
                    var point = _textBuffer.GetPointInLine(1, 3);
                    _textView.MoveCaretTo(point);
                    _vimBuffer.ProcessNotation("<C-Q>");
                    Assert.Equal(ModeKind.VisualBlock, _vimBuffer.ModeKind);
                    var blockSpan = new BlockSpan(point, tabStop: _tabStop, spaces: 1, height: 1);
                    Assert.Equal(blockSpan, _vimBuffer.GetSelectionBlockSpan());
                }

                /// <summary>
                /// A left movement in block selection should move the selection to the left
                /// </summary>
                [WpfFact]
                public void Backwards()
                {
                    Create("big cats", "big dogs");
                    _textView.MoveCaretTo(2);
                    _vimBuffer.ProcessNotation("<C-Q>jh");
                    Assert.Equal(ModeKind.VisualBlock, _vimBuffer.ModeKind);
                    var blockSpan = new BlockSpan(_textView.GetPoint(1), tabStop: _tabStop, spaces: 2, height: 2);
                    Assert.Equal(blockSpan, _vimBuffer.GetSelectionBlockSpan());
                }

                /// <summary>
                /// A delete in end-of-line block mode should delete to the
                /// end of all lines
                /// </summary>
                [WpfFact]
                public void DeleteToEndOfLine()
                {
                    Create("abc def ghi", "jkl mno", "pqr", "");
                    _vimBuffer.ProcessNotation("2l<C-Q>2j$d");
                    Assert.Equal(new[] { "ab", "jk", "pq", "" }, _textBuffer.GetLines());
                }

                /// <summary>
                /// Block put should work even when using the clipboard as the
                /// unnamed register
                /// </summary>
                [WpfTheory]
                [InlineData("")]
                [InlineData("unnamed")]
                public void DeleteAndPut(string clipboard)
                {
                    // Reported in issue #2694.
                    Create("abc def ghi jkl", "mno pqr stu vwx", "");
                    _globalSettings.Clipboard = clipboard;
                    _textView.MoveCaretToLine(0, 4);
                    _vimBuffer.ProcessNotation("<C-Q>jeldwP");
                    Assert.Equal(new[] { "abc ghi def jkl", "mno stu pqr vwx", "" }, _textBuffer.GetLines());
                }
            }

            public sealed class ExclusiveTest : BlockSelectionTest
            {
                /// <summary>
                /// When selection is exclusive there should still be a single column selected in block
                /// mode even if the original width is 1
                /// </summary>
                [WpfFact]
                public void OneWidthBlock()
                {
                    Create("the dog", "the cat");
                    _textView.MoveCaretTo(1);
                    _globalSettings.Selection = "exclusive";
                    _vimBuffer.Process(KeyInputUtil.CharWithControlToKeyInput('q'));
                    _vimBuffer.Process('j');
                    var blockSpan = _textBuffer.GetBlockSpan(1, 1, 0, 2, tabStop: _tabStop);
                    Assert.Equal(blockSpan, _vimBuffer.GetSelectionBlockSpan());
                    Assert.Equal(_textView.GetPointInLine(1, 1), _textView.GetCaretPoint());
                }

                /// <summary>
                /// When selection is exclusive block selection should shrink by one in width
                /// </summary>
                [WpfFact]
                public void TwoWidthBlock()
                {
                    Create("the dog", "the cat");
                    _textView.MoveCaretTo(1);
                    _globalSettings.Selection = "exclusive";
                    _vimBuffer.Process(KeyInputUtil.CharWithControlToKeyInput('q'));
                    _vimBuffer.Process("jl");
                    var blockSpan = _textBuffer.GetBlockSpan(1, 1, 0, 2, tabStop: _tabStop);
                    Assert.Equal(blockSpan, _vimBuffer.GetSelectionBlockSpan());
                    Assert.Equal(_textView.GetPointInLine(1, 2), _textView.GetCaretPoint());
                }
            }

            public sealed class VirtualEditTest : BlockSelectionTest
            {
                protected override void Create(params string[] lines)
                {
                    base.Create(lines);
                    _globalSettings.VirtualEdit = "block";
                }

                [WpfFact]
                public void Inclusive()
                {
                    Create("cat", "dog bear", "tree", "");
                    _globalSettings.Selection = "inclusive";
                    _vimBuffer.ProcessNotation("<C-q>2j8l");
                    Assert.Equal(_textBuffer.GetVirtualPointInLine(2, 4, 4), _textView.GetCaretVirtualPoint());
                    var blockSpan = _vimBuffer.GetSelectionBlockSpan();
                    Assert.Equal(3, blockSpan.Height);
                    Assert.Equal(9, blockSpan.SpacesLength);
                }

                [WpfFact]
                public void Exclusive()
                {
                    Create("cat", "dog bear", "tree", "");
                    _globalSettings.Selection = "exclusive";
                    _vimBuffer.ProcessNotation("<C-q>2j8l");
                    Assert.Equal(_textBuffer.GetVirtualPointInLine(2, 4, 4), _textView.GetCaretVirtualPoint());
                    var blockSpan = _vimBuffer.GetSelectionBlockSpan();
                    Assert.Equal(3, blockSpan.Height);
                    Assert.Equal(8, blockSpan.SpacesLength);
                }
            }
        }

        public sealed class AddSubtractTest : VisualModeIntegrationTest
        {
            [WpfTheory]
            [InlineData("v")]
            [InlineData("V")]
            [InlineData("<C-v>")]
            public void Basic(string visualKey)
            {
                Create("1", "2", "", "3", "");
                _vimBuffer.ProcessNotation(visualKey);
                _vimBuffer.ProcessNotation("3j$");
                _vimBuffer.ProcessNotation("<C-a>");
                Assert.Equal(new[] { "2", "3", "", "4", "" }, _textBuffer.GetLines());
            }

            [WpfTheory]
            [InlineData("v")]
            [InlineData("V")]
            [InlineData("<C-v>")]
            public void Progressive(string visualKey)
            {
                Create("1", "2", "", "3", "");
                _vimBuffer.ProcessNotation(visualKey);
                _vimBuffer.ProcessNotation("3j$");
                _vimBuffer.ProcessNotation("g<C-a>");
                Assert.Equal(new[] { "2", "4", "", "6", "" }, _textBuffer.GetLines());
            }
        }

        public abstract class VisualShiftTest : VisualModeIntegrationTest
        {
            protected abstract string Select { get; }
            protected abstract IEnumerable<string> Lines();

            [WpfFact]
            public void IndentAdds()
            {
                Create("one", "two", "three");
                _vimBuffer.ProcessNotation($"{Select}>");
                Assert.All(Lines(), line => Assert.StartsWith("\t", line));
            }

            [WpfFact]
            public void OutdentRemoves()
            {
                Create("\tone", "\ttwo", "\tthree");
                _vimBuffer.ProcessNotation($"{Select}<lt>");
                Assert.All(Lines(), line => Assert.False(line.StartsWith("\t")));
            }

            public sealed class Block : VisualShiftTest
            {
                protected override string Select => "<C-Q>jj";
                protected override IEnumerable<string> Lines()
                    => _textBuffer.GetLines();
            }

            public sealed class Line : VisualShiftTest
            {
                protected override string Select => "Vj";
                protected override IEnumerable<string> Lines()
                    => _textBuffer.GetLines().Take(2);
            }

            public sealed class Character : VisualShiftTest
            {
                protected override string Select => "v";
                protected override IEnumerable<string> Lines()
                    => _textBuffer.GetLines().Take(1);
            }
        }

        public sealed class ChangeLineSelectionTest : VisualModeIntegrationTest
        {
            /// <summary>
            /// Even a visual character change is still a linewise delete
            /// </summary>
            [WpfFact]
            public void CharacterIsLineWise()
            {
                Create("cat", "dog");
                _vimBuffer.Process("vC");
                Assert.Equal("cat" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal(new[] { "", "dog" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void LineIsLineWise()
            {
                Create("cat", "dog");
                _vimBuffer.Process("VC");
                Assert.Equal("cat" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal(new[] { "", "dog" }, _textBuffer.GetLines());
            }
        }

        public abstract class EnterVisualModeWithCountTest : VisualModeIntegrationTest
        {
            public sealed class CharacterTest : EnterVisualModeWithCountTest
            {
                [WpfTheory]
                [InlineData('v')]
                [InlineData('V')]
                public void SimpleCharacter(char kind)
                {
                    Create("dog");
                    _vimBuffer.ProcessNotation("vy");
                    Assert.Equal(StoredVisualSelection.NewCharacter(width: 1), VimData.LastVisualSelection.Value);
                    _vimBuffer.ProcessNotation($"2{kind}");
                    Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                    Assert.Equal("do", _textView.Selection.GetSpan().GetText());
                }

                [WpfTheory]
                [InlineData('v')]
                [InlineData('V')]
                public void CountGoesPastSingleLine(char kind)
                {
                    Create("dog", "");
                    _vimBuffer.ProcessNotation("vly");
                    Assert.Equal(StoredVisualSelection.NewCharacter(width: 2), VimData.LastVisualSelection.Value);
                    _vimBuffer.ProcessNotation($"20{kind}");
                    Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                    _vimBuffer.ProcessNotation("y");
                    Assert.Equal("dog" + Environment.NewLine, UnnamedRegister.StringValue);
                }

                [WpfTheory]
                [InlineData('v')]
                [InlineData('V')]
                public void CountAcrossLines(char kind)
                {
                    Create("dog", "cat", "fish", "tree");
                    _vimBuffer.ProcessNotation("vjy");
                    Assert.Equal(StoredVisualSelection.NewCharacterLine(lineCount: 2, lastLineMaxOffset: 0), VimData.LastVisualSelection.Value);
                    _vimBuffer.ProcessNotation($"2{kind}");
                    Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                    Assert.Equal(_textBuffer.GetPoint(0), _textView.Selection.Start.Position);
                    Assert.Equal(_textBuffer.GetPointInLine(line: 3, column: 1), _textView.Selection.End.Position);
                }

                /// <summary>
                /// When using a count across a multi-line character selection the count just multilies lines
                /// but keeps the character in the same column.
                /// </summary>
                [WpfTheory]
                [InlineData('v')]
                [InlineData('V')]
                public void CountAcrossLinesNonZeroColumn(char kind)
                {
                    Create("dog", "cat", "fish", "tree");
                    _vimBuffer.ProcessNotation("lvjy");
                    Assert.Equal(StoredVisualSelection.NewCharacterLine(lineCount: 2, lastLineMaxOffset: 0), VimData.LastVisualSelection.Value);
                    _vimBuffer.ProcessNotation($"2{kind}");
                    Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                    Assert.Equal(_textBuffer.GetPointInLine(line: 0, column: 1), _textView.Selection.Start.Position);
                    Assert.Equal(_textBuffer.GetPointInLine(line: 3, column: 2), _textView.Selection.End.Position);
                }

                /// <summary>
                /// The 3rd column doesn't exist here but should go to the 2nd which is the
                /// new line
                /// </summary>
                [WpfTheory]
                [InlineData('v')]
                [InlineData('V')]
                public void CountAcrossLinesNonZeroColumnThatExtends(char kind)
                {
                    Create("dog", "cat", "fish", "t");
                    _vimBuffer.ProcessNotation("llvjy");
                    Assert.Equal(StoredVisualSelection.NewCharacterLine(lineCount: 2, lastLineMaxOffset: 0), VimData.LastVisualSelection.Value);
                    _vimBuffer.ProcessNotation($"2{kind}");
                    Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                    Assert.Equal(_textBuffer.GetPointInLine(line: 0, column: 2), _textView.Selection.Start.Position);
                    Assert.Equal(_textBuffer.GetPointInLine(line: 3, column: 1), _textView.Selection.End.Position);
                }

                /// <summary>
                /// When looking at a multiline character span the last column is stored as an offset of the
                /// start point: positive or negative. In the case where the 1v command results in a single line
                /// this will result in a reverse selection.
                /// </summary>
                [WpfTheory]
                [InlineData('v')]
                [InlineData('V')]
                public void MultipleLinesShrunkResultsInReverseSpan(char kind)
                {
                    Create("dog", "cat", "fish", "tt");
                    _vimBuffer.ProcessNotation("llvjhy");
                    Assert.Equal(StoredVisualSelection.NewCharacterLine(lineCount: 2, lastLineMaxOffset: -1), VimData.LastVisualSelection.Value);
                    _textView.MoveCaretToLine(lineNumber: 3, column: 2);
                    _vimBuffer.ProcessNotation($"1{kind}");
                    Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                    Assert.Equal(_textBuffer.GetPointInLine(line: 3, column: 1), _textView.Selection.Start.Position);
                    Assert.Equal(_textBuffer.GetPointInLine(line: 3, column: 2), _textView.Selection.End.Position);
                }
            }

            public sealed class LineTest : EnterVisualModeWithCountTest
            {
                [WpfTheory]
                [InlineData('v')]
                [InlineData('V')]
                public void SimpleLine(char kind)
                {
                    Create("dog", "cat", "fish", "tree");
                    _vimBuffer.ProcessNotation("Vy");
                    Assert.Equal(StoredVisualSelection.NewLine(1), VimData.LastVisualSelection.Value);
                    _vimBuffer.ProcessNotation($"1{kind}");
                    Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                    var selection = _vimBuffer.VisualLineMode.VisualSelection;
                    var range = selection.AsLine().LineRange;
                    Assert.Equal(range, _textBuffer.GetLineRange(startLine: 0, endLine: 0));
                }

                [WpfTheory]
                [InlineData('v')]
                [InlineData('V')]
                public void SimpleLineDoubleCount(char kind)
                {
                    Create("dog", "cat", "fish", "tree");
                    _vimBuffer.ProcessNotation("Vy");
                    Assert.Equal(StoredVisualSelection.NewLine(1), VimData.LastVisualSelection.Value);
                    _vimBuffer.ProcessNotation($"2{kind}");
                    Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                    var selection = _vimBuffer.VisualLineMode.VisualSelection;
                    var range = selection.AsLine().LineRange;
                    Assert.Equal(range, _textBuffer.GetLineRange(startLine: 0, endLine: 1));
                }
            }
        }

        public sealed class DeleteLineSelectionTest : VisualModeIntegrationTest
        {
            /// <summary>
            /// Even a visual character change is still a linewise delete
            /// </summary>
            [WpfFact]
            public void CharacterIsLineWise()
            {
                Create("cat", "dog");
                _vimBuffer.Process("vD");
                Assert.Equal("cat" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal(new[] { "dog" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void LineIsLineWise()
            {
                Create("cat", "dog");
                _vimBuffer.Process("VD");
                Assert.Equal("cat" + Environment.NewLine, UnnamedRegister.StringValue);
                Assert.Equal(new[] { "dog" }, _textBuffer.GetLines());
            }
        }

        public abstract class DeleteSelectionTest : VisualModeIntegrationTest
        {
            public sealed class CharacterTest : DeleteSelectionTest
            {
                /// <summary>
                /// When an entire line is selected in character wise mode and then deleted
                /// it should not be a line delete but instead delete the contents of the
                /// line.
                /// </summary>
                [WpfFact]
                public void LineContents()
                {
                    Create("cat", "dog");
                    EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 3));
                    _vimBuffer.Process("x");
                    Assert.Equal("", _textView.GetLine(0).GetText());
                    Assert.Equal("dog", _textView.GetLine(1).GetText());
                }

                /// <summary>
                /// If the character wise selection extents into the line break then the
                /// entire line should be deleted
                /// </summary>
                [WpfFact]
                public void LineContentsFromBreak()
                {
                    Create("cat", "dog");
                    _globalSettings.VirtualEdit = "onemore";
                    EnterMode(ModeKind.VisualCharacter, _textView.GetLine(0).ExtentIncludingLineBreak);
                    _vimBuffer.Process("x");
                    Assert.Equal("dog", _textView.GetLine(0).GetText());
                }

                [WpfFact]
                public void Issue1507()
                {
                    Create("cat", "dog", "fish");
                    _textView.MoveCaretTo(1);
                    _vimBuffer.Process("vjllx");
                    Assert.Equal(new[] { "cfish" }, _textBuffer.GetLines());
                }
            }

            public sealed class LineTest : DeleteSelectionTest
            {
                /// <summary>
                /// Deleting to the end of the file should move the caret up
                /// </summary>
                [WpfFact]
                public void DeleteLines_ToEndOfFile()
                {
                    // Reported in issue #2477.
                    Create("cat", "dog", "fish", "");
                    _textView.MoveCaretToLine(1, 0);
                    _vimBuffer.Process("VGd");
                    Assert.Equal(new[] { "cat", "" }, _textBuffer.GetLines());
                    Assert.Equal(_textView.GetPointInLine(0, 0), _textView.GetCaretPoint());
                }

                /// <summary>
                /// Deleting lines should obey the 'startofline' setting
                /// </summary>
                [WpfFact]
                public void DeleteLines_StartOfLine()
                {
                    // Reported in issue #2477.
                    Create(" cat", "  dog", " fish", "");
                    _textView.MoveCaretToLine(1, 2);
                    _vimBuffer.Process("Vd");
                    Assert.Equal(new[] { " cat", " fish", "" }, _textBuffer.GetLines());
                    Assert.Equal(_textView.GetPointInLine(1, 1), _textView.GetCaretPoint());
                }

                /// <summary>
                /// Deleting lines should preserve spaces to caret when
                /// 'nostartofline' is in effect
                /// </summary>
                [WpfFact]
                public void DeleteLines_NoStartOfLine()
                {
                    // Reported in issue #2477.
                    Create(" cat", "  dog", " fish", "");
                    _globalSettings.StartOfLine = false;
                    _textView.MoveCaretToLine(1, 2);
                    _vimBuffer.Process("Vd");
                    Assert.Equal(new[] { " cat", " fish", "" }, _textBuffer.GetLines());
                    Assert.Equal(_textView.GetPointInLine(1, 2), _textView.GetCaretPoint());
                }

                /// <summary>
                /// Undoing a visual line delete should return the caret to the first line
                /// </summary>
                [WpfFact]
                public void DeleteLines_Undo()
                {
                    // Reported in issue #2477.
                    Create("cat", "dog", "fish", "bear", "");
                    _textView.MoveCaretToLine(1, 0);
                    _vimBuffer.Process("Vj");
                    Assert.Equal(_textView.GetPointInLine(2, 0), _textView.GetCaretPoint());
                    _vimBuffer.Process("d");
                    Assert.Equal(new[] { "cat", "bear", "" }, _textBuffer.GetLines());
                    Assert.Equal(_textView.GetPointInLine(1, 0), _textView.GetCaretPoint());
                    _vimBuffer.Process("u");
                    Assert.Equal(new[] { "cat", "dog", "fish", "bear", "" }, _textBuffer.GetLines());
                    Assert.Equal(_textView.GetPointInLine(1, 0), _textView.GetCaretPoint());
                }
            }

            public sealed class BlockTest : DeleteSelectionTest
            {
                [WpfFact]
                public void Simple()
                {
                    Create(4, "cat", "dog", "fish");
                    _vimBuffer.ProcessNotation("<C-q>jjx");
                    Assert.Equal(new[]
                        {
                            "at",
                            "og",
                            "ish"
                        },
                        _textBuffer.GetLines());
                }

                [WpfFact]
                public void PartialTab()
                {
                    Create(4, "cat", "\tdog", "fish");
                    _vimBuffer.ProcessNotation("<C-q>jjx");
                    Assert.Equal(new[]
                        {
                            "at",
                            "   dog",
                            "ish"
                        },
                        _textBuffer.GetLines());
                }
            }

            public sealed class MiscTest : DeleteSelectionTest
            {
                /// <summary>
                /// The 'e' motion should result in a selection that encompasses the entire word
                /// </summary>
                [WpfFact]
                public void EndOfWord()
                {
                    Create("the dog. cat");
                    _textView.MoveCaretTo(4);
                    _vimBuffer.Process("vex");
                    Assert.Equal("dog", UnnamedRegister.StringValue);
                    Assert.Equal(4, _textView.GetCaretPoint().Position);
                }

                /// <summary>
                /// The 'e' motion should result in a selection that encompasses the entire word
                /// </summary>
                [WpfFact]
                public void EndOfWord_Block()
                {
                    Create("the dog. end", "the cat. end", "the fish. end");
                    _textView.MoveCaretTo(4);
                    _vimBuffer.Process(KeyInputUtil.CharWithControlToKeyInput('q'));
                    _vimBuffer.Process("jex");
                    Assert.Equal("the . end", _textBuffer.GetLine(0).GetText());
                    Assert.Equal("the . end", _textBuffer.GetLine(1).GetText());
                    Assert.Equal("the fish. end", _textBuffer.GetLine(2).GetText());
                }

                [WpfFact]
                public void SwitchToCommandMode()
                {
                    Create("cat");
                    _textView.MoveCaretTo(0);
                    _vimBuffer.Process("V:");
                    Assert.Equal(ModeKind.Command, _vimBuffer.Mode.ModeKind);
                    Assert.Equal("'<,'>", _vimBuffer.CommandMode.Command);
                }

                /// <summary>
                /// The 'w' motion should result in a selection that encompasses the entire word
                /// </summary>
                [WpfFact]
                public void Word()
                {
                    Create("the dog. cat");
                    _textView.MoveCaretTo(4);
                    _vimBuffer.Process("vwx");
                    Assert.Equal("dog.", UnnamedRegister.StringValue);
                    Assert.Equal(4, _textView.GetCaretPoint().Position);
                }

                /// <summary>
                /// The 'e' motion should select up to and including the end of the word
                ///
                /// https://github.com/VsVim/VsVim/issues/568
                /// </summary>
                [WpfFact]
                public void EndOfWordMotion()
                {
                    Create("ThisIsALongWord. ThisIsAnotherLongWord!");
                    _vimBuffer.Process("vex");
                    Assert.Equal(". ThisIsAnotherLongWord!", _textBuffer.GetLine(0).GetText());
                }
            }
        }

        public sealed class InclusiveSelection : VisualModeIntegrationTest
        {
            protected override void Create(params string[] lines)
            {
                base.Create(lines);
                _globalSettings.Selection = "inclusive";
            }

            /// <summary>
            /// The $ movement should put the caret past the end of the line
            /// </summary>
            [WpfFact]
            public void MoveEndOfLine_Dollar()
            {
                Create("cat", "dog");
                _vimBuffer.Process("v$");
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }
        }

        public sealed class ExclusiveSelection : VisualModeIntegrationTest
        {
            protected override void Create(params string[] lines)
            {
                base.Create(lines);
                _globalSettings.Selection = "exclusive";
            }

            /// <summary>
            /// The caret position should be on the next character for a move right
            /// </summary>
            [WpfFact]
            public void CaretPosition_Right()
            {
                Create("the dog");
                _vimBuffer.Process("vl");
                _vimBuffer.Process(VimKey.Escape);
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// The caret position should be on the start of the next word after leaving visual mode
            /// </summary>
            [WpfFact]
            public void CaretPosition_Word()
            {
                Create("the dog");
                _vimBuffer.Process("vw");
                _vimBuffer.Process(VimKey.Escape);
                Assert.Equal(4, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Make sure the 'e' motion still goes one character extra during a line wise movement
            /// </summary>
            [WpfFact]
            public void CaretPosition_EndOfWordLineWise()
            {
                Create("the dog. the cat");
                _textView.MoveCaretTo(4);
                _vimBuffer.Process("Ve");
                Assert.Equal(7, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// The $ movement should put the caret past the end of the line
            /// </summary>
            [WpfFact]
            public void MoveEndOfLine_Dollar()
            {
                Create("cat", "dog");
                _vimBuffer.Process("v$");
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// The 'l' movement should put the caret past the end of the line
            /// </summary>
            [WpfFact]
            public void MoveEndOfLine_Right()
            {
                Create("cat", "dog");
                _vimBuffer.Process("vlll");
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// The entire word should be selected
            /// </summary>
            [WpfFact]
            public void InnerWord()
            {
                Create("cat   dog");
                _vimBuffer.Process("viw");
                Assert.Equal("cat", _textView.GetSelectionSpan().GetText());
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// The entire word plus the trailing white space should be selected
            /// </summary>
            [WpfFact]
            public void AllWord()
            {
                Create("cat   dog");
                _vimBuffer.Process("vaw");
                Assert.Equal("cat   ", _textView.GetSelectionSpan().GetText());
                Assert.Equal(6, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// The initial character selection in exclusive selection should be empty
            /// </summary>
            [WpfFact]
            public void Issue1483()
            {
                Create("cat dog");
                _vimBuffer.Process("v");
                Assert.Equal(0, _textView.GetSelectionSpan().Length);
            }

            /// <summary>
            /// The visual 'o' command should not modify what is selected
            /// </summary>
            [WpfFact]
            public void SwapAnchor()
            {
                Create("cat dog");
                _vimBuffer.Process("v   ");
                Assert.Equal("cat", _textView.GetSelectionSpan().GetText());
                _vimBuffer.Process("o");
                Assert.Equal("cat", _textView.GetSelectionSpan().GetText());
                _vimBuffer.Process("o");
                Assert.Equal("cat", _textView.GetSelectionSpan().GetText());
            }

            /// <summary>
            /// Switch from character to line mode after 'o' should expand the selection
            /// </summary>
            [WpfFact]
            public void Issue1395()
            {
                Create("cat", "dog", "bear", "bat");
                _vimBuffer.Process(" vj");
                Assert.Equal("at\r\nd", _textView.GetSelectionSpan().GetText());
                _vimBuffer.Process("o");
                Assert.Equal("at\r\nd", _textView.GetSelectionSpan().GetText());
                _vimBuffer.Process("V");
                Assert.Equal("cat\r\ndog\r\n", _textView.GetSelectionSpan().GetText());
            }

            /// <summary>
            /// If the entire block is linewise empty, the selection is unchanged
            /// </summary>
            [WpfFact]
            public void LinewiseEmpty()
            {
                // Reported in issue #1969.
                Create("    Main(", "    );", "");
                _textView.MoveCaretToLine(1);
                var caretPoint = _textView.Caret.Position.BufferPosition;
                _vimBuffer.Process("vi(");
                Assert.Equal(new SnapshotSpan(caretPoint, 0), _textView.GetSelectionSpan());
            }

            [WpfFact]
            public void AtStartOfEmptyLine()
            {
                Create("", "");
                _vimBuffer.Process("v");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 0);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void DownToEmptyLine()
            {
                Create("cat", "", "dog", "");
                _vimBuffer.Process("vj");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 4);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void RightToEndOfLine()
            {
                // Reported in issue #1507.
                Create("cat", "dog", "");
                _vimBuffer.Process("v3l");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 3);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void RightToEndOfLineFromMiddle()
            {
                Create("cat", "dog", "");
                _vimBuffer.Process("lv2l");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 1);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 3);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }
        }

        public sealed class VirtualInclusiveSelection : VisualModeIntegrationTest
        {
            protected override void Create(params string[] lines)
            {
                base.Create(lines);
                _globalSettings.Selection = "inclusive";
                _globalSettings.VirtualEdit = "all";
            }

            [WpfFact]
            public void AtStartOfEmptyLine()
            {
                Create("", "");
                _vimBuffer.Process("v");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 1);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void RightOnEmptyLine()
            {
                Create("", "");
                _vimBuffer.Process("vl");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 2);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void DownToEmptyLine()
            {
                Create("cat", "", "");
                _vimBuffer.Process("vj");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(1, 1);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void RightToVirtualSpace()
            {
                Create("cat", "", "");
                _vimBuffer.Process("v4l");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 5);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }
        }

        public sealed class VirtualExclusiveSelection : VisualModeIntegrationTest
        {
            protected override void Create(params string[] lines)
            {
                base.Create(lines);
                _globalSettings.Selection = "exclusive";
                _globalSettings.VirtualEdit = "all";
            }

            [WpfFact]
            public void AtStartOfEmptyLine()
            {
                Create("", "");
                _vimBuffer.Process("v");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 0);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void RightOnEmptyLine()
            {
                Create("", "");
                _vimBuffer.Process("vl");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 1);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void DownToEmptyLine()
            {
                Create("cat", "", "");
                _vimBuffer.Process("vj");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 4);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }

            [WpfFact]
            public void RightToVirtualSpace()
            {
                Create("cat", "", "");
                _vimBuffer.Process("v4l");
                var point1 = _textBuffer.GetVirtualPointInLine(0, 0);
                var point2 = _textBuffer.GetVirtualPointInLine(0, 4);
                Assert.Equal(point1, _textView.Selection.Start);
                Assert.Equal(point2, _textView.Selection.End);
            }
        }

        public abstract class BlockInsertTest : VisualModeIntegrationTest
        {
            /// <summary>
            /// Simulate intellisense scenarios and make sure that we correctly insert the resulting
            /// text
            /// </summary>
            public sealed class IntellisenseTest : BlockInsertTest
            {
                /// <summary>
                /// Pretend there was nothing to delete, it just got inserted by hitting Ctrl+Space
                /// and selecting the value
                /// </summary>
                [WpfFact]
                public void SimpleIntellisense()
                {
                    Create(
@" string Prop1
 string Prop2
 string Prop3");
                    _vimBuffer.ProcessNotation("<C-Q>jjI");

                    // No simulate the intellisense operation here.  It will delete the "matched" text
                    // and replace with the "completed" text
                    _textBuffer.Replace(new Span(0, 0), "protected");
                    _vimBuffer.ProcessNotation("<Esc>");
                    Assert.Equal(new[]
                        {
                            "protected string Prop1",
                            "protected string Prop2",
                            "protected string Prop3"
                        },
                        _textBuffer.GetLines());
                }

                [WpfFact]
                public void Issue1108()
                {
                    Create(
@" string Prop1
 string Prop2
 string Prop3");
                    _vimBuffer.ProcessNotation("<C-Q>jjIpr");

                    // No simulate the intellisense operation here.  It will delete the "matched" text
                    // and replace with the "completed" text
                    _textBuffer.Replace(new Span(0, 2), "protected");
                    _vimBuffer.ProcessNotation("<Esc>");
                    Assert.Equal(new[]
                        {
                            "protected string Prop1",
                            "protected string Prop2",
                            "protected string Prop3"
                        },
                        _textBuffer.GetLines());
                }
            }

            public sealed class PartialTabEditTest : BlockInsertTest
            {
                [WpfFact]
                public void SimpleMiddle()
                {
                    Create(4, "trucker", "\tdog", "tester");
                    _vimBuffer.ProcessNotation("ll<c-q>jjjIa<Esc>");
                    Assert.Equal(new[]
                        {
                            "traucker",
                            "  a  dog",
                            "teaster"
                        },
                        _textBuffer.GetLines());
                    Assert.Equal(2, _textView.GetCaretPoint().Position);
                }

                /// <summary>
                /// When the selection is at the start of the tab then the tab should be
                /// kept because it is not being split
                /// </summary>
                [WpfFact]
                public void SimpleStartOfLine()
                {
                    Create(4, "trucker", "\tdog", "tester");
                    _vimBuffer.ProcessNotation("<c-q>jjjIa<Esc>");
                    Assert.Equal(new[]
                        {
                            "atrucker",
                            "a\tdog",
                            "atester"
                        },
                        _textBuffer.GetLines());
                    Assert.Equal(0, _textView.GetCaretPoint().Position);
                }

                [WpfFact]
                public void SimpleOneSpaceIn()
                {
                    Create(4, "trucker", "\tdog", "tester");
                    _vimBuffer.ProcessNotation("l<c-q>jjjIa<Esc>");
                    Assert.Equal(new[]
                        {
                            "tarucker",
                            " a   dog",
                            "taester"
                        },
                        _textBuffer.GetLines());
                    Assert.Equal(1, _textView.GetCaretPoint().Position);
                }

                [WpfFact]
                public void SimpleLastSpaceInTab()
                {
                    Create(4, "trucker", "\tdog", "tester");
                    _vimBuffer.ProcessNotation("lll<c-q>jjjIa<Esc>");
                    Assert.Equal(new[]
                        {
                            "truacker",
                            "   a dog",
                            "tesater"
                        },
                        _textBuffer.GetLines());
                    Assert.Equal(3, _textView.GetCaretPoint().Position);
                }
            }

            public sealed class MiscTest : BlockInsertTest
            {
                /// <summary>
                /// The block insert should add the text to every column
                /// </summary>
                [WpfFact]
                public void Simple()
                {
                    Create("dog", "cat", "fish");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i>the <Esc>");
                    Assert.Equal("the dog", _textBuffer.GetLine(0).GetText());
                    Assert.Equal("the cat", _textBuffer.GetLine(1).GetText());
                }

                /// <summary>
                /// The block insert should add the text to every column
                /// </summary>
                [WpfFact]
                public void SimpleAfterDollar()
                {
                    Create("dog", "cat", "fish");
                    _vimBuffer.ProcessNotation("<C-q>j$<S-i>the <Esc>");
                    Assert.Equal("the dog", _textBuffer.GetLine(0).GetText());
                    Assert.Equal("the cat", _textBuffer.GetLine(1).GetText());
                }

                /// <summary>
                /// The caret should be positioned at the start of the block span when the insertion
                /// starts
                /// </summary>
                [WpfFact]
                public void CaretPosition()
                {
                    Create("dog", "cat", "fish");
                    _vimBuffer.ProcessNotation("<C-q>jl<S-i>");
                    Assert.Equal(0, _textView.GetCaretPoint().Position);
                    Assert.Equal(ModeKind.Insert, _vimBuffer.ModeKind);
                }

                /// <summary>
                /// The block insert shouldn't add text to any of the columns which didn't extend into
                /// the original selection
                /// </summary>
                [WpfFact]
                public void EmptyColumn()
                {
                    Create("dog", "", "fish");
                    _vimBuffer.ProcessNotation("l<C-q>jjl<S-i> the <Esc>");
                    Assert.Equal("d the og", _textBuffer.GetLine(0).GetText());
                    Assert.Equal("", _textBuffer.GetLine(1).GetText());
                    Assert.Equal("f the ish", _textBuffer.GetLine(2).GetText());
                    Assert.Equal(1, _textView.GetCaretPoint().Position);
                }

                /// <summary>
                /// The undo of a block insert should undo all of the inserts
                /// </summary>
                [WpfFact]
                public void Undo()
                {
                    Create("dog", "cat", "fish");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i>the <Esc>");
                    Assert.Equal("the dog", _textBuffer.GetLine(0).GetText());
                    Assert.Equal("the cat", _textBuffer.GetLine(1).GetText());
                    _vimBuffer.Process('u');
                    Assert.Equal("dog", _textBuffer.GetLine(0).GetText());
                    Assert.Equal("cat", _textBuffer.GetLine(1).GetText());
                    Assert.Equal(0, _textView.GetCaretPoint().Position);
                }

                /// <summary>
                /// Delete actions aren't repeated
                /// </summary>
                [WpfFact]
                public void DontRepeatDelete()
                {
                    Create("dog", "cat", "fish");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i><Del><Esc>");
                    Assert.Equal("og", _textView.GetLine(0).GetText());
                    Assert.Equal("cat", _textView.GetLine(1).GetText());
                }

                /// <summary>
                /// When the block selection is in column zero then empty lines need to 
                /// get the block insert applied as well
                /// Issue 2342
                /// </summary>
                [WpfFact]
                public void EmptyLineBlockAtStartOfLine()
                {
                    Create("dog", "", "tree");
                    _vimBuffer.ProcessNotation(@"<C-q>jjI#<Esc>");
                    Assert.Equal(new[] { "#dog", "#", "#tree" }, _textBuffer.GetLines());
                }

                /// <summary>
                /// A block insertion should add the text when the insertion is at the end column
                /// of the line. 
                /// </summary>
                [WpfFact]
                public void EndOfLine()
                {
                    Create("dog", "x", "tree");
                    _vimBuffer.ProcessNotation(@"l<C-q>jjI#<Esc>");
                    Assert.Equal(new[] { "d#og", "x#", "t#ree" }, _textBuffer.GetLines());
                }

                /// <summary>
                /// On completely empty lines a block insertion should apply to
                /// all lines
                /// </summary>
                [WpfFact]
                public void AllEmptyLines()
                {
                    // Reported in issue #2675.
                    Create("", "", "", "");
                    _vimBuffer.ProcessNotation(@"<C-q>jjI#<Esc>");
                    Assert.Equal(new[] { "#", "#", "#", "" }, _textBuffer.GetLines());

                }
            }

            public sealed class SurrogatePairTest : BlockInsertTest
            {
                /// <summary>
                /// Block insert should not be splitting surrogate pairs into spaces as it does for non-surrogate
                /// pairs which comprise multiple spaces.
                /// </summary>
                [WpfFact]
                public void MiddleOfSurrogatePair()
                {
                    const string alien = "\U0001F47D"; // 👽
                    Create(
                        "dogs",
                        $"{alien}{alien}{alien}",
                        "trees");
                    _vimBuffer.ProcessNotation(@"l<C-q>jjI#<Esc>");
                    Assert.Equal(
                        new[]
                        {
                            "d#ogs",
                            $"#{alien}{alien}{alien}",
                            "t#rees"
                        },
                        _textBuffer.GetLines());
                }

                [WpfFact]
                public void MiddleOfSurrogatePairNonFirstColumn()
                {
                    const string alien = "\U0001F47D"; // 👽
                    Create(
                        "dogs",
                        $"{alien}{alien}{alien}",
                        "trees");
                    _vimBuffer.ProcessNotation(@"lll<C-q>jjI#<Esc>");
                    Assert.Equal(
                        new[]
                        {
                            "dog#s",
                            $"{alien}#{alien}{alien}",
                            "tre#es"
                        },
                        _textBuffer.GetLines());
                }

                [WpfFact]
                public void Normal()
                {
                    const string alien = "\U0001F47D"; // 👽
                    Create(
                        "doggies",
                        $"{alien}{alien}{alien}",
                        "trees");
                    _vimBuffer.ProcessNotation(@"ll<C-q>jjI#<Esc>");
                    Assert.Equal(
                        new[]
                        {
                            "do#ggies",
                            $"{alien}#{alien}{alien}",
                            "tr#ees"
                        },
                        _textBuffer.GetLines());
                }

                [WpfFact]
                public void EndOfLine()
                {
                    const string alien = "\U0001F47D"; // 👽
                    Create(
                        "dos",
                        $"{alien}",
                        "bi");
                    _vimBuffer.ProcessNotation(@"ll<C-q>jjIg<Esc>");
                    Assert.Equal(
                        new[]
                        {
                            "dogs",
                            $"{alien}g",
                            "big",
                        },
                        _textBuffer.GetLines());
                }

                [WpfFact]
                public void StartingOnSurrogatePair()
                {
                    const string alien = "\U0001F47D"; // 👽
                    Create(
                        $"{alien}{alien}{alien}",
                        "dogs",
                        "trees");
                    _vimBuffer.ProcessNotation(@"l<C-q>jjI#<Esc>");
                    Assert.Equal(
                        new[]
                        {
                            $"{alien}#{alien}{alien}",
                            "do#gs",
                            "tr#ees",
                        },
                        _textBuffer.GetLines());
                }
            }

            public sealed class InsertTabTest : BlockInsertTest
            {
                /// <summary>
                /// A block inserted tab with 'expandtab' should obey the starting column
                /// </summary>
                [WpfFact]
                public void UnalignedExpandTabs()
                {
                    // Reported in issue #2073.
                    Create(" dog", " cat", " bat", "");
                    _vimBufferData.LocalSettings.TabStop = 4;
                    _vimBufferData.LocalSettings.ExpandTab = true;
                    EnterBlock(_textView.GetBlockSpan(1, 1, 0, 3));
                    _vimBuffer.ProcessNotation("<S-i><Tab><Esc>");
                    Assert.Equal(new[] { "    dog", "    cat", "    bat", "", }, _textBuffer.GetLines());
                }

                /// <summary>
                /// A block inserted tab with 'noexpandtab' should eat preceding spaces if possible
                /// </summary>
                [WpfFact]
                public void UnalignedNoExpandTabs()
                {
                    Create(" dog", " cat", " bat", "");
                    _vimBufferData.LocalSettings.TabStop = 4;
                    _vimBufferData.LocalSettings.ExpandTab = false;
                    EnterBlock(_textView.GetBlockSpan(1, 1, 0, 3));
                    _vimBuffer.ProcessNotation("<S-i><Tab><Esc>");
                    Assert.Equal(new[] { "\tdog", "\tcat", "\tbat", "", }, _textBuffer.GetLines());
                }

                /// <summary>
                /// A block inserted tab with 'noexpandtab' should not eat preceding non-spaces
                /// </summary>
                [WpfFact]
                public void UnalignedNoExpandTabsNotSpaces()
                {
                    Create("xdog", "xcat", "xbat", "");
                    _vimBufferData.LocalSettings.TabStop = 4;
                    _vimBufferData.LocalSettings.ExpandTab = false;
                    EnterBlock(_textView.GetBlockSpan(1, 1, 0, 3));
                    _vimBuffer.ProcessNotation("<S-i><Tab><Esc>");
                    Assert.Equal(new[] { "x\tdog", "x\tcat", "x\tbat", "", }, _textBuffer.GetLines());
                }

                /// <summary>
                /// A block inserted tab should allow custom processing on each
                /// line, and in Visual Studio a tab expands to the shiftwidth
                /// </summary>
                [WpfFact]
                public void CustomProcessedTab()
                {
                    // Reported in issue #2420.
                    Create("dog", "cat", "bat", "");
                    _vimBufferData.LocalSettings.TabStop = 8;
                    _vimBufferData.LocalSettings.ShiftWidth = 4;
                    _vimBufferData.LocalSettings.ExpandTab = true;
                    EnterBlock(_textView.GetBlockSpan(0, 1, 0, 3));
                    _vimHost.TryCustomProcessFunc = (textView, insertCommand) =>
                        {
                            if (insertCommand.IsInsertTab)
                            {
                                _textBuffer.Insert(_textView.GetCaretPoint().Position, "    ");
                                return true;
                            }
                            return false;
                        };
                    _vimBuffer.ProcessNotation("<S-i><Tab><Esc>");
                    Assert.Equal(new[] { "    dog", "    cat", "    bat", "", }, _textBuffer.GetLines());
                }

                /// <summary>
                /// Without custom processing, a tab in vim expands to the
                /// tabstop, not shiftwidth
                /// </summary>
                [WpfFact]
                public void NonCustomProcessedTab()
                {
                    // Reported in issue #2420.
                    Create("dog", "cat", "bat", "");
                    _vimBufferData.LocalSettings.TabStop = 8;
                    _vimBufferData.LocalSettings.ShiftWidth = 4;
                    _vimBufferData.LocalSettings.ExpandTab = true;
                    EnterBlock(_textView.GetBlockSpan(0, 1, 0, 3));
                    _vimBuffer.ProcessNotation("<S-i><Tab><Esc>");
                    Assert.Equal(new[] { "        dog", "        cat", "        bat", "", }, _textBuffer.GetLines());
                }
            }

            public sealed class AppendEndTest : BlockInsertTest
            {
                /// <summary>
                /// Using shift-a from visual block mode appends starting at
                /// the column of the end of the primary block span
                /// </summary>
                [WpfFact]
                public void Basic()
                {
                    // Reported in issue #2667.
                    Create("hot dog", "fat cat", "big bat", "");
                    EnterBlock(_textView.GetBlockSpan(0, 4, 0, 3));
                    _vimBuffer.ProcessNotation("<S-a>x <Esc>");
                    Assert.Equal(new[] { "hot x dog", "fat x cat", "big x bat", "" }, _textBuffer.GetLines());
                }

                /// <summary>
                /// Using shift-a from visual block mode differs from shift-i
                /// with respect to short lines, specifically it pads them
                /// </summary>
                [WpfFact]
                public void ShortLine()
                {
                    Create("hot dog", "", "big bat", "");
                    EnterBlock(_textView.GetBlockSpan(0, 4, 0, 3));
                    _vimBuffer.ProcessNotation("<S-a>x <Esc>");
                    Assert.Equal(new[] { "hot x dog", "    x ", "big x bat", "" }, _textBuffer.GetLines());
                }
            }

            public sealed class AppendEndOfLineTabTest : BlockInsertTest
            {
                /// <summary>
                /// A block appended tab with 'expandtab' should obey the starting column
                /// </summary>
                [WpfFact]
                public void UnalignedExpandTabs()
                {
                    Create(" dog ", " cat  ", " bat   ", "");
                    _vimBufferData.LocalSettings.TabStop = 4;
                    _vimBufferData.LocalSettings.ExpandTab = true;
                    EnterBlock(_textView.GetBlockSpan(1, 1, 0, 3));
                    _vimBuffer.ProcessNotation("$<S-a><Tab>x<Esc>");
                    Assert.Equal(new[] { " dog    x", " cat    x", " bat    x", "", }, _textBuffer.GetLines());
                }

                /// <summary>
                /// A block appended tab with 'noexpandtab' should eat preceding spaces if possible
                /// </summary>
                [WpfFact]
                public void UnalignedNoExpandTabs()
                {
                    Create(" dog ", " cat  ", " bat   ", "");
                    _vimBufferData.LocalSettings.TabStop = 4;
                    _vimBufferData.LocalSettings.ExpandTab = false;
                    EnterBlock(_textView.GetBlockSpan(1, 1, 0, 3));
                    _vimBuffer.ProcessNotation("$<S-a><Tab>x<Esc>");
                    Assert.Equal(new[] { " dog\tx", " cat\tx", " bat\tx", "", }, _textBuffer.GetLines());
                }

                /// <summary>
                /// A block appended tab with 'noexpandtab' should not eat preceding non-spaces
                /// </summary>
                [WpfFact]
                public void UnalignedNoExpandTabsNotSpaces()
                {
                    // Reported in issue #2217.
                    Create(" dog_", " cat__", " bat___", "");
                    _vimBufferData.LocalSettings.TabStop = 4;
                    _vimBufferData.LocalSettings.ExpandTab = false;
                    EnterBlock(_textView.GetBlockSpan(1, 1, 0, 3));
                    _vimBuffer.ProcessNotation("$<S-a><Tab>x<Esc>");
                    Assert.Equal(new[] { " dog_\tx", " cat__\tx", " bat___\tx", "", }, _textBuffer.GetLines());
                }
            }

            public sealed class RepeatTest : BlockInsertTest
            {
                /// <summary>
                /// The repeat of a block insert should work against the same number of lines as the
                /// original change
                /// </summary>
                [WpfFact]
                public void SameNumberOfLines()
                {
                    Create("cat", "dog", "fish");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i>x<Esc>j.");
                    Assert.Equal(new[] { "xcat", "xxdog", "xfish" }, _textBuffer.GetLines());
                }

                /// <summary>
                /// If the repeat goes off the end of the ITextBuffer then the change should just be
                /// applied to the lines from the caret to the end
                /// </summary>
                [WpfFact]
                public void PasteEndOfBuffer()
                {
                    Create("cat", "dog", "fish");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i>x<Esc>jj.");
                    Assert.Equal(new[] { "xcat", "xdog", "xfish" }, _textBuffer.GetLines());
                }

                /// <summary>
                /// Spaces don't matter in the repeat.  The code should just treat them as normal characters and
                /// repeat the edits into them
                /// </summary>
                [WpfFact]
                public void DontConsiderSpaces()
                {
                    Create("cat", "dog", " fish");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i>x<Esc>jj.");
                    Assert.Equal(new[] { "xcat", "xdog", "x fish" }, _textBuffer.GetLines());
                }

                /// <summary>
                /// Make sure that we handle deletes properly.  So long as it leaves us with a new bit of text then
                /// we can repeat it
                /// </summary>
                [WpfFact]
                public void HandleDeletes()
                {
                    Create("cat", "dog", "fish", "store");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i>xy<BS><Esc>jj.");
                    Assert.Equal(new[] { "xcat", "xdog", "xfish", "xstore" }, _textBuffer.GetLines());
                }

                /// <summary>
                /// Make sure the code properly handles the case where the insert results in 0 text being added
                /// to the file.  This should cause us to not do anything even on repeat
                /// </summary>
                [WpfFact]
                public void HandleEmptyInsertString()
                {
                    Create("cat", "dog", "fish", "store");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i>xy<BS><BS><Esc>jj.");
                    Assert.Equal(new[] { "cat", "dog", "fish", "store" }, _textBuffer.GetLines());
                }

                [WpfFact]
                public void Issue1136()
                {
                    Create("cat", "dog");
                    _vimBuffer.ProcessNotation("<C-q>j<S-i>x<Esc>.");
                    Assert.Equal(new[] { "xxcat", "xxdog" }, _textBuffer.GetLines());
                }
            }
        }

        public sealed class BlockChange : VisualModeIntegrationTest
        {
            /// <summary>
            /// The block insert should add the text to every column
            /// </summary>
            [WpfFact]
            public void Simple()
            {
                Create("dog", "cat", "fish");
                _vimBuffer.ProcessNotation("<C-q>jcthe <Esc>");
                Assert.Equal("the og", _textBuffer.GetLine(0).GetText());
                Assert.Equal("the at", _textBuffer.GetLine(1).GetText());
            }

            /// <summary>
            /// Make sure an undo of a block edit goes back to the original text and replaces
            /// the cursor at the start of the block
            /// </summary>
            [WpfFact]
            public void Undo()
            {
                Create("dog", "cat", "fish");
                _vimBuffer.ProcessNotation("<C-q>jcthe <Esc>u");
                Assert.Equal(
                    new[] { "dog", "cat", "fish" },
                    _textBuffer.GetLines());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void RenameFunction()
            {
                Create("foo()", "foo()");
                _vimBuffer.ProcessNotation("<C-q>jllcbar<Esc>");
                Assert.Equal(
                    new[] { "bar()", "bar()" },
                    _textBuffer.GetLines());
            }
        }

        public sealed class BlockAdd : VisualModeIntegrationTest
        {
            /// <summary>
            /// The block add should only add to numbers within the selection
            /// </summary>
            [WpfTheory]
            [MemberData(nameof(SelectionOptions))]
            public void Simple(string selection)
            {
                // Reported in issue #2501.
                Create(
                    " 1  This is the first thing. Here's a number: 99.",
                    "",
                    " 2 This is the second thing.",
                    "   Here is another number 123.",
                    "",
                    " 3 This is the last thing.",
                    ""
                );
                _globalSettings.Selection = selection;
                _vimBuffer.ProcessNotation("<C-q>5j2l<C-a>");
                var expected = new[] {
                    " 2  This is the first thing. Here's a number: 99.",
                    "",
                    " 3 This is the second thing.",
                    "   Here is another number 123.",
                    "",
                    " 4 This is the last thing.",
                    ""
                };
                Assert.Equal(expected, _textBuffer.GetLines());
            }
        }

        public sealed class Move : VisualModeIntegrationTest
        {
            [WpfFact]
            public void HomeToStartOfLine()
            {
                Create("cat dog");
                _textView.MoveCaretTo(2);
                _vimBuffer.ProcessNotation("v<Home>");
                Assert.Equal("cat", _textView.GetSelectionSpan().GetText());
                Assert.Equal(0, _textView.GetCaretPoint());
            }

            [WpfFact]
            public void HomeToStartOfLineViaKeypad()
            {
                Create("cat dog");
                _textView.MoveCaretTo(2);
                _vimBuffer.ProcessNotation("v<kHome>");
                Assert.Equal("cat", _textView.GetSelectionSpan().GetText());
                Assert.Equal(0, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Jump to a mark and make sure that the selection correctly updates
            /// </summary>
            [WpfFact]
            public void JumpMarkLine_Character()
            {
                Create("cat", "dog");
                _textView.MoveCaretTo(1);
                _vimBuffer.MarkMap.SetLocalMark('b', _vimBufferData, 1, 1);
                _vimBuffer.Process("v'b");
                Assert.Equal("at\r\nd", _textView.GetSelectionSpan().GetText());
            }

            /// <summary>
            /// Jump to a mark and make sure that the selection correctly updates
            /// </summary>
            [WpfFact]
            public void JumpMark_Character()
            {
                Create("cat", "dog");
                _textView.MoveCaretTo(1);
                _vimBuffer.MarkMap.SetLocalMark('b', _vimBufferData, 1, 1);
                _vimBuffer.Process("v`b");
                Assert.Equal("at\r\ndo", _textView.GetSelectionSpan().GetText());
            }
        }

        public abstract class ReplaceSelectionTest : VisualModeIntegrationTest
        {
            public sealed class CharacterWiseTest : ReplaceSelectionTest
            {
                [WpfFact]
                public void Simple()
                {
                    Create("cat dog", "tree fish");
                    _vimBuffer.ProcessNotation("vllra");
                    Assert.Equal(new[] { "aaa dog", "tree fish" }, _textBuffer.GetLines());
                }

                [WpfFact]
                public void ExtendIntoNewLine()
                {
                    Create("cat", "dog");
                    _vimBuffer.GlobalSettings.VirtualEdit = "onemore";
                    _vimBuffer.ProcessNotation("vlllllra");
                    Assert.Equal(new[] { "aaa", "dog" }, _textBuffer.GetLines());
                }

                [WpfFact]
                public void MultiLine()
                {
                    Create("cat", "dog");
                    _vimBuffer.ProcessNotation("vjra");
                    Assert.Equal(new[] { "aaa", "aog" }, _textBuffer.GetLines());
                }

                [WpfFact]
                public void NonAscii()
                {
                    // Reported in issue #2702.
                    Create("abc", "");
                    _vimBuffer.Process("v");
                    Assert.False(_vimBuffer.CanProcess(KeyInputUtil.CharToKeyInput('©')));
                    _vimBuffer.Process("r");
                    Assert.True(_vimBuffer.CanProcess(KeyInputUtil.CharToKeyInput('©')));
                    _vimBuffer.Process("©");
                    Assert.Equal(new[] { "©bc", "" }, _textBuffer.GetLines());
                }
            }

            public sealed class LineWiseTest : ReplaceSelectionTest
            {
                [WpfFact]
                public void Single()
                {
                    Create("cat", "dog");
                    _vimBuffer.ProcessNotation("Vra");
                    Assert.Equal(new[] { "aaa", "dog" }, _textBuffer.GetLines());
                }

                [WpfFact]
                public void Issue1201()
                {
                    Create("one two three", "four five six");
                    _vimBuffer.ProcessNotation("Vr-");
                    Assert.Equal("-------------", _textBuffer.GetLine(0).GetText());
                }
            }

            public sealed class BlockWiseTest : ReplaceSelectionTest
            {
                /// <summary>
                /// This is an anti test.
                ///
                /// The WPF editor has no way to position the caret in the middle of a
                /// tab.  It can't for instance place it on the 2 space of the 4 spaces
                /// the caret occupies.  Hence this test have a deviating behavior from
                /// gVim because the caret position differs on the final 'l'
                /// </summary>
                [WpfFact]
                public void Overlap()
                {
                    Create("cat", "d\tog");
                    _vimBuffer.LocalSettings.TabStop = 4;
                    _vimBuffer.ProcessNotation("ll<C-q>jlra");
                    Assert.Equal(new[] { "caa", "d aaag" }, _textBuffer.GetLines());
                }
            }
        }

        public sealed class Insert : VisualModeIntegrationTest
        {
            /// <summary>
            /// When switching to insert mode the caret should move to the start of the line
            /// </summary>
            [WpfFact]
            public void MiddleOfLine()
            {
                Create("cat", "dog");
                _vimBuffer.Process("vllI");
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.Equal(ModeKind.Insert, _vimBuffer.ModeKind);
            }
        }

        public sealed class ParagraphTest : VisualModeIntegrationTest
        {
            /// <summary>
            /// Inner paragraph is always linewise
            /// </summary>
            [WpfFact]
            public void InnerLinewise()
            {
                // Reported in issue #2006.
                Create("ram", "goat", "", "cat", "dog", "", "bat", "bear");
                _textView.MoveCaretToLine(3);
                _vimBuffer.Process("vip");
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                _vimBuffer.Process("y");
                Assert.True(UnnamedRegister.OperationKind.IsLineWise);
            }

            /// <summary>
            /// Outer paragraph is always linewise
            /// </summary>
            [WpfFact]
            public void OuterLinewise()
            {
                Create("ram", "goat", "", "cat", "dog", "", "bat", "bear");
                _textView.MoveCaretToLine(3);
                _vimBuffer.Process("vip");
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                _vimBuffer.Process("y");
                Assert.True(UnnamedRegister.OperationKind.IsLineWise);
            }
        }

        public sealed class SelectionTest : VisualModeIntegrationTest
        {
            /// <summary>
            /// In Visual Mode it is possible to move the caret past the end of the line even if
            /// 'virtualedit='.
            /// </summary>
            [WpfFact]
            public void MoveToEndOfLineCharacter()
            {
                Create("cat", "dog");
                _vimBuffer.Process("vlll");
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void MoveToEndOfLineLine()
            {
                Create("cat", "dog");
                _vimBuffer.Process("Vlll");
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void Issue1790()
            {
                Create(" the");
                _vimBuffer.Process("vas");
                Assert.Equal(_textBuffer.GetSpan(start: 0, length: 4), _textView.GetSelectionSpan());
            }
        }

        public abstract class TagBlockTest : VisualModeIntegrationTest
        {
            public sealed class CharacterWiseTest : TagBlockTest
            {
                [WpfFact]
                public void InnerSimpleMultiLine()
                {
                    Create("<a>", "blah", "</a>");
                    _textView.MoveCaretToLine(1);
                    _vimBuffer.Process("vity");
                    Assert.Equal(Environment.NewLine + "blah" + Environment.NewLine, UnnamedRegister.StringValue);
                }

                /// <summary>
                /// Visual selection of a block from an empty line should not expand
                /// </summary>
                [WpfFact]
                public void InnerSimpleMultiLineFromEmptyLine()
                {
                    // Reported in issue #2081.
                    Create("<a>", "", "blah", "</a>");
                    _textView.MoveCaretToLine(1);
                    _vimBuffer.Process("vity");
                    Assert.Equal(Environment.NewLine + Environment.NewLine + "blah" + Environment.NewLine, UnnamedRegister.StringValue);
                }

                [WpfFact]
                public void InnerSimpleSingleLine()
                {
                    Create("<a>blah</a>");
                    _textView.MoveCaretTo(4);
                    _vimBuffer.Process("vit");

                    var span = new Span(_textBuffer.GetPointInLine(0, 3), 4);
                    Assert.Equal(span, _textView.GetSelectionSpan());
                }

                [WpfFact]
                public void AllSimpleSingleLine()
                {
                    Create("<a>blah</a>");
                    _textView.MoveCaretTo(4);
                    _vimBuffer.Process("vat");

                    var span = new Span(_textBuffer.GetPoint(0), _textBuffer.CurrentSnapshot.Length);
                    Assert.Equal(span, _textView.GetSelectionSpan());
                }

                [WpfTheory]
                [InlineData("inclusive")]
                [InlineData("exclusive")]
                public void SelectPartialInnerExpandAll(string selectionSetting)
                {
                    Create("<a><b><c/></b></a>");
                    _globalSettings.Selection = selectionSetting;

                    _textView.MoveCaretTo("<a><b><c".Length);
                    _vimBuffer.Process("vlit");
                    Assert.Equal("<c/>", _textView.GetSelectionSpan().GetText());
                }

                [WpfFact]
                public void SelectFullInnerExpandAll()
                {
                    Create("<parent>", "<child>", "<grandchild />", "</child>", "</parent>");

                    var initialPosition = _textBuffer.GetLineFromLineNumber(2).Start;
                    _textView.MoveCaretTo(initialPosition);
                    _vimBuffer.Process("vitat");
                    Assert.Equal(_textBuffer.GetLineRange(1, 3).Extent, _textView.GetSelectionSpan());
                }

                [WpfFact]
                public void SelectMultiLineTag()
                {
                    Create("<parent>", "<child", "attr1=\"value1\"", "attr2", "=", "\"value2\"", ">", "content", "</child>", "</parent>");

                    var initialPosition = _textBuffer.GetLineFromLineNumber(7).Start;
                    _textView.MoveCaretTo(initialPosition);
                    _vimBuffer.Process("vat");
                    Assert.Equal(_textBuffer.GetLineRange(1, 8).Extent, _textView.GetSelectionSpan());
                }

                [WpfTheory]
                [InlineData("inclusive", 1)]
                [InlineData("exclusive", 0)]
                public void EmptyTag_SelectInnerExpandInner(string selectionSetting, int selectionLength)
                {
                    Create("<parent>", "<child>", "<grandchild></grandchild>", "</child>", "</parent>");
                    _globalSettings.Selection = selectionSetting;

                    var initialPosition = _textBuffer.GetLineSpan(2, "<grandchild>".Length - 1, 0).Start;
                    _textView.MoveCaretTo(initialPosition);
                    _vimBuffer.Process("vit");
                    Assert.Equal(_textBuffer.GetLineSpan(2, "<grandchild>".Length, selectionLength), _textView.GetSelectionSpan());

                    _vimBuffer.Process("it");
                    Assert.Equal(_textBuffer.GetLine(2).Extent, _textView.GetSelectionSpan());
                }
            }

            public sealed class ExpandSelectionTest : TagBlockTest
            {
                [WpfFact]
                public void InnerSimple()
                {
                    var text = "<a>blah</a>";
                    Create(text);
                    _textView.MoveCaretTo(5);
                    _vimBuffer.Process("vit");
                    Assert.Equal("blah", _textView.GetSelectionSpan().GetText());
                    _vimBuffer.Process("it");
                    Assert.Equal(text, _textView.GetSelectionSpan().GetText());
                }

                [WpfFact]
                public void InnerNestedNoPadding()
                {
                    var text = "<a><b>blah</b></a>";
                    Create(text);
                    _textView.MoveCaretTo(7);
                    _vimBuffer.Process("vit");
                    Assert.Equal("blah", _textView.GetSelectionSpan().GetText());
                    _vimBuffer.Process("it");
                    Assert.Equal("<b>blah</b>", _textView.GetSelectionSpan().GetText());
                    _vimBuffer.Process("it");
                    Assert.Equal(text, _textView.GetSelectionSpan().GetText());
                }

                [WpfFact]
                public void InnerNestedPadding()
                {
                    var text = "<a>  <b>blah</b></a>";
                    Create(text);
                    _textView.MoveCaretTo(7);
                    _vimBuffer.Process("vit");
                    Assert.Equal("blah", _textView.GetSelectionSpan().GetText());
                    _vimBuffer.Process("it");
                    Assert.Equal("<b>blah</b>", _textView.GetSelectionSpan().GetText());
                    _vimBuffer.Process("it");
                    Assert.Equal("  <b>blah</b>", _textView.GetSelectionSpan().GetText());
                    _vimBuffer.Process("it");
                    Assert.Equal(text, _textView.GetSelectionSpan().GetText());
                }

                [WpfFact]
                public void AllNested()
                {
                    var text = "<a><b>blah</b></a>";
                    Create(text);
                    _textView.MoveCaretTo(7);
                    _vimBuffer.Process("vat");
                    Assert.Equal("<b>blah</b>", _textView.GetSelectionSpan().GetText());
                    _vimBuffer.Process("at");
                    Assert.Equal(text, _textView.GetSelectionSpan().GetText());
                }
            }
        }

        public abstract class InvertSelectionTest : VisualModeIntegrationTest
        {
            public sealed class CharacterWiseTest : InvertSelectionTest
            {
                [WpfFact]
                public void Simple()
                {
                    Create("cat and the dog");
                    _vimBuffer.Process("vlllo");
                    Assert.Equal(0, _textView.GetCaretPoint().Position);
                    Assert.Equal(4, _textView.Selection.AnchorPoint.Position);
                    Assert.Equal("cat ", _textView.GetSelectionSpan().GetText());
                }

                [WpfFact]
                public void SingleCharacterSelected()
                {
                    Create("cat");
                    _vimBuffer.Process("voooo");
                    Assert.Equal(0, _textView.GetCaretPoint().Position);
                    Assert.Equal(0, _textView.Selection.AnchorPoint.Position);
                    Assert.Equal("c", _textView.GetSelectionSpan().GetText());
                }

                [WpfFact]
                public void BackAndForth()
                {
                    Create("cat and the dog");
                    _vimBuffer.Process("vllloo");
                    Assert.Equal(3, _textView.GetCaretPoint().Position);
                    Assert.Equal(0, _textView.Selection.AnchorPoint.Position);
                    Assert.Equal("cat ", _textView.GetSelectionSpan().GetText());
                }

                [WpfFact]
                public void Multiline()
                {
                    Create("cat", "dog");
                    _vimBuffer.Process("lvjo");
                    var span = _textView.GetSelectionSpan();
                    Assert.Equal("at" + Environment.NewLine + "do", span.GetText());
                    Assert.True(_textView.Selection.IsReversed);
                    Assert.Equal(1, _textView.GetCaretPoint().Position);
                }

                [WpfFact]
                public void PastEndOfLine()
                {
                    Create("cat", "dog");
                    _vimBuffer.GlobalSettings.VirtualEdit = "onemore";
                    _vimBuffer.Process("vlllo");
                    Assert.Equal(4, _textView.Selection.StreamSelectionSpan.Length);
                    Assert.Equal(0, _textView.GetCaretPoint().Position);
                }

                [WpfFact]
                public void PastEndOfLineReverse()
                {
                    Create("cat", "dog");
                    _vimBuffer.GlobalSettings.VirtualEdit = "onemore";
                    _vimBuffer.Process("vllloo");
                    Assert.Equal(4, _textView.Selection.StreamSelectionSpan.Length);
                    Assert.Equal(3, _textView.GetCaretPoint().Position);
                }
            }

            public sealed class LineWiseTest : InvertSelectionTest
            {
                [WpfFact]
                public void Simple()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("Vjo");
                    Assert.Equal(0, _textView.GetCaretPoint());
                    var span = _textView.GetSelectionSpan();
                    Assert.Equal("cat" + Environment.NewLine + "dog" + Environment.NewLine, span.GetText());
                }

                [WpfFact]
                public void BackAndForth()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("Vjoo");
                    Assert.Equal(_textBuffer.GetLine(1).Start, _textView.GetCaretPoint());
                    var span = _textView.GetSelectionSpan();
                    Assert.Equal("cat" + Environment.NewLine + "dog" + Environment.NewLine, span.GetText());
                }

                [WpfFact]
                public void SimpleNonZeroStart()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("lVjo");
                    Assert.Equal(1, _textView.GetCaretPoint());
                    var span = _textView.GetSelectionSpan();
                    Assert.Equal("cat" + Environment.NewLine + "dog" + Environment.NewLine, span.GetText());
                }

                [WpfFact]
                public void StartOnEmptyLine()
                {
                    Create("cat", "", "dog", "tree");
                    _textView.MoveCaretTo(_textBuffer.GetLine(1).Start);
                    _vimBuffer.ProcessNotation("Vjo");
                    Assert.Equal(_textBuffer.GetLine(1).Start, _textView.GetCaretPoint());
                    var span = _textView.GetSelectionSpan();
                    Assert.Equal(Environment.NewLine + "dog" + Environment.NewLine, span.GetText());
                }
            }

            public sealed class BlockTest : InvertSelectionTest
            {
                [WpfFact]
                public void Simple()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("<C-q>ljo");
                    Assert.Equal(0, _textView.GetCaretPoint().Position);
                    var blockSpan = _vimBuffer.GetSelectionBlockSpan();
                    Assert.Equal(2, blockSpan.Height);
                    Assert.Equal(2, blockSpan.SpacesLength);
                }

                [WpfFact]
                public void SimpleBackAndForth()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("<C-q>ljoo");
                    Assert.Equal(_textView.GetPointInLine(1, 1), _textView.GetCaretPoint().Position);
                    var blockSpan = _vimBuffer.GetSelectionBlockSpan();
                    Assert.Equal(2, blockSpan.Height);
                    Assert.Equal(2, blockSpan.SpacesLength);
                }
            }

            public sealed class BlockColumnOnlyTest : InvertSelectionTest
            {
                [WpfFact]
                public void Simple()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("<C-q>ljO");
                    Assert.Equal(_textView.GetPointInLine(1, 0), _textView.GetCaretPoint().Position);
                    var blockSpan = _vimBuffer.GetSelectionBlockSpan();
                    Assert.Equal(2, blockSpan.Height);
                    Assert.Equal(2, blockSpan.SpacesLength);
                }

                [WpfFact]
                public void SimpleBackAndForth()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("<C-q>ljOO");
                    Assert.Equal(_textView.GetPointInLine(1, 1), _textView.GetCaretPoint().Position);
                    var blockSpan = _vimBuffer.GetSelectionBlockSpan();
                    Assert.Equal(2, blockSpan.Height);
                    Assert.Equal(2, blockSpan.SpacesLength);
                }

                [WpfFact]
                public void SimpleReverse()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("<C-q>ljoO");
                    Assert.Equal(_textView.GetPointInLine(0, 1), _textView.GetCaretPoint().Position);
                    var blockSpan = _vimBuffer.GetSelectionBlockSpan();
                    Assert.Equal(2, blockSpan.Height);
                    Assert.Equal(2, blockSpan.SpacesLength);
                }

                [WpfFact]
                public void SimpleReverseAndForth()
                {
                    Create("cat", "dog", "tree");
                    _vimBuffer.ProcessNotation("<C-q>ljoOO");
                    Assert.Equal(_textView.GetPointInLine(0, 0), _textView.GetCaretPoint().Position);
                    var blockSpan = _vimBuffer.GetSelectionBlockSpan();
                    Assert.Equal(2, blockSpan.Height);
                    Assert.Equal(2, blockSpan.SpacesLength);
                }
            }
        }

        public sealed class KeyMappingTest : VisualModeIntegrationTest
        {
            [WpfFact]
            public void VisualAfterCount()
            {
                Create("cat dog");
                _vimBuffer.Process(":vmap <space> l", enter: true);
                _vimBuffer.ProcessNotation("v2<Space>");
                Assert.Equal(2, _textView.GetCaretPoint().Position);
                Assert.Equal("cat", _textView.GetSelectionSpan().GetText());
            }

            [WpfFact]
            public void Issue890()
            {
                Create("cat > dog");
                _vimBuffer.ProcessNotation(@":vmap > >gv", enter: true);
                _vimBuffer.ProcessNotation(@"vf>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.Equal(4, _textView.GetCaretPoint().Position);
            }
        }

        public sealed class CanProcessTest : VisualModeIntegrationTest
        {
            [WpfFact]
            public void Simple()
            {
                Create("");
                Assert.True(_vimBuffer.CanProcess('l'));
                Assert.True(_vimBuffer.CanProcess('k'));
            }
        }

        public sealed class ChangeCase : VisualModeIntegrationTest
        {
            [WpfFact]
            public void Upper_Character()
            {
                Create("cat dog");
                _vimBuffer.ProcessNotation("vllU");
                Assert.Equal("CAT dog", _textBuffer.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretColumn().ColumnNumber);
            }

            [WpfFact]
            public void Lower_Character()
            {
                Create("CAT dog");
                _vimBuffer.ProcessNotation("vllu");
                Assert.Equal("cat dog", _textBuffer.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretColumn().ColumnNumber);
            }

            [WpfFact]
            public void Rot13_Character()
            {
                Create("cat dog");
                _vimBuffer.ProcessNotation("vllg?");
                Assert.Equal("png dog", _textBuffer.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretColumn().ColumnNumber);
            }
        }

        public sealed class FormatTextLines : VisualModeIntegrationTest
        {
            [WpfFact]
            public void PlainText()
            {
                Create("cat", "dog", "bear", "bat", "");
                _vimBuffer.LocalSettings.TextWidth = 10;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "cat dog", "bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void InclusiveCharacterSelection()
            {
                Create("cat", "dog", "bear", "bat", "");
                _globalSettings.Selection = "inclusive";
                _vimBuffer.LocalSettings.TextWidth = 10;
                _vimBuffer.ProcessNotation("v3jgq");
                Assert.Equal(new[] { "cat dog", "bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void ExclusiveCharacterSelection()
            {
                Create("cat", "dog", "bear", "bat", "");
                _globalSettings.Selection = "exclusive";
                _vimBuffer.LocalSettings.TextWidth = 10;
                _vimBuffer.ProcessNotation("v3jgq");
                Assert.Equal(new[] { "cat dog", "bear", "bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void BlockSelection()
            {
                Create("cat", "dog", "bear", "bat", "");
                _vimBuffer.LocalSettings.TextWidth = 10;
                _vimBuffer.ProcessNotation("<C-v>3jgq");
                Assert.Equal(new[] { "cat dog", "bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void LongLine()
            {
                Create("cat dog bear bat", "");
                _vimBuffer.LocalSettings.TextWidth = 10;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "cat dog", "bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void PreserveSpacing()
            {
                Create("cat  dog bear bat", "");
                _vimBuffer.LocalSettings.TextWidth = 10;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "cat  dog", "bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void TinyWidth()
            {
                Create("cat dog bear bat", "");
                _vimBuffer.LocalSettings.TextWidth = 1;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "cat", "dog", "bear", "bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void NoAutoIndent()
            {
                Create("  cat", "dog", "bear", "bat", "");
                _vimBuffer.LocalSettings.TextWidth = 10;
                _vimBuffer.LocalSettings.AutoIndent = false;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "  cat dog", "bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void AutoIndent()
            {
                Create("  cat", "dog", "bear", "bat", "");
                _vimBuffer.LocalSettings.TextWidth = 10;
                _vimBuffer.LocalSettings.AutoIndent = true;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "  cat dog", "  bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfTheory]
            [InlineData(false)]
            [InlineData(true)]
            public void SlashSlash(bool autoIndent)
            {
                Create("  // cat", "// dog", "// bear", "// bat", "");
                _vimBuffer.LocalSettings.TextWidth = 15;
                _vimBuffer.LocalSettings.AutoIndent = autoIndent;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "  // cat dog", "  // bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void TripleSlash()
            {
                Create("  /// cat", "/// dog", "/// bear", "/// bat", "");
                _vimBuffer.LocalSettings.TextWidth = 15;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "  /// cat dog", "  /// bear bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void BlankParagraph()
            {
                Create("  // cat", "//", "// dog", "// bear", "// bat", "");
                _vimBuffer.LocalSettings.TextWidth = 15;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "  // cat", "  //", "  // dog bear", "  // bat", "" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void WhitespaceParagraph()
            {
                Create("  // cat", "// ", "// dog", "// bear", "// bat", "");
                _vimBuffer.LocalSettings.TextWidth = 15;
                _vimBuffer.ProcessNotation("VGgq");
                Assert.Equal(new[] { "  // cat", "  //", "  // dog bear", "  // bat", "" }, _textBuffer.GetLines());
            }
        }

        public sealed class MiscAllTest : VisualModeIntegrationTest
        {
            /// <summary>
            /// When changing a line wise selection one blank line should be left remaining in the ITextBuffer
            /// </summary>
            [WpfTheory]
            [MemberData(nameof(VirtualEditOptions))]
            public void Change_LineWise(string virtualEdit)
            {
                Create("cat", "  dog", "  bear", "tree");
                _globalSettings.VirtualEdit = virtualEdit;
                SwitchEnterMode(ModeKind.VisualLine, _textView.GetLineRange(1, 2).ExtentIncludingLineBreak);
                _vimBuffer.LocalSettings.AutoIndent = true;
                _vimBuffer.Process("c");
                Assert.Equal("cat", _textView.GetLine(0).GetText());
                Assert.Equal("", _textView.GetLine(1).GetText());
                Assert.Equal("tree", _textView.GetLine(2).GetText());
                Assert.Equal(2, _textView.Caret.Position.VirtualBufferPosition.VirtualSpaces);
                Assert.Equal(_textView.GetLine(1).Start, _textView.GetCaretPoint());
                Assert.Equal(ModeKind.Insert, _vimBuffer.ModeKind);
            }

            /// <summary>
            /// When changing a word we just delete it all and put the caret at the start of the deleted
            /// selection
            /// </summary>
            [WpfFact]
            public void Change_Word()
            {
                Create("cat chases the ball");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 0, 4));
                _vimBuffer.LocalSettings.AutoIndent = true;
                _vimBuffer.Process("c");
                Assert.Equal("chases the ball", _textView.GetLine(0).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.Equal(ModeKind.Insert, _vimBuffer.ModeKind);
            }

            /// <summary>
            /// Make sure we handle the virtual spaces properly here.  The 'C' command should leave the caret
            /// in virtual space due to the previous indent and escape should cause the caret to jump back to
            /// real spaces when leaving insert mode
            /// </summary>
            [WpfFact]
            public void ChangeLineSelection_VirtualSpaceHandling()
            {
                Create("  cat", "dog");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 2, 2));
                _vimBuffer.Process('C');
                _vimBuffer.Process(VimKey.Escape);
                Assert.Equal("", _textView.GetLine(0).GetText());
                Assert.Equal("dog", _textView.GetLine(1).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.False(_textView.GetCaretVirtualPoint().IsInVirtualSpace);
            }

            /// <summary>
            /// Verify that Shift-V enters Visual Line Mode
            /// </summary>
            [WpfFact]
            public void EnterVisualLine()
            {
                Create("hello", "world");
                _vimBuffer.Process(KeyNotationUtil.StringToKeyInput("<S-v>"));
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
            }

            [WpfFact]
            public void JoinSelection_KeepSpaces_Simple()
            {
                Create("cat", "dog", "tree");
                _vimBuffer.Process("VjJ");
                Assert.Equal(new[] { "cat dog", "tree" }, _textBuffer.GetLines());
            }

            [WpfFact]
            public void JoinSelection_RemoveSpaces_Simple()
            {
                Create("cat", "dog", "tree");
                _vimBuffer.Process("VjgJ");
                Assert.Equal(new[] { "catdog", "tree" }, _textBuffer.GetLines());
            }

            [WpfTheory]
            [MemberData(nameof(VirtualEditOptions))]
            public void Repeat1(string virtualEdit)
            {
                Create("dog again", "cat again", "chicken");
                _globalSettings.VirtualEdit = virtualEdit;
                SwitchEnterMode(ModeKind.VisualLine, _textView.GetLineRange(0, 1).ExtentIncludingLineBreak);
                _vimBuffer.LocalSettings.ShiftWidth = 2;
                _vimBuffer.Process(">.");
                Assert.Equal("    dog again", _textView.GetLine(0).GetText());
            }

            [WpfTheory]
            [MemberData(nameof(VirtualEditOptions))]
            public void Repeat2(string virtualEdit)
            {
                Create("dog again", "cat again", "chicken");
                _globalSettings.VirtualEdit = virtualEdit;
                SwitchEnterMode(ModeKind.VisualLine, _textView.GetLineRange(0, 1).ExtentIncludingLineBreak);
                _vimBuffer.LocalSettings.ShiftWidth = 2;
                _vimBuffer.Process(">..");
                Assert.Equal("      dog again", _textView.GetLine(0).GetText());
            }

            [WpfFact]
            public void ResetCaretFromShiftLeft1()
            {
                Create("  hello", "  world");
                EnterMode(_textView.GetLineRange(0, 1).Extent);
                _vimBuffer.Process("<");
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void ResetCaretFromShiftLeft2()
            {
                Create("  hello", "  world");
                EnterMode(_textView.GetLineRange(0, 1).Extent);
                _vimBuffer.Process("<");
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void ResetCaretFromYank1()
            {
                Create("  hello", "  world");
                EnterMode(_textView.TextBuffer.GetSpan(0, 2));
                _vimBuffer.Process("y");
                Assert.Equal(0, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Moving the caret which resets the selection should go to normal mode
            /// </summary>
            [WpfFact]
            public void SelectionChange1()
            {
                Create("  hello", "  world");
                EnterMode(_textView.TextBuffer.GetSpan(0, 2));
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                _textView.Selection.Select(
                    new SnapshotSpan(_textView.GetLine(1).Start, 0),
                    false);
                DoEvents();
                Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
            }

            /// <summary>
            /// Moving the caret which resets the selection should go visual if there is still a selection
            /// </summary>
            [WpfFact]
            public void SelectionChange2()
            {
                Create("  hello", "  world");
                EnterMode(_textView.TextBuffer.GetSpan(0, 2));
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                _textView.Selection.Select(
                    new SnapshotSpan(_textView.GetLine(1).Start, 1),
                    false);
                DoEvents();
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
            }

            /// <summary>
            /// Make sure we reset the span we need
            /// </summary>
            [WpfFact]
            public void SelectionChange3()
            {
                Create("  hello", "  world");
                EnterMode(_textView.GetLine(0).Extent);
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                _textView.Selection.Select(_textView.GetLine(1).Extent, false);
                _vimBuffer.Process(KeyInputUtil.CharToKeyInput('y'));
                DoEvents();
                Assert.Equal("  world", _vimBuffer.RegisterMap.GetRegister(RegisterName.Unnamed).StringValue);
            }

            /// <summary>
            /// Make sure we reset the span we need
            /// </summary>
            [WpfFact]
            public void SelectionChange4()
            {
                Create("  hello", "  world");
                EnterMode(_textView.GetLine(0).Extent);
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                _textView.SelectAndMoveCaret(new SnapshotSpan(_textView.GetLine(1).Start, 3));
                DoEvents();
                _vimBuffer.Process("ly");
                Assert.Equal("  wo", _vimBuffer.RegisterMap.GetRegister(RegisterName.Unnamed).StringValue);
            }

            /// <summary>
            /// Make sure that LastVisualSelection is set to the SnapshotSpan before the shift right
            /// command is executed
            /// </summary>
            [WpfFact]
            public void ShiftLinesRight_LastVisualSelection()
            {
                Create("cat", "dog", "fish");
                EnterMode(ModeKind.VisualCharacter, new SnapshotSpan(_textView.GetLine(0).Start, _textView.GetLine(1).Start.Add(1)));
                _vimBuffer.Process('>');
                var visualSelection = VisualSelection.NewCharacter(
                    new CharacterSpan(_textView.GetLine(0).Start, 2, 1),
                    SearchPath.Forward);
                Assert.True(_vimTextBuffer.LastVisualSelection.IsSome());
                Assert.Equal(visualSelection, _vimTextBuffer.LastVisualSelection.Value);
            }

            /// <summary>
            /// Even though a text span is selected, substitute should operate on the line
            /// </summary>
            [WpfFact]
            public void Substitute1()
            {
                Create("the boy hit the cat", "bat");
                EnterMode(new SnapshotSpan(_textView.TextSnapshot, 0, 2));
                _vimBuffer.Process(":s/a/o", enter: true);
                Assert.Equal("the boy hit the cot", _textView.GetLine(0).GetText());
                Assert.Equal("bat", _textView.GetLine(1).GetText());
            }

            /// <summary>
            /// Muliline selection should cause a replace per line
            /// </summary>
            [WpfFact]
            public void Substitute2()
            {
                Create("the boy hit the cat", "bat");
                EnterMode(_textView.GetLineRange(0, 1).ExtentIncludingLineBreak);
                _vimBuffer.Process(":s/a/o", enter: true);
                Assert.Equal("the boy hit the cot", _textView.GetLine(0).GetText());
                Assert.Equal("bot", _textView.GetLine(1).GetText());
            }

            /// <summary>
            /// Switching to command mode shouldn't clear the selection
            /// </summary>
            [WpfFact]
            public void Switch_ToCommandShouldNotClearSelection()
            {
                Create("cat", "dog", "tree");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0, 1).ExtentIncludingLineBreak);
                _vimBuffer.Process(":");
                Assert.False(_textView.GetSelectionSpan().IsEmpty);
            }

            /// <summary>
            /// Switching to normal mode should clear the selection
            /// </summary>
            [WpfFact]
            public void Switch_ToNormalShouldClearSelection()
            {
                Create("cat", "dog", "tree");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0, 1).ExtentIncludingLineBreak);
                _vimBuffer.Process(VimKey.Escape);
                Assert.True(_textView.GetSelectionSpan().IsEmpty);
            }

            [WpfFact]
            public void Handle_D_BlockMode()
            {
                Create("dog", "cat", "tree");
                EnterBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                _vimBuffer.Process("D");
                Assert.Equal("d", _textView.GetLine(0).GetText());
                Assert.Equal("c", _textView.GetLine(1).GetText());
            }

            [WpfTheory]
            [MemberData(nameof(VirtualEditOptions))]
            public async Task IncrementalSearch_LineModeShouldSelectFullLine(string virtualEdit)
            {
                Create("dog", "cat", "tree");
                _globalSettings.VirtualEdit = virtualEdit;
                SwitchEnterMode(ModeKind.VisualLine, _textView.GetLineRange(0, 1).ExtentIncludingLineBreak);
                _vimBuffer.Process("/c");
                await _vimBuffer.GetSearchCompleteAsync();
                Assert.Equal(_textView.GetLineRange(0, 1).ExtentIncludingLineBreak, _textView.GetSelectionSpan());
            }

            [WpfFact]
            public async Task IncrementalSearch_LineModeShouldSelectFullLineAcrossBlanks()
            {
                Create("dog", "", "cat", "tree");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0, 1).ExtentIncludingLineBreak);
                _vimBuffer.Process("/ca");
                await _vimBuffer.GetSearchCompleteAsync();
                Assert.Equal(_textView.GetLineRange(0, 2).ExtentIncludingLineBreak, _textView.GetSelectionSpan());
            }

            [WpfFact]
            public async Task IncrementalSearch_CharModeShouldExtendToSearchResult()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualCharacter, new SnapshotSpan(_textView.GetLine(0).Start, 1));
                _vimBuffer.Process("/o");
                await _vimBuffer.GetSearchCompleteAsync();
                Assert.Equal(new SnapshotSpan(_textView.GetLine(0).Start, 2), _textView.GetSelectionSpan());
            }

            /// <summary>
            /// An incremental search operation shouldn't change the location of the caret until the search is
            /// completed
            /// </summary>
            [WpfFact]
            public void IncrementalSearch_DontChangeCaret()
            {
                Create("cat", "dog", "tree");
                _vimBuffer.Process("v/do");
                Assert.Equal(0, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Make sure that Escape will properly exit the incremental search and return us to the previous
            /// visual mode state (with the same selection)
            /// </summary>
            [WpfFact]
            public void IncrementalSearch_EscapeShouldExitSearch()
            {
                Create("cat", "dog", "tree");
                _vimBuffer.ProcessNotation("vl/dog<Esc>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.False(_vimBuffer.IncrementalSearch.HasActiveSession);
                Assert.Equal("ca", _textView.GetSelectionSpan().GetText());
            }

            /// <summary>
            /// Make sure that enter completes the search which includes updating the caret
            /// </summary>
            [WpfFact]
            public void IncrementalSearch_EnterShouldCompleteSearch()
            {
                Create("cat", "dog", "tree");
                _vimBuffer.ProcessNotation("vl/dog<Enter>");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                Assert.False(_vimBuffer.IncrementalSearch.HasActiveSession);
                Assert.Equal(_textBuffer.GetLine(1).Start, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Enter visual mode with the InitialVisualSelection argument which is a character span
            /// </summary>
            [WpfFact]
            public void InitialVisualSelection_Character()
            {
                Create("dogs", "cats");

                var visualSpan = VimUtil.CreateVisualSpanCharacter(_textBuffer.GetSpan(1, 2));
                var visualSelection = VisualSelection.CreateForward(visualSpan);
                _vimBuffer.SwitchMode(ModeKind.VisualCharacter, ModeArgument.NewInitialVisualSelection(visualSelection, FSharpOption<SnapshotPoint>.None));
                DoEvents();
                Assert.Equal(visualSelection, VisualSelection.CreateForSelection(_textView, VisualKind.Character, SelectionKind.Inclusive, tabStop: 4));
            }

            /// <summary>
            /// Enter visual mode with the InitialVisualSelection argument which is a line span
            /// </summary>
            [WpfFact]
            public void InitialVisualSelection_Line()
            {
                Create("dogs", "cats", "fish");

                var lineRange = _textView.GetLineRange(0, 1);
                var visualSelection = VisualSelection.NewLine(lineRange, SearchPath.Forward, 1);
                _vimBuffer.SwitchMode(ModeKind.VisualLine, ModeArgument.NewInitialVisualSelection(visualSelection, FSharpOption<SnapshotPoint>.None));
                DoEvents();
                Assert.Equal(visualSelection, VisualSelection.CreateForSelection(_textView, VisualKind.Line, SelectionKind.Inclusive, tabStop: 4));
            }

            /// <summary>
            /// Enter visual mode with the InitialVisualSelection argument which is a block span
            /// </summary>
            [WpfFact]
            public void InitialVisualSelection_Block()
            {
                Create("dogs", "cats", "fish");

                var blockSpan = _textView.GetBlockSpan(1, 2, 0, 2);
                var visualSelection = VisualSelection.NewBlock(blockSpan, BlockCaretLocation.BottomLeft);
                _vimBuffer.SwitchMode(ModeKind.VisualBlock, ModeArgument.NewInitialVisualSelection(visualSelection, FSharpOption<SnapshotPoint>.None));
                DoEvents();
                Assert.Equal(visualSelection, VisualSelection.CreateForSelection(_textView, VisualKind.Block, SelectionKind.Inclusive, tabStop: 4));
            }

            /// <summary>
            /// Record a macro which delets selected text.  When the macro is played back it should
            /// just run the delete against unselected text.  In other words it's just the raw keystrokes
            /// which are saved not the selection state
            /// </summary>
            [WpfFact]
            public void Macro_RecordDeleteSelectedText()
            {
                Create("the cat chased the dog");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 0, 3));
                _vimBuffer.Process("qcxq");
                Assert.Equal(" cat chased the dog", _textView.GetLine(0).GetText());
                _textView.MoveCaretTo(1);
                _vimBuffer.Process("@c");
                Assert.Equal(" at chased the dog", _textView.GetLine(0).GetText());
            }

            /// <summary>
            /// Run the macro to delete the selected text
            /// </summary>
            [WpfFact]
            public void Macro_RunDeleteSelectedText()
            {
                Create("the cat chased the dog");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 0, 3));
                TestRegister.UpdateValue("x");
                _vimBuffer.Process("@c");
                Assert.Equal(" cat chased the dog", _textView.GetLine(0).GetText());
            }

            /// <summary>
            /// When the final line of the ITextBuffer is an empty line make sure that we can
            /// move up off of it when in Visual Line Mode.
            ///
            /// Issue #769
            /// </summary>
            [WpfFact]
            public void Move_Line_FromBottom()
            {
                Create("cat", "dog", "", "");
                _textView.MoveCaretToLine(2);
                _vimBuffer.Process("Vk");
                Assert.Equal(_textBuffer.GetLineRange(1, 2).ExtentIncludingLineBreak, _textView.GetSelectionSpan());
            }

            /// <summary>
            /// Make sure that we can use 'j' to go over an empty line in Visual Character
            /// mode
            ///
            /// Issue #758
            /// </summary>
            [WpfFact]
            public void Move_Character_OverEmptyLine()
            {
                Create("cat", "", "dog");
                _vimBuffer.Process("vjj");
                Assert.Equal(_textBuffer.GetLine(2).Start, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Test the movement of the caret over a shorter line and then back to a line long
            /// enough
            /// </summary>
            [WpfFact]
            public void Move_Block_OverShortLine()
            {
                Create("really long line", "short", "really long line");
                _textView.MoveCaretTo(7);
                _vimBuffer.ProcessNotation("<C-v>lll");
                Assert.Equal("long", _textView.Selection.SelectedSpans[0].GetText());
                _vimBuffer.ProcessNotation("jj");
                var spans = _textView.Selection.SelectedSpans;
                Assert.Equal(3, spans.Count);
                Assert.Equal("long", spans[0].GetText());
                Assert.Equal("", spans[1].GetText());
                Assert.Equal("long", spans[2].GetText());
            }

            /// <summary>
            /// Character should be positioned at the end of the inserted text
            /// </summary>
            [WpfFact]
            public void PutOver_CharacterWise_WithSingleCharacterWise()
            {
                Create("dog");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 1, 1));
                UnnamedRegister.UpdateValue("cat", OperationKind.CharacterWise);
                _vimBuffer.Process("p");
                Assert.Equal("dcatg", _textView.GetLine(0).GetText());
                Assert.Equal(3, _textView.GetCaretPoint().Position);
                Assert.Equal("o", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned after the end of the inserted text
            /// </summary>
            [WpfFact]
            public void PutOver_CharacterWise_WithSingleCharacterWiseAndCaretMove()
            {
                Create("dog");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 1, 1));
                UnnamedRegister.UpdateValue("cat", OperationKind.CharacterWise);
                _vimBuffer.Process("gp");
                Assert.Equal("dcatg", _textView.GetLine(0).GetText());
                Assert.Equal(4, _textView.GetCaretPoint().Position);
                Assert.Equal("o", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned at the start of the inserted line
            /// </summary>
            [WpfFact]
            public void PutOver_CharacterWise_WithLineWise()
            {
                Create("dog");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 1, 1));
                UnnamedRegister.UpdateValue("cat\n", OperationKind.LineWise);
                _vimBuffer.Process("p");
                Assert.Equal("d", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
                Assert.Equal("g", _textView.GetLine(2).GetText());
                Assert.Equal(_textView.GetLine(1).Start, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Character should be positioned at the first line after the inserted
            /// lines
            /// </summary>
            [WpfFact]
            public void PutOver_CharacterWise_WithLineWiseAndCaretMove()
            {
                Create("dog");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 1, 1));
                UnnamedRegister.UpdateValue("cat\n", OperationKind.LineWise);
                _vimBuffer.Process("gp");
                Assert.Equal("d", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
                Assert.Equal("g", _textView.GetLine(2).GetText());
                Assert.Equal(_textView.GetLine(2).Start, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Character should be positioned at the start of the first line in the
            /// block
            /// </summary>
            [WpfFact]
            public void PutOver_CharacterWise_WithBlock()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 1, 1));
                UnnamedRegister.UpdateBlockValues("aa", "bb");
                _vimBuffer.Process("p");
                Assert.Equal("daag", _textView.GetLine(0).GetText());
                Assert.Equal("cbbat", _textView.GetLine(1).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Caret should be positioned after the line character in the last
            /// line of the inserted block
            /// </summary>
            [WpfFact]
            public void PutOver_CharacterWise_WithBlockAndCaretMove()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 1, 1));
                UnnamedRegister.UpdateBlockValues("aa", "bb");
                _vimBuffer.Process("gp");
                Assert.Equal("daag", _textView.GetLine(0).GetText());
                Assert.Equal("cbbat", _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetLine(1).Start.Add(3), _textView.GetCaretPoint());
            }

            /// <summary>
            /// When doing a put over selection the text being deleted should be put into
            /// the unnamed register.
            /// </summary>
            [WpfFact]
            public void PutOver_CharacterWise_NamedRegisters()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 0, 3));
                _registerMap.GetRegister('c').UpdateValue("pig");
                _vimBuffer.Process("\"cp");
                Assert.Equal("pig", _textView.GetLine(0).GetText());
                Assert.Equal("dog", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// When doing a put over selection the text being deleted should be put into
            /// the unnamed register.  If the put came from the unnamed register then the
            /// original put value is overwritten
            /// </summary>
            [WpfFact]
            public void PutOver_CharacterWise_UnnamedRegisters()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineSpan(0, 0, 3));
                UnnamedRegister.UpdateValue("pig");
                _vimBuffer.Process("p");
                Assert.Equal("pig", _textView.GetLine(0).GetText());
                Assert.Equal("dog", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned at the end of the inserted text
            /// </summary>
            [WpfFact]
            public void PutOver_LineWise_WithCharcterWise()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0).ExtentIncludingLineBreak);
                UnnamedRegister.UpdateValue("fish", OperationKind.CharacterWise);
                _vimBuffer.Process("p");
                Assert.Equal("fish", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.Equal("dog\r\n", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned after the end of the inserted text
            /// </summary>
            [WpfFact]
            public void PutOver_LineWise_WithCharacterWiseAndCaretMove()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0).ExtentIncludingLineBreak);
                UnnamedRegister.UpdateValue("fish", OperationKind.CharacterWise);
                _vimBuffer.Process("gp");
                Assert.Equal("fish", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetLine(1).Start, _textView.GetCaretPoint());
                Assert.Equal("dog\r\n", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned at the end of the inserted text
            /// </summary>
            [WpfFact]
            public void PutOver_LineWise_WithLineWise()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0).ExtentIncludingLineBreak);
                UnnamedRegister.UpdateValue("fish\n", OperationKind.LineWise);
                _vimBuffer.Process("p");
                Assert.Equal("fish", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.Equal("dog\r\n", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned after the end of the inserted text
            /// </summary>
            [WpfFact]
            public void PutOver_LineWise_WithLineWiseAndCaretMove()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0).ExtentIncludingLineBreak);
                UnnamedRegister.UpdateValue("fish\n", OperationKind.LineWise);
                _vimBuffer.Process("gp");
                Assert.Equal("fish", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetLine(1).Start, _textView.GetCaretPoint());
                Assert.Equal("dog\r\n", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned at the start of the first inserted value
            /// </summary>
            [WpfFact]
            public void PutOver_LineWise_WithBlock()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0).ExtentIncludingLineBreak);
                UnnamedRegister.UpdateBlockValues("aa", "bb");
                _vimBuffer.Process("p");
                Assert.Equal("aa", _textView.GetLine(0).GetText());
                Assert.Equal("bb", _textView.GetLine(1).GetText());
                Assert.Equal("cat", _textView.GetLine(2).GetText());
                Assert.Equal(0, _textView.GetCaretPoint().Position);
                Assert.Equal("dog\r\n", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned at the first character after the inserted
            /// text
            /// </summary>
            [WpfFact]
            public void PutOver_LineWise_WithBlockAndCaretMove()
            {
                Create("dog", "cat");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0).ExtentIncludingLineBreak);
                UnnamedRegister.UpdateBlockValues("aa", "bb");
                _vimBuffer.Process("gp");
                Assert.Equal("aa", _textView.GetLine(0).GetText());
                Assert.Equal("bb", _textView.GetLine(1).GetText());
                Assert.Equal("cat", _textView.GetLine(2).GetText());
                Assert.Equal(_textView.GetLine(2).Start, _textView.GetCaretPoint());
                Assert.Equal("dog\r\n", UnnamedRegister.StringValue);
            }

            /// <summary>
            /// Character should be positioned at the start of the first inserted value
            /// </summary>
            [WpfFact]
            public void PutOver_Block_WithCharacterWise()
            {
                Create("dog", "cat");
                EnterBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                UnnamedRegister.UpdateValue("fish", OperationKind.CharacterWise);
                _vimBuffer.Process("p");
                Assert.Equal("dfishg", _textView.GetLine(0).GetText());
                Assert.Equal("cfisht", _textView.GetLine(1).GetText());
                Assert.Equal(4, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Character should be positioned after the last character after the inserted
            /// text
            /// </summary>
            [WpfFact]
            public void PutOver_Block_WithCharacterWiseAndCaretMove()
            {
                Create("dog", "cat");
                EnterBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                UnnamedRegister.UpdateValue("fish", OperationKind.CharacterWise);
                _vimBuffer.Process("gp");
                Assert.Equal("dfishg", _textView.GetLine(0).GetText());
                Assert.Equal("cfisht", _textView.GetLine(1).GetText());
                Assert.Equal(5, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Character should be positioned at the start of the inserted line
            /// </summary>
            [WpfFact]
            public void PutOver_Block_WithLineWise()
            {
                Create("dog", "cat");
                EnterBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                UnnamedRegister.UpdateValue("fish\n", OperationKind.LineWise);
                _vimBuffer.Process("p");
                Assert.Equal("dg", _textView.GetLine(0).GetText());
                Assert.Equal("ct", _textView.GetLine(1).GetText());
                Assert.Equal("fish", _textView.GetLine(2).GetText());
                Assert.Equal(_textView.GetLine(2).Start, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Caret should be positioned at the start of the line which follows the
            /// inserted lines
            /// </summary>
            [WpfFact]
            public void PutOver_Block_WithLineWiseAndCaretMove()
            {
                Create("dog", "cat", "bear");
                EnterBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                UnnamedRegister.UpdateValue("fish\n", OperationKind.LineWise);
                _vimBuffer.Process("gp");
                Assert.Equal("dg", _textView.GetLine(0).GetText());
                Assert.Equal("ct", _textView.GetLine(1).GetText());
                Assert.Equal("fish", _textView.GetLine(2).GetText());
                Assert.Equal(_textView.GetLine(3).Start, _textView.GetCaretPoint());
            }

            /// <summary>
            /// Character should be positioned at the start of the first inserted string
            /// from the block
            /// </summary>
            [WpfFact]
            public void PutOver_Block_WithBlock()
            {
                Create("dog", "cat");
                EnterBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                UnnamedRegister.UpdateBlockValues("aa", "bb");
                _vimBuffer.Process("p");
                Assert.Equal("daag", _textView.GetLine(0).GetText());
                Assert.Equal("cbbt", _textView.GetLine(1).GetText());
                Assert.Equal(1, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Caret should be positioned at the first character after the last inserted
            /// charecter of the last string in the block
            /// </summary>
            [WpfFact]
            public void PutOver_Block_WithBlockAndCaretMove()
            {
                Create("dog", "cat");
                EnterBlock(_textView.GetBlockSpan(1, 1, 0, 2));
                UnnamedRegister.UpdateBlockValues("aa", "bb");
                _vimBuffer.Process("gp");
                Assert.Equal("daag", _textView.GetLine(0).GetText());
                Assert.Equal("cbbt", _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetLine(1).Start.Add(3), _textView.GetCaretPoint());
            }

            [WpfFact]
            public void PutOver_Legacy1()
            {
                Create("dog", "cat", "bear", "tree");
                EnterMode(ModeKind.VisualCharacter, new SnapshotSpan(_textView.TextSnapshot, 0, 2));
                _vimBuffer.RegisterMap.GetRegister(RegisterName.Unnamed).UpdateValue("pig");
                _vimBuffer.Process("p");
                Assert.Equal("pigg", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
                Assert.Equal(2, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void PutOver_Legacy2()
            {
                Create("dog", "cat", "bear", "tree");
                var span = new SnapshotSpan(
                    _textView.GetLine(0).Start.Add(1),
                    _textView.GetLine(1).Start.Add(2));
                EnterMode(ModeKind.VisualCharacter, span);
                _vimBuffer.RegisterMap.GetRegister(RegisterName.Unnamed).UpdateValue("pig");
                _vimBuffer.Process("p");
                Assert.Equal("dpigt", _textView.GetLine(0).GetText());
                Assert.Equal("bear", _textView.GetLine(1).GetText());
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void PutBefore_Legacy1()
            {
                Create("dog", "cat", "bear", "tree");
                EnterMode(ModeKind.VisualCharacter, _textView.GetLineRange(0).Extent);
                _vimBuffer.RegisterMap.GetRegister(RegisterName.Unnamed).UpdateValue("pig");
                _vimBuffer.Process("P");
                Assert.Equal("pig", _textView.GetLine(0).GetText());
                Assert.Equal("cat", _textView.GetLine(1).GetText());
                Assert.Equal(2, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Put with indent commands are another odd ball item in Vim.  It's the one put command
            /// which doesn't delete the selection when putting the text into the buffer.  Instead
            /// it just continues on in visual mode after the put
            /// </summary>
            [WpfFact]
            public void PutAfterWithIndent_VisualLine()
            {
                Create("  dog", "  cat", "bear");
                EnterMode(ModeKind.VisualLine, _textView.GetLineRange(0).ExtentIncludingLineBreak);
                UnnamedRegister.UpdateValue("bear" + Environment.NewLine, OperationKind.LineWise);
                _vimBuffer.Process("]p");
                Assert.Equal("  dog", _textView.GetLine(0).GetText());
                Assert.Equal("  bear", _textView.GetLine(1).GetText());
                Assert.Equal(_textView.GetPointInLine(1, 2), _textView.GetCaretPoint());
                Assert.Equal(_textView.GetLineRange(0, 1).ExtentIncludingLineBreak, _textView.GetSelectionSpan());
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
            }

            /// <summary>
            /// Simple inner word selection on visual mode
            /// </summary>
            [WpfFact]
            public void TextObject_InnerWord()
            {
                Create("cat dog fish");
                _textView.MoveCaretTo(4);
                _vimBuffer.Process("viw");
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(6, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// When a 'iw' text selection occurs and extends the selection backwards it should reset
            /// the visual caret start point.  This can be demonstrated jumping back and forth between
            /// character and line mode
            /// </summary>
            [WpfFact]
            public void TextObject_InnerWord_ResetVisualStartPoint()
            {
                Create("cat dog fish");
                _textView.MoveCaretTo(5);
                _vimBuffer.Process("viwVv");
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(6, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Simple inner word selection from the middle of a word.  Should still select the entire
            /// word
            /// </summary>
            [WpfFact]
            public void TextObject_InnerWord_FromMiddle()
            {
                Create("cat dog fish");
                _textView.MoveCaretTo(5);
                _vimBuffer.Process("viw");
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(6, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// This behavior isn't documented.  But if iw begins on a single white space character
            /// then repeated iw shouldn't change anything.  It should select the single space and
            /// go from there
            /// </summary>
            [WpfFact]
            public void TextObject_InnerWord_FromSingleWhiteSpace()
            {
                Create("cat dog fish");
                _textView.MoveCaretTo(3);
                _vimBuffer.Process('v');
                for (var i = 0; i < 10; i++)
                {
                    _vimBuffer.Process("iw");
                    Assert.Equal(" ", _textView.GetSelectionSpan().GetText());
                    Assert.Equal(3, _textView.GetCaretPoint().Position);
                }
            }

            /// <summary>
            /// From a non-single white space the inner word motion should select
            /// the entire white space
            /// </summary>
            [WpfFact]
            public void TextObject_InnerWord_FromMultipleWhiteSpace()
            {
                Create("cat  dog fish");
                _textView.MoveCaretTo(3);
                _vimBuffer.Process("viw");
                Assert.Equal("  ", _textView.GetSelectionSpan().GetText());
                Assert.Equal(4, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// The non initial selection from white space should extend to the
            /// next word
            /// </summary>
            [WpfFact]
            public void TextObject_InnerWord_MultipleWhiteSpace_Second()
            {
                Create("cat  dog fish");
                _textView.MoveCaretTo(3);
                _vimBuffer.Process("viwiw");
                Assert.Equal("  dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(7, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Simple all word selection
            /// </summary>
            [WpfFact]
            public void TextObject_AllWord()
            {
                Create("cat dog fish");
                _vimBuffer.Process("vaw");
                Assert.Equal("cat ", _textView.GetSelectionSpan().GetText());
                Assert.Equal(3, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Unlike the 'iw' motion the 'aw' motion doesn't have truly odd behavior from
            /// a single white space
            /// </summary>
            [WpfFact]
            public void TextObject_AllWord_FromSingleWhiteSpace()
            {
                Create("cat dog fish");
                _textView.MoveCaretTo(3);
                _vimBuffer.Process("vaw");
                Assert.Equal(" dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(6, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Ensure the ab motion includes the parens and puts the caret on the last
            /// character
            /// </summary>
            [WpfFact]
            public void TextObject_AllParen_MiddleOfWord()
            {
                Create("cat (dog) fish");
                _textView.MoveCaretTo(6);
                _vimBuffer.Process("vab");
                Assert.Equal("(dog)", _textView.GetSelectionSpan().GetText());
                Assert.Equal(8, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Unlike non-block selections multiple calls to ab won't extend the selection
            /// to a sibling block
            /// </summary>
            [WpfFact]
            public void TextObject_AllParen_Multiple()
            {
                Create("cat (dog) (bear)");
                _textView.MoveCaretTo(6);
                _vimBuffer.Process("vabababab");
                Assert.Equal("(dog)", _textView.GetSelectionSpan().GetText());
                Assert.Equal(8, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Text object selections will extend to outer blocks
            /// </summary>
            [WpfFact]
            public void TextObject_AllParen_ExpandOutward()
            {
                Create("cat (fo(bad)od) bear");
                _textView.MoveCaretTo(9);
                _vimBuffer.Process("vab");
                Assert.Equal("(bad)", _textView.GetSelectionSpan().GetText());
                _vimBuffer.Process("ab");
                Assert.Equal("(fo(bad)od)", _textView.GetSelectionSpan().GetText());
                Assert.Equal(14, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Text object selections will extend to outer blocks
            /// </summary>
            [WpfFact]
            public void TextObject_Count_AllParen_ExpandOutward()
            {
                Create("cat (fo(bad)od) bear");
                _textView.MoveCaretTo(9);
                _vimBuffer.Process("v2ab");
                Assert.Equal("(fo(bad)od)", _textView.GetSelectionSpan().GetText());
                Assert.Equal(14, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void TextObject_Quotes_Included()
            {
                Create(@"cat ""dog"" tree");
                EnterMode(ModeKind.VisualCharacter, new SnapshotSpan(_textView.TextSnapshot, 5, 1));
                _vimBuffer.Process(@"i""i""");
                Assert.Equal(@"""dog""", _textView.GetSelectionSpan().GetText());
                Assert.Equal(8, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void TextObject_Count_Quotes_Included()
            {
                Create(@"cat ""dog"" tree");
                EnterMode(ModeKind.VisualCharacter, new SnapshotSpan(_textView.TextSnapshot, 5, 1));
                _vimBuffer.Process(@"2i""");
                Assert.Equal(@"""dog""", _textView.GetSelectionSpan().GetText());
                Assert.Equal(8, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// If we've already selected the inner block at the caret then move outward
            /// and select the containing block
            /// </summary>
            [WpfFact]
            public void TextObject_InnerParen_ExpandOutward()
            {
                Create("a (fo(tree)od) b");
                _textView.MoveCaretTo(7);
                _vimBuffer.Process("vib");
                Assert.Equal("tree", _textView.GetSelectionSpan().GetText());
                _vimBuffer.Process("ib");
                Assert.Equal("fo(tree)od", _textView.GetSelectionSpan().GetText());
                Assert.Equal(12, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// If the entire inner block is not yet selected then go ahead and select it
            /// </summary>
            [WpfFact]
            public void TextObject_InnerParen_ExpandToFullBlock()
            {
                Create("a (fo(tree)od) b");
                _textView.MoveCaretTo(8);
                _vimBuffer.Process("vl");
                Assert.Equal("ee", _textView.GetSelectionSpan().GetText());
                _vimBuffer.Process("ib");
            }

            /// <summary>
            /// Ensure the ib motion excludes the parens and puts the caret on the last
            /// character
            /// </summary>
            [WpfFact]
            public void TextObject_InnerParen_MiddleOfWord()
            {
                Create("cat (dog) fish");
                _textView.MoveCaretTo(6);
                _vimBuffer.Process("vib");
                Assert.Equal("dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(7, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// Ensure the iB motion excludes the brackets and puts the caret on the last
            /// character
            /// </summary>
            [WpfFact]
            public void TextObject_InnerBlock()
            {
                Create("int foo (bar b)", "{", "if (true)", "{", "int a;", "int b;", "}", "}");
                _textView.MoveCaretToLine(4);
                _vimBuffer.Process("viB");
                Assert.Equal(_textBuffer.GetLineRange(4, 5).GetText(), _textView.GetSelectionSpan().GetText());
                Assert.Equal(48, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// All white space and the following word should be selecetd
            /// </summary>
            [WpfFact]
            public void TextObject_AllWord_FromMultipleWhiteSpace()
            {
                Create("cat  dog fish");
                _textView.MoveCaretTo(3);
                _vimBuffer.Process("vaw");
                Assert.Equal("  dog", _textView.GetSelectionSpan().GetText());
                Assert.Equal(7, _textView.GetCaretPoint().Position);
            }

            /// <summary>
            /// When standing in middle of word the following whitespace after . should be selected
            /// </summary>
            [WpfFact]
            public void TextObject_AllSentence_MiddleWord()
            {
                Create("cat. dog. fish.");
                _textView.MoveCaretTo(6);
                _vimBuffer.Process("vas");
                Assert.Equal("dog. ", _textView.GetSelectionSpan().GetText());
                Assert.Equal(9, _textView.GetCaretPoint().Position);
            }

            [WpfFact]
            public void Issue1456()
            {
                Create("foo", "bar", "baz");

                _textView.MoveCaretTo(5);
                _vimBuffer.Process("vap");

                Assert.Equal(_textView.GetLineRange(0, 2).GetText(), _textView.GetSelectionSpan().GetText());
            }

            [WpfFact]
            public void Issue679()
            {
                Create(4, "  <div>", "\t<b>Reason:</b>", "\t@Model.Foo", "  </div>");
                _vimBuffer.ProcessNotation("<c-q>ljjjjx");
                Assert.Equal(new[]
                    {
                       "<div>",
                       "  <b>Reason:</b>",
                       "  @Model.Foo",
                       "</div>"
                    },
                    _textBuffer.GetLines());
            }

            [WpfFact]
            public void Issue903()
            {
                Create(4, "some line1", "\tsome line 2");
                _textView.MoveCaretTo(8);
                _vimBuffer.ProcessNotation("<c-q>j");
                Assert.Equal(new[]
                    {
                        _textBuffer.GetLineSpan(0, 8, 1),
                        _textBuffer.GetLineSpan(1, 5, 1)
                    },
                    _textView.Selection.SelectedSpans);
            }

            [WpfFact]
            public void Issue1213()
            {
                Create("hello world");
                _vimBuffer.ProcessNotation("v<c-c>");
                Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
            }

            [WpfFact]
            public void Issue1317()
            {
                Create("hello world");
                _vimBuffer.ProcessNotation("vl");
                Assert.False(_vimBuffer.CanProcess(VimKey.RightMouse));
            }

            [WpfFact]
            public void Issue1715()
            {
                Create(@"        public override void Name(List<object> parameter)
        {
            throw new NotImplementedException();
        }");
                var index = _textBuffer.GetLine(0).GetText().IndexOf('N');
                _textView.MoveCaretTo(index);
                _vimBuffer.Process("Vj%");
                Assert.Equal(_textBuffer.GetLineRange(startLine: 0, endLine: 3).ExtentIncludingLineBreak, _textView.GetSelectionSpan());
            }

            [WpfFact]
            public void OpenLink_Character()
            {
                Create("foo Xhttps://github.com/VsVim/VsVimX bar", "");
                _globalSettings.Selection = "inclusive";
                var line = _textBuffer.GetLine(0).GetText();
                var beg = line.IndexOf('X') + 1;
                var end = line.IndexOf('X', beg);
                var count = end - beg - 1;
                _textView.MoveCaretToLine(0, beg);
                _vimBuffer.Process($"v{count}l");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                var link = "";
                _vimHost.OpenLinkFunc = arg =>
                    {
                        link = arg;
                        return true;
                    };
                _vimBuffer.Process("gx");
                Assert.Equal("https://github.com/VsVim/VsVim", link);
                Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
            }

            [WpfFact]
            public void OpenLink_Line()
            {
                Create("https://github.com/VsVim/VsVim", "");
                _vimBuffer.Process("V");
                Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                var link = "";
                _vimHost.OpenLinkFunc = arg =>
                    {
                        link = arg;
                        return true;
                    };
                _vimBuffer.Process("gx");
                Assert.Equal("https://github.com/VsVim/VsVim", link);
                Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
            }
        }

        public sealed class TextObjectTest : VisualModeIntegrationTest
        {
            [WpfFact]
            public void InnerBlockYankAndPasteIsLinewise()
            {
                Create("if (true)", "{", "  statement;", "}", "// after");
                _textView.MoveCaretToLine(2);
                _vimBuffer.ProcessNotation("vi}");
                Assert.Equal(ModeKind.VisualCharacter, _vimBuffer.ModeKind);
                _vimBuffer.ProcessNotation("y");
                Assert.True(UnnamedRegister.OperationKind.IsCharacterWise);
                _vimBuffer.ProcessNotation("p");
                Assert.Equal(
                    new[] { "   statement;", " statement;" },
                    _textBuffer.GetLineRange(startLine: 2, endLine: 3).Lines.Select(x => x.GetText()));
            }

            [WpfFact]
            public void InnerBlockShouldGoToEol()
            {
                Create("if (true)", "{", "  statement;", "}", "// after");
                _textView.MoveCaretToLine(2);
                _vimBuffer.ProcessNotation("vi}");

                var column = _textView.GetCaretColumn();
                Assert.True(column.IsLineBreak);
            }
        }

        public sealed class NextMatchTest : VisualModeIntegrationTest
        {
            [WpfTheory]
            [MemberData(nameof(SelectionOptions))]
            public void SelectNextMatchForward(string selection)
            {
                Create("cat", "dog", "cat", "dog", "cat", "dog", "");
                _globalSettings.Selection = selection;
                _vimBuffer.ProcessNotation("/cat<CR>");
                Assert.Equal(_textBuffer.GetPointInLine(2, 0), _textView.GetCaretPoint());
                _vimBuffer.ProcessNotation("vgn");
                var span1 = new SnapshotSpan(_textBuffer.GetPointInLine(2, 0), _textBuffer.GetPointInLine(2, 3));
                Assert.Equal(span1, _textView.GetSelectionSpan());
                _vimBuffer.ProcessNotation("gn");
                var span2 = new SnapshotSpan(_textBuffer.GetPointInLine(2, 0), _textBuffer.GetPointInLine(4, 3));
                Assert.Equal(span2, _textView.GetSelectionSpan());
            }

            [WpfTheory]
            [MemberData(nameof(SelectionOptions))]
            public void SelectNextMatchForwardWithCount(string selection)
            {
                Create("cat", "dog", "cat", "dog", "cat", "dog", "");
                _globalSettings.Selection = selection;
                _vimBuffer.ProcessNotation("/cat<CR>");
                Assert.Equal(_textBuffer.GetPointInLine(2, 0), _textView.GetCaretPoint());
                _vimBuffer.ProcessNotation("v2gn");
                var span = new SnapshotSpan(_textBuffer.GetPointInLine(2, 0), _textBuffer.GetPointInLine(4, 3));
                Assert.Equal(span, _textView.GetSelectionSpan());
            }

            [WpfFact]
            public void SelectNextMatchBackwardInclusive()
            {
                Create("cat", "dog", "cat", "dog", "cat", "dog", "");
                _globalSettings.Selection = "inclusive";
                _vimBuffer.ProcessNotation("/cat<CR>n");
                Assert.Equal(_textBuffer.GetPointInLine(4, 0), _textView.GetCaretPoint());
                _vimBuffer.ProcessNotation("vgN");
                var span1 = new SnapshotSpan(_textBuffer.GetPointInLine(2, 0), _textBuffer.GetPointInLine(4, 1));
                Assert.Equal(span1, _textView.GetSelectionSpan());
                _vimBuffer.ProcessNotation("gN");
                var span2 = new SnapshotSpan(_textBuffer.GetPointInLine(0, 0), _textBuffer.GetPointInLine(4, 1));
                Assert.Equal(span2, _textView.GetSelectionSpan());
            }

            [WpfFact]
            public void SelectNextMatchBackwardExclusive()
            {
                Create("cat", "dog", "cat", "dog", "cat", "dog", "");
                _globalSettings.Selection = "exclusive";
                _vimBuffer.ProcessNotation("/cat<CR>nl");
                Assert.Equal(_textBuffer.GetPointInLine(4, 1), _textView.GetCaretPoint());
                _vimBuffer.ProcessNotation("vgN");
                var span1 = new SnapshotSpan(_textBuffer.GetPointInLine(2, 0), _textBuffer.GetPointInLine(4, 1));
                Assert.Equal(span1, _textView.GetSelectionSpan());
                _vimBuffer.ProcessNotation("gN");
                var span2 = new SnapshotSpan(_textBuffer.GetPointInLine(0, 0), _textBuffer.GetPointInLine(4, 1));
                Assert.Equal(span2, _textView.GetSelectionSpan());
            }

            [WpfFact]
            public void SelectNextMatchBackwardInclusiveWithCount()
            {
                Create("cat", "dog", "cat", "dog", "cat", "dog", "");
                _globalSettings.Selection = "inclusive";
                _vimBuffer.ProcessNotation("/cat<CR>n");
                Assert.Equal(_textBuffer.GetPointInLine(4, 0), _textView.GetCaretPoint());
                _vimBuffer.ProcessNotation("v2gN");
                var span = new SnapshotSpan(_textBuffer.GetPointInLine(0, 0), _textBuffer.GetPointInLine(4, 1));
                Assert.Equal(span, _textView.GetSelectionSpan());
            }

            [WpfFact]
            public void SelectNextMatchBackwardExclusiveWithCount()
            {
                Create("cat", "dog", "cat", "dog", "cat", "dog", "");
                _globalSettings.Selection = "exclusive";
                _vimBuffer.ProcessNotation("/cat<CR>nl");
                Assert.Equal(_textBuffer.GetPointInLine(4, 1), _textView.GetCaretPoint());
                _vimBuffer.ProcessNotation("v2gN");
                var span = new SnapshotSpan(_textBuffer.GetPointInLine(0, 0), _textBuffer.GetPointInLine(4, 1));
                Assert.Equal(span, _textView.GetSelectionSpan());
            }
        }

        public abstract class YankSelectionTest : VisualModeIntegrationTest
        {
            private void AssertRegister(params string[] lines)
            {
                var data = UnnamedRegister.StringData;
                Assert.True(data.IsBlock);
                Assert.Equal(lines, ((StringData.Block)data).BlockTexts);
            }

            public sealed class BlockTest : YankSelectionTest
            {
                [WpfFact]
                public void Simple()
                {
                    Create("cat", "dog");
                    _vimBuffer.ProcessNotation("<c-q>ljy");
                    AssertRegister("ca", "do");
                }

                [WpfFact]
                public void SimpleNonZeroColumn()
                {
                    Create("cats", "dogs");
                    _vimBuffer.ProcessNotation("l<c-q>ljy");
                    AssertRegister("at", "og");
                }

                [WpfFact]
                public void SimpleWidthOneSelection()
                {
                    Create("cats", "dogs");
                    _vimBuffer.ProcessNotation("l<c-q>jy");
                    AssertRegister("a", "o");
                }

                [WpfFact]
                public void PartialTab()
                {
                    Create(4, "trucker", "\tdog");
                    _vimBuffer.ProcessNotation("ll<c-q>lljy");
                    AssertRegister("uck", "  d");
                }

                [WpfFact]
                public void CompleteTab()
                {
                    Create(4, "trucker", "\tdog");
                    _vimBuffer.ProcessNotation("<c-q>lllljy");
                    AssertRegister("truck", "\td");
                }

                [WpfFact]
                public void PartialTabInMiddleLine()
                {
                    Create(4, "trucker", "\tdog", "fisher");
                    _vimBuffer.ProcessNotation("ll<c-q>lljjy");
                    AssertRegister("uck", "  d", "she");
                }
            }

            public sealed class VirtualBlock : YankSelectionTest
            {
                protected override void Create(params string[] lines)
                {
                    base.Create(lines);
                    _globalSettings.VirtualEdit = "block";
                }

                [WpfTheory]
                [MemberData(nameof(SelectionOptions))]
                public void YankBlock(string selection)
                {
                    Create("cat", "dog bear", "tree", "");
                    _globalSettings.Selection = selection;
                    _vimBuffer.ProcessNotation("<C-q>2j8ly");
                    AssertRegister("cat", "dog bear", "tree");
                }
            }

            public sealed class CharacterTest : YankSelectionTest
            {
                [WpfFact]
                public void InsideLineBreak()
                {
                    Create("cat dog", "bear");
                    _globalSettings.VirtualEdit = "onemore";
                    _textView.MoveCaretTo(4);
                    _vimBuffer.Process("vllly");
                    Assert.Equal("dog" + Environment.NewLine, UnnamedRegister.StringValue);
                }

                /// <summary>
                /// When the caret ends on an empty line then that line is included when the
                /// yank is performed
                /// </summary>
                [WpfFact]
                public void EmptyLine()
                {
                    Create("the dog", "", "cat");
                    _textView.MoveCaretTo(4);
                    _vimBuffer.Process("vjy");
                    Assert.Equal("dog" + Environment.NewLine + Environment.NewLine, UnnamedRegister.StringValue);
                }

                /// <summary>
                /// The yank selection command should exit visual mode after the operation
                /// </summary>
                [WpfFact]
                public void ShouldExitVisualMode()
                {
                    Create("cat", "dog");
                    EnterMode(ModeKind.VisualCharacter, _textView.GetLine(0).Extent);
                    _vimBuffer.Process("y");
                    Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
                    Assert.True(_textView.Selection.IsEmpty);
                }
            }

            public sealed class VirtualCharacterTest : YankSelectionTest
            {
                [WpfFact]
                public void InclusiveVirtualSpaces()
                {
                    Create("cat", "");
                    _globalSettings.VirtualEdit = "all";
                    _globalSettings.Selection = "inclusive";
                    _vimBuffer.Process("v6ly");
                    Assert.Equal("cat    ", UnnamedRegister.StringValue);
                }

                [WpfFact]
                public void ExclusiveVirtualSpaces()
                {
                    Create("cat", "");
                    _globalSettings.VirtualEdit = "all";
                    _globalSettings.Selection = "exclusive";
                    _vimBuffer.Process("v6ly");
                    Assert.Equal("cat   ", UnnamedRegister.StringValue);
                }
            }

            public sealed class LineTest : YankSelectionTest
            {
                /// <summary>
                /// Ensure that after yanking and leaving Visual Mode that the proper value is
                /// maintained for LastVisualSelection.  It should be the selection before the command
                /// was executed
                /// </summary>
                [WpfTheory]
                [MemberData(nameof(VirtualEditOptions))]
                public void LastVisualSelectionWithVeOnemore(string virtualEdit)
                {
                    Create("cat", "dog", "fish");
                    var span = _textView.GetLineRange(0, 1).ExtentIncludingLineBreak;
                    _globalSettings.VirtualEdit = virtualEdit;
                    SwitchEnterMode(ModeKind.VisualLine, span);
                    _vimBuffer.Process('y');
                    Assert.True(_vimTextBuffer.LastVisualSelection.IsSome());
                    Assert.Equal(span, _vimTextBuffer.LastVisualSelection.Value.VisualSpan.EditSpan.OverarchingSpan);
                }

                [WpfTheory]
                [MemberData(nameof(VirtualEditOptions))]
                public void ReselectLastVisual(string virtualEdit)
                {
                    Create("cat", "dog", "fish");
                    _globalSettings.VirtualEdit = virtualEdit;
                    _vimBuffer.Process("Vj");
                    var expected = _textView.GetSelectionSpan();
                    Assert.Equal(10, _textView.Selection.Start.Position.Difference(_textView.Selection.End.Position));
                    _vimBuffer.Process("y");
                    Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
                    _vimBuffer.Process("gv");
                    Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                    Assert.Equal(expected, _textView.GetSelectionSpan());
                }

                /// <summary>
                /// The yank line selection command should exit visual mode after the operation
                /// </summary>
                [WpfFact]
                public void ShouldExitVisualMode()
                {
                    Create("cat", "dog");
                    EnterMode(ModeKind.VisualCharacter, _textView.GetLine(0).Extent);
                    _vimBuffer.Process("Y");
                    Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
                    Assert.True(_textView.Selection.IsEmpty);
                }

                /// <summary>
                /// After deletion and undo, we should be able to restore the visual selection
                /// </summary>
                [WpfFact]
                public void RestoreVisualSelectionAfterUndo()
                {
                    // Reported in issue #2079.
                    Create("foo", "bar", "baz", "qux");
                    Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
                    _vimBuffer.ProcessNotation("2GV+");
                    Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                    Assert.Equal("bar\r\nbaz\r\n", _textView.GetSelectionSpan().GetText());
                    _vimBuffer.ProcessNotation("d");
                    Assert.True(_textView.Selection.IsEmpty);
                    Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
                    Assert.Equal(new[] { "foo", "qux" }, _textBuffer.GetLines());
                    _vimBuffer.ProcessNotation("u");
                    Assert.Equal(ModeKind.Normal, _vimBuffer.ModeKind);
                    Assert.Equal(new[] { "foo", "bar", "baz", "qux" }, _textBuffer.GetLines());
                    _vimBuffer.ProcessNotation("gv");
                    Assert.Equal(ModeKind.VisualLine, _vimBuffer.ModeKind);
                    Assert.Equal("bar\r\nbaz\r\n", _textView.GetSelectionSpan().GetText());
                }

                /// <summary>
                /// Ensure that putting over a characterwise selection updates
                /// the start/end of the last visual selection
                /// </summary>
                /// <param name="selection"></param>
                [WpfTheory]
                [InlineData("inclusive")]
                [InlineData("exclusive")]
                public void PutOverCharacterwiseReselect(string selection)
                {
                    // Reported in issue #2260.
                    Create("horse", "cat", "bear", "tree");
                    _globalSettings.Selection = selection;
                    _vimBuffer.ProcessNotation("1Gvey");
                    Assert.Equal("horse", UnnamedRegister.StringValue);
                    _vimBuffer.ProcessNotation("3Gvep");
                    Assert.Equal(new[] { "horse", "cat", "horse", "tree" }, _textBuffer.GetLines());
                    Assert.Equal("bear", UnnamedRegister.StringValue);
                    _vimBuffer.ProcessNotation("gv");
                    var span = new SnapshotSpan(_textBuffer.GetPointInLine(2, 0), _textBuffer.GetPointInLine(2, 5));
                    Assert.Equal(span, _textView.Selection.GetSpan());
                    _vimBuffer.ProcessNotation("y");
                    Assert.Equal("horse", UnnamedRegister.StringValue);
                }

                /// <summary>
                /// Ensure that putting over a linewise selection updates
                /// the start/end of the last visual selection
                /// </summary>
                /// <param name="selection"></param>
                [WpfTheory]
                [InlineData("inclusive")]
                [InlineData("exclusive")]
                public void PutOverLinewiseReselect(string selection)
                {
                    // Reported in issue #2260.
                    Create("horse", "cat", "bear", "tree", "dog", "");
                    _globalSettings.Selection = selection;
                    _vimBuffer.ProcessNotation("1GVjy");
                    Assert.Equal("horse" + Environment.NewLine + "cat" + Environment.NewLine,
                        UnnamedRegister.StringValue);
                    _vimBuffer.ProcessNotation("4GVp");
                    Assert.Equal(new[] { "horse", "cat", "bear", "horse", "cat", "dog", "" }, _textBuffer.GetLines());
                    Assert.Equal("tree" + Environment.NewLine, UnnamedRegister.StringValue);
                    _vimBuffer.ProcessNotation("gv");
                    var span = new SnapshotSpan(_textBuffer.GetPointInLine(3, 0), _textBuffer.GetPointInLine(5, 0));
                    Assert.Equal(span, _textView.Selection.GetSpan());
                    _vimBuffer.ProcessNotation("y");
                    Assert.Equal("horse" + Environment.NewLine + "cat" + Environment.NewLine,
                        UnnamedRegister.StringValue);
                }
            }
        }
    }
}
