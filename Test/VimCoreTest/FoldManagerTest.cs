using Vim.EditorHost;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Outlining;
using Moq;
using Vim.Extensions;
using Xunit;

namespace Vim.UnitTest
{
#if !VS_SPECIFIC_2022 // fold manager in test
    /// <summary>
    /// Tests for the IFoldManager implementation.  The Vim behavior for folds, especial that of 
    /// nested folds, is largely undocumented and this class serves to test the implementation 
    /// we've inferred via behavior checks
    ///
    /// This test eschews any mocks because the behavior of both Vim and several editor types, 
    /// IOutuliningManager, in particular are not documented well.  Their behavior is the documentation
    /// and it's important that it's correct
    /// </summary>
    public sealed class FoldManagerTest : VimTestBase
    {
        private ITextView _textView;
        private ITextBuffer _textBuffer;
        private IFoldData _foldData;
        private FoldManager _foldManagerRaw;
        private IFoldManager _foldManager;
        private Mock<IStatusUtil> _statusUtil;
        private ITextBuffer _visualBuffer;
        private IAdhocOutliner _adhocOutliner;
        private IOutliningManager _outliningeManager;

        private void Create(params string[] lines)
        {
            _textView = CreateTextView(lines);
            _textBuffer = _textView.TextBuffer;
            _visualBuffer = _textView.TextViewModel.VisualBuffer;
            _adhocOutliner = TaggerUtil.GetOrCreateOutliner(_textView.TextBuffer);
            _outliningeManager = OutliningManagerService.GetOutliningManager(_textView);
            _statusUtil = new Mock<IStatusUtil>(MockBehavior.Strict);
            _foldData = FoldManagerFactory.GetFoldData(_textView.TextBuffer);
            _foldManagerRaw = new FoldManager(
                _textView,
                _foldData,
                _statusUtil.Object,
                FSharpOption.Create(OutliningManagerService.GetOutliningManager(_textView)));
            _foldManager = _foldManagerRaw;
        }

        /// <summary>
        /// Creating a new fold in the ITextView should automatically close it
        /// </summary>
        [WpfFact]
        public void CreateFold_ShouldClose()
        {
            Create("cat", "dog", "bear", "fish");
            _foldManager.CreateFold(_textBuffer.GetLineRange(1, 2));
            Assert.Equal(3, _visualBuffer.CurrentSnapshot.LineCount);
            Assert.Equal("cat", _visualBuffer.GetLine(0).GetText());
            Assert.Equal("fish", _visualBuffer.GetLine(2).GetText());
        }

        /// <summary>
        /// Creating a fold with a range of 1 line should have no affect.  Vim doesn't supporting
        /// folding of 1 line as it makes no sense since it only supports line based folds
        /// </summary>
        [WpfFact]
        public void CreateFold_OneLine()
        {
            Create("cat", "dog", "bear");
            _foldManager.CreateFold(_textBuffer.GetLineRange(0, 0));
            Assert.Equal(3, _visualBuffer.CurrentSnapshot.LineCount);
        }

        /// <summary>
        /// When dealing with a non-vim outline which does not stretch the span of the line, we should 
        /// still be able to open it from any point on the line
        /// </summary>
        [WpfFact]
        public void OpenFold_AdhocPartialLine()
        {
            Create("cat dog", "fish tree");
            _adhocOutliner.CreateOutliningRegion(_textBuffer.GetLineSpan(0, 3, 4), SpanTrackingMode.EdgeInclusive, "", "");
            _outliningeManager.CollapseAll(_textBuffer.GetLine(0).ExtentIncludingLineBreak, _ => true);
            Assert.Equal("cat", _visualBuffer.GetLine(0).GetText());
            _foldManager.OpenFold(_textView.GetPoint(0), 1);
            Assert.Equal("cat dog", _visualBuffer.GetLine(0).GetText());
        }

