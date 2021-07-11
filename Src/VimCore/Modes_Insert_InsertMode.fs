#light

namespace Vim.Modes.Insert
open Vim
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Operations
open Microsoft.VisualStudio.Text.Editor
open System
open System.Threading.Tasks
open System.Threading
open System.Collections.Generic

type WordCompletionUtil
    (
        _vim: IVim,
        _wordUtil: WordUtil
    ) =

    let _globalSettings = _vim.GlobalSettings

    /// Get a fun (string -> bool) that determines if a particular word should be included in the output
    /// based on the text that we are searching for 
    ///
    /// This method will be called for every single word in the file hence avoid allocations whenever
    /// possible.  That dramatically reduces the allocations
    member private x.GetFilterFunc (filterText: string) (comparer: CharComparer) =

        // Is this actually a word we're interest in.  Need to clear out new lines, 
        // comment characters, one character items, etc ... 
        let isWord (span: SnapshotSpan) =
            if span.Length = 0 || span.Length = 1 then
                false
            else
                let c = span.Start.GetChar()
                _wordUtil.IsKeywordChar c

        // Is this span a match for the filter text? 
        let isMatch (span: SnapshotSpan) =
            if span.Length < filterText.Length then
                false
            else
                let mutable same = true
                let mutable point = span.Start
                let mutable i = 0
                while i < filterText.Length && same do
                    if not (comparer.IsEqual filterText.[i] (point.GetChar())) then
                        same <- false
                    else 
                        i <- i + 1
                        point <- point.Add(1)
                same

        if String.IsNullOrEmpty filterText then
            isWord
        else
            isMatch

    /// Get the word completions for the given word text in the ITextBuffer
    member private x.GetWordCompletionsCore (wordSpan: SnapshotSpan) (comparer: CharComparer) =
        // Get the sequence of words before the completion word 
        let snapshot = wordSpan.Snapshot
        let wordsBefore = 
            let startPoint = SnapshotUtil.GetStartPoint snapshot
            _wordUtil.GetWordSpans WordKind.NormalWord SearchPath.Forward startPoint
            |> Seq.filter (fun span -> span.End.Position <= wordSpan.Start.Position)

        // Get the sequence of words after the completion word 
        let wordsAfter =

            // The provided SnapshotSpan can be a subset of an entire word.  If so then
            // we want to consider the text to the right of the caret as a full word
            match _wordUtil.GetFullWordSpan WordKind.NormalWord wordSpan.Start with
            | None -> _wordUtil.GetWordSpans WordKind.NormalWord SearchPath.Forward wordSpan.End
            | Some fullWordSpan ->
                if fullWordSpan = wordSpan then
                    _wordUtil.GetWordSpans WordKind.NormalWord SearchPath.Forward wordSpan.End
                else
                    let remaining = SnapshotSpan(wordSpan.End, fullWordSpan.End)
                    let after = _wordUtil.GetWordSpans WordKind.NormalWord SearchPath.Forward fullWordSpan.End
                    Seq.append (Seq.singleton remaining) after

        let filterText = wordSpan.GetText()
        let filterFunc = x.GetFilterFunc filterText comparer

        // Combine the collections
        Seq.append wordsAfter wordsBefore
        |> Seq.filter filterFunc

    /// Get the word completion entries in the specified ITextSnapshot.  If the token is cancelled the 
    /// exception will be propagated out of this method.  This method will return duplicate words too
    member private x.GetWordCompletionsInFile (filterText: string) comparer (snapshot: ITextSnapshot) =
        let filterFunc = x.GetFilterFunc filterText comparer
        let startPoint = SnapshotPoint(snapshot, 0) 
        startPoint
        |> _wordUtil.GetWordSpans WordKind.NormalWord SearchPath.Forward
        |> Seq.filter filterFunc

    member x.GetWordCompletions (wordSpan: SnapshotSpan) =
        let comparer = if _globalSettings.IgnoreCase then CharComparer.IgnoreCase else CharComparer.Exact
        let filterText = wordSpan.GetText()
        let fileCompletions = x.GetWordCompletionsCore wordSpan comparer 
        let fileTextBuffer = wordSpan.Snapshot.TextBuffer

        let otherFileCompletions = 
            _vim.VimBuffers
            |> Seq.map (fun vimBuffer -> vimBuffer.TextSnapshot)
            |> Seq.filter (fun snapshot -> snapshot.TextBuffer <> fileTextBuffer)
            |> Seq.collect (fun snapshot -> x.GetWordCompletionsInFile filterText comparer snapshot)

        let wordSpanStart = wordSpan.Start.Position
        let sortComparer (x: SnapshotSpan) (y: SnapshotSpan) = 
            let xPos = x.Start.Position
            let yPos = y.Start.Position
            if x.Snapshot.TextBuffer = fileTextBuffer && y.Snapshot.TextBuffer = fileTextBuffer then
                let xPos = if xPos < wordSpanStart then xPos + wordSpan.Snapshot.Length else xPos
                let yPos = if yPos < wordSpanStart then yPos + wordSpan.Snapshot.Length else yPos
                xPos.CompareTo(yPos)
            elif x.Snapshot.TextBuffer = fileTextBuffer then
                -1
            elif y.Snapshot.TextBuffer = fileTextBuffer then
                1
            else
                xPos.CompareTo(yPos)

        fileCompletions
        |> Seq.append otherFileCompletions  
        |> Seq.filter (fun span -> span.Length > 1)
        |> List.ofSeq
        |> List.sortWith sortComparer
        |> Seq.map SnapshotSpanUtil.GetText
        |> Seq.distinct
        |> List.ofSeq

[<RequireQualifiedAccess>]
type InsertKind =

    | Normal

    /// This Insert is a repeat operation this holds the count and 
    /// whether or not a newline should be inserted after the text
    | Repeat of Count: int * AddNewLine: bool * TextChange: TextChange

    | Block of VisualInsertKind: VisualInsertKind * BlockSpan: BlockSpan

/// The CTRL-R command comes in a number of varieties which really come down to just the 
/// following options.  Detailed information is available on ':help i_CTRL-R_CTRL-R'
[<RequireQualifiedAccess>]
[<System.Flags>]
type PasteFlags =

    | None = 0x0

    /// The text should be properly intended
    | Indent = 0x1

    /// Both textwith and formatoptions apply
    | Formatting = 0x2

    /// The text should be processed as if it were typed
    | TextAsTyped = 0x4

    /// Insert text literally
    | LiteralInsert = 0x8

/// Certain types of edits type predecence over the normal insert commands and are 
/// represented here
[<RequireQualifiedAccess>]
type ActiveEditItem = 

    /// In the middle of a word completion session
    | WordCompletion of WordCompletionSession: IWordCompletionSession 

    /// In the middle of a paste operation.  Waiting for the register to paste from
    | Paste 

    /// In the middle of one of the special paste operations.  The provided flags should 
    /// be passed along to the final Paste operation
    | PasteSpecial of PasteFlag: PasteFlags

    /// In Replace mode, will overwrite using the selected Register
    | OverwriteReplace

    /// In the middle of a special sequence.  Waiting for the next key
    | Special 

    /// In the middle of a digraph operation. Wait for the first digraph key
    | Digraph1

    /// In the middle of a digraph operation. Wait for the second digraph key
    | Digraph2 of KeyInput: KeyInput

    /// In the middle of a insert literal operation. Wait for the next key
    | Literal of KeyInputSet: KeyInputSet

    /// No active items
    | None

[<RequireQualifiedAccess>]
type LiteralFormat =

    /// Up to three decimal digits
    | Decimal

    // Up to three octal digits
    | Octal

    // Up to two hexadecimal digits
    | Hexadecimal8

    // Up to four hexadecimal digits
    | Hexadecimal16

    // Up to eight hexadecimal digits
    | Hexadecimal32

/// Data relating to a particular Insert mode session
type InsertSessionData = {

    /// The transaction bracketing the edits of this session
    Transaction: ILinkedUndoTransaction option

    /// The kind of insert we are currently performing
    InsertKind: InsertKind

    /// This is the current InsertCommand being built up
    CombinedEditCommand: InsertCommand option

    /// The Active edit item 
    ActiveEditItem: ActiveEditItem

    /// Whether we should suppress breaking the undo sequence for the
    /// next left/right caret movement
    SuppressBreakUndoSequence: bool

    /// Whether we should be suppressing abbreviations for the currently typed
    /// text
    SuppressAbbreviation: bool
} with

    member x.RightMostCommand = 
        match x.CombinedEditCommand with
        | None -> None
        | Some command -> Some command.RightMostCommand

