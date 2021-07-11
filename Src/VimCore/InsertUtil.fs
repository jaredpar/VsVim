
namespace Vim
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Operations
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Text.Outlining


/// Gets information on the type of backspace that needs to be performed at 
/// a given point
[<RequireQualifiedAccess>]
[<NoComparison>]
[<StructuralEquality>]
type internal BackspaceCommand = 

    /// The command is invalid
    | None

    /// Backspace over the set number of characters
    | Characters of Count: int

    /// Replace the number of characters with the specified string
    | Replace of Count: int * ReplaceText: string

/// This type houses the functionality for many of the insert mode commands
type internal InsertUtil
    (
        _vimBufferData: IVimBufferData,
        _motionUtil: IMotionUtil,
        _operations: ICommonOperations
    ) =

    let _textView = _vimBufferData.TextView
    let _textBuffer = _textView.TextBuffer
    let _localSettings = _vimBufferData.LocalSettings
    let _globalSettings = _localSettings.GlobalSettings
    let _undoRedoOperations = _vimBufferData.UndoRedoOperations
    let _editorOperations = _operations.EditorOperations
    let _editorOptions = _textView.Options
    let _wordUtil = _vimBufferData.WordUtil
    let _vimHost = _vimBufferData.Vim.VimHost
    let _vimTextBuffer = _vimBufferData.VimTextBuffer
    let _registermap = _vimBufferData.Vim.RegisterMap
    let mutable _combinedEditStartPoint: ITrackingPoint option = None
    let mutable _replaceUndoLine: ITextSnapshotLine option = None

    /// The SnapshotPoint for the caret
    member x.CaretPoint = TextViewUtil.GetCaretPoint _textView

    /// The VirtualSnapshotPoint for the caret
    member x.CaretVirtualPoint = TextViewUtil.GetCaretVirtualPoint _textView

    /// The column of the caret
    member x.CaretColumn = SnapshotColumn(x.CaretPoint)

    /// The ITextSnapshotLine for the caret
    member x.CaretLine = TextViewUtil.GetCaretLine _textView

    /// The line number for the caret
    member x.CaretLineNumber = x.CaretLine.LineNumber

    /// The SnapshotLineRange for the caret line
    member x.CaretLineRange = x.CaretLine |> SnapshotLineRangeUtil.CreateForLine

    /// The SnapshotPoint and ITextSnapshotLine for the caret
    member x.CaretPointAndLine = TextViewUtil.GetCaretPointAndLine _textView

    /// The current ITextSnapshot instance for the ITextBuffer
    member x.CurrentSnapshot = _textBuffer.CurrentSnapshot

    /// Reset the record of the current line
    member x.ClearReplaceUndoLine () =
        _replaceUndoLine <- None

    /// Record snapshot of line before any replacements
    member x.CheckRecordReplaceUndoLine () =
        let recordLine =
            match _replaceUndoLine with
            | None -> true
            | Some line -> line.LineNumber <> x.CaretLineNumber
        if recordLine then
            _replaceUndoLine <- SnapshotUtil.GetLine x.CurrentSnapshot x.CaretLineNumber |> Some

    /// Get recorded character for the current line at the specified column
    member x.TryGetRecordedCharacter (columnNumber: int) =
        match _replaceUndoLine with
            | None -> None
            | Some line ->
                if line.LineNumber <> x.CaretLineNumber then
                    None
                else
                    match SnapshotColumn.GetForColumnNumber(line, columnNumber, includeLineBreak = false) with
                    | None -> None
                    | Some column -> Some column.CodePoint

    /// Run the specified action with a wrapped undo transaction.  This is often necessary when
    /// an edit command manipulates the caret
    member x.EditWithUndoTransaction<'T> (name: string) (action: unit -> 'T): 'T = 
        _undoRedoOperations.EditWithUndoTransaction name _textView action

    /// Apply the TextChange to the given ITextEdit at the specified position.  This will
    /// return the position of the edit after the operation completes.  None is returned
    /// if the edit cannot be completed 
    member x.ApplyTextChangeCore (textEdit: ITextEdit) (position: int) (bounds: Span) (textChange: TextChange) addNewLines =

        let textChange = textChange.Reduce

        // Note that when doing edits in this method that all delete and insert need to be 
        // calculated against the original layout.  For example if you have insert "dog" 
        // followed by delete right 3, the delete right should occur from the original 
        // start position.  This is just how ITextEdit works 
        //
        // After a TextChange structure is reduced we should be able to break it down into
        // the 3 components that we care about: new text, delete left, delete right 
        // and perform those actions independently
        let insertText = ref ""
        let deleteLeft = ref 0
        let deleteRight = ref 0 
        let caretPosition = ref position

        let rec build textChange = 
            match textChange with
            | TextChange.Insert text ->
                let text = 
                    if addNewLines then
                        let newLine = _operations.GetNewLineText x.CaretPoint
                        newLine + text
                    else 
                        text
                insertText.Value <- insertText.Value + text
                caretPosition.Value <- caretPosition.Value + text.Length
            | TextChange.DeleteRight count -> 
                deleteRight.Value <- deleteRight.Value + count
            | TextChange.DeleteLeft count -> 
                deleteLeft.Value <- deleteLeft.Value + count
                caretPosition.Value <- caretPosition.Value - count
            | TextChange.Combination (left, right) ->
                build left
                build right
        build textChange

        let mutable error = false

        if not (StringUtil.IsNullOrEmpty insertText.Value) then
            if not (textEdit.Insert(position, insertText.Value)) then
                error <- true

        if deleteRight.Value > 0 then

            // Delete 'count * deleteCount' more characters.  Make sure that we don't
            // go off the end of the specified bounds
            let maxCount = bounds.End - position
            let realCount = min maxCount deleteRight.Value
            if not (textEdit.Delete(position, realCount)) then
                error <- true


        if deleteLeft.Value > 0 then
            // Delete 'count' characters to the left of the caret.  Make sure that
            // we don't delete outside the specified bounds
            let start, count = 
                let diff = position - deleteLeft.Value
                if diff < bounds.Start then
                    bounds.Start, position - bounds.Start
                else
                    diff, deleteLeft.Value

            if not (textEdit.Delete(start, count)) then
                error <- true

        if error then
            None
        else
            Some caretPosition.Value

    /// Apply the TextChange to the ITextBuffer 'count' times as a single operation.
    member x.ApplyTextChange textChange addNewLines =

        use textEdit = _textBuffer.CreateEdit()
        let bounds = Span(0, x.CurrentSnapshot.Length)
        match x.ApplyTextChangeCore textEdit x.CaretPoint.Position bounds textChange addNewLines with
        | Some position -> 
            let snapshot = TextEditUtil.ApplyAndGetLatest textEdit
            TextViewUtil.MoveCaretToPosition _textView position
        | None -> textEdit.Cancel()

    /// Apply the given TextChange to the specified BlockSpan
    member x.ApplyBlockInsert (insertCommand: InsertCommand) padShortLines atEndOfLine startLineNumber spaces height =
        let tabStop = _localSettings.TabStop

        // Don't edit past the end of the ITextBuffer.
        let height =
            let maxHeight = x.CurrentSnapshot.LineCount - startLineNumber
            min maxHeight height

        // It is possible that a repeat of a block edit will begin part of the
        // way through a wide character (think a 4 space tab).  If any edits
        // have this behavior the first pass will break them up into the
        // appropriate number of spaces.
        let fixOverlapEdits () =
            use textEdit = _textBuffer.CreateEdit()
            let currentSnapshot = x.CurrentSnapshot
            for i = 0 to height - 1 do

                let lineNumber = startLineNumber + i
                let line = SnapshotUtil.GetLine currentSnapshot lineNumber
                let column = SnapshotOverlapColumn.GetColumnForSpacesOrEnd(line, spaces, tabStop)
                if column.SpacesBefore > 0 && column.Column.IsCharacter '\t' then
                    let text = StringUtil.RepeatChar column.TotalSpaces ' '
                    let span = column.Column.Span
                    textEdit.Replace(span.Span, text) |> ignore
                elif padShortLines && column.Column.StartPoint = line.End then
                    let endSpaces = SnapshotColumn(line.End).GetSpacesToColumn(tabStop)
                    let padding = spaces - endSpaces
                    if padding > 0 then
                        let text = StringUtil.RepeatChar padding ' '
                        textEdit.Insert(line.End.Position, text) |> ignore

            if textEdit.HasEffectiveChanges then
                textEdit.Apply() |> ignore
            else
                textEdit.Cancel()

        // Actually apply the edit to the lines in the block selection.
        let doApply () =
            for i = 0 to height - 1 do

                let currentSnapshot = x.CurrentSnapshot
                let lineNumber = startLineNumber + i
                let currentLine = SnapshotUtil.GetLine currentSnapshot lineNumber

                // Only apply the edit to lines which were included in the
                // original selection.
                let column =
                    if atEndOfLine then SnapshotColumn.GetLineEnd(currentLine) |> Some
                    else SnapshotColumn.GetColumnForSpaces(currentLine, spaces, tabStop)
                match column with
                | Some column ->
                    TextViewUtil.MoveCaretToPoint _textView column.StartPoint
                    x.RunInsertCommand insertCommand |> ignore
                | None -> ()

        if not atEndOfLine then
            fixOverlapEdits()
        doApply()

    /// Delete the character before the cursor
    member x.Back () =
        x.RunBackspacingCommand InsertCommand.Back

    /// Block insert the specified text at the caret point over the specified
    /// number of lines
    member x.BlockInsert text padShortLines atEndOfLine height =
        let column = x.CaretColumn
        let spaces = column.GetSpacesToColumn _localSettings.TabStop
        x.ApplyBlockInsert text padShortLines atEndOfLine column.LineNumber spaces height
        CommandResult.Completed ModeSwitch.NoSwitch

    member x.Combined left right =
        let clear = 
            if Option.isNone _combinedEditStartPoint then
                _combinedEditStartPoint <- x.CurrentSnapshot.CreateTrackingPoint(x.CaretPoint.Position, PointTrackingMode.Negative) |> Some
                true
            else
                false
        try
            x.RunInsertCommand left |> ignore
            x.RunInsertCommand right
        finally
            if clear then
                _combinedEditStartPoint <- None

    /// Complete the insert mode session.
    member x.CompleteMode moveCaretLeft = 
        x.ClearReplaceUndoLine()

        // If the caret is in virtual space we move regardless of the flag.
        let virtualPoint = TextViewUtil.GetCaretVirtualPoint _textView
        if virtualPoint.IsInVirtualSpace then 
            _operations.MoveCaretToPoint virtualPoint.Position ViewFlags.None
            CommandResult.Completed ModeSwitch.NoSwitch
        elif moveCaretLeft && not (SnapshotPointUtil.IsStartOfLine x.CaretPoint) then
            x.MoveCaret Direction.Left
        else
            CommandResult.Completed ModeSwitch.NoSwitch

    /// Delete the character under the cursor
    member x.Delete () = 
        _editorOperations.Delete() |> ignore
        CommandResult.Completed ModeSwitch.NoSwitch

    /// Delete count characters to the left of the caret
    member x.DeleteLeft count = 
        let span = 
            let diff = x.CaretPoint.Position - count
            if diff < 0 then
                let count = diff + count
                Span(0, count)
            else
                Span(diff, count)

        _textBuffer.Delete(span) |> ignore
        CommandResult.Completed ModeSwitch.NoSwitch

    /// Delete count characters to the right of the caret
    member x.DeleteRight count = 
        let start = x.CaretPoint.Position
        let maxCount = x.CurrentSnapshot.Length - start
        let realCount = min maxCount count
        let span = Span(start, realCount)
        _textBuffer.Delete(span) |> ignore
        CommandResult.Completed ModeSwitch.NoSwitch

    /// Delete all of the indentation on the current line.  This should not affect caret
    /// position
    member x.DeleteAllIndent () =

        let indentSpan = 
            let endPoint = SnapshotLineUtil.GetFirstNonBlankOrEnd x.CaretLine
            SnapshotSpan(x.CaretLine.Start, endPoint)

        _textBuffer.Delete(indentSpan.Span) |> ignore

        CommandResult.Completed ModeSwitch.NoSwitch

    /// Delete the word before the cursor
    member x.DeleteWordBeforeCursor () =
        x.RunBackspacingCommand InsertCommand.DeleteWordBeforeCursor

    member x.Insert text =
        _operations.FillInVirtualSpace()
        if _editorOperations.InsertText(text) then
            if _localSettings.EndOfLine then

                // Ensure that the line containing the insertion point has a linebreak.
                let insertionPoint = x.CaretPoint
                let insertionLine = x.CaretLine
                if not (SnapshotLineUtil.HasLineBreak insertionLine) then
                    TextViewUtil.InsertFinalNewLine _textView
                    let point = SnapshotUtil.GetPoint _textBuffer.CurrentSnapshot insertionPoint.Position
                    TextViewUtil.MoveCaretToPoint _textView point

            CommandResult.Completed ModeSwitch.NoSwitch
        else
            CommandResult.Error

    /// Do a replacement under the caret of the specified char
    member x.Replace (c: char) =
        x.CheckRecordReplaceUndoLine()

        // Typically we have the overwrite option set for all of replace mode so this
        // is a redundant check.  But during repeat we only see the commands and not
        // the mode changes so we need to check here
        let oldValue = EditorOptionsUtil.GetOptionValueOrDefault _editorOptions DefaultTextViewOptions.OverwriteModeId true
        if not oldValue then
            EditorOptionsUtil.SetOptionValue _editorOptions DefaultTextViewOptions.OverwriteModeId true

        try
            let text = StringUtil.OfChar c
            x.Insert text
        finally 
            if not oldValue then
                EditorOptionsUtil.SetOptionValue _editorOptions DefaultTextViewOptions.OverwriteModeId false

    member x.Replace (s: string) =
        // overwrite does not seem to be respected, just using delete instead
        x.DeleteRight(s.Length - 1) |> ignore
        x.Insert s

    member x.InsertCharacterCore lineNumber isReplace =
        match SnapshotColumn.GetForLineAndColumnNumber(x.CurrentSnapshot, lineNumber, x.CaretColumn.ColumnNumber) with
        | None -> 
            _operations.Beep()
            CommandResult.Error
        | Some column -> 
            let text = column.GetText()
            let position = x.CaretPoint.Position
            if isReplace then x.DeleteRight 1 |> ignore
            _textBuffer.Insert(position, text) |> ignore
            TextViewUtil.MoveCaretToPosition _textView (position + column.Length)
            CommandResult.Completed ModeSwitch.NoSwitch

    /// Insert the character immediately above the caret
    member x.InsertCharacterAboveCaret() = 
        x.InsertCharacterCore (x.CaretLineNumber - 1) false

    /// Insert the character immediately below the caret
    member x.InsertCharacterBelowCaret() = 
        x.InsertCharacterCore (x.CaretLineNumber + 1) false

    /// Replace the character immediately above the caret
    member x.ReplaceCharacterAboveCaret() = 
        x.InsertCharacterCore (x.CaretLineNumber - 1) true

    /// Replace the character immediately below the caret
    member x.ReplaceCharacterBelowCaret() = 
        x.InsertCharacterCore (x.CaretLineNumber + 1) true

    /// Insert a new line into the ITextBuffer.  Make sure to indent the text
    member x.InsertNewLine() =
        let newLineText = _operations.GetNewLineText x.CaretPoint
        x.EditWithUndoTransaction "New Line" (fun () -> 
            let contextLine = x.CaretLine
            _textBuffer.Insert(x.CaretPoint.Position, newLineText) |> ignore

            // Now we need to position the caret within the new line
            let newLine = SnapshotUtil.GetLine x.CurrentSnapshot (contextLine.LineNumber + 1)
            match _operations.GetNewLineIndent contextLine newLine with
            | None -> ()
            | Some indent ->
                // Calling GetNewLineIndent can cause a buffer edit.  Need to rebind all of the
                // snapshot related items
                match SnapshotUtil.TryGetLine x.CurrentSnapshot newLine.LineNumber with
                | None -> ()
                | Some newLine ->
                    // If there is actual text on this new line (enter in the middle of the
                    // line) then we need to insert white space.  Else we just put the caret
                    // into virtual space
                    let indentText =
                        StringUtil.Repeat indent " "
                        |> (fun text -> _operations.NormalizeBlanks text 0)
                    if indentText.Length > 0 && newLine.Length > 0 then
                        _textBuffer.Insert(newLine.Start.Position, indentText) |> ignore
                        TextViewUtil.MoveCaretToPosition _textView (newLine.Start.Position + indentText.Length)
                    else
                        let virtualPoint = VirtualSnapshotPoint(newLine.Start, indent)
                        TextViewUtil.MoveCaretToVirtualPoint _textView virtualPoint)

        CommandResult.Completed ModeSwitch.NoSwitch

    /// Insert previously inserted text, optionally stopping insert
    member x.InsertPreviouslyInsertedText stopInsert =
        match _vimBufferData.Vim.VimData.LastTextInsert with
        | Some text ->
            _textBuffer.Insert(x.CaretPoint.Position, text) |> ignore
        | None ->
            ()

        if stopInsert then
            ModeSwitch.SwitchMode ModeKind.Normal
        else
            ModeSwitch.NoSwitch
        |> CommandResult.Completed 

    /// Insert a single tab into the ITextBuffer.  If 'expandtab' is enabled then insert
    /// the appropriate number of spaces
    ///
    /// This function specifically doesn't consider the 'backspace' option.  It is the job
    /// of the caller to see that this enforced 
    member x.InsertTab () =

        x.EditWithUndoTransaction "Insert Tab" (fun () -> 

            // First off convert any virtual spaces around the caret into actual spaces
            _operations.FillInVirtualSpace()

            // Stores the length in spaces of a logical tab 
            let indentSpaces = 
                if _localSettings.SoftTabStop <> 0 then 
                    _localSettings.SoftTabStop
                else
                    _localSettings.TabStop

            // Calculate how many spaces are being added because of this tab operation.  If the caret
            // is currently on an indent boundary then we add a full indent, otherwise we just move
            // out to the end of the current indent
            let addedSpaces = 
                let caretSpaces = _operations.GetSpacesToPoint x.CaretPoint
                let remainder = caretSpaces % indentSpaces
                indentSpaces - remainder

            let caretPosition = 
                if _localSettings.ExpandTab then
                    // When only spaces are being inserted we don't normalize away any tabs that exist before
                    // the caret.  Just insert the spaces
                    let caretPosition = x.CaretPoint.Position + addedSpaces
                    let text = StringUtil.RepeatChar addedSpaces ' '
                    _textBuffer.Insert(x.CaretPoint.Position, text) |> ignore
                    caretPosition
                 else
                    // When inserting tabs the spaces before the caret are actually normalized into spaces if
                    // we've hit a tab boundary which is defined by 'tabstop'
                    let insertColumn = 
                        let mutable column = x.CaretColumn
                        while column.ColumnNumber > 0 && column.Subtract(1).CodePoint.IsBlank do
                            column <- column.Subtract(1)
                        column

                    let existingRange = Span.FromBounds(insertColumn.StartPoint.Position, x.CaretPoint.Position)

                    let text = 
                        let existingText = x.CurrentSnapshot.GetText(existingRange)
                        let indentText = StringUtil.RepeatChar addedSpaces ' ' 
                        _operations.NormalizeBlanksAtColumn (existingText + indentText) insertColumn

                    let caretPosition = insertColumn.StartPoint.Position + text.Length
                    _textBuffer.Replace(existingRange, text) |> ignore
                    caretPosition

            // Move the caret to the end of the insertion
            let point = SnapshotPoint(x.CurrentSnapshot, caretPosition)
            _operations.MoveCaretToPoint point ViewFlags.None)

        CommandResult.Completed ModeSwitch.NoSwitch

    member x.MoveCaretToEndOfLine () =
        let line = TextViewUtil.GetCaretLine _textView
        _operations.MoveCaretToPoint line.End ViewFlags.None
        CommandResult.Completed ModeSwitch.NoSwitch

    /// Move the caret in the given direction
    member x.MoveCaret direction = 
        let caretMovement = CaretMovement.OfDirection direction
        if _operations.MoveCaret caretMovement then
            CommandResult.Completed ModeSwitch.NoSwitch
        else
            _operations.Beep()
            CommandResult.Error

    /// Move the caret in the given direction with an arrow key
    member x.MoveCaretWithArrow direction =
        let caretMovement = CaretMovement.OfDirection direction
        if _operations.MoveCaretWithArrow caretMovement then
            CommandResult.Completed ModeSwitch.NoSwitch
        else
            _operations.Beep()
            CommandResult.Error

    /// Move the card by word in the specified direction
    member x.MoveCaretByWord direction = 

        // Note: This could be simplified somewhat if we were to use
        // ICommonOperations.MoveCaretToMotionResult but then the final
        // caret position would depend on the current mode and we would
        // rather InsertUtil not be affected by the current mode.
        let doMotion wordMotion (spanToPoint: SnapshotSpan -> SnapshotPoint) =
            let argument = MotionArgument(MotionContext.Movement)
            match _motionUtil.GetMotion (wordMotion WordKind.NormalWord) argument with
            | Some motionResult ->
                let point = spanToPoint motionResult.Span
                let point = SnapshotPointUtil.GetPointOrEndPointOfLastLine point
                let viewFlags = ViewFlags.All &&& ~~~ViewFlags.VirtualEdit
                _operations.MoveCaretToPoint point viewFlags
                true
            | None ->
                false

        let success =
            match direction with
            | Direction.Left ->
                doMotion Motion.WordBackward (fun span -> span.Start)
            | Direction.Right ->
                doMotion Motion.WordForward (fun span -> span.End)
            | _ ->
                false

        if success then
            CommandResult.Completed ModeSwitch.NoSwitch
        else
            _operations.Beep()
            CommandResult.Error

    /// Repeat the given edit InsertCommand.  This is used at the exit of insert mode to
    /// apply the edits again and again
    member x.RepeatEdit textChange addNewLines count = 

        // Create a transaction so the textChange is applied as a single edit and to 
        // maintain caret position 
        _undoRedoOperations.EditWithUndoTransaction "Repeat Edits" _textView (fun () -> 

            for i = 1 to count do
                x.ApplyTextChange textChange addNewLines)

    /// Repeat the edits for the other lines in the block span.
    member x.RepeatBlock (insertCommand: InsertCommand) (visualInsertKind: VisualInsertKind) (blockSpan: BlockSpan) =

        // Unfortunately the ITextEdit implementation doesn't properly reduce the changes that are
        // applied to it.  For example if you add 2 characters, delete them and then insert text
        // at the original start point you will not end up with simply the new text.  
        //
        // To account for this we reduce the TextChange to the smallest possible TextChange value
        // and apply that change 
        //
        // Block edits don't apply if the user inserts a new line into the buffer.  Check for that
        // early on
        let isRepeatable = blockSpan.Snapshot.LineCount = x.CurrentSnapshot.LineCount && blockSpan.Height > 1 
        match isRepeatable, insertCommand.TextChange _editorOptions _textBuffer |> Option.map (fun textChange -> textChange.Reduce) with
        | true, Some textChange -> 
            match textChange with
            | TextChange.Insert text -> 
                x.EditWithUndoTransaction "Repeat Block Edit" (fun () ->
                    let startLineNumber = blockSpan.Start.LineNumber + 1
                    let padShortLines, atEndOfLine, spaces =
                        match visualInsertKind with
                        | VisualInsertKind.Start ->
                            false, false, blockSpan.BeforeSpaces
                        | VisualInsertKind.End ->
                            true, false, blockSpan.BeforeSpaces + blockSpan.SpacesLength
                        | VisualInsertKind.EndOfLine ->
                            false, true, blockSpan.BeforeSpaces
                    x.ApplyBlockInsert insertCommand padShortLines atEndOfLine startLineNumber spaces (blockSpan.Height - 1)

                    // insertion point which is the start of the BlockSpan.
                    _operations.MapPointNegativeToCurrentSnapshot blockSpan.Start.StartPoint
                    |> TextViewUtil.MoveCaretToPoint _textView
                    
                    Some text)
            | _ -> None
        | _ -> None

    /// Whether we should run the specified command for all carets
    member x.ShouldRunForEachCaret command =
        match command with
        | InsertCommand.Back -> true
        | InsertCommand.Combined _ -> true
        | InsertCommand.Delete -> true
        | InsertCommand.DeleteLeft _ -> true
        | InsertCommand.DeleteRight _ -> true
        | InsertCommand.DeleteAllIndent -> true
        | InsertCommand.DeleteWordBeforeCursor -> true
        | InsertCommand.InsertLiteral _ -> true
        | InsertCommand.InsertCharacterAboveCaret -> true
        | InsertCommand.InsertCharacterBelowCaret -> true
        | InsertCommand.InsertNewLine -> true
        | InsertCommand.InsertPreviouslyInsertedText _ -> true
        | InsertCommand.InsertTab -> true
        | InsertCommand.MoveCaret _ -> true
        | InsertCommand.MoveCaretWithArrow _ -> true
        | InsertCommand.MoveCaretByWord _ -> true
        | InsertCommand.MoveCaretToEndOfLine -> true
        | InsertCommand.Replace _ -> true
        | InsertCommand.ReplaceCharacterAboveCaret -> true
        | InsertCommand.ReplaceCharacterBelowCaret -> true
        | InsertCommand.Overwrite _ -> true
        | InsertCommand.ShiftLineLeft -> true
        | InsertCommand.ShiftLineRight -> true
        | InsertCommand.UndoReplace -> true
        | InsertCommand.DeleteLineBeforeCursor -> true
        | InsertCommand.Paste -> true
        | _ -> false

    member x.RunInsertCommand command = 
        if x.ShouldRunForEachCaret command then
            fun () -> x.RunInsertCommandCore command
            |> _operations.RunForAllSelections
        else
            x.RunInsertCommandCore command

    member x.RunInsertCommandCore command = 

        // Allow the host to custom process this message here.
        if _vimHost.TryCustomProcess _textView command then
            CommandResult.Completed ModeSwitch.NoSwitch
        else
            match command with
            | InsertCommand.Back -> x.Back()
            | InsertCommand.BlockInsert (insertCommand, padShortLines, atEndOfLine, count) -> x.BlockInsert insertCommand padShortLines atEndOfLine count
            | InsertCommand.Combined (left, right) -> x.Combined left right
            | InsertCommand.CompleteMode moveCaretLeft -> x.CompleteMode moveCaretLeft
            | InsertCommand.Delete -> x.Delete()
            | InsertCommand.DeleteLeft count -> x.DeleteLeft count
            | InsertCommand.DeleteRight count -> x.DeleteRight count
            | InsertCommand.DeleteAllIndent -> x.DeleteAllIndent() 
            | InsertCommand.DeleteWordBeforeCursor -> x.DeleteWordBeforeCursor()
            | InsertCommand.Insert text -> x.Insert text 
            | InsertCommand.InsertLiteral text -> x.Insert text 
            | InsertCommand.InsertCharacterAboveCaret -> x.InsertCharacterAboveCaret()
            | InsertCommand.InsertCharacterBelowCaret -> x.InsertCharacterBelowCaret()
            | InsertCommand.InsertNewLine -> x.InsertNewLine()
            | InsertCommand.InsertPreviouslyInsertedText stopInsert -> x.InsertPreviouslyInsertedText stopInsert
            | InsertCommand.InsertTab -> x.InsertTab()
            | InsertCommand.MoveCaret direction -> x.MoveCaret direction
            | InsertCommand.MoveCaretWithArrow direction -> x.MoveCaretWithArrow direction
            | InsertCommand.MoveCaretByWord direction -> x.MoveCaretByWord direction
            | InsertCommand.MoveCaretToEndOfLine -> x.MoveCaretToEndOfLine();
            | InsertCommand.Replace c -> x.Replace c
            | InsertCommand.ReplaceCharacterAboveCaret -> x.ReplaceCharacterAboveCaret()
            | InsertCommand.ReplaceCharacterBelowCaret -> x.ReplaceCharacterBelowCaret()
            | InsertCommand.Overwrite s -> x.Replace s
            | InsertCommand.ShiftLineLeft -> x.ShiftLineLeft ()
            | InsertCommand.ShiftLineRight -> x.ShiftLineRight ()
            | InsertCommand.UndoReplace -> x.UndoReplace ()
            | InsertCommand.DeleteLineBeforeCursor -> x.DeleteLineBeforeCursor()
            | InsertCommand.Paste -> x.Paste()
            | InsertCommand.LiteralPaste register -> x.LiteralPaste register

    /// Shift the caret line one 'shiftwidth' in a direction.  This is
    /// different than both normal and visual mode shifts because it will
    /// round the blanks to a 'shiftwidth' before indenting
    member x.ShiftLine (direction: int) =

        // Get the current indent and whether the line is blank
        let indentSpan = SnapshotLineUtil.GetIndentSpan x.CaretLine
        let isBlankLine = SnapshotLineUtil.IsBlankOrEmpty x.CaretLine

        // Convert spaces, tabs and virtual space to a column
        let column = 
            (_operations.NormalizeBlanksToSpaces (indentSpan.GetText()) 0).Length +
                if isBlankLine && x.CaretVirtualPoint.IsInVirtualSpace then
                    x.CaretVirtualPoint.VirtualSpaces
                else
                    0

        // Compute offset to the nearest multiple of 'shiftwidth'
        let offset =
            let remainder = column % _localSettings.ShiftWidth
            if remainder = 0 then
                direction * _localSettings.ShiftWidth
            else if direction = -1 then
                -remainder
            else
                _localSettings.ShiftWidth - remainder

        // Replace the current indent with a new indent
        let newColumn = max (column + offset) 0
        let spaces = StringUtil.RepeatChar newColumn ' '
        let indent = _operations.NormalizeBlanks spaces 0

        x.EditWithUndoTransaction "Shift line" (fun () ->
            _textBuffer.Replace(indentSpan.Span, indent) |> ignore

            // If the line is all spaces, move it to the end and
            // the caret will no longer be in virtual space
            if isBlankLine then
                TextViewUtil.MoveCaretToPoint _textView x.CaretLine.End)

        CommandResult.Completed ModeSwitch.NoSwitch

    /// Shift the caret line one 'shiftwidth' to the left
    member x.ShiftLineLeft () =
        x.ShiftLine -1

    /// Shift the caret line one 'shiftwidth' to the right
    member x.ShiftLineRight () =
        x.ShiftLine 1

    /// Undo replace, or equivalently, backspace in replace mode
    member x.UndoReplace () =
        if x.CaretColumn.ColumnNumber > 0 then
            let point = x.CaretPoint.Subtract(1)
            TextViewUtil.MoveCaretToPosition _textView point.Position
            match x.TryGetRecordedCharacter x.CaretColumn.ColumnNumber with
            | None -> ()
            | Some codePoint -> 
                let span = SnapshotSpan(x.CaretPoint, 1)
                let text = codePoint.GetText()
                _textBuffer.Replace(span.Span, text) |> ignore
                TextViewUtil.MoveCaretToPosition _textView point.Position
        else
            if _globalSettings.IsBackspaceEol && x.CaretLineNumber > 0 then
                let previousLineNumber = x.CaretLineNumber - 1
                let previousLine = SnapshotUtil.GetLine x.CurrentSnapshot previousLineNumber
                TextViewUtil.MoveCaretToPosition _textView previousLine.End.Position
            else
                _operations.Beep()
        CommandResult.Completed ModeSwitch.NoSwitch
        
    /// Delete the line before the cursor
    member x.DeleteLineBeforeCursor () =
        x.RunBackspacingCommand InsertCommand.DeleteLineBeforeCursor

    /// Paste clipboard
    member x.Paste () =
        _editorOperations.Paste() |> ignore
        CommandResult.Completed ModeSwitch.NoSwitch

    /// Paste clipboard
    member x.LiteralPaste register =
        let sometext =
            register |> Some
            |> OptionUtil.map2 RegisterName.OfChar
            |> Option.map _registermap.GetRegister
            |> OptionUtil.map2 (fun register ->
                let value = register.StringValue
                match value with
                | "" -> None
                | _ -> Some value)

        let registerContent =
            match sometext with
            | None -> ""
            | Some text-> text

        x.Insert (registerContent)

    member x.ExtraTextChange textChange addNewLines = 
        x.ApplyTextChange textChange addNewLines 
        CommandResult.Completed ModeSwitch.NoSwitch

    /// Run an insert command that backspaces over characters before the cursor
    member x.RunBackspacingCommand command =
        let backspaceCommand = x.GetBackspaceCommand command |> x.AdjustBackspaceForStartSetting
        match backspaceCommand with 
        | BackspaceCommand.None -> 
            _operations.Beep()
        | BackspaceCommand.Characters count ->
            let startPoint = x.CaretPoint.Subtract(count)
            let span = SnapshotSpan(startPoint, x.CaretPoint)
            _textBuffer.Delete(span.Span) |> ignore
            TextViewUtil.MoveCaretToPosition _textView span.Start.Position
        | BackspaceCommand.Replace (count, text) ->
            let startPoint = x.CaretPoint.Subtract(count)
            let span = SnapshotSpan(startPoint, x.CaretPoint)
            _textBuffer.Replace(span.Span, text) |> ignore
            TextViewUtil.MoveCaretToPosition _textView (span.Start.Position + text.Length)
        CommandResult.Completed ModeSwitch.NoSwitch

    /// Get the BackspaceCommand for the given InsertCommand at the caret point
    member x.GetBackspaceCommand insertCommand = 
        if x.CaretVirtualPoint.IsInVirtualSpace && SnapshotLineUtil.IsBlankOrEmpty x.CaretLine then
            // The 'backspace=indent' setting covers backspacing over autoindent which 
            // doesn't have a direct 1-1 mapping in VsVim because the host controls indent
            // not VsVim.  The closest equivalent is when the caret is in virtual space 
            // on a blank line.  
            if _globalSettings.IsBackspaceIndent then
                _operations.FillInVirtualSpace()
                x.GetBackspaceCommandNoIndent insertCommand
            else
                BackspaceCommand.None
        else
            _operations.FillInVirtualSpace()
            x.GetBackspaceCommandNoIndent insertCommand

    /// Get the BackspaceCommand for the given InsertCommand at the caret point.  This does not 
    /// consider the indent option in the 'backspace' setting.  It runs with the assumption that
    /// the caret is not in virtual space
    member x.GetBackspaceCommandNoIndent insertCommand = 
        Contract.Assert (x.CaretVirtualPoint.VirtualSpaces = 0)

        if x.CaretPoint = x.CaretLine.Start then
            // All of the delete commands when invoked at the start of the line will
            // cause the line break of the previous line to be deleted if the 
            // 'backspace' setting contains 'eol'
            if _globalSettings.IsBackspaceEol && x.CaretLineNumber > 0 then
                let previousLineNumber = x.CaretLineNumber - 1
                let previousLine = SnapshotUtil.GetLine x.CurrentSnapshot previousLineNumber
                BackspaceCommand.Characters previousLine.LineBreakLength 
            else
                BackspaceCommand.None
        else
            // Normal execution of a backspace command
            match insertCommand with
            | InsertCommand.Back -> x.BackspaceOverCharPoint()
            | InsertCommand.DeleteWordBeforeCursor -> x.BackspaceOverWordPoint()
            | InsertCommand.DeleteLineBeforeCursor -> x.BackspaceOverLinePoint()
            | _ -> BackspaceCommand.None

    /// Adjust the backspace command for the start option
    member x.AdjustBackspaceForStartSetting backspaceCommand = 

        // It is possible for InsertUtil to be used when vim is not currently in insert 
        // mode.  This happens during a repeat operation (.).  For operations such as that
        // the caret point is considered to be the start point.  It must be adjusted 
        // for edits which is tracked by _combinedEditStartPoint
        let insertStartPoint = 
            match _combinedEditStartPoint, _vimBufferData.VimTextBuffer.ModeKind with
            | Some editStartPoint, _ -> TrackingPointUtil.GetPoint x.CurrentSnapshot editStartPoint
            | None, ModeKind.Insert -> _vimTextBuffer.InsertStartPoint
            | None, ModeKind.Replace -> _vimTextBuffer.InsertStartPoint
            | None, _ -> Some x.CaretPoint

        match _globalSettings.IsBackspaceStart, insertStartPoint, backspaceCommand with
        | true, _, _ -> backspaceCommand
        | false, None, _ -> backspaceCommand
        | false, Some startPoint, BackspaceCommand.None -> backspaceCommand
        | false, Some startPoint, BackspaceCommand.Characters count -> 
            let maxCount = abs (x.CaretPoint.Position - startPoint.Position)
            let count = min maxCount count
            if count = 0 then
                BackspaceCommand.None
            else    
                BackspaceCommand.Characters count
        | false, Some startPoint, BackspaceCommand.Replace _ -> BackspaceCommand.None

    // A backspace over line (Ctrl-U)  and word (Ctrl-W) which begins to the right of the insert start
    // point has a max delete of the start point itself.  Do the minimization here
    member x.AdjustBackspaceDeletePointForStartPoint (deletePoint: SnapshotPoint) =
        match _vimBufferData.VimTextBuffer.InsertStartPoint with
        | None -> deletePoint
        | Some startPoint ->
            if x.CaretPoint.Position > startPoint.Position then 
                let position = max deletePoint.Position startPoint.Position
                SnapshotPoint(deletePoint.Snapshot, position)
            else 
                deletePoint

    /// The point we should backspace to in order to delete a character.  This will never 
    /// be called when the caret is at the start of the line
    member x.BackspaceOverCharPoint() =
        Contract.Assert (x.CaretColumn.ColumnNumber > 0)
        let prevPoint = x.CaretPoint.Subtract(1)
        if _localSettings.SoftTabStop <> 0 && SnapshotPointUtil.IsBlank prevPoint && _vimTextBuffer.IsSoftTabStopValidForBackspace then
            x.BackspaceOverIndent()
        else
            BackspaceCommand.Characters 1 

    /// Attempt to backspace over the indent before the caret in the line.  This only occurs when
    /// 'softtabstop' is set and there is a blank before the caret
    member x.BackspaceOverIndent() = 
        Contract.Assert (_localSettings.SoftTabStop <> 0)
        Contract.Assert (x.CaretColumn.ColumnNumber > 0)
        Contract.Assert (SnapshotPointUtil.IsBlank (x.CaretPoint.Subtract(1)))

        let prevPoint = x.CaretPoint.Subtract(1)
        if SnapshotPointUtil.IsChar '\t' prevPoint then
            // Backspacing over a tab.  If 'sts' is the same as 'tabstop' then this is just a 
            // single character deletion.  If it is less then we need to split the tab and 
            // delete the appropriate number of spaces
            let diff = _localSettings.TabStop - _localSettings.SoftTabStop
            if diff <= 0 then
                BackspaceCommand.Characters 1
            else
                let text = StringUtil.RepeatChar diff ' '
                BackspaceCommand.Replace (1, text)
        else
            // Backspacing over white space.  If everything between here and the previous tab boundary
            // is indent then take it all.  
            let tabBoundarySpaces = 
                let caretSpaces = _operations.GetSpacesToPoint x.CaretPoint
                let remainder = caretSpaces % _localSettings.SoftTabStop 
                if remainder = 0 then 
                    caretSpaces - _localSettings.SoftTabStop
                else
                    caretSpaces - remainder

            if tabBoundarySpaces < 0 then
                BackspaceCommand.Characters 1
            else
                let tabBoundaryColumn = _operations.GetColumnForSpacesOrEnd x.CaretLine tabBoundarySpaces
                let allBlank = 
                    SnapshotSpan(tabBoundaryColumn.StartPoint, x.CaretPoint)
                    |> SnapshotSpanUtil.GetPoints SearchPath.Forward 
                    |> Seq.forall SnapshotPointUtil.IsBlank
                if allBlank then
                    BackspaceCommand.Characters (x.CaretPoint.Position - tabBoundaryColumn.StartPosition)
                else
                    BackspaceCommand.Characters 1 

    /// The point we should backspace to in order to delete a word
    member x.BackspaceOverWordPoint() =
        Contract.Assert (x.CaretColumn.ColumnNumber > 0)

        // Jump past any blanks before the caret
        let searchPoint = 
            let mutable current = x.CaretColumn.Subtract 1
            while current.ColumnNumber > 0 && current.CodePoint.IsBlank do
                current <- current.Subtract 1
            current.StartPoint

        let deletePoint =  
            match _wordUtil.GetFullWordSpan WordKind.NormalWord searchPoint with
            | None -> searchPoint
            | Some span -> span.Start

        let deletePoint = x.AdjustBackspaceDeletePointForStartPoint deletePoint
        let length = x.CaretPoint.Position - deletePoint.Position
        BackspaceCommand.Characters length

    /// The point we should backspace to in order to delete a line
    member x.BackspaceOverLinePoint() =
        let deletePoint = SnapshotLineUtil.GetFirstNonBlankOrEnd x.CaretLine
        if deletePoint.Position < x.CaretPoint.Position then
            let deletePoint = x.AdjustBackspaceDeletePointForStartPoint deletePoint
            let length = x.CaretPoint.Position - deletePoint.Position
            BackspaceCommand.Characters length
        else
            x.BackspaceOverWordPoint()

    interface IInsertUtil with
        member x.RunInsertCommand command = x.RunInsertCommand command
        member x.RepeatEdit textChange addNewLines count = x.RepeatEdit textChange addNewLines count
        member x.RepeatBlock command atEndOfLine blockSpan = x.RepeatBlock command atEndOfLine blockSpan
        member x.NewUndoSequence () = x.ClearReplaceUndoLine()

