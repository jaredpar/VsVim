#light

namespace Vim.Modes.Normal
open Vim
open Vim.Modes
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor

type internal NormalModeData = {
    Command: string
    InReplace: bool
}

type internal NormalMode 
    ( 
        _vimBufferData: IVimBufferData,
        _operations: ICommonOperations,
        _motionUtil: IMotionUtil,
        _runner: ICommandRunner,
        _capture: IMotionCapture,
        _incrementalSearch: IIncrementalSearch
    ) as this =

    let _vimTextBuffer = _vimBufferData.VimTextBuffer
    let _textView = _vimBufferData.TextView
    let _localSettings = _vimTextBuffer.LocalSettings
    let _globalSettings = _vimTextBuffer.GlobalSettings
    let _statusUtil = _vimBufferData.StatusUtil

    /// Reset state for data in Normal Mode
    static let EmptyData = {
        Command = StringUtil.Empty
        InReplace = false
    }

    /// Contains the state information for Normal mode
    let mutable _data = EmptyData

    /// This is the list of commands that have the same key binding as the selection 
    /// commands and run when keymodel doesn't have startsel as a value
    let mutable _selectionAlternateCommands: CommandBinding list = List.empty

    let _eventHandlers = DisposableBag()

    /// The set of standard commands that are shared amongst all instances 
    static let SharedStandardCommands =
        let normalSeq = 
            seq {
                yield ("a", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertAfterCaret)
                yield ("A", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertAtEndOfLine)
                yield ("C", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.ChangeTillEndOfLine)
                yield ("cc", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.ChangeLines)
                yield ("dd", CommandFlags.Repeatable, NormalCommand.DeleteLines)
                yield ("D", CommandFlags.Repeatable, NormalCommand.DeleteTillEndOfLine)
                yield ("ga", CommandFlags.Special, NormalCommand.DisplayCharacterCodePoint)
                yield ("gf", CommandFlags.None, NormalCommand.GoToFileUnderCaret false)
                yield ("gJ", CommandFlags.Repeatable, NormalCommand.JoinLines JoinKind.KeepEmptySpaces)
                yield ("gh", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.SelectCharacter, ModeArgument.None))
                yield ("gH", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.SelectLine, ModeArgument.None))
                yield ("g<C-h>", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.SelectBlock, ModeArgument.None))
                yield ("gI", CommandFlags.None, NormalCommand.InsertAtStartOfLine)
                yield ("gp", CommandFlags.Repeatable, NormalCommand.PutAfterCaret true)
                yield ("gP", CommandFlags.Repeatable, NormalCommand.PutBeforeCaret true)
                yield ("gt", CommandFlags.Special, NormalCommand.GoToNextTab SearchPath.Forward)
                yield ("gT", CommandFlags.Special, NormalCommand.GoToNextTab SearchPath.Backward)
                yield ("gv", CommandFlags.Special, NormalCommand.SwitchPreviousVisualMode)
                yield ("gn", CommandFlags.Special, NormalCommand.SelectNextMatch SearchPath.Forward)
                yield ("gN", CommandFlags.Special, NormalCommand.SelectNextMatch SearchPath.Backward)
                yield ("gugu", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToLowerCase)
                yield ("guu", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToLowerCase)
                yield ("gUgU", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToUpperCase)
                yield ("gUU", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToUpperCase)
                yield ("g~g~", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToggleCase)
                yield ("g~~", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToggleCase)
                yield ("g?g?", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.Rot13)
                yield ("g??", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.Rot13)
                yield ("g&", CommandFlags.Special, NormalCommand.RepeatLastSubstitute (true, true))
                yield ("g<LeftMouse>", CommandFlags.Special, NormalCommand.GoToDefinitionUnderMouse)
                yield ("g8", CommandFlags.Special, NormalCommand.DisplayCharacterBytes)
                yield ("i", CommandFlags.None, NormalCommand.InsertBeforeCaret)
                yield ("I", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertAtFirstNonBlank)
                yield ("J", CommandFlags.Repeatable, NormalCommand.JoinLines JoinKind.RemoveEmptySpaces)
                yield ("o", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertLineBelow)
                yield ("O", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertLineAbove)
                yield ("p", CommandFlags.Repeatable, NormalCommand.PutAfterCaret false)
                yield ("P", CommandFlags.Repeatable, NormalCommand.PutBeforeCaret false)
                yield ("R", CommandFlags.Repeatable ||| CommandFlags.LinkedWithNextCommand, NormalCommand.ReplaceAtCaret)
                yield ("s", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.SubstituteCharacterAtCaret)
                yield ("S", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.ChangeLines)
                yield ("u", CommandFlags.Special, NormalCommand.Undo)
                yield ("U", CommandFlags.Special, NormalCommand.UndoLine)
                yield ("v", CommandFlags.Special, NormalCommand.SwitchModeVisualCommand VisualKind.Character)
                yield ("V", CommandFlags.Special, NormalCommand.SwitchModeVisualCommand VisualKind.Line)
                yield ("x", CommandFlags.Repeatable, NormalCommand.DeleteCharacterAtCaret)
                yield ("X", CommandFlags.Repeatable, NormalCommand.DeleteCharacterBeforeCaret)
                yield ("Y", CommandFlags.None, NormalCommand.YankLines)
                yield ("yy", CommandFlags.None, NormalCommand.YankLines)
                yield ("za", CommandFlags.Special, NormalCommand.ToggleFoldUnderCaret)
                yield ("zA", CommandFlags.Special, NormalCommand.ToggleAllFolds)
                yield ("zo", CommandFlags.Special, NormalCommand.OpenFoldUnderCaret)
                yield ("zO", CommandFlags.Special, NormalCommand.OpenAllFoldsUnderCaret)
                yield ("zc", CommandFlags.Special, NormalCommand.CloseFoldUnderCaret)
                yield ("zC", CommandFlags.Special, NormalCommand.CloseAllFoldsUnderCaret)
                yield ("zd", CommandFlags.Special, NormalCommand.DeleteFoldUnderCaret)
                yield ("zD", CommandFlags.Special, NormalCommand.DeleteAllFoldsUnderCaret)
                yield ("zE", CommandFlags.Special, NormalCommand.DeleteAllFoldsInBuffer)
                yield ("zF", CommandFlags.Special, NormalCommand.FoldLines)
                yield ("zM", CommandFlags.Special, NormalCommand.CloseAllFolds)
                yield ("zR", CommandFlags.Special, NormalCommand.OpenAllFolds)
                yield ("ZQ", CommandFlags.Special, NormalCommand.CloseBuffer)
                yield ("ZZ", CommandFlags.Special, NormalCommand.WriteBufferAndQuit)
                yield ("<Insert>", CommandFlags.None, NormalCommand.InsertBeforeCaret)
                yield ("<C-a>", CommandFlags.Repeatable, NormalCommand.AddToWord)
                yield ("<C-c>", CommandFlags.Special, NormalCommand.CancelOperation)
                yield ("<C-g>", CommandFlags.Special, NormalCommand.PrintFileInformation)
                yield ("<C-i>", CommandFlags.Movement, NormalCommand.JumpToNewerPosition)
                yield ("<C-o>", CommandFlags.Movement, NormalCommand.JumpToOlderPosition)
                yield ("<C-PageDown>", CommandFlags.Special, NormalCommand.GoToNextTab SearchPath.Forward)
                yield ("<C-PageUp>", CommandFlags.Special, NormalCommand.GoToNextTab SearchPath.Backward)
                yield ("<C-q>", CommandFlags.Special, NormalCommand.SwitchModeVisualCommand VisualKind.Block)
                yield ("<C-r>", CommandFlags.Special, NormalCommand.Redo)
                yield ("<C-v>", CommandFlags.Special, NormalCommand.SwitchModeVisualCommand VisualKind.Block)
                yield ("<C-w>c", CommandFlags.None, NormalCommand.CloseWindow)
                yield ("<C-w><C-j>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Down)
                yield ("<C-w>j", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Down)
                yield ("<C-w><C-Down>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Down)
                yield ("<C-w><Down>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Down)
                yield ("<C-w><C-k>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Up)
                yield ("<C-w>k", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Up)
                yield ("<C-w><C-Up>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Up)
                yield ("<C-w><Up>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Up)
                yield ("<C-w><C-l>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Right)
                yield ("<C-w>l", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Right)
                yield ("<C-w><C-Right>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Right)
                yield ("<C-w><Right>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Right)
                yield ("<C-w><C-h>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Left)
                yield ("<C-w>h", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Left)
                yield ("<C-w><C-Left>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Left)
                yield ("<C-w><Left>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Left)
                yield ("<C-w>J", CommandFlags.None, NormalCommand.GoToWindow WindowKind.FarDown)
                yield ("<C-w>K", CommandFlags.None, NormalCommand.GoToWindow WindowKind.FarUp)
                yield ("<C-w>L", CommandFlags.None, NormalCommand.GoToWindow WindowKind.FarRight)
                yield ("<C-w>H", CommandFlags.None, NormalCommand.GoToWindow WindowKind.FarLeft)
                yield ("<C-w><C-t>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Top)
                yield ("<C-w>t", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Top)
                yield ("<C-w><C-b>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Bottom)
                yield ("<C-w>b", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Bottom)
                yield ("<C-w><C-p>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Recent)
                yield ("<C-w>p", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Recent)
                yield ("<C-w><C-w>", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Next)
                yield ("<C-w>w", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Next)
                yield ("<C-w>W", CommandFlags.None, NormalCommand.GoToWindow WindowKind.Previous)
                yield ("<C-w><C-s>", CommandFlags.None, NormalCommand.SplitViewHorizontally)
                yield ("<C-w>s", CommandFlags.None, NormalCommand.SplitViewHorizontally)
                yield ("<C-w>S", CommandFlags.None, NormalCommand.SplitViewHorizontally)
                yield ("<C-w><C-v>", CommandFlags.None, NormalCommand.SplitViewVertically)
                yield ("<C-w>v", CommandFlags.None, NormalCommand.SplitViewVertically)
                yield ("<C-w><C-g><C-f>", CommandFlags.None, NormalCommand.GoToFileUnderCaret true)
                yield ("<C-w>gf", CommandFlags.None, NormalCommand.GoToFileUnderCaret true)
                yield ("<C-x>", CommandFlags.Repeatable, NormalCommand.SubtractFromWord)
                yield ("<C-]>", CommandFlags.Special, NormalCommand.GoToDefinition)
                yield ("<Del>", CommandFlags.Repeatable, NormalCommand.DeleteCharacterAtCaret)
                yield ("<C-LeftMouse>", CommandFlags.Special, NormalCommand.GoToDefinitionUnderMouse)
                yield ("<C-LeftRelease>", CommandFlags.Special, NormalCommand.NoOperation)
                yield ("<MiddleMouse>", CommandFlags.Special, NormalCommand.PutAfterCaretMouse)
                yield ("[p", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield ("[P", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield ("]p", CommandFlags.Repeatable, NormalCommand.PutAfterCaretWithIndent)
                yield ("]P", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield ("&", CommandFlags.Special, NormalCommand.RepeatLastSubstitute (false, false))
                yield (".", CommandFlags.Special, NormalCommand.RepeatLastCommand)
                yield ("<lt><lt>", CommandFlags.Repeatable, NormalCommand.ShiftLinesLeft)
                yield (">>", CommandFlags.Repeatable, NormalCommand.ShiftLinesRight)
                yield ("==", CommandFlags.Repeatable, NormalCommand.FormatCodeLines)
                yield ("gx", CommandFlags.Repeatable, NormalCommand.OpenLinkUnderCaret)
                yield ("gqgq", CommandFlags.Repeatable, NormalCommand.FormatTextLines false)
                yield ("gqq", CommandFlags.Repeatable, NormalCommand.FormatTextLines false)
                yield ("gwgw", CommandFlags.Repeatable, NormalCommand.FormatTextLines true)
                yield ("gww", CommandFlags.Repeatable, NormalCommand.FormatTextLines true)
                yield ("!!", CommandFlags.Repeatable, NormalCommand.FilterLines)
                yield (":", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.Command, (ModeArgument.CommandWithCount Option.None)))
                yield ("<C-^>", CommandFlags.None, NormalCommand.GoToRecentView)
                yield ("<LeftMouse>", CommandFlags.Special, NormalCommand.MoveCaretToMouse)
                yield ("<LeftDrag>", CommandFlags.Special, NormalCommand.SelectTextForMouseDrag)
                yield ("<LeftRelease>", CommandFlags.Special, NormalCommand.SelectTextForMouseRelease)
                yield ("<S-LeftMouse>", CommandFlags.Special, NormalCommand.SelectTextForMouseClick)
                yield ("<2-LeftMouse>", CommandFlags.Special, NormalCommand.SelectWordOrMatchingTokenAtMousePoint)
                yield ("<3-LeftMouse>", CommandFlags.Special, NormalCommand.SelectLine)
                yield ("<4-LeftMouse>", CommandFlags.Special, NormalCommand.SelectBlock)

                // Multi-selection bindings not in Vim.
                yield ("<C-A-LeftMouse>", CommandFlags.Special, NormalCommand.AddCaretAtMousePoint)
                yield ("<C-A-Up>", CommandFlags.Special, NormalCommand.AddCaretOnAdjacentLine Direction.Up)
                yield ("<C-A-Down>", CommandFlags.Special, NormalCommand.AddCaretOnAdjacentLine Direction.Down)
                yield ("<C-A-2-LeftMouse>", CommandFlags.Special, NormalCommand.SelectWordOrMatchingTokenAtMousePoint)
                yield ("<C-A-n>", CommandFlags.Special, NormalCommand.SelectWordOrMatchingToken)
                yield ("C-A-p>", CommandFlags.Special, NormalCommand.RestoreMultiSelection)
            } |> Seq.map (fun (str, flags, command) -> 
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                CommandBinding.NormalBinding (keyInputSet, flags, command))
            
        let motionSeq = 
            seq {
                yield ("c", CommandFlags.Change ||| CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.ChangeMotion)
                yield ("d", CommandFlags.Repeatable ||| CommandFlags.Delete, NormalCommand.DeleteMotion)
                yield ("gU", CommandFlags.Repeatable, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.ToUpperCase, motion)))
                yield ("gu", CommandFlags.Repeatable, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.ToLowerCase, motion)))
                yield ("g?", CommandFlags.Repeatable, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.Rot13, motion)))
                yield ("g~", CommandFlags.Repeatable, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.ToggleCase, motion)))
                yield ("y", CommandFlags.Yank, NormalCommand.Yank)
                yield ("zf", CommandFlags.None, NormalCommand.FoldMotion)
                yield ("<lt>", CommandFlags.Repeatable ||| CommandFlags.ShiftLeft, NormalCommand.ShiftMotionLinesLeft)
                yield (">", CommandFlags.Repeatable ||| CommandFlags.ShiftRight, NormalCommand.ShiftMotionLinesRight)
                yield ("!", CommandFlags.Repeatable, NormalCommand.FilterMotion)
                yield ("=", CommandFlags.Repeatable, NormalCommand.FormatCodeMotion)
                yield ("gq", CommandFlags.Repeatable, (fun motion -> NormalCommand.FormatTextMotion (false, motion)))
                yield ("gw", CommandFlags.Repeatable, (fun motion -> NormalCommand.FormatTextMotion (true, motion)))
            } |> Seq.map (fun (str, flags, command) -> 
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                CommandBinding.MotionBinding (keyInputSet, flags, command))

        Seq.append normalSeq motionSeq 
        |> List.ofSeq

    let mutable _sharedSelectionCommands: List<CommandBinding> = List.empty

    let mutable _sharedSelectionCommandNameSet: Set<KeyInputSet> = Set.empty

    do
        // Up cast here to work around the F# bug which prevents accessing a CLIEvent from
        // a derived type
        let settings = _globalSettings :> IVimSettings
        settings.SettingChanged.Subscribe this.OnGlobalSettingsChanged |> _eventHandlers.Add

    member x.TextView = _vimBufferData.TextView
    member x.TextBuffer = _vimTextBuffer.TextBuffer
    member x.CaretPoint = this.TextView.Caret.Position.BufferPosition
    member x.IsCommandRunnerPopulated = _runner.CommandCount > 0
    member x.KeyRemapMode = 
        if _runner.IsWaitingForMoreInput then
            _runner.KeyRemapMode
        else
            KeyRemapMode.Normal
    member x.Command = _data.Command
    member x.Commands = 
        this.EnsureCommands()
        _runner.Commands
    member x.CommandNames = 
        x.EnsureCommands()
        _runner.Commands |> Seq.map (fun command -> command.KeyInputSet)

    member x.EnsureCommands() = 

        let bindDataToStorage bindData = BindDataStorage<_>.Simple bindData

        /// Get a mark and us the provided 'func' to create a Motion value
        let bindMark func = 
            let bindFunc (keyInput: KeyInput) =
                match Mark.OfChar keyInput.Char with
                | None -> BindResult<NormalCommand>.Error
                | Some localMark -> BindResult<_>.Complete (func localMark)
            let bindData = { KeyRemapMode = KeyRemapMode.None; BindFunction = bindFunc }
            bindDataToStorage bindData

        if not x.IsCommandRunnerPopulated then
            let factory = CommandFactory(_operations, _capture)

            _sharedSelectionCommands <- factory.CreateSelectionCommands() |> List.ofSeq
            _sharedSelectionCommandNameSet <-
                _sharedSelectionCommands
                |> Seq.map (fun binding -> binding.KeyInputSet)
                |> Set.ofSeq

            let complexSeq = 
                seq {
                    yield ("r", CommandFlags.Repeatable, x.BindReplaceChar ())
                    yield ("'", CommandFlags.Movement, bindMark NormalCommand.JumpToMarkLine)
                    yield ("`", CommandFlags.Movement, bindMark NormalCommand.JumpToMark)
                    yield ("m", CommandFlags.Special, bindDataToStorage (BindData<_>.CreateForChar KeyRemapMode.None NormalCommand.SetMarkToCaret))
                    yield ("@", CommandFlags.Special, bindDataToStorage (BindData<_>.CreateForChar KeyRemapMode.None NormalCommand.RunAtCommand))
                } |> Seq.map (fun (str, flags, storage) -> 
                    let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                    CommandBinding.ComplexNormalBinding (keyInputSet, flags, storage))

            SharedStandardCommands
            |> Seq.append complexSeq
            |> Seq.append (factory.CreateMovementCommands())
            |> Seq.append (factory.CreateScrollCommands())
            |> Seq.iter _runner.Add

            // Add in the special ~ command
            let _, command = x.GetTildeCommand()
            _runner.Add command

            // Add in the macro command
            factory.CreateMacroEditCommands _runner _vimTextBuffer.Vim.MacroRecorder _eventHandlers

            // Save the alternate selection command bindings
            _selectionAlternateCommands <-
                _runner.Commands
                |> Seq.filter (fun commandBinding -> Set.contains commandBinding.KeyInputSet _sharedSelectionCommandNameSet)
                |> List.ofSeq

            // The command list is built without selection commands.  If selection is enabled go 
            // ahead and switch over now 
            if Util.IsFlagSet _globalSettings.KeyModelOptions KeyModelOptions.StartSelection then
                x.UpdateSelectionCommands()

    /// Raised when a global setting is changed
    member x.OnGlobalSettingsChanged (args: SettingEventArgs) = 
        if x.IsCommandRunnerPopulated then
            let setting = args.Setting
            if StringUtil.IsEqual setting.Name GlobalSettingNames.TildeOpName then
                x.UpdateTildeCommand()
            elif StringUtil.IsEqual setting.Name GlobalSettingNames.KeyModelName then
                x.UpdateSelectionCommands()

    /// Bind the character in a replace character command: 'r'.  
    member x.BindReplaceChar () =
        let func () = 
            _data <- { _data with InReplace = true }

            let bindFunc (keyInput: KeyInput) = 
                _data <- { _data with InReplace = false }
                match keyInput.Key with
                | VimKey.Escape -> BindResult.Cancelled
                | VimKey.Back -> BindResult.Cancelled
                | VimKey.Delete -> BindResult.Cancelled
                | _ -> NormalCommand.ReplaceChar keyInput |> BindResult.Complete

            { KeyRemapMode = KeyRemapMode.Language; BindFunction = bindFunc }
        BindDataStorage.Complex func

    /// Get the information on how to handle the tilde command based on the current setting for 'tildeop'
    member x.GetTildeCommand() =
        let name = KeyInputUtil.CharToKeyInput '~' |> KeyInputSetUtil.Single
        let flags = CommandFlags.Repeatable
        let command = 
            if _globalSettings.TildeOp then
                CommandBinding.MotionBinding (name, flags, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.ToggleCase, motion)))
            else
                CommandBinding.NormalBinding (name, flags, NormalCommand.ChangeCaseCaretPoint ChangeCharacterKind.ToggleCase)
        name, command

    /// Ensure that the correct commands are loaded for the selection KeyInput values 
    member x.UpdateSelectionCommands() =

        // Remove all of the commands with that binding
        _sharedSelectionCommandNameSet |> Seq.iter _runner.Remove

        let commandList = 
            if Util.IsFlagSet _globalSettings.KeyModelOptions KeyModelOptions.StartSelection then
                _sharedSelectionCommands
            else
                _selectionAlternateCommands
        commandList |> List.iter _runner.Add

    member x.UpdateTildeCommand() =
        let name, command = x.GetTildeCommand()
        _runner.Remove name
        _runner.Add command

    /// Create the CommandBinding instances for the supported NormalCommand values
    member x.Reset() =
        _runner.ResetState()
        _data <- EmptyData

    member x.CanProcess (keyInput: KeyInput) =
        KeyInputUtil.IsCore keyInput && not keyInput.IsMouseKey
        || _runner.DoesCommandStartWith keyInput
        || x.KeyRemapMode = KeyRemapMode.Language && KeyInputUtil.IsTextInput keyInput
    
    member x.Process (keyInputData: KeyInputData) = 

        // Update the text of the command so long as this isn't a control character 
        let keyInput = keyInputData.KeyInput
        if not (CharUtil.IsControl keyInput.Char) then
            let command = _data.Command + keyInput.Char.ToString()
            _data <- { _data with Command = command }

        let wasWaitingForMoreInput = _runner.IsWaitingForMoreInput
        match _runner.Run keyInput with
        | BindResult.NeedMoreInput _ -> 
            ProcessResult.HandledNeedMoreInput
        | BindResult.Complete commandData -> 
            x.Reset()
            ProcessResult.OfCommandResult commandData.CommandResult
        | BindResult.Error -> 
            x.Reset()
            ProcessResult.NotHandled
        | BindResult.Cancelled -> 
            _incrementalSearch.CancelSession()
            if wasWaitingForMoreInput then
                x.Reset()
                ProcessResult.Handled ModeSwitch.NoSwitch
            else
                (ModeKind.Normal, ModeArgument.CancelOperation)
                |> ModeSwitch.SwitchModeWithArgument
                |> ProcessResult.Handled

    member x.OnEnter (arg: ModeArgument) =
        x.EnsureCommands()
        x.Reset()

        // Ensure the caret is positioned correctly vis a vis virtual edit
        if not _textView.IsClosed && not (TextViewUtil.GetCaretPoint(_textView).Position = 0) then
            _operations.EnsureAtCaret ViewFlags.VirtualEdit

        // Process the argument if it's applicable
        arg.CompleteAnyTransaction()

    interface INormalMode with 
        member x.KeyRemapMode = x.KeyRemapMode
        member x.InCount = _runner.InCount
        member x.InReplace = _data.InReplace
        member x.VimTextBuffer = _vimTextBuffer
        member x.Command = x.Command
        member x.CommandRunner = _runner
        member x.CommandNames = x.CommandNames
        member x.ModeKind = ModeKind.Normal
        member x.CanProcess keyInput = x.CanProcess keyInput
        member x.Process keyInputData = x.Process keyInputData
        member x.OnEnter arg = x.OnEnter arg
        member x.OnLeave () = ()
        member x.OnClose() = _eventHandlers.DisposeAll()
    

