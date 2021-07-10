#light

namespace Vim.Modes.Command
open Vim
open Vim.Interpreter
open Microsoft.VisualStudio.Text
open System.Text.RegularExpressions

type internal CommandMode
    ( 
        _buffer: IVimBuffer, 
        _operations: ICommonOperations
    ) =

    let _commandChangedEvent = StandardEvent()
    let _commandRanEvent = StandardEvent<CommandEventArgs>()
    let _vimData = _buffer.VimData
    let _statusUtil = _buffer.VimBufferData.StatusUtil
    let _parser = Parser(_buffer.Vim.GlobalSettings, _vimData)
    let _vimHost = _buffer.Vim.VimHost

    static let BindDataError: MappedBindData<int> = {
        KeyRemapMode = KeyRemapMode.None;
        MappedBindFunction = fun _ -> MappedBindResult.Error
    }

    let mutable _command = EditableCommand.Empty
    let mutable _historySession: IHistorySession<int, int> option = None
    let mutable _bindData = BindDataError
    let mutable _keepSelection = false
    let mutable _isPartialCommand = false

    /// Currently queued up editable command
    member x.EditableCommand 
        with get() = _command
        and set value = 
            if value <> _command then
                _command <- value
                _commandChangedEvent.Trigger x

    /// Currently queued up command string
    member x.Command 
        with get() = x.EditableCommand.Text
        and set value = x.EditableCommand <- EditableCommand(value)

    member x.InPasteWait = 
        match _historySession with
        | Some historySession -> historySession.InPasteWait
        | None -> false

    member x.ParseAndRunInput (command: string) (wasMapped: bool) = 
        let command = 
            if command.Length > 0 && command.[0] = ':' then
                command.Substring(1)
            else
                command

        let lineCommand = _parser.ParseLineCommand command 

        // We clear the selection for all line commands except a host command,
        // which manages any selection clearing itself.
        match lineCommand with
        | LineCommand.HostCommand _ -> _keepSelection <- true
        | _ -> ()

        let vimInterpreter = _buffer.Vim.GetVimInterpreter _buffer
        vimInterpreter.RunLineCommand lineCommand
        if not wasMapped then
            _vimData.LastCommandLine <- command
            _vimData.LastLineCommand <- Some lineCommand

        let fullCommand = 
            match _parser.TryExpandCommandName command with
            | Some c -> c
            | None -> command
        _commandRanEvent.Trigger x (CommandEventArgs(command, fullCommand, wasMapped, lineCommand))

    // Command mode can be validly entered with the selection active.  Consider
    // hitting ':' in Visual Mode.  The selection should be cleared when leaving
    member x.MaybeClearSelection moveCaretToStart = 
        let selection = _buffer.TextView.Selection
        if not selection.IsEmpty && not _buffer.TextView.IsClosed && not _keepSelection then 
            if moveCaretToStart then
                let point = selection.StreamSelectionSpan.SnapshotSpan.Start
                TextViewUtil.ClearSelection _buffer.TextView
                TextViewUtil.MoveCaretToPoint _buffer.TextView point
            else 
                TextViewUtil.ClearSelection _buffer.TextView

    member x.Process (keyInputData: KeyInputData) =
        match _bindData.MappedBindFunction keyInputData with
        | MappedBindResult.Complete _ ->
            _bindData <- BindDataError

            // It is possible for the execution of the command to change the mode (say :s.../c) 
            if _buffer.ModeKind = ModeKind.Command then
                if _isPartialCommand then
                    ProcessResult.OfModeKind ModeKind.Normal
                else
                    ProcessResult.Handled ModeSwitch.SwitchPreviousMode
            else 
                ProcessResult.Handled ModeSwitch.NoSwitch
        | MappedBindResult.Cancelled ->
            _bindData <- BindDataError
            ProcessResult.OfModeKind ModeKind.Normal
        | MappedBindResult.Error ->
            _bindData <- BindDataError
            ProcessResult.OfModeKind ModeKind.Normal
        | MappedBindResult.NeedMoreInput bindData ->
            _bindData <- bindData
            ProcessResult.HandledNeedMoreInput

    member x.CreateHistorySession() =

        // The ProcessCommand call back just means a new command state was reached.  Until it's
        // completed we just keep updating the current state 
        let processCommand command = 
            x.EditableCommand <- command
            0

        /// Run the specified command
        let completed command wasMapped =
            x.EditableCommand <- EditableCommand.Empty
            x.ParseAndRunInput command wasMapped
            x.MaybeClearSelection false
            0

        /// User cancelled input.  Reset the selection
        let cancelled () = 
            x.EditableCommand <- EditableCommand.Empty
            x.MaybeClearSelection true

        // First key stroke.  Create a history client and get going
        let historyClient = {
            new IHistoryClient<int, int> with
                member this.HistoryList = _vimData.CommandHistory
                member this.RegisterMap = _buffer.RegisterMap
                member this.RemapMode = KeyRemapMode.Command
                member this.Beep() = _operations.Beep()
                member this.ProcessCommand _ command = processCommand command
                member this.Completed _ command wasMapped = completed command.Text wasMapped
                member this.Cancelled _ = cancelled ()
            }
        HistoryUtil.CreateHistorySession historyClient 0 _command _buffer.VimTextBuffer.LocalAbbreviationMap _buffer.MotionUtil

    member x.OnEnter (arg: ModeArgument) = 
        let historySession = x.CreateHistorySession()

        _command <- EditableCommand.Empty
        _historySession <- Some historySession
        _bindData <- historySession.CreateBindDataStorage().CreateMappedBindData()
        _keepSelection <- false
        _isPartialCommand <- false

        arg.CompleteAnyTransaction()

        let commandTextWithCount count = 
            match count with
            | Some c ->
                if c = 1 then
                    "." 
                elif 1 < c then
                    sprintf ".,.+%i" (c - 1)
                else
                    StringUtil.Empty
            | Option.None -> StringUtil.Empty

        let commandText = 
            match arg with
            | ModeArgument.CommandWithCount count -> _isPartialCommand <- true; commandTextWithCount count
            | ModeArgument.PartialCommand command -> _isPartialCommand <- true; command
            | _ -> StringUtil.Empty

        if not (StringUtil.IsNullOrEmpty commandText) then
            EditableCommand(commandText)
            |> x.ChangeCommand

    member x.OnLeave() = 
        x.MaybeClearSelection true
        _command <- EditableCommand.Empty
        _historySession <- None
        _bindData <- BindDataError
        _keepSelection <- false
        _isPartialCommand <- false

    /// Called externally to update the command.  Do this by modifying the history 
    /// session.  If we aren't in command mode currently then this is a no-op 
    member x.ChangeCommand (command: EditableCommand) = 
        match _historySession with
        | None -> ()
        | Some historySession -> historySession.ResetCommand command

    interface ICommandMode with
        member x.VimTextBuffer = _buffer.VimTextBuffer
        member x.EditableCommand 
            with get() = x.EditableCommand
            and set value = x.ChangeCommand value
        member x.Command
            with get() = x.Command
            and set value = EditableCommand(value) |> x.ChangeCommand
        member x.CommandNames = HistoryUtil.CommandNames |> Seq.map KeyInputSetUtil.Single
        member x.InPasteWait = x.InPasteWait
        member x.ModeKind = ModeKind.Command
        member x.CanProcess keyInput = KeyInputUtil.IsCore keyInput && not keyInput.IsMouseKey
        member x.Process keyInputData = x.Process keyInputData
        member x.OnEnter arg = x.OnEnter arg
        member x.OnLeave () = x.OnLeave ()
        member x.OnClose() = ()

        member x.RunCommand command = 
            x.ParseAndRunInput command true

        [<CLIEvent>]
        member x.CommandChanged = _commandChangedEvent.Publish

        [<CLIEvent>]
        member x.CommandRan = _commandRanEvent.Publish