[<RequireQualifiedAccess>]
type RawInsertCommand =
    | InsertCommand of KeyInputSet: KeyInputSet * InsertCommand: InsertCommand * CommandFlags: CommandFlags
    | CustomCommand of CustomFunc: (KeyInput -> ProcessResult)

type internal InsertMode
    ( 
        _vimBuffer: IVimBuffer, 
        _operations: ICommonOperations,
        _broker: IDisplayWindowBroker, 
        _editorOptions: IEditorOptions,
        _undoRedoOperations: IUndoRedoOperations,
        _textChangeTracker: ITextChangeTracker,
        _insertUtil: IInsertUtil,
        _motionUtil: IMotionUtil,
        _commandUtil: ICommandUtil,
        _capture: IMotionCapture,
        _isReplace: bool,
        _keyboard: IKeyboardDevice,
        _mouse: IMouseDevice,
        _wordCompletionSessionFactoryService: IWordCompletionSessionFactoryService
    ) as this =

    static let _emptySessionData = {
        InsertKind = InsertKind.Normal
        Transaction = None
        CombinedEditCommand = None
        ActiveEditItem = ActiveEditItem.None
        SuppressBreakUndoSequence = false
        SuppressAbbreviation = false
    }

    /// The set of commands supported by insert mode
    static let InsertCommandDataArray =
        let rawCommands =
            [
                ("<Del>", InsertCommand.Delete, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<End>", InsertCommand.MoveCaretToEndOfLine, CommandFlags.Movement)
                ("<Enter>", InsertCommand.InsertNewLine, CommandFlags.Repeatable ||| CommandFlags.InsertEdit ||| CommandFlags.ContextSensitive)
                ("<Left>", InsertCommand.MoveCaretWithArrow Direction.Left, CommandFlags.Movement)
                ("<Down>", InsertCommand.MoveCaret Direction.Down, CommandFlags.Movement)
                ("<Right>", InsertCommand.MoveCaretWithArrow Direction.Right, CommandFlags.Movement)
                ("<Tab>", InsertCommand.InsertTab, CommandFlags.Repeatable ||| CommandFlags.InsertEdit ||| CommandFlags.ContextSensitive)
                ("<Up>", InsertCommand.MoveCaret Direction.Up, CommandFlags.Movement)
                ("<C-i>", InsertCommand.InsertTab, CommandFlags.Repeatable ||| CommandFlags.InsertEdit ||| CommandFlags.ContextSensitive)
                ("<C-@>", InsertCommand.InsertPreviouslyInsertedText true, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<C-a>", InsertCommand.InsertPreviouslyInsertedText false, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<C-d>", InsertCommand.ShiftLineLeft, CommandFlags.Repeatable)
                ("<C-e>", InsertCommand.InsertCharacterBelowCaret, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<C-j>", InsertCommand.InsertNewLine, CommandFlags.Repeatable ||| CommandFlags.InsertEdit ||| CommandFlags.ContextSensitive)
                ("<C-m>", InsertCommand.InsertNewLine, CommandFlags.Repeatable ||| CommandFlags.InsertEdit ||| CommandFlags.ContextSensitive)
                ("<C-t>", InsertCommand.ShiftLineRight, CommandFlags.Repeatable)
                ("<C-y>", InsertCommand.InsertCharacterAboveCaret, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<C-v>", InsertCommand.Paste, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<C-Left>", InsertCommand.MoveCaretByWord Direction.Left, CommandFlags.Movement)
                ("<C-Right>", InsertCommand.MoveCaretByWord Direction.Right, CommandFlags.Movement)
                ("<BS>", InsertCommand.Back, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<C-h>", InsertCommand.Back, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<C-w>", InsertCommand.DeleteWordBeforeCursor, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
                ("<C-u>", InsertCommand.DeleteLineBeforeCursor, CommandFlags.Repeatable ||| CommandFlags.InsertEdit)
            ]

        rawCommands 
        |> Seq.map (fun (text, insertCommand, commandFlags) ->
            let keyInput = KeyNotationUtil.StringToKeyInput text
            (keyInput, insertCommand, commandFlags))
        |> Seq.toArray

    let _bag = DisposableBag()
    let _textView = _vimBuffer.TextView
    let _textBuffer = _vimBuffer.TextBuffer
    let _localSettings = _vimBuffer.LocalSettings
    let _globalSettings = _vimBuffer.GlobalSettings
    let _editorOperations = _operations.EditorOperations
    let _commandRanEvent = StandardEvent<CommandRunDataEventArgs>()
    let _wordUtil = _vimBuffer.VimTextBuffer.WordUtil
    let _wordCompletionUtil = WordCompletionUtil(_vimBuffer.Vim, _wordUtil)
    let _localAbbreviationMap = _vimBuffer.VimTextBuffer.LocalAbbreviationMap
    let mutable _commandMap: Map<KeyInput, RawInsertCommand> = Map.empty
    let mutable _sessionData = _emptySessionData
    let mutable _isInProcess = false

    do
        // Caret changes can end a text change operation.
        _textView.Caret.PositionChanged
        |> Observable.filter (fun _ -> this.IsActive && not this.IsInProcess)
        |> Observable.subscribe (fun args -> this.OnCaretPositionChanged args)
        |> _bag.Add

        // Listen for text changes
        _textChangeTracker.ChangeCompleted
        |> Observable.filter (fun _ -> this.IsActive)
        |> Observable.subscribe (fun args -> this.OnTextChangeCompleted args)
        |> _bag.Add

        // Listen for global settings changes
        (_globalSettings :> IVimSettings).SettingChanged 
        |> Observable.subscribe (fun args -> this.OnGlobalSettingsChanged args)
        |> _bag.Add

    member x.EnsureCommandsBuilt () =
        if _commandMap.IsEmpty then
            x.BuildCommands()

    member x.CommandNames =
        x.EnsureCommandsBuilt()
        _commandMap |> Seq.map (fun p -> p.Key) |> Seq.map KeyInputSetUtil.Single

    member x.BuildCommands () =

        // These commands have nothing to do with selection settings
        let regularCommands: (KeyInput * RawInsertCommand) seq =
            [|
                ("<Esc>", RawInsertCommand.CustomCommand this.ProcessEscape)
                ("<Insert>", RawInsertCommand.CustomCommand this.ProcessInsert)
                ("<C-c>", RawInsertCommand.CustomCommand this.ProcessCancel)
                ("<C-n>", RawInsertCommand.CustomCommand this.ProcessWordCompletionNext)
                ("<C-o>", RawInsertCommand.CustomCommand this.ProcessNormalModeOneCommand)
                ("<C-p>", RawInsertCommand.CustomCommand this.ProcessWordCompletionPrevious)
                ("<C-r>", RawInsertCommand.CustomCommand this.ProcessPasteStart)
                ("<C-g>", RawInsertCommand.CustomCommand this.ProcessSpecialStart)
                ("<C-k>", RawInsertCommand.CustomCommand this.ProcessDigraphStart)
                ("<C-q>", RawInsertCommand.CustomCommand this.ProcessLiteralStart)
                ("<C-^>", RawInsertCommand.CustomCommand this.ProcessToggleLanguage)
                ("<C-]>", RawInsertCommand.CustomCommand this.ProcessAbbreviationCompletion)
                ("<LeftMouse>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.MoveCaretToMouse))
                ("<LeftDrag>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.SelectTextForMouseDrag))
                ("<LeftRelease>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.SelectTextForMouseRelease))
                ("<S-LeftMouse>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.SelectTextForMouseClick))
                ("<2-LeftMouse>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.SelectWordOrMatchingTokenAtMousePoint))
                ("<3-LeftMouse>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.SelectLine))
                ("<4-LeftMouse>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.SelectBlock))

                // Multi-selection bindings not in Vim.
                ("<C-A-LeftMouse>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.AddCaretAtMousePoint))
                ("<C-A-2-LeftMouse>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.SelectWordOrMatchingTokenAtMousePoint))
                ("<C-A-Up>", RawInsertCommand.CustomCommand (this.ForwardToNormal (NormalCommand.AddCaretOnAdjacentLine Direction.Up)))
                ("<C-A-Down>", RawInsertCommand.CustomCommand (this.ForwardToNormal (NormalCommand.AddCaretOnAdjacentLine Direction.Down)))
                ("<C-A-n>", RawInsertCommand.CustomCommand (this.ForwardToNormal NormalCommand.SelectWordOrMatchingToken))
            |]
            |> Seq.map (fun (text, rawInsertCommand) ->
                let keyInput = KeyNotationUtil.StringToKeyInput text
                (keyInput, rawInsertCommand))

        let noSelectionCommands: (KeyInput * InsertCommand * CommandFlags) seq =
            [|
                ("<S-Left>", InsertCommand.MoveCaretByWord Direction.Left, CommandFlags.Movement)
                ("<S-Right>", InsertCommand.MoveCaretByWord Direction.Right, CommandFlags.Movement)
            |]
            |> Seq.map (fun (text, insertCommand, commandFlags) ->
                let keyInput = KeyNotationUtil.StringToKeyInput text
                (keyInput, insertCommand, commandFlags))

        /// The list of commands that initiate select mode
        ///
        /// TODO: Because insert mode does not yet use a command runner, we have
        /// to simulate a little mini-runner here.  Should this be upgraded?
        let selectionCommands: (KeyInput * RawInsertCommand) seq =

            // Create a command factory so we can access the selection commands
            let factory = CommandFactory(_operations, _capture)

            // Run a normal command bound to a key input and return a command result
            let runNormalCommand normalCommand keyInput =
                let commandData = { Count = None; RegisterName = None }
                let commandResult = _commandUtil.RunNormalCommand normalCommand commandData
                ProcessResult.OfCommandResult commandResult

            // Extract those bindings that are bound to normal commands and
            // create raw insert command that runs the normal command
            seq {
                for commandBinding in factory.CreateSelectionCommands() do
                    match commandBinding with
                    | CommandBinding.NormalBinding (_, _,  normalCommand) ->
                        match commandBinding.KeyInputSet.FirstKeyInput with
                        | Some keyInput ->
                            let customCommand = runNormalCommand normalCommand
                            let rawInsertCommand = RawInsertCommand.CustomCommand customCommand
                            yield (keyInput, rawInsertCommand)
                        | None ->
                            ()
                    | _ ->
                        ()
            }

        // Choose which commands are applicable for conflicting keys
        let (applicableNoSelectionCommands, applicableSelectionCommands) =
            if Util.IsFlagSet _globalSettings.KeyModelOptions KeyModelOptions.StartSelection then
                (Seq.empty, selectionCommands)
            else
                (noSelectionCommands, Seq.empty)

        // Map all of the InsertCommand values to their RawInsertCommand counterpart 
        // Create a list of 
        let mappedInsertCommands: (KeyInput * RawInsertCommand) seq = 
            InsertCommandDataArray
            |> Seq.append applicableNoSelectionCommands
            |> Seq.map (fun (keyInput, insertCommand, commandFlags) ->
                let keyInputSet = KeyInputSet(keyInput)
                let rawInsertCommand = RawInsertCommand.InsertCommand (keyInputSet, insertCommand, commandFlags)
                (keyInput, rawInsertCommand))

        // Build a list of all applicable commands
        _commandMap <-
            regularCommands
            |> Seq.append mappedInsertCommands
            |> Seq.append applicableSelectionCommands
            |> Map.ofSeq

    member x.ActiveWordCompletionSession = 
        match _sessionData.ActiveEditItem with
        | ActiveEditItem.WordCompletion wordCompletionSession -> Some wordCompletionSession
        | _ -> None

    member x.PasteCharacter =
        match _sessionData.ActiveEditItem with
        | ActiveEditItem.Paste -> Some '"'
        | ActiveEditItem.PasteSpecial _ -> Some '"'
        | ActiveEditItem.Digraph1 -> Some '?'
        | ActiveEditItem.Digraph2 firstKeyInput -> Some firstKeyInput.Char
        | ActiveEditItem.Literal _ -> Some '^'
        | ActiveEditItem.None -> None
        | ActiveEditItem.OverwriteReplace -> None
        | ActiveEditItem.WordCompletion _ -> None
        | ActiveEditItem.Special _ -> None

    member x.IsInPaste = x.PasteCharacter.IsSome

    member x.CaretPoint = TextViewUtil.GetCaretPoint _textView

    member x.CaretLine = TextViewUtil.GetCaretLine _textView

    /// The VirtualSnapshotPoint for the caret
    member x.CaretVirtualPoint = TextViewUtil.GetCaretVirtualPoint _textView

    member x.CurrentSnapshot = _textView.TextSnapshot

    member x.ModeKind = if _isReplace then ModeKind.Replace else ModeKind.Insert

    member x.WordCompletionUtil = _wordCompletionUtil

    /// Is this the currently active mode?
    member x.IsActive = x.ModeKind = _vimBuffer.ModeKind

    /// Is this mode processing key input?
    member x.IsInProcess = _isInProcess

    /// Cancel the active IWordCompletionSession if there is such a session 
    /// active
    member x.CancelWordCompletionSession keyInput = 
        match _sessionData.ActiveEditItem with
        | ActiveEditItem.WordCompletion wordCompletionSession -> 
            if not wordCompletionSession.IsDismissed then
                wordCompletionSession.Dismiss()

            _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.None }
        | _ -> ()

    /// Can Insert mode handle this particular KeyInput value 
    member x.CanProcess keyInput =
        x.GetRawInsertCommand keyInput |> Option.isSome

    /// Complete the current batched edit command if one exists
    member x.CompleteCombinedEditCommand keyInput = 
        match _sessionData.CombinedEditCommand with
        | None -> () 
        | Some command -> 
            _sessionData <- { _sessionData with CombinedEditCommand = None }

            let data = {
                CommandBinding = CommandBinding.InsertBinding (KeyInputSet.Empty, CommandFlags.Repeatable ||| CommandFlags.InsertEdit, command)
                Command = Command.InsertCommand command
                CommandResult = CommandResult.Completed ModeSwitch.NoSwitch }
            let args = CommandRunDataEventArgs(data)
            _commandRanEvent.Trigger x args

    /// Get the RawInsertCommand for the given KeyInput
    member x.GetRawInsertCommand keyInput = 
        match Map.tryFind keyInput _commandMap with
        | Some rawInsertCommand ->
            if _isReplace then

                // Map any insert commands to their replacement counterparts.
                match rawInsertCommand with
                | RawInsertCommand.InsertCommand (keyInput, command, flags) ->
                    let replaceCommand =
                        match command with
                        | InsertCommand.Back ->
                            Some InsertCommand.UndoReplace
                        | InsertCommand.InsertCharacterAboveCaret ->
                            Some InsertCommand.ReplaceCharacterAboveCaret
                        | InsertCommand.InsertCharacterBelowCaret ->
                            Some InsertCommand.ReplaceCharacterBelowCaret
                        | _ -> None
                    match replaceCommand with
                    | Some replaceCommand ->
                        RawInsertCommand.InsertCommand (keyInput, replaceCommand, flags) |> Some
                    | None -> Some rawInsertCommand
                | _ -> Some rawInsertCommand
            else
                Some rawInsertCommand
        | None ->
            match keyInput.RawChar with
            | None -> None
            | Some c ->

                // Since the char is not mapped to a particular command then it's a direct insert or
                // replace 
                let getDirectInsert () =
                    let command = 
                        if _isReplace then
                            InsertCommand.Replace c
                        else
                            let text = StringUtil.OfChar c
                            InsertCommand.Insert text
                    let commandFlags = CommandFlags.Repeatable ||| CommandFlags.InsertEdit
                    let keyInputSet = KeyInputSet(keyInput)
                    RawInsertCommand.InsertCommand (keyInputSet, command, commandFlags) |> Some

                if keyInput.KeyModifiers = VimKeyModifiers.Control then

                    // A key with only the control modifier that isn't mapped
                    // is never a direct insert. But on some international
                    // keyboards, it might translate to an ASCII control
                    // character. See issue #2462.
                    None

                else if keyInput.HasKeyModifiers && not (CharUtil.IsLetterOrDigit c) then

                    // Certain keys such as Delete, Esc, etc ... have the same
                    // behavior when invoked with or without any modifiers.
                    // The modifiers must be considered because they do
                    // participate in key mapping.  Once we get here though we
                    // must discard them
                    let alternateKeyInput = KeyInputUtil.ChangeKeyModifiersDangerous keyInput VimKeyModifiers.None
                    match Map.tryFind alternateKeyInput _commandMap with
                    | Some rawInsertCommand -> Some rawInsertCommand
                    | None -> getDirectInsert()

                else
                    getDirectInsert()

    /// Get the Span for the word we are trying to complete if there is one
    member x.GetWordCompletionSpan () =
        if SnapshotLineUtil.IsBlankOrEmpty x.CaretLine then
            // Have to special case a bit here.  Blank lines are actually words but we
            // don't want to replace the new line when doing a completion
            None
        elif SnapshotPointUtil.IsBlankOrInsideLineBreak x.CaretPoint && x.CaretPoint.Position > 0 then
            // If we are currently on a blank and the previous point is the end of a word
            // then we are replacing that word
            let previousPoint = SnapshotPointUtil.SubtractOne x.CaretPoint
            _wordUtil.GetFullWordSpan WordKind.NormalWord previousPoint
        else
            // Calculate the word span based on the information before the caret. 
            let point = 
                SnapshotPointUtil.TryGetPreviousPointOnLine x.CaretPoint 1
                |> OptionUtil.getOrDefault x.CaretPoint

            match _wordUtil.GetFullWordSpan WordKind.NormalWord point with
            | None -> None
            | Some span -> SnapshotSpan(span.Start, x.CaretPoint) |> Some

    /// Is this KeyInput a raw text insert into the ITextBuffer.  Anything that would be 
    /// processed by adding characters to the ITextBuffer.  This is anything which has an
    /// associated character that is not an insert mode command
    member x.IsDirectInsert (keyInput: KeyInput) = 
        match x.GetRawInsertCommand keyInput with
        | None -> false
        | Some rawInsertCommand ->
            match rawInsertCommand with
            | RawInsertCommand.InsertCommand (_, insertCommand, _) ->
                match insertCommand with
                | InsertCommand.Insert _ -> true
                | InsertCommand.Replace _ -> true
                | _ -> false
            | RawInsertCommand.CustomCommand _ -> false

    /// Apply any edits which must occur after insert mode is completed
    member x.ApplyAfterEdits() = 

        // Flush out any existing text changes so the CombinedEditCommand has the final
        // edit data for the session
        _textChangeTracker.CompleteChange()

        // If applicable, use the effective change for the combined edit.
        if _textChangeTracker.IsEffectiveChangeInsert then

            match _textChangeTracker.EffectiveChange with
            | Some span when span.Length <> 0 ->

                // Override the piecewise combined edit command with
                // the effective change which is a simple insertion.
                span
                |> SnapshotSpanUtil.GetText
                |> TextChange.Insert
                |> InsertCommand.OfTextChange
                |> Some
                |> x.ChangeCombinedEditCommand
            | _ ->
                ()

        try
            match _sessionData.InsertKind, _sessionData.CombinedEditCommand with
            | InsertKind.Normal, _ -> ()
            | InsertKind.Repeat (count, addNewLines, textChange), _ -> _insertUtil.RepeatEdit textChange addNewLines (count - 1)
            | InsertKind.Block _, None -> ()
            | InsertKind.Block (visualInsertKind, blockSpan), Some insertCommand -> 

                // The RepeatBlock command will be performing edits on the ITextBuffer.  We don't want to 
                // track these changes.  They instead will be tracked by the InsertCommand that we return
                try 
                    _textChangeTracker.TrackCurrentChange <- false
                    let combinedCommand = 
                        match _insertUtil.RepeatBlock insertCommand visualInsertKind blockSpan with
                        | Some text ->
                            let padShortLines = visualInsertKind = VisualInsertKind.End
                            let atEndOfLine = visualInsertKind = VisualInsertKind.EndOfLine
                            InsertCommand.BlockInsert (insertCommand, padShortLines, atEndOfLine, blockSpan.Height)
                            |> Some
                        | None -> None
                    x.ChangeCombinedEditCommand combinedCommand
                finally 
                    _textChangeTracker.TrackCurrentChange <- true

        finally
            // Make sure to close out the transaction
            match _sessionData.Transaction with
            | None -> ()
            | Some transaction -> 
                transaction.Complete()
                _sessionData <- { _sessionData with Transaction = None }

    /// Process the <Insert> command.  This toggles between insert an replace mode
    member x.ProcessInsert keyInput = 

        let mode = if _isReplace then ModeKind.Insert else ModeKind.Replace
        ProcessResult.Handled (ModeSwitch.SwitchMode mode)

    /// Enter normal mode for a single command.
    member x.ProcessNormalModeOneCommand keyInput =

        let switch = ModeSwitch.SwitchModeOneTimeCommand ModeKind.Normal
        ProcessResult.Handled switch

    /// Process the CTRL-N key stroke which calls for the previous word completion
    member x.ProcessWordCompletionNext keyInput = 
        x.StartWordCompletionSession true

    /// Process the CTRL-P key stroke which calls for the previous word completion
    member x.ProcessWordCompletionPrevious keyInput =
        x.StartWordCompletionSession false

    member x.ProcessEscape _ =
        ProcessResult.OfModeKind ModeKind.Normal

    member x.ProcessCancel _ =
        (ModeKind.Normal, ModeArgument.CancelOperation)
        |> ModeSwitch.SwitchModeWithArgument
        |> ProcessResult.Handled

    member x.StopInsert _ =

        x.ApplyAfterEdits()

        if _broker.IsCompletionActive || _broker.IsSignatureHelpActive || _broker.IsQuickInfoActive then
            _broker.DismissDisplayWindows()

        // Save the last edit point before moving the column to the left
        let insertExitPoint = Some x.CaretPoint
        _vimBuffer.VimTextBuffer.LastInsertExitPoint <- insertExitPoint
        _vimBuffer.VimTextBuffer.LastChangeOrYankEnd <- insertExitPoint

        // Save the last text edit
        match _sessionData.CombinedEditCommand with
        | Some (InsertCommand.Insert text) -> _vimBuffer.VimData.LastTextInsert <- Some text
        | _ -> ()

        // Don't move the caret for block inserts.  It's explicitly positioned 
        let moveCaretLeft = 
            match _sessionData.InsertKind with
            | InsertKind.Normal -> true
            | InsertKind.Repeat _ -> true
            | InsertKind.Block _ -> false

        // Run the mode cleanup command.  This must be done as a command.  Many commands call
        // into insert mode expecting to link with at least one following command (cw, o, ct,
        // etc ...).  Ending insert with an explicit link previous command guarantees these 
        // commands are completed and not left hanging open for subsequent commands to link
        // to.  
        let keyInputSet = KeyNotationUtil.StringToKeyInputSet "<Left>"
        let commandFlags = CommandFlags.Repeatable ||| CommandFlags.LinkedWithPreviousCommand
        x.RunInsertCommand (InsertCommand.CompleteMode moveCaretLeft) keyInputSet commandFlags |> ignore

        // The last edit point is the position of the cursor after it moves to the left
        _vimBuffer.VimTextBuffer.LastEditPoint <- Some x.CaretPoint

        ProcessResult.OfModeKind ModeKind.Normal

    /// Start a word completion session in the given direction at the current caret point
    member x.StartWordCompletionSession isForward = 

        // If the caret is currently in virtual space we need to fill in that space with
        // real spaces before starting a completion session.
        _operations.FillInVirtualSpace()

        // Time to start a completion.  
        let wordSpan = 
            match x.GetWordCompletionSpan() with
            | Some span -> span
            | None -> SnapshotSpan(x.CaretPoint, 0)
        let wordList  = _wordCompletionUtil.GetWordCompletions wordSpan

        // If we have at least one item then begin a word completion session.  Don't do this for an 
        // empty completion list as there is nothing to display.  The lack of anything to display 
        // doesn't make the command an error though
        if not (List.isEmpty wordList) then
            match _wordCompletionSessionFactoryService.CreateWordCompletionSession _textView wordSpan wordList isForward with
            | None -> ()
            | Some wordCompletionSession ->
                // When the completion session is dismissed we want to clean out the session 
                // data 
                wordCompletionSession.Dismissed
                |> Event.add (fun _ -> x.CancelWordCompletionSession())

                let activeEditItem = ActiveEditItem.WordCompletion
                _sessionData <- { _sessionData with ActiveEditItem = activeEditItem wordCompletionSession }

        ProcessResult.Handled ModeSwitch.NoSwitch

    /// When calculating a combined edit command we reduce the simple text changes down
    /// as far as possible.  No since in creating a set of 5 combined commands for inserting 
    /// the text "watch" when a simple InsertCommand.Insert "watch" will do.  
    ///
    /// It is important to only combine direct text edit commands here.  We don't want to be 
    /// combining any logic commands like Backspace.  Those use settings to do their work and
    /// hence must be reprocessed on a repeat
    static member CreateCombinedEditCommand left right =

        // Certain commands are simply not combinable with others.  Once executed they stand 
        // as the lone command
        let isUncombinable command = 
            match command with
            | InsertCommand.DeleteLineBeforeCursor -> true
            | InsertCommand.DeleteWordBeforeCursor -> true
            | _ -> false

        if isUncombinable left then
            right
        elif isUncombinable right then
            right
        else
            // Is this a simple text change which can be combined
            let convert command = 
                match command with 
                | InsertCommand.Insert text -> TextChange.Insert text |> Some
                | InsertCommand.DeleteLeft count -> TextChange.DeleteLeft count |> Some
                | InsertCommand.DeleteRight count -> TextChange.DeleteRight count |> Some
                | _ -> None

            match convert left, convert right with
            | Some leftChange, Some rightChange ->
                let textChange = TextChange.CreateReduced leftChange rightChange
                match textChange with
                | TextChange.Insert text -> InsertCommand.Insert text
                | TextChange.DeleteLeft count -> InsertCommand.DeleteLeft count
                | TextChange.DeleteRight count -> InsertCommand.DeleteRight count
                | TextChange.Combination _ -> InsertCommand.Combined (left, right)
            | _ -> InsertCommand.Combined (left, right)

    /// Run the insert command with the given information
    member x.RunInsertCommand (command: InsertCommand) (keyInputSet: KeyInputSet) commandFlags: ProcessResult =

        // Dismiss the completion when running an explicit insert commend.
        x.CancelWordCompletionSession()

        // When running an explicit command then we need to go ahead and
        // complete the previous  extra text change.  It needs to be completed
        // now so that it happens before the  command we are about to run
        _textChangeTracker.CompleteChange()

        let result = 
            try

                // We don't want the edits which are executed as part of the
                // command to be tracked through  an external / extra text
                // change so disable tracking while executing the command
                _textChangeTracker.TrackCurrentChange <- false
                _insertUtil.RunInsertCommand command

            finally
                _textChangeTracker.TrackCurrentChange <- true

        x.OnAfterRunInsertCommand command

        // Now we need to decided how the external world sees this edit.  If it
        // links with an existing edit then we save it and send it out as a
        // batch later.
        let isEdit = Util.IsFlagSet commandFlags CommandFlags.InsertEdit
        let isMovement = Util.IsFlagSet commandFlags CommandFlags.Movement
        let isContextSensitive = Util.IsFlagSet commandFlags CommandFlags.ContextSensitive

        /// Handle suppression of breaking the undo sequence.
        let suppressBreakUndoSequence =
            match _sessionData.SuppressBreakUndoSequence, command with
            | true, InsertCommand.MoveCaretWithArrow Direction.Left -> true
            | true, InsertCommand.MoveCaretWithArrow Direction.Right -> true
            | _ ->  false
        _sessionData <- { _sessionData with SuppressBreakUndoSequence = false }

        if
            isEdit
            || isMovement && _globalSettings.AtomicInsert
            || suppressBreakUndoSequence
        then

            // If it is context sensitive (e.g. <Tab> or <Return>), or not an
            // edit (e.g. an arrow key), then cancel using the effective
            // change.
            if isContextSensitive || not isEdit then
                _textChangeTracker.StopTrackingEffectiveChange()

            // If it's an edit then combine it with the existing command and
            // batch them  together.  Don't raise the event yet
            let command = 
                match _sessionData.CombinedEditCommand with
                | None -> command
                | Some previousCommand -> InsertMode.CreateCombinedEditCommand previousCommand command
            x.ChangeCombinedEditCommand (Some command)

        else

            // Not an edit command.  If there is an existing edit command then
            // go ahead and flush it out before raising this command
            x.CompleteCombinedEditCommand()

            let data = {
                CommandBinding = CommandBinding.InsertBinding (keyInputSet, commandFlags, command)
                Command = Command.InsertCommand command
                CommandResult = result }
            let args = CommandRunDataEventArgs(data)
            _commandRanEvent.Trigger x args

            match command with
            | InsertCommand.ShiftLineLeft -> ()
            | InsertCommand.ShiftLineRight -> ()
            | _ -> 

                // All other commands break the undo sequence.
                x.BreakUndoSequence "Insert after motion" 

        // Arrow keys start a new insert point.
        if isMovement && not suppressBreakUndoSequence then
            x.ResetInsertPoint()
        else
            _vimBuffer.VimTextBuffer.LastChangeOrYankEnd <- Some x.CaretPoint

        ProcessResult.OfCommandResult result

    member x.BreakUndoSequence name =
        _insertUtil.NewUndoSequence()
        match _sessionData.Transaction with
        | None -> ()
        | Some transaction ->
            transaction.Complete()
            _textChangeTracker.StartTrackingEffectiveChange()
            let transaction = x.CreateLinkedUndoTransaction name
            _sessionData <- { _sessionData with Transaction = Some transaction }

    /// Paste the contents of the specified register with the given flags 
    ///
    /// TODO: Right now PasteFlags are ignored.  With better formatting support these should
    /// be respected.  Since we let the host control formatting at this point there isn't a lot
    /// that can be done now
    member x.Paste (keyInput: KeyInput) (flags: PasteFlags) = 

        let isLiteral = (flags &&& PasteFlags.LiteralInsert) > PasteFlags.None
        let isOverwrite = _sessionData.ActiveEditItem = ActiveEditItem.OverwriteReplace
        let register = keyInput.RawChar.Value
        _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.None }

        if keyInput = KeyInputUtil.EscapeKey then
            ProcessResult.Handled ModeSwitch.NoSwitch
        else
            let text = 
                keyInput.RawChar
                |> OptionUtil.map2 RegisterName.OfChar
                |> Option.map _vimBuffer.RegisterMap.GetRegister
                |> OptionUtil.map2 (fun register -> 
                    let value = register.StringValue
                    match value with
                    | "" -> None
                    | _ -> Some value)

            match text with
            | None -> 
                _operations.Beep()
                ProcessResult.Handled ModeSwitch.NoSwitch
            | Some text -> 

                // Normalize the line endings here
                let text =
                    let newLine = _operations.GetNewLineText x.CaretPoint
                    EditUtil.NormalizeNewLines text newLine

                let keyInputSet = KeyInputSet(keyInput)
                let insertCommand = if isOverwrite then InsertCommand.Overwrite text
                                    elif isLiteral then InsertCommand.LiteralPaste register
                                    else InsertCommand.Insert text

                x.RunInsertCommand insertCommand keyInputSet CommandFlags.InsertEdit

    /// This will attempt to complete an abbreviation based on the currently typed text without inserting
    /// any additional characters.
    member x.ProcessAbbreviationCompletion keyInput =
        x.TryCompleteAbbreviation keyInput |> ignore
        ProcessResult.Handled ModeSwitch.NoSwitch

    member x.TryCompleteAbbreviation keyInput =
        if not _sessionData.SuppressAbbreviation then
            match _sessionData.RightMostCommand with
            | Some (InsertCommand.Insert text) ->
                match _localAbbreviationMap.Abbreviate(text, keyInput, AbbreviationMode.Insert) with
                | Some result -> 
                    let flags = CommandFlags.Repeatable ||| CommandFlags.InsertEdit
                    x.RunInsertCommand (InsertCommand.DeleteLeft result.ReplacedSpan.Length) KeyInputSet.Empty flags |> ignore
                    _sessionData <- { _sessionData with SuppressAbbreviation = true }
                    for keyInput in result.Replacement.KeyInputs do
                        x.ProcessCore keyInput |> ignore
                    _sessionData <- { _sessionData with SuppressAbbreviation = false }
                    true
                | _ -> false
            | _ -> false
        else
            false

    /// Try and process the KeyInput by considering the current text edit in Insert Mode
    member x.ProcessWithCurrentChange keyInput = 

        // Actually try and process this with the current change 
        let func (text: string) = 
            if text.EndsWith("0") && keyInput = KeyInputUtil.CharWithControlToKeyInput 'd' then
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet "0<C-d>"
                // First step is to delete the portion of the current change which matches up with
                // our command.
                if x.CaretPoint.Position >= text.Length then
                    let span = 
                        let startPoint = SnapshotPoint(x.CurrentSnapshot, x.CaretPoint.Position - text.Length)
                        SnapshotSpan(startPoint, text.Length)
                    _textBuffer.Delete(span.Span) |> ignore

                // Now run the command
                x.RunInsertCommand InsertCommand.DeleteAllIndent keyInputSet (CommandFlags.Repeatable ||| CommandFlags.ContextSensitive) |> Some

            else
                x.TryCompleteAbbreviation keyInput |> ignore

                // Return None to allow keyInput to process no matter whether or not the abbreviation was 
                // completed. The keyInput still needs to be appended to the buffer.
                None

        match _sessionData.CombinedEditCommand with
        | Some insertCommand ->
            match insertCommand.RightMostCommand with
            | InsertCommand.Insert text -> func text
            | InsertCommand.Back ->
                if _globalSettings.Digraph && keyInput.RawChar.IsSome then
                    match insertCommand.SecondRightMostCommand with
                    | Some (InsertCommand.Insert text) when text.Length > 0 -> 

                        // The user entered 'char1 <BS> char2' and digraphs are
                        // enabled, so check whether 'char1 char2' is a digraph
                        // and if so, insert it.
                        let firstKeyInput =
                            text.[text.Length - 1]
                            |> KeyInputUtil.CharToKeyInput
                        let secondKeyInput = keyInput
                        match x.TryInsertDigraph firstKeyInput secondKeyInput with
                        | Some processResult ->
                            Some processResult
                        | None ->
                            x.TryInsertDigraph secondKeyInput firstKeyInput
                    | _ ->
                        None
                else
                    None
            | _ -> None
        | _ -> None

    /// Called when we need to process a key stroke and an IWordCompletionSession
    /// is active.
    member x.ProcessWithWordCompletionSession (wordCompletionSession: IWordCompletionSession) keyInput = 
        let handled = 
            if keyInput = KeyNotationUtil.StringToKeyInput("<C-n>") then
                wordCompletionSession.MoveNext() |> Some
            elif keyInput = KeyNotationUtil.StringToKeyInput("<Down>") then
                wordCompletionSession.MoveNext() |> Some
            elif keyInput = KeyNotationUtil.StringToKeyInput("<C-p>") then
                wordCompletionSession.MovePrevious() |> Some
            elif keyInput = KeyNotationUtil.StringToKeyInput("<Up>") then
                wordCompletionSession.MovePrevious() |> Some
            elif keyInput = KeyNotationUtil.StringToKeyInput("<C-y>") then
                wordCompletionSession.Commit()
                Some true
            elif keyInput = KeyNotationUtil.StringToKeyInput("<C-e>") then
                x.CancelWordCompletionSession()
                Some true
            else
                None

        match handled with
        | Some handled -> 
            if handled then 
                ProcessResult.Handled ModeSwitch.NoSwitch 
            else 
                ProcessResult.Error
        | None -> 
            // Any other key should commit the IWordCompletionSession and we should process
            // the KeyInput as normal
            wordCompletionSession.Commit()
            // Commit will set IsDismissed on wordCompletionSession so CancelWordCompletionSession will not call Dismiss() it
            // Cancel as finish. we need this because otherwise we get a stackoverflow in tests from call to ProcessCore
            x.CancelWordCompletionSession()
            x.ProcessCore keyInput

    /// Start a paste session in insert mode
    member x.ProcessPasteStart keyInput =
        x.CancelWordCompletionSession()
        _sessionData <- { _sessionData with ActiveEditItem = if _isReplace then ActiveEditItem.OverwriteReplace else ActiveEditItem.Paste }
        ProcessResult.Handled ModeSwitch.NoSwitch

    /// Start a digraph session in insert mode
    member x.ProcessDigraphStart keyInput =
        x.CancelWordCompletionSession()
        _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.Digraph1 }
        ProcessResult.Handled ModeSwitch.NoSwitch

    /// Start an insertion of a literal character
    member x.ProcessLiteralStart keyInput =
        x.CancelWordCompletionSession()
        x.ProcessLiteral KeyInputSet.Empty

    /// Start a special sequence in insert mode
    member x.ProcessSpecialStart keyInput =
        x.CancelWordCompletionSession()
        _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.Special }
        ProcessResult.Handled ModeSwitch.NoSwitch

    /// Toggle the use of typing language characters
    member x.ProcessToggleLanguage keyInput =
        _operations.ToggleLanguage true
        ProcessResult.Handled ModeSwitch.NoSwitch

    /// Forward the specified command to normal mode
    member x.ForwardToNormal (normalCommand: NormalCommand) keyInput =
        x.CancelWordCompletionSession()
        x.BreakUndoSequence "Mouse"
        match _commandUtil.RunNormalCommand normalCommand CommandData.Default with
        | CommandResult.Completed modeSwitch ->
            ProcessResult.Handled modeSwitch
        | CommandResult.Error ->
            ProcessResult.Handled ModeSwitch.NoSwitch

    /// Process the second key of a paste operation.  
    member x.ProcessPaste keyInput = 

        let pasteSpecial flags = 
            _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.PasteSpecial flags }
            ProcessResult.Handled ModeSwitch.NoSwitch

        // Get the text to be inserted.
        if keyInput = KeyInputUtil.CharWithControlToKeyInput 'r' then
            let flags = PasteFlags.Formatting ||| PasteFlags.Indent
            pasteSpecial flags
        elif keyInput = KeyInputUtil.CharWithControlToKeyInput 'o' then
            let flags = PasteFlags.LiteralInsert
            pasteSpecial flags
        elif keyInput = KeyInputUtil.CharWithControlToKeyInput 'p' then
            let flags = PasteFlags.Indent ||| PasteFlags.LiteralInsert
            pasteSpecial flags
        else
            let flags = PasteFlags.Formatting ||| PasteFlags.Indent ||| PasteFlags.TextAsTyped
            x.Paste keyInput flags

    /// Process the second key of a special sequence.
    member x.ProcessSpecial keyInput = 

        // Reset the special sequence.
        _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.None }

        // Handle the next key.
        match keyInput with
        | keyInput when keyInput = KeyInputUtil.CharToKeyInput 'u' ->

            // See ':vimhelp i_CTRL-G_u'.
            x.BreakUndoSequence "Break undo sequence"

        | keyInput when keyInput = KeyInputUtil.CharToKeyInput 'U' ->

            // See ':vimhelp i_CTRL-G_U'.
            _sessionData <- { _sessionData with SuppressBreakUndoSequence = true }

        | keyInput when
            keyInput = KeyInputUtil.VimKeyToKeyInput VimKey.Up
            || keyInput = KeyInputUtil.CharToKeyInput 'k'
            || keyInput = KeyInputUtil.CharWithControlToKeyInput 'k' ->

                // See ':vimhelp i_CTRL-G_<Up>'.
                x.BreakUndoSequence "Line up to insert start column"
                if _operations.MoveCaretWithArrow CaretMovement.Up then
                    x.MoveToInsertStartColumn()

        | keyInput when
            keyInput = KeyInputUtil.VimKeyToKeyInput VimKey.Down
            || keyInput = KeyInputUtil.CharToKeyInput 'j'
            || keyInput = KeyInputUtil.CharWithControlToKeyInput 'j' ->

                // See ':vimhelp i_CTRL-G_<Down>'.
                x.BreakUndoSequence "Line down to insert start column"
                if _operations.MoveCaretWithArrow CaretMovement.Down then
                    x.MoveToInsertStartColumn()

        | _ ->
            _operations.Beep()

        ProcessResult.Handled ModeSwitch.NoSwitch

    /// Move to the insert start column
    member x.MoveToInsertStartColumn () =
        match _vimBuffer.VimTextBuffer.InsertStartPoint with
        | Some startPoint ->
            let spaces = SnapshotColumn(startPoint).GetSpacesToColumn _localSettings.TabStop
            let column = SnapshotColumn.GetColumnForSpacesOrEnd(x.CaretLine, spaces, _localSettings.TabStop)
            _operations.MoveCaretToColumn column ViewFlags.Standard
        | None ->
            ()

    /// Process the second key of a digraph command
    member x.ProcessDigraph1 firstKeyInput = 
        if firstKeyInput = KeyInputUtil.EscapeKey then
            _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.None }
        elif firstKeyInput.RawChar.IsNone then
            let keyInputSet = KeyInputSet(firstKeyInput)
            let text = KeyNotationUtil.GetDisplayName firstKeyInput
            let commandFlags = CommandFlags.Repeatable ||| CommandFlags.InsertEdit
            x.RunInsertCommand (InsertCommand.Insert text) keyInputSet commandFlags |> ignore
            _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.None }
        else
            _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.Digraph2 firstKeyInput }
        ProcessResult.Handled ModeSwitch.NoSwitch

    /// Process the third key of a digraph command
    member x.ProcessDigraph2 secondKeyInput = 
        try
            if secondKeyInput = KeyInputUtil.EscapeKey then
                ProcessResult.Handled ModeSwitch.NoSwitch
            else
                match _sessionData.ActiveEditItem with
                | ActiveEditItem.Digraph2 firstKeyInput ->
                    if firstKeyInput = KeyInputUtil.CharToKeyInput(' ') then
                        string(char(int(secondKeyInput.Char) ||| 0x80))
                        |> x.InsertText
                    else
                        match x.TryInsertDigraph firstKeyInput secondKeyInput with
                        | Some processResult ->
                            processResult
                        | None ->
                            match x.TryInsertDigraph secondKeyInput firstKeyInput with
                            | Some processResult ->
                                processResult
                            | None ->
                                string(secondKeyInput.Char)
                                |> x.InsertText
                | _ ->
                    ProcessResult.Handled ModeSwitch.NoSwitch
        finally
            _sessionData <- { _sessionData with ActiveEditItem = ActiveEditItem.None }

    /// Process the key input set of a literal insertion session
    member x.ProcessLiteral (keyInputSet: KeyInputSet) =

        // Function to insert literal text, i.e. text not custom processed.
        let insertLiteral text =
            let insertCommand = InsertCommand.InsertLiteral text
            let commandFlags = CommandFlags.Repeatable ||| CommandFlags.InsertEdit
            x.RunInsertCommand insertCommand keyInputSet commandFlags |> ignore

        // Base converion helpers.
        let convertDecimal (chars: string) = Convert.ToInt32(chars)
        let convertOctal (chars: string) = Convert.ToInt32(chars, 8)
        let convertHex (chars: string) = Convert.ToInt32(chars, 16)

        // Process decimal, octal or hexadecimal digits. This returns a tuple
        // of whether the keys were processed and any key input that needs to
        // be reprocessed. See vim ':help i_CTRL-V_digit' for details.
        let processDigits literalFormat (keyInputSet: KeyInputSet) =
            let maxDigits, isDigit, convert =
                match literalFormat with
                | LiteralFormat.Decimal -> 3, CharUtil.IsDigit, convertDecimal
                | LiteralFormat.Octal -> 3, CharUtil.IsOctalDigit, convertOctal
                | LiteralFormat.Hexadecimal8 -> 2, CharUtil.IsHexDigit, convertHex
                | LiteralFormat.Hexadecimal16 -> 4, CharUtil.IsHexDigit, convertHex
                | LiteralFormat.Hexadecimal32 -> 8, CharUtil.IsHexDigit, convertHex
            let digits =
                keyInputSet.KeyInputs
                |> Seq.map (fun keyInput -> keyInput.Char)
                |> Seq.filter isDigit
                |> String.Concat
            if digits.Length = maxDigits || digits.Length < keyInputSet.Length then
                convert digits
                |> Char.ConvertFromUtf32
                |> insertLiteral
                let keyInput =
                    keyInputSet.KeyInputs
                    |> Seq.skip digits.Length
                    |> SeqUtil.tryHeadOnly
                true, keyInput
            else
                false, None

        // Try to process the key input set. See vim help 'i_CTRL-V' for
        // details.
        let processed, keyInputToReprocess =
            match keyInputSet.FirstKeyInput with
            | Some firstKeyInput when firstKeyInput.IsDigit ->
                processDigits LiteralFormat.Decimal keyInputSet
            | Some firstKeyInput when (Char.ToLower firstKeyInput.Char) = 'o' ->
                processDigits LiteralFormat.Octal keyInputSet.Rest
            | Some firstKeyInput when (Char.ToLower firstKeyInput.Char) = 'x' ->
                processDigits LiteralFormat.Hexadecimal8 keyInputSet.Rest
            | Some firstKeyInput when firstKeyInput.Char = 'u' ->
                processDigits LiteralFormat.Hexadecimal16 keyInputSet.Rest
            | Some firstKeyInput when firstKeyInput.Char = 'U' ->
                processDigits LiteralFormat.Hexadecimal32 keyInputSet.Rest
            | Some firstKeyInput when firstKeyInput.RawChar.IsSome ->
                firstKeyInput.Char
                |> string
                |> insertLiteral
                true, None
            | Some firstKeyInput ->
                KeyNotationUtil.GetDisplayName firstKeyInput
                |> insertLiteral
                true, None
            | None ->
                false, None

        // Update the active edit item.
        let activeEditItem =
            if processed then
                ActiveEditItem.None
            else
                ActiveEditItem.Literal keyInputSet
        _sessionData <- { _sessionData with ActiveEditItem = activeEditItem }

        // Reprocess any unprocessed key input.
        match keyInputToReprocess with
        | Some keyInput ->
            x.ProcessCore keyInput
        | None ->
            ProcessResult.Handled ModeSwitch.NoSwitch

    // Insert the raw characters associated with a key input set
    member x.InsertText (text: string): ProcessResult =
        let insertCommand = InsertCommand.Insert text
        let keyInputSet =
            text
            |> Seq.map KeyInputUtil.CharToKeyInput
            |> List.ofSeq
            |> (fun keyInputs -> KeyInputSet(keyInputs))
        let commandFlags = CommandFlags.Repeatable ||| CommandFlags.InsertEdit
        x.RunInsertCommand insertCommand keyInputSet commandFlags

    /// Try to process a key pair as a digraph and insert it
    member x.TryInsertDigraph firstKeyInput secondKeyInput =
        let digraphMap = _vimBuffer.Vim.DigraphMap
        match digraphMap.GetMapping firstKeyInput.Char secondKeyInput.Char with
        | Some code ->
            code
            |> DigraphUtil.GetText
            |> x.InsertText
            |> Some
        | None ->
            None

    /// Process the KeyInput value
    member x.Process (keyInputData: KeyInputData) = 
        let keyInput = keyInputData.KeyInput
        _isInProcess <- true
        try
            let result = x.ProcessCore keyInput
            match result with
            | ProcessResult.Handled (ModeSwitch.SwitchMode ModeKind.Normal) ->
                x.StopInsert keyInput
            | _ ->
                result
        finally
            _isInProcess <- false

    member x.ProcessCore keyInput =
        match _sessionData.ActiveEditItem with
        | ActiveEditItem.WordCompletion wordCompletionSession ->
            x.ProcessWithWordCompletionSession wordCompletionSession keyInput
        | ActiveEditItem.Paste ->
            x.ProcessPaste keyInput
        | ActiveEditItem.PasteSpecial pasteFlags ->
            x.Paste keyInput pasteFlags
        | ActiveEditItem.OverwriteReplace ->
            x.Paste keyInput PasteFlags.None
        | ActiveEditItem.None ->

            // Next try and process by examining the current change
            match x.ProcessWithCurrentChange keyInput with
            | Some result -> result
            | None ->
                match x.GetRawInsertCommand keyInput with
                | Some rawInsertCommand ->
                    match rawInsertCommand with
                    | RawInsertCommand.CustomCommand func -> func keyInput
                    | RawInsertCommand.InsertCommand (keyInputSet, insertCommand, commandFlags) -> x.RunInsertCommand insertCommand keyInputSet commandFlags
                | None -> ProcessResult.NotHandled

        | ActiveEditItem.Special ->
            x.ProcessSpecial keyInput
        | ActiveEditItem.Digraph1 ->
            x.ProcessDigraph1 keyInput
        | ActiveEditItem.Digraph2 _ ->
            x.ProcessDigraph2 keyInput
        | ActiveEditItem.Literal keyInputSet ->
            keyInputSet.Add keyInput |> x.ProcessLiteral

    /// Record special marks associated with a new insert point
    member x.ResetInsertPoint () =
        let insertPoint = Some x.CaretPoint
        _vimBuffer.VimTextBuffer.InsertStartPoint <- insertPoint
        _vimBuffer.VimTextBuffer.LastChangeOrYankStart <- insertPoint
        _vimBuffer.VimTextBuffer.LastChangeOrYankEnd <- insertPoint
        _vimBuffer.VimTextBuffer.IsSoftTabStopValidForBackspace <- true

    /// Raised when the caret position changes
    member x.OnCaretPositionChanged args = 

        // Because this is invoked only when are active but not processing a
        // command, it means that some other component (e.g. a language
        // service, ReSharper or Visual Assist) changed the caret position
        // programmatically.
        _textChangeTracker.CompleteChange()

        // Don't break the undo sequence if the caret was moved within the
        // active insertion region of the effective change. This allows code
        // assistants to perform a variety of edits without breaking the undo
        // sequence. With a little more work we could allow edits completely
        // outside the active insertion region without breaking the undo
        // sequence, and this would allow code assistants to e.g. automatically
        // add usings and have the command still be repeatable.
        let breakUndoSequence =
            match _textChangeTracker.EffectiveChange with
            | Some span ->
                let newPoint = args.NewPosition.BufferPosition
                VimTrace.TraceInfo("InsertMode:OnCaretPositionChanged {0}", newPoint.Position)
                not (span.Contains(newPoint) || span.End = newPoint)
            | None ->
                true

        if breakUndoSequence then
            x.BreakUndoSequence "Insert after motion"
            x.ChangeCombinedEditCommand None

        // This is now a separate insert.
        x.ResetInsertPoint()

    member x.OnAfterRunInsertCommand (insertCommand: InsertCommand) =

        match insertCommand with
        | InsertCommand.Insert " " ->

            // If the user typed a <Space> then 'sts' shouldn't be considered for <BS> operations
            // until the start point is reset 
            _vimBuffer.VimTextBuffer.IsSoftTabStopValidForBackspace <- false

        | InsertCommand.InsertNewLine ->
            _operations.AdjustTextViewForScrollOffset()

        | _ -> ()

        let updateRepeat count addNewLines textChange =

            let insertKind = 
                let commandTextChange = insertCommand.TextChange _editorOptions _textBuffer
                match commandTextChange with
                | None -> 
                    // Certain actions such as caret movement cause us to abandon the repeat session
                    // and move to a normal insert
                    InsertKind.Normal
                | Some otherTextChange ->
                    let textChange = TextChange.CreateReduced textChange otherTextChange
                    InsertKind.Repeat (count, addNewLines, textChange)

            _sessionData <- { _sessionData with InsertKind = insertKind }

        match _sessionData.InsertKind with
        | InsertKind.Normal -> ()
        | InsertKind.Repeat (count, addNewLines, textChange) -> updateRepeat count addNewLines textChange
        | InsertKind.Block _ -> ()

    /// Raised on the completion of a TextChange event.  This event is not raised immediately
    /// and instead is added to the CombinedEditCommand value for this session which will be 
    /// raised as a command at a later time
    member x.OnTextChangeCompleted (args: TextChangeEventArgs) =

        let textChange = args.TextChange
        let command = 
            let textChangeCommand = InsertCommand.OfTextChange textChange
            x.OnAfterRunInsertCommand textChangeCommand
            match _sessionData.CombinedEditCommand with
            | None -> textChangeCommand
            | Some command -> InsertCommand.Combined (command, textChangeCommand)
        x.ChangeCombinedEditCommand (Some command)

    member x.ChangeCombinedEditCommand (command: InsertCommand option) =

        let rec getText command = 
            match command with 
            | InsertCommand.Insert text -> Some text
            | InsertCommand.InsertLiteral text -> Some text
            | InsertCommand.InsertNewLine -> Some Environment.NewLine
            | InsertCommand.InsertTab -> Some "\t"
            | InsertCommand.Combined (left, right) ->
                match getText left, getText right with
                | Some left, Some right -> Some (left + right)
                | _ -> None
            | _ -> None

        match OptionUtil.map2 getText command with
        | Some text -> _vimBuffer.VimData.LastTextInsert <- Some text
        | _ -> ()

        _sessionData <- { _sessionData with CombinedEditCommand = command } 

    /// Raised when a global setting is changed
    member x.OnGlobalSettingsChanged (args: SettingEventArgs) =

        // The constructed command map depends on the value of the 'keymodel' setting so rebuild
        // if it ever changes 
        if not _commandMap.IsEmpty && args.IsValueChanged && args.Setting.Name = GlobalSettingNames.KeyModelName then
            x.BuildCommands()

    /// Called when the IVimBuffer is closed.  We need to unsubscribe from several events
    /// when this happens to prevent the ITextBuffer / ITextView from being kept alive
    member x.OnClose () =
        _bag.DisposeAll()

    /// Create a linked undo transaction suitable for insert mode
    member x.CreateLinkedUndoTransaction name =
        let flags = LinkedUndoTransactionFlags.CanBeEmpty ||| LinkedUndoTransactionFlags.EndsWithInsert
        _undoRedoOperations.CreateLinkedUndoTransactionWithFlags name flags

    /// Entering an insert or replace mode.  Setup the InsertSessionData based on the 
    /// ModeArgument value. 
    member x.OnEnter arg =
        x.EnsureCommandsBuilt()
        _insertUtil.NewUndoSequence()

        // Record start point upon initial entry to insert mode.
        x.ResetInsertPoint()

        // Suppress change marks, which would be too fine-grained. We'll manually
        // keep them updated and this will avoid a lot of tracking point churn.
        _textChangeTracker.SuppressLastChangeMarks <- true

        // When starting insert mode we want to track the edits to the IVimBuffer as a 
        // text change
        _textChangeTracker.TrackCurrentChange <- true
        _textChangeTracker.StartTrackingEffectiveChange()

        // Set up transaction and kind of insert
        let transaction, insertKind =
            match arg with
            | ModeArgument.InsertBlock (blockSpan, visualInsertKind, transaction) ->
                Some transaction, InsertKind.Block (visualInsertKind, blockSpan)
            | ModeArgument.InsertWithCount count ->
                if count > 1 then
                    let transaction = x.CreateLinkedUndoTransaction "Insert with count"
                    Some transaction, InsertKind.Repeat (count, false, TextChange.Insert StringUtil.Empty)
                else
                    let transaction = x.CreateLinkedUndoTransaction "Insert"
                    Some transaction, InsertKind.Normal
            | ModeArgument.InsertWithCountAndNewLine (count, transaction) ->
                if count > 1 then
                    Some transaction, InsertKind.Repeat (count, true, TextChange.Insert StringUtil.Empty)
                else
                    Some transaction, InsertKind.Normal
            | ModeArgument.InsertWithTransaction transaction ->
                Some transaction, InsertKind.Normal
            | _ -> 
                let transaction = x.CreateLinkedUndoTransaction "Insert with transaction"
                Some transaction, InsertKind.Normal

        // If the LastCommand coming into insert / replace mode is not setup for linking 
        // with the next change then clear it out now.  This is needed to implement functions
        // like 'dw' followed by insert, <Esc> and immediately by '.'.  It should simply 
        // move the caret left
        match _vimBuffer.VimData.LastCommand with
        | None ->
            ()
        | Some lastCommand ->
            if not (Util.IsFlagSet lastCommand.CommandFlags CommandFlags.LinkedWithNextCommand) then
                _vimBuffer.VimData.LastCommand <- None

        _sessionData <- {
            Transaction = transaction
            InsertKind = insertKind
            CombinedEditCommand = None
            ActiveEditItem = ActiveEditItem.None
            SuppressBreakUndoSequence = false
            SuppressAbbreviation = false
        }

        // If this is replace mode then go ahead and setup overwrite
        if _isReplace then
            _editorOptions.SetOptionValue(DefaultTextViewOptions.OverwriteModeId, true)

    /// Called when leaving insert mode.  Here we will do any remaining cleanup on the
    /// InsertSessionData.  It's possible to get here with active session data if there
    /// is an exception during the processing of the transaction.
    ///
    /// Or more sinister.  A simple API call to OnLeave could force us to leave while 
    /// a transaction was open
    member x.OnLeave () =

        // When leaving insert mode we complete the current change
        _textChangeTracker.CompleteChange()
        _textChangeTracker.TrackCurrentChange <- false
        _textChangeTracker.StopTrackingEffectiveChange()
        _textChangeTracker.SuppressLastChangeMarks <- false

        // Escape might have moved the caret back, but it recorded the correct value.
        _vimBuffer.VimTextBuffer.LastChangeOrYankEnd <- _vimBuffer.VimTextBuffer.LastInsertExitPoint

        // Possibly raise the edit command.  This will have already happened if <Esc> was used
        // to exit insert mode.  This case takes care of being asked to exit programmatically 
        x.CompleteCombinedEditCommand()

        // Dismiss any active ICompletionSession 
        x.CancelWordCompletionSession()

        // The 'start' point is not valid when we are not in insert mode 
        _vimBuffer.VimTextBuffer.InsertStartPoint <- None
        _vimBuffer.VimTextBuffer.IsSoftTabStopValidForBackspace <- true

        try
            match _sessionData.Transaction with
            | None -> ()
            | Some transaction -> transaction.Complete()
        finally
            _sessionData <- _emptySessionData

        // If this is replace mode then go ahead and undo overwrite
        if _isReplace then
            _editorOptions.SetOptionValue(DefaultTextViewOptions.OverwriteModeId, false)

    interface IInsertMode with 
        member x.ActiveWordCompletionSession = x.ActiveWordCompletionSession
        member x.PasteCharacter = x.PasteCharacter
        member x.IsInPaste = x.IsInPaste
        member x.VimTextBuffer = _vimBuffer.VimTextBuffer
        member x.CommandNames =  x.CommandNames
        member x.ModeKind = x.ModeKind
        member x.CanProcess keyInput = x.CanProcess keyInput
        member x.IsDirectInsert keyInput = x.IsDirectInsert keyInput
        member x.Process keyInputData = x.Process keyInputData
        member x.OnEnter arg = x.OnEnter arg
        member x.OnLeave () = x.OnLeave ()
        member x.OnClose() = x.OnClose ()

        [<CLIEvent>]
        member x.CommandRan = _commandRanEvent.Publish

