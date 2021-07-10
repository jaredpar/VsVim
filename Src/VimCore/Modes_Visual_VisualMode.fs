#light

namespace Vim.Modes.Visual
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Operations
open Microsoft.VisualStudio.Text.Editor
open Vim
open Vim.Modes

type internal VisualMode
    (
        _vimBufferData: IVimBufferData,
        _operations: ICommonOperations,
        _motionUtil: IMotionUtil,
        _visualKind: VisualKind,
        _runner: ICommandRunner,
        _capture: IMotionCapture,
        _selectionTracker: ISelectionTracker
    ) = 

    let _vimTextBuffer = _vimBufferData.VimTextBuffer
    let _textView = _vimBufferData.TextView
    let _textBuffer = _vimTextBuffer.TextBuffer
    let _globalSettings = _vimTextBuffer.GlobalSettings
    let _eventHandlers = DisposableBag()
    let _operationKind, _modeKind = 
        match _visualKind with
        | VisualKind.Character -> (OperationKind.CharacterWise, ModeKind.VisualCharacter)
        | VisualKind.Line -> (OperationKind.LineWise, ModeKind.VisualLine)
        | VisualKind.Block -> (OperationKind.CharacterWise, ModeKind.VisualBlock)

    // Command to show when entering command from Visual Mode	
    static let CommandFromVisualModeString = "'<,'>"

    /// Get a mark and use the provided 'func' to create a Motion value
    static let BindMark func = 
        let bindFunc (keyInput: KeyInput) =
            match Mark.OfChar keyInput.Char with
            | None -> BindResult<NormalCommand>.Error
            | Some localMark -> BindResult<_>.Complete (func localMark)
        let bindData = {
            KeyRemapMode = KeyRemapMode.None
            BindFunction = bindFunc }
        BindDataStorage<_>.Simple bindData

    static let SharedCommands = 
        let visualSeq = 
            seq {
                yield ("c", CommandFlags.Repeatable ||| CommandFlags.LinkedWithNextCommand, VisualCommand.ChangeSelection)
                yield ("C", CommandFlags.Repeatable ||| CommandFlags.LinkedWithNextCommand, VisualCommand.ChangeLineSelection true)
                yield ("d", CommandFlags.Repeatable, VisualCommand.DeleteSelection)
                yield ("D", CommandFlags.Repeatable, VisualCommand.DeleteLineSelection)
                yield ("gf", CommandFlags.None, VisualCommand.GoToFileInSelection)
                yield ("gJ", CommandFlags.Repeatable, VisualCommand.JoinSelection JoinKind.KeepEmptySpaces)
                yield ("gp", CommandFlags.Repeatable, VisualCommand.PutOverSelection true)
                yield ("gP", CommandFlags.Repeatable, VisualCommand.PutOverSelection true)
                yield ("g?", CommandFlags.Repeatable, VisualCommand.ChangeCase ChangeCharacterKind.Rot13)
                yield ("gn", CommandFlags.Repeatable, VisualCommand.ExtendSelectionToNextMatch SearchPath.Forward)
                yield ("gN", CommandFlags.Repeatable, VisualCommand.ExtendSelectionToNextMatch SearchPath.Backward)
                yield ("J", CommandFlags.Repeatable, VisualCommand.JoinSelection JoinKind.RemoveEmptySpaces)
                yield ("o", CommandFlags.Movement ||| CommandFlags.ResetAnchorPoint, VisualCommand.InvertSelection false)
                yield ("O", CommandFlags.Movement ||| CommandFlags.ResetAnchorPoint, VisualCommand.InvertSelection true)
                yield ("p", CommandFlags.Repeatable, VisualCommand.PutOverSelection false)
                yield ("P", CommandFlags.Repeatable, VisualCommand.PutOverSelection false)
                yield ("R", CommandFlags.Repeatable ||| CommandFlags.LinkedWithNextCommand, VisualCommand.ChangeLineSelection false)
                yield ("s", CommandFlags.Repeatable ||| CommandFlags.LinkedWithNextCommand, VisualCommand.ChangeSelection)
                yield ("S", CommandFlags.Repeatable ||| CommandFlags.LinkedWithNextCommand, VisualCommand.ChangeLineSelection false)
                yield ("u", CommandFlags.Repeatable, VisualCommand.ChangeCase ChangeCharacterKind.ToLowerCase)
                yield ("U", CommandFlags.Repeatable, VisualCommand.ChangeCase ChangeCharacterKind.ToUpperCase)
                yield ("v", CommandFlags.Special, VisualCommand.SwitchModeVisual VisualKind.Character)
                yield ("V", CommandFlags.Special, VisualCommand.SwitchModeVisual VisualKind.Line)
                yield ("x", CommandFlags.Repeatable, VisualCommand.DeleteSelection)
                yield ("X", CommandFlags.Repeatable, VisualCommand.DeleteLineSelection)
                yield ("y", CommandFlags.ResetCaret, VisualCommand.YankSelection)
                yield ("Y", CommandFlags.ResetCaret, VisualCommand.YankLineSelection)
                yield ("zf", CommandFlags.None, VisualCommand.FoldSelection)
                yield ("zF", CommandFlags.None, VisualCommand.FoldSelection)
                yield ("zo", CommandFlags.Special, VisualCommand.OpenFoldInSelection)
                yield ("zO", CommandFlags.Special, VisualCommand.OpenAllFoldsInSelection)
                yield ("zc", CommandFlags.Special, VisualCommand.CloseFoldInSelection)
                yield ("zC", CommandFlags.Special, VisualCommand.CloseAllFoldsInSelection)
                yield ("za", CommandFlags.Special, VisualCommand.ToggleFoldInSelection)
                yield ("zA", CommandFlags.Special, VisualCommand.ToggleAllFoldsInSelection)
                yield ("zd", CommandFlags.Special, VisualCommand.DeleteAllFoldsInSelection)
                yield ("zD", CommandFlags.Special, VisualCommand.DeleteAllFoldsInSelection)
                yield ("<C-c>", CommandFlags.Special, VisualCommand.CancelOperation)
                yield ("<C-q>", CommandFlags.Special, VisualCommand.SwitchModeVisual VisualKind.Block)
                yield ("<C-v>", CommandFlags.Special, VisualCommand.SwitchModeVisual VisualKind.Block)
                yield ("<S-i>", CommandFlags.Special, VisualCommand.SwitchModeInsert VisualInsertKind.Start)
                yield ("<S-a>", CommandFlags.Special, VisualCommand.SwitchModeInsert VisualInsertKind.End)
                yield ("<C-g>", CommandFlags.Special, VisualCommand.SwitchModeOtherVisual)
                yield ("<C-w>gf", CommandFlags.None, VisualCommand.GoToFileInSelectionInNewWindow)
                yield ("<Del>", CommandFlags.Repeatable, VisualCommand.DeleteSelection)
                yield ("<lt>", CommandFlags.Repeatable, VisualCommand.ShiftLinesLeft)
                yield (">", CommandFlags.Repeatable, VisualCommand.ShiftLinesRight)
                yield ("~", CommandFlags.Repeatable, VisualCommand.ChangeCase ChangeCharacterKind.ToggleCase)
                yield ("=", CommandFlags.Repeatable, VisualCommand.FormatCodeLines)
                yield ("gq", CommandFlags.Repeatable, VisualCommand.FormatTextLines false)
                yield ("gw", CommandFlags.Repeatable, VisualCommand.FormatTextLines true)
                yield ("gx", CommandFlags.Repeatable, VisualCommand.OpenLinkInSelection)
                yield ("!", CommandFlags.Repeatable, VisualCommand.FilterLines)
                yield ("<C-a>", CommandFlags.Repeatable, VisualCommand.AddToSelection false)
                yield ("<C-x>", CommandFlags.Repeatable, VisualCommand.SubtractFromSelection false)
                yield ("g<C-a>", CommandFlags.Repeatable, VisualCommand.AddToSelection true)
                yield ("g<C-x>", CommandFlags.Repeatable, VisualCommand.SubtractFromSelection true)
                yield ("<LeftMouse>", CommandFlags.Special, VisualCommand.MoveCaretToMouse)
                yield ("<LeftDrag>", CommandFlags.Special, VisualCommand.ExtendSelectionForMouseDrag)
                yield ("<LeftRelease>", CommandFlags.Special, VisualCommand.ExtendSelectionForMouseRelease)
                yield ("<S-LeftMouse>", CommandFlags.Special, VisualCommand.ExtendSelectionForMouseClick)
                yield ("<2-LeftMouse>", CommandFlags.Special, VisualCommand.SelectWordOrMatchingTokenAtMousePoint)
                yield ("<3-LeftMouse>", CommandFlags.Special, VisualCommand.SelectLine)
                yield ("<4-LeftMouse>", CommandFlags.Special, VisualCommand.SelectBlock)

                // Multi-selection bindings not in Vim.
                yield ("<C-A-LeftMouse>", CommandFlags.Special, VisualCommand.AddCaretAtMousePoint)
                yield ("<C-A-2-LeftMouse>", CommandFlags.Special, VisualCommand.AddWordOrMatchingTokenAtMousePointToSelection)
                yield ("<C-A-Up>", CommandFlags.Special, VisualCommand.AddSelectionOnAdjacentLine Direction.Up)
                yield ("<C-A-Down>", CommandFlags.Special, VisualCommand.AddSelectionOnAdjacentLine Direction.Down)
                yield ("<C-A-n>", CommandFlags.Special, VisualCommand.StartMultiSelection)
            } |> Seq.map (fun (str, flags, command) -> 
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                CommandBinding.VisualBinding (keyInputSet, flags, command))

        let complexSeq = 
            seq {
                yield ("r", CommandFlags.Repeatable, BindData<_>.CreateForKeyInput KeyRemapMode.Language VisualCommand.ReplaceSelection)
            } |> Seq.map (fun (str, flags, bindCommand) -> 
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                let storage = BindDataStorage.Simple bindCommand
                CommandBinding.ComplexVisualBinding (keyInputSet, flags, storage))

        let normalSeq = 
            seq {
                yield ("gv", CommandFlags.Special, NormalCommand.SwitchPreviousVisualMode)
                yield ("zE", CommandFlags.Special, NormalCommand.DeleteAllFoldsInBuffer)
                yield ("zM", CommandFlags.Special, NormalCommand.CloseAllFolds)
                yield ("zR", CommandFlags.Special, NormalCommand.OpenAllFolds)
                yield ("[p", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield ("[P", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield ("]p", CommandFlags.Repeatable, NormalCommand.PutAfterCaretWithIndent)
                yield ("]P", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield (":", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.Command, (ModeArgument.PartialCommand CommandFromVisualModeString)))
            } |> Seq.map (fun (str, flags, command) -> 
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                CommandBinding.NormalBinding (keyInputSet, flags, command))

        let normalComplexSeq = 
            seq {
                yield ("'", CommandFlags.Movement, BindMark NormalCommand.JumpToMarkLine)
                yield ("`", CommandFlags.Movement, BindMark NormalCommand.JumpToMark)
            } |> Seq.map (fun (str, flags, storage) -> 
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                CommandBinding.ComplexNormalBinding (keyInputSet, flags, storage))

        Seq.append visualSeq complexSeq 
        |> Seq.append normalSeq
        |> Seq.append normalComplexSeq
        |> List.ofSeq

    let mutable _builtCommands = false

    member x.CurrentSnapshot = _textBuffer.CurrentSnapshot

    member x.CaretPoint = TextViewUtil.GetCaretPoint _textView

    member x.CanProcess (keyInput: KeyInput) =
        KeyInputUtil.IsCore keyInput && not keyInput.IsMouseKey
        ||_runner.DoesCommandStartWith keyInput
        || x.KeyRemapMode = KeyRemapMode.Language && KeyInputUtil.IsTextInput keyInput

    member x.CommandNames = 
        x.EnsureCommandsBuilt()
        _runner.Commands |> Seq.map (fun command -> command.KeyInputSet)

    member this.KeyRemapMode = 
        if _runner.IsWaitingForMoreInput then
            _runner.KeyRemapMode
        else
            KeyRemapMode.Visual

    member x.SelectedSpan = (TextSelectionUtil.GetStreamSelectionSpan _textView.Selection).SnapshotSpan

    member x.EnsureCommandsBuilt() =
        if not _builtCommands then
            let factory = CommandFactory(_operations, _capture)

            // Add in the standard commands
            factory.CreateMovementCommands()
            |> Seq.append (factory.CreateMovementTextObjectCommands())
            |> Seq.append (factory.CreateScrollCommands())
            |> Seq.append SharedCommands
            |> Seq.iter _runner.Add 

            // Add in macro editing
            factory.CreateMacroEditCommands _runner _vimTextBuffer.Vim.MacroRecorder _eventHandlers

            _builtCommands <- true

    member x.OnEnter modeArgument = 
        x.EnsureCommandsBuilt()
        _selectionTracker.RecordCaretTrackingPoint modeArgument
        _selectionTracker.Start()

    member x.OnClose() =
        _eventHandlers.DisposeAll()

    /// Called when the Visual Mode is left.  Need to update the LastVisualSpan based on our selection
    member x.OnLeave() =
        _runner.ResetState()
        _selectionTracker.Stop()

    /// The current Visual Selection
    member x.VisualSelection =
        let selectionKind = _globalSettings.SelectionKind
        let tabStop = _vimBufferData.LocalSettings.TabStop
        let useVirtualSpace = _vimTextBuffer.UseVirtualSpace
        let visualSelection =
            VisualSelection.CreateForVirtualSelection _textView _visualKind selectionKind tabStop useVirtualSpace
        let isMaintainingEndOfLine = _vimBufferData.MaintainCaretColumn.IsMaintainingEndOfLine
        visualSelection.AdjustWithEndOfLine isMaintainingEndOfLine

    member x.Process (keyInputData: KeyInputData) =  
        let keyInput = keyInputData.KeyInput

        // Save the last visual selection at the global level for use with [count]V|v except
        // in the case of <Esc>. This <Esc> exception is not a documented behavior but exists
        // experimentally. 
        match keyInput, _vimTextBuffer.LastVisualSelection with
        | keyInput, Some lastVisualSelection when keyInput <> KeyInputUtil.EscapeKey ->
            let vimData = _vimBufferData.Vim.VimData
            match StoredVisualSelection.CreateFromVisualSpan lastVisualSelection.VisualSpan with
            | None -> ()
            | Some v -> vimData.LastVisualSelection <- Some v
        | _ -> ()

        let result = 
            if keyInput = KeyInputUtil.EscapeKey && x.ShouldHandleEscape then
                ProcessResult.Handled ModeSwitch.SwitchPreviousMode
            else
                match _runner.Run keyInput with
                | BindResult.NeedMoreInput _ -> 
                    _selectionTracker.UpdateSelection()
                    ProcessResult.HandledNeedMoreInput

                | BindResult.Complete commandRanData ->

                    if Util.IsFlagSet commandRanData.CommandBinding.CommandFlags CommandFlags.ResetCaret then
                        x.ResetCaret()

                    if Util.IsFlagSet commandRanData.CommandBinding.CommandFlags CommandFlags.ResetAnchorPoint then
                        match _vimBufferData.VisualAnchorPoint |>  OptionUtil.map2 (TrackingPointUtil.GetPoint x.CurrentSnapshot) with
                        | None -> ()
                        | Some anchorPoint ->
                            let anchorPoint = VirtualSnapshotPointUtil.OfPoint anchorPoint
                            _selectionTracker.UpdateSelectionWithAnchorPoint anchorPoint

                    match commandRanData.CommandResult with
                    | CommandResult.Error ->
                        _selectionTracker.UpdateSelection()
                    | CommandResult.Completed modeSwitch ->
                        match modeSwitch with
                        | ModeSwitch.NoSwitch -> _selectionTracker.UpdateSelection()
                        | ModeSwitch.SwitchMode(_) -> ()
                        | ModeSwitch.SwitchModeWithArgument(_,_) -> ()
                        | ModeSwitch.SwitchPreviousMode -> ()
                        | ModeSwitch.SwitchModeOneTimeCommand _ -> ()

                    ProcessResult.OfCommandResult commandRanData.CommandResult
                | BindResult.Error ->
                    _selectionTracker.UpdateSelection()
                    _operations.Beep()
                    if keyInput.IsMouseKey then
                        ProcessResult.NotHandled
                    else
                        ProcessResult.Handled ModeSwitch.NoSwitch
                | BindResult.Cancelled -> 
                    _selectionTracker.UpdateSelection()
                    ProcessResult.Handled ModeSwitch.NoSwitch

        // If we are switching out Visual Mode then reset the selection.  Only do this if 
        // we are the active IMode.  It's very possible that we were switched out already 
        // as part of a complex command
        if result.IsAnySwitch && _selectionTracker.IsRunning then

            // On teardown we will get calls to Stop when the view is closed.  It's invalid to access 
            // the selection at that point
            if not _textView.IsClosed then
                if result.IsAnySwitchToVisual then
                    _selectionTracker.UpdateSelection()
                elif not result.IsAnySwitchToCommand then
                    TextViewUtil.ClearSelection _textView
                    _textView.Selection.Mode <- TextSelectionMode.Stream

        result

    /// Certain operations cause the caret to be reset to it's position before visual mode 
    /// started once visual mode is complete (y for example).  This function handles that
    member x.ResetCaret() =

        // Calculate the start point of the selection
        let startPoint = 

            match _vimBufferData.VisualCaretStartPoint with
            | None -> None
            | Some trackingPoint -> TrackingPointUtil.GetPoint x.CurrentSnapshot trackingPoint

        match startPoint with
        | None ->
            // If we couldn't calculate a start point then there is just nothing to do.  This
            // really shouldn't happen though
            ()
        | Some startPoint ->

            // Calculate the actual point to use.  If the selection turned backwards then we
            // prefer the current caret over the original
            let point =
                if startPoint.Position < x.CaretPoint.Position then 
                    startPoint
                else 
                    x.CaretPoint
            _operations.MoveCaretToPoint point ViewFlags.Standard

    member x.ShouldHandleEscape = not _runner.IsHandlingEscape

    member x.SyncSelection() =
        if _selectionTracker.IsRunning then
            _selectionTracker.Stop()
            _selectionTracker.Start()

    interface IMode with
        member x.VimTextBuffer = _vimTextBuffer
        member x.CommandNames = x.CommandNames
        member x.ModeKind = _modeKind
        member x.CanProcess keyInput = x.CanProcess keyInput
        member x.Process keyInputData =  x.Process keyInputData
        member x.OnEnter modeArgument = x.OnEnter modeArgument
        member x.OnLeave () = x.OnLeave()
        member x.OnClose() = x.OnClose()

    interface IVisualMode with
        member x.CommandRunner = _runner
        member x.KeyRemapMode = x.KeyRemapMode
        member x.InCount = _runner.InCount
        member x.VisualSelection = x.VisualSelection
        member x.SyncSelection () = x.SyncSelection()