        /// <summary>
        /// When there are multiple collapsed folds the first fold should be preferred when opening
        /// </summary>
        [WpfFact]
        public void OpenFold_PreferFirstFold()
        {
            Create("dog", "cat", "fish", "bear", "tree", "pig");
            _foldManager.CreateFold(_textBuffer.GetLineRange(2, 3));
            _foldManager.CreateFold(_textBuffer.GetLineRange(1, 4));
            Assert.Equal(3, _visualBuffer.CurrentSnapshot.LineCount);
            _foldManager.OpenFold(_textBuffer.GetLine(2).Start, 1);
            Assert.Equal("cat", _visualBuffer.GetLine(1).GetText());
            _foldManager.OpenFold(_textBuffer.GetLine(2).Start, 1);
            Assert.Equal("fish", _visualBuffer.GetLine(2).GetText());
        }

        /// <summary>
        /// When there are multiple collapsed folds the first fold should be preferred when toggling
        /// </summary>
        [WpfFact]
        public void ToggleFold_PreferFirstFold()
        {
            Create("dog", "cat", "fish", "bear", "tree", "pig");
            _foldManager.CreateFold(_textBuffer.GetLineRange(2, 3));
            _foldManager.CreateFold(_textBuffer.GetLineRange(1, 4));
            Assert.Equal(3, _visualBuffer.CurrentSnapshot.LineCount);
            _foldManager.ToggleFold(_textBuffer.GetLine(2).Start, 1);
            Assert.Equal("cat", _visualBuffer.GetLine(1).GetText());
            _foldManager.ToggleFold(_textBuffer.GetLine(2).Start, 1);
            Assert.Equal("fish", _visualBuffer.GetLine(2).GetText());
        }

        /// <summary>
        /// When there is an open fold
        /// </summary>
        [WpfFact]
        public void ToggleFold_CloseFold()
        {
            Create("cat", "dog", "bear", "fish");
            _foldManager.CreateFold(_textBuffer.GetLineRange(1, 2));
            Assert.Equal(3, _visualBuffer.CurrentSnapshot.LineCount);
            Assert.Equal("cat", _visualBuffer.GetLine(0).GetText());
            Assert.Equal("fish", _visualBuffer.GetLine(2).GetText());
            _foldManager.OpenFold(_textBuffer.GetLine(1).Start, 1);
            Assert.Equal("cat", _visualBuffer.GetLine(0).GetText());
            Assert.Equal("fish", _visualBuffer.GetLine(3).GetText());
            _foldManager.ToggleFold(_textBuffer.GetLine(1).Start, 1);
            Assert.Equal("cat", _visualBuffer.GetLine(0).GetText());
            Assert.Equal("fish", _visualBuffer.GetLine(2).GetText());
        }

        /// <summary>
        /// When there is a closed fold
        /// </summary>
        [WpfFact]
        public void ToggleFold_OpenFold()
        {
            Create("cat", "dog", "bear", "fish");
            _foldManager.CreateFold(_textBuffer.GetLineRange(1, 2));
            Assert.Equal(3, _visualBuffer.CurrentSnapshot.LineCount);
            Assert.Equal("cat", _visualBuffer.GetLine(0).GetText());
            Assert.Equal("fish", _visualBuffer.GetLine(2).GetText());
            _foldManager.ToggleFold(_textBuffer.GetLine(1).Start, 1);
            Assert.Equal("cat", _visualBuffer.GetLine(0).GetText());
            Assert.Equal("fish", _visualBuffer.GetLine(3).GetText());
        }

        /// <summary>
        /// When dealing with a non-vim outline which does not stretch the span of the line, we should 
        /// still be able to close it from any point on the line
        /// </summary>
        [WpfFact]
        public void CloseFold_AdhocPartialLine()
        {
            Create("cat dog", "fish tree");
            _adhocOutliner.CreateOutliningRegion(_textBuffer.GetLineSpan(0, 3, 4), SpanTrackingMode.EdgeInclusive, "", "");
            _foldManager.CloseFold(_textView.GetPoint(0), 1);
            Assert.Equal("cat", _visualBuffer.GetLine(0).GetText());
        }
    }
#endif
}
