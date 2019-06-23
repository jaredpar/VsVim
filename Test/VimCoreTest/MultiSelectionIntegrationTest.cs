﻿using System;
using System.Linq;
using Vim.EditorHost;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;
using Vim.Extensions;
using Vim.UnitTest.Exports;
using Vim.UnitTest.Mock;
using Xunit;
using Microsoft.FSharp.Core;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Vim.UnitTest
{
    /// <summary>
    /// Class for testing the full integration story of multiple carets
    /// </summary>
    public abstract class MultiSelectionIntegrationTest : VimTestBase
    {
        protected IVimBuffer _vimBuffer;
        protected IVimBufferData _vimBufferData;
        protected IVimTextBuffer _vimTextBuffer;
        protected IWpfTextView _textView;
        protected ITextBuffer _textBuffer;
        protected IVimGlobalSettings _globalSettings;
        protected IVimLocalSettings _localSettings;
        protected IVimWindowSettings _windowSettings;
        protected IJumpList _jumpList;
        protected IKeyMap _keyMap;
        protected IVimData _vimData;
        protected INormalMode _normalMode;
        protected IVimHost _vimHost;
        internal MultiSelectionTracker _multiSelectionTracker;
        protected MockVimHost _mockVimHost;
        protected TestableClipboardDevice _clipboardDevice;
        protected TestableMouseDevice _testableMouseDevice;

        protected virtual void Create(params string[] lines)
        {
            _textView = CreateTextView(lines);
            _textBuffer = _textView.TextBuffer;
            _vimBuffer = Vim.CreateVimBuffer(_textView);
            _vimBufferData = _vimBuffer.VimBufferData;
            _vimTextBuffer = _vimBuffer.VimTextBuffer;
            _normalMode = _vimBuffer.NormalMode;
            _keyMap = _vimBuffer.Vim.KeyMap;
            _localSettings = _vimBuffer.LocalSettings;
            _globalSettings = _localSettings.GlobalSettings;
            _windowSettings = _vimBuffer.WindowSettings;
            _jumpList = _vimBuffer.JumpList;
            _vimHost = _vimBuffer.Vim.VimHost;
            _mockVimHost = (MockVimHost)_vimHost;
            _mockVimHost.BeepCount = 0;
            _mockVimHost.IsMultiSelectionSupported = true;
            _mockVimHost.RegisterVimBuffer(_vimBuffer);
            _vimData = Vim.VimData;
            var commonOperations = CommonOperationsFactory.GetCommonOperations(_vimBufferData);
            _multiSelectionTracker = new MultiSelectionTracker(_vimBuffer, commonOperations, _testableMouseDevice);
            _clipboardDevice = (TestableClipboardDevice)CompositionContainer.GetExportedValue<IClipboardDevice>();

            _testableMouseDevice = (TestableMouseDevice)MouseDevice;
            _testableMouseDevice.IsLeftButtonPressed = false;
            _testableMouseDevice.Point = null;
        }

        public override void Dispose()
        {
            _testableMouseDevice.IsLeftButtonPressed = false;
            _testableMouseDevice.Point = null;
            base.Dispose();
        }

        private void ProcessNotation(string notation)
        {
            _vimBuffer.ProcessNotation(notation);
            DoEvents();
        }

        private VirtualSnapshotPoint GetPoint(int lineNumber, int column)
        {
            return _textView.GetVirtualPointInLine(lineNumber, column);
        }

        private SnapshotPoint[] CaretPoints =>
            _vimHost.GetSelectedSpans(_textView).Select(x => x.CaretPoint.Position).ToArray();

        private VirtualSnapshotPoint[] CaretVirtualPoints =>
            _vimHost.GetSelectedSpans(_textView).Select(x => x.CaretPoint).ToArray();

        private SelectedSpan[] SelectedSpans =>
            _vimHost.GetSelectedSpans(_textView).ToArray();

        private void SetCaretPoints(params VirtualSnapshotPoint[] caretPoints)
        {
            _vimHost.SetSelectedSpans(_textView, caretPoints.Select(x => new SelectedSpan(x)));
        }

        private void AssertCarets(params VirtualSnapshotPoint[] expectedCarets)
        {
            AssertSelections(expectedCarets.Select(x => new SelectedSpan(x)).ToArray());
        }

        private void AssertSelections(params SelectedSpan[] expectedSpans)
        {
            Assert.Equal(expectedSpans, SelectedSpans);
        }

        private void AssertSelectionsAdjustCaret(params SelectedSpan[] expectedSpans)
        {
            if (!_globalSettings.IsSelectionInclusive)
            {
                AssertSelections(expectedSpans);
                return;
            }
            var adjustedExpectedSpans =
                expectedSpans.Select(x => x.AdjustCaretForInclusive())
                .ToArray();
            Assert.Equal(adjustedExpectedSpans, SelectedSpans);
        }

        private void AssertSelectionsAdjustEnd(params SelectedSpan[] expectedSpans)
        {
            if (!_globalSettings.IsSelectionInclusive)
            {
                AssertSelections(expectedSpans);
                return;
            }
            var adjustedExpectedSpans =
                expectedSpans.Select(x => x.AdjustEndForInclusive())
                .ToArray();
            Assert.Equal(adjustedExpectedSpans, SelectedSpans);
        }

        private void AssertLines(params string[] lines)
        {
            Assert.Equal(lines, _textBuffer.GetLines().ToArray());
        }

        public sealed class MockTest : MultiSelectionIntegrationTest
        {
            /// <summary>
            /// Mock inftrastructure should use the real text view for the
            /// primary selection and the internal data structure for the
            /// secondary selection
            /// </summary>
            [WpfFact]
            public void Basic()
            {
                Create("cat", "bat", "");
                SetCaretPoints(
                    _textView.GetVirtualPointInLine(0, 1),
                    _textView.GetVirtualPointInLine(1, 1));

                // Verify real caret and real selection.
                Assert.Equal(
                    _textView.GetVirtualPointInLine(0, 1),
                    _textView.GetCaretVirtualPoint());
                Assert.Equal(
                    new VirtualSnapshotSpan(new SnapshotSpan(_textView.GetPointInLine(0, 1), 0)),
                    _textView.GetVirtualSelectionSpan());

                // Verify secondary selection agrees with mock vim host.
                Assert.Single(_mockVimHost.SecondarySelectedSpans);
                Assert.Equal(
                    new SelectedSpan(_textView.GetVirtualPointInLine(1, 1)),
                    _mockVimHost.SecondarySelectedSpans[0]);
            }
        }

        public sealed class MultiSelectionTrackerTest : MultiSelectionIntegrationTest
        {
            [WpfFact]
            public void RestoreCarets()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                _globalSettings.StartOfLine = false;
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation(":1<CR>");
                AssertCarets(GetPoint(0, 4), GetPoint(1, 4));
            }

            [WpfFact]
            public void MoveCarets()
            {
                Create("abc def ghi", "jkl mno pqr", "stu vwx yz.", "");
                _globalSettings.StartOfLine = false;
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation(":2<CR>");
                AssertCarets(GetPoint(1, 4), GetPoint(2, 4));
            }
        }

        public sealed class AddCaretTest : MultiSelectionIntegrationTest
        {
            /// <summary>
            /// Using alt-click adds a new caret
            /// </summary>
            [WpfFact]
            public void MouseClick()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                _textView.Caret.MoveTo(GetPoint(0, 4));
                _testableMouseDevice.Point = GetPoint(1, 8).Position; // 'e' in 'def'
                ProcessNotation("<A-LeftMouse><A-LeftRelease>");
                AssertCarets(GetPoint(0, 4), GetPoint(1, 8));
            }

            /// <summary>
            /// Using ctrl-alt-arrow adds a new caret
            /// </summary>
            [WpfFact]
            public void AddOnLineAbove()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                _textView.Caret.MoveTo(GetPoint(1, 4));
                ProcessNotation("<C-A-Up>");
                AssertCarets(GetPoint(1, 4), GetPoint(0, 4));
            }

            /// <summary>
            /// Using ctrl-alt-arrow adds a new caret
            /// </summary>
            [WpfFact]
            public void AddOnLineBelow()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                _textView.Caret.MoveTo(GetPoint(0, 4));
                ProcessNotation("<C-A-Down>");
                AssertCarets(GetPoint(0, 4), GetPoint(1, 4));
            }

            /// <summary>
            /// Using ctrl-alt-arrow adds a new caret
            /// </summary>
            [WpfFact]
            public void AddTwoAboveAndBelow()
            {
                Create(
                    "abc def ghi",
                    "jkl mno pqr",
                    "abc def ghi",
                    "jkl mno pqr",
                    "abc def ghi",
                    "jkl mno pqr",
                    "");
                _textView.Caret.MoveTo(GetPoint(2, 4));
                ProcessNotation("<C-A-Up><C-A-Up><C-A-Down><C-A-Down>");
                AssertCarets(
                    GetPoint(2, 4),
                    GetPoint(0, 4),
                    GetPoint(1, 4),
                    GetPoint(3, 4),
                    GetPoint(4, 4));
            }
        }

        public sealed class NormalModeTest : MultiSelectionIntegrationTest
        {
            /// <summary>
            /// Test moving the caret
            /// </summary>
            [WpfFact]
            public void Motion()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("w");
                AssertCarets(GetPoint(0, 8), GetPoint(1, 8));
            }

            /// <summary>
            /// Test inserting text
            /// </summary>
            [WpfFact]
            public void Insert()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("ixxx <Esc>");
                AssertLines("abc xxx def ghi", "jkl xxx mno pqr", "");
                AssertCarets(GetPoint(0, 7), GetPoint(1, 7));
            }

            /// <summary>
            /// Test undoing inserting text
            /// </summary>
            [WpfFact]
            public void UndoInsert()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("ixxx <Esc>");
                AssertLines("abc xxx def ghi", "jkl xxx mno pqr", "");
                AssertCarets(GetPoint(0, 7), GetPoint(1, 7));
                ProcessNotation("u");
                AssertLines("abc def ghi", "jkl mno pqr", "");
                AssertCarets(GetPoint(0, 4), GetPoint(1, 4));
            }

            /// <summary>
            /// Test repeating inserting text
            /// </summary>
            [WpfFact]
            public void RepeatInsert()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("ixxx <Esc>ww.");
                AssertLines("abc xxx def xxx ghi", "jkl xxx mno xxx pqr", "");
                AssertCarets(GetPoint(0, 15), GetPoint(1, 15));
            }

            /// <summary>
            /// Test deleting the word at the caret
            /// </summary>
            [WpfFact]
            public void Delete()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("dw");
                AssertLines("abc ghi", "jkl pqr", "");
                AssertCarets(GetPoint(0, 4), GetPoint(1, 4));
            }

            /// <summary>
            /// Test changing the word at the caret
            /// </summary>
            [WpfFact]
            public void Change()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("cwxxx<Esc>");
                AssertLines("abc xxx ghi", "jkl xxx pqr", "");
                AssertCarets(GetPoint(0, 6), GetPoint(1, 6));
            }

            /// <summary>
            /// Test putting before word at the caret
            /// </summary>
            [WpfFact]
            public void Put()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                ProcessNotation("yw");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("wP");
                AssertLines("abc def abc ghi", "jkl mno abc pqr", "");
                AssertCarets(GetPoint(0, 11), GetPoint(1, 11));
            }

            /// <summary>
            /// Test deleting and putting the word at the caret
            /// </summary>
            [WpfTheory, InlineData(""), InlineData("unnamed")]
            public void DeleteAndPut(string clipboardSetting)
            {
                Create("abc def ghi jkl", "mno pqr stu vwx", "");
                _globalSettings.Clipboard = clipboardSetting;
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("dwwP");
                AssertLines("abc ghi def jkl", "mno stu pqr vwx", "");
                AssertCarets(GetPoint(0, 11), GetPoint(1, 11));
            }
        }

        public sealed class VisualModeTest : MultiSelectionIntegrationTest
        {
            private void Create(bool isInclusive, params string[] lines)
            {
                Create(lines);
                _globalSettings.Selection = isInclusive ? "inclusive" : "exclusive";
            }

            /// <summary>
            /// Test moving the caret forward
            /// </summary>
            [WpfTheory, InlineData(false), InlineData(true)]
            public void MotionForward(bool isInclusive)
            {
                Create(isInclusive, "abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("vw");
                AssertSelectionsAdjustEnd(
                    GetPoint(0, 8).GetSelectedSpan(-4, 0, false), // 'def |'
                    GetPoint(1, 8).GetSelectedSpan(-4, 0, false)); // 'mno |'
            }

            /// <summary>
            /// Test moving the caret backward
            /// </summary>
            [WpfTheory, InlineData(false), InlineData(true)]
            public void MotionBackward(bool isInclusive)
            {
                Create(isInclusive, "abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 8), GetPoint(1, 8));
                ProcessNotation("vb");
                AssertSelectionsAdjustEnd(
                    GetPoint(0, 4).GetSelectedSpan(0, 4, true), // '|def '
                    GetPoint(1, 4).GetSelectedSpan(0, 4, true)); // '|mno '
            }

            /// <summary>
            /// Motion through zero width
            /// </summary>
            [WpfTheory, InlineData(false), InlineData(true)]
            public void MotionThroughZeroWidth(bool isInclusive)
            {
                Create(isInclusive, "abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("vwbb");
                AssertSelectionsAdjustEnd(
                    GetPoint(0, 0).GetSelectedSpan(0, 4, true), // '|abc '
                    GetPoint(1, 0).GetSelectedSpan(0, 4, true)); // '|jkl '
            }

            /// <summary>
            /// Test deleting text
            /// </summary>
            [WpfTheory, InlineData(false), InlineData(true)]
            public void Delete(bool isInclusive)
            {
                Create(isInclusive, "abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("veld");
                AssertLines("abc ghi", "jkl pqr", "");
                AssertCarets(GetPoint(0, 4), GetPoint(1, 4));
            }
        }

        public sealed class SelectModeTest : MultiSelectionIntegrationTest
        {
            protected override void Create(params string[] lines)
            {
                base.Create(lines);
                _globalSettings.SelectModeOptions =
                    SelectModeOptions.Mouse | SelectModeOptions.Keyboard;
                _globalSettings.KeyModelOptions = KeyModelOptions.StartSelection;
                _globalSettings.Selection = "exclusive";
            }

            /// <summary>
            /// Test entering select mode
            /// </summary>
            [WpfFact]
            public void Enter()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 0), GetPoint(1, 0));
                ProcessNotation("<S-Right>");
                AssertSelections(
                    GetPoint(0, 1).GetSelectedSpan(-1, 0, false), // 'a|'
                    GetPoint(1, 1).GetSelectedSpan(-1, 0, false)); // 'j|'
            }

            /// <summary>
            /// Test extending the selection forward
            /// </summary>
            [WpfFact]
            public void ReplaceSelection()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("gh<C-S-Right>xxx ");
                AssertLines("abc xxx ghi", "jkl xxx pqr", "");
                AssertCarets(GetPoint(0, 8), GetPoint(1, 8));
            }

            /// <summary>
            /// Test extending the selection forward
            /// </summary>
            [WpfFact]
            public void ExtendForward()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("gh<C-S-Right>");
                AssertSelections(
                    GetPoint(0, 8).GetSelectedSpan(-4, 0, false), // 'def |'
                    GetPoint(1, 8).GetSelectedSpan(-4, 0, false)); // 'mno |'
            }

            /// <summary>
            /// Test extending the selection backward
            /// </summary>
            [WpfFact]
            public void ExtendBackward()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 8), GetPoint(1, 8));
                ProcessNotation("gh<C-S-Left>");
                AssertSelections(
                    GetPoint(0, 4).GetSelectedSpan(0, 4, true), // '|def '
                    GetPoint(1, 4).GetSelectedSpan(0, 4, true)); // '|mno '
            }

            /// <summary>
            /// Test extending the selection through zero width
            /// </summary>
            [WpfFact]
            public void ExtendThroughZeroWidth()
            {
                Create("abc def ghi", "jkl mno pqr", "");
                SetCaretPoints(GetPoint(0, 4), GetPoint(1, 4));
                ProcessNotation("gh<C-S-Right><C-S-Left><C-S-Left>");
                AssertSelections(
                    GetPoint(0, 0).GetSelectedSpan(0, 4, true), // 'abc |'
                    GetPoint(1, 0).GetSelectedSpan(0, 4, true)); // 'jkl |'
            }

            /// <summary>
            /// Using alt-double-click should add a word to the selection
            /// </summary>
            [WpfFact]
            public void ExclusiveDoubleClick()
            {
                Create("abc def ghi jkl", "mno pqr stu vwx", "");
                _globalSettings.Selection = "exclusive";

                _testableMouseDevice.Point = GetPoint(0, 5).Position; // 'e' in 'def'
                ProcessNotation("<LeftMouse><LeftRelease><2-LeftMouse><LeftRelease>");
                Assert.Equal(ModeKind.SelectCharacter, _vimBuffer.ModeKind);
                AssertSelections(GetPoint(0, 7).GetSelectedSpan(-3, 0, false)); // 'def|'

                _testableMouseDevice.Point = GetPoint(1, 9).Position; // 't' in 'stu'
                ProcessNotation("<A-LeftMouse><A-LeftRelease><A-2-LeftMouse><LeftRelease>");
                Assert.Equal(ModeKind.SelectCharacter, _vimBuffer.ModeKind);
                AssertSelections(
                    GetPoint(0, 7).GetSelectedSpan(-3, 0, false), // 'def|'
                    GetPoint(1, 11).GetSelectedSpan(-3, 0, false)); // 'stu|'
            }

            /// <summary>
            /// Using alt-double-click should add a word to the selection
            /// </summary>
            [WpfFact]
            public void InclusiveDoubleClick()
            {
                Create("abc def ghi jkl", "mno pqr stu vwx", "");
                _globalSettings.Selection = "inclusive";

                _testableMouseDevice.Point = GetPoint(0, 5).Position; // 'e' in 'def'
                ProcessNotation("<LeftMouse><LeftRelease><2-LeftMouse><LeftRelease>");
                Assert.Equal(ModeKind.SelectCharacter, _vimBuffer.ModeKind);
                AssertSelections(GetPoint(0, 6).GetSelectedSpan(-2, 1, false)); // 'def|'

                _testableMouseDevice.Point = GetPoint(1, 9).Position; // 't' in 'stu'
                ProcessNotation("<A-LeftMouse><A-LeftRelease><A-2-LeftMouse><LeftRelease>");
                Assert.Equal(ModeKind.SelectCharacter, _vimBuffer.ModeKind);
                AssertSelections(
                    GetPoint(0, 6).GetSelectedSpan(-2, 1, false), // 'de|f'
                    GetPoint(1, 10).GetSelectedSpan(-2, 1, false)); // 'st|u'
            }
        }
    }
}