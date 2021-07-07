namespace Vim

open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Text.Operations
open Microsoft.VisualStudio.Text.Outlining
open Microsoft.VisualStudio.Utilities
open System.Diagnostics
open System.Runtime.CompilerServices
open System.Collections.Generic

module GlobalSettingNames = 
    let AtomicInsertName = "vsvimatomicinsert"
    let BackspaceName = "backspace"
    let CaretOpacityName = "vsvimcaret"
    let CurrentDirectoryPathName = "cdpath"
    let ClipboardName = "clipboard"
    let DigraphName = "digraph"
    let GlobalDefaultName = "gdefault"
    let HighlightSearchName = "hlsearch"
    let HistoryName = "history"
    let IgnoreCaseName = "ignorecase"
    let ImeCommandName = "imcmdline"
    let ImeDisableName = "imdisable"
    let ImeInsertName = "iminsert"
    let ImeSearchName= "imsearch"
    let IncrementalSearchName = "incsearch"
    let JoinSpacesName = "joinspaces"
    let KeyModelName = "keymodel"
    let LastStatusName = "laststatus"
    let MagicName = "magic"
    let MaxMapDepth =  "maxmapdepth"
    let ModeLineName = "modeline"
    let ModeLinesName = "modelines"
    let MouseModelName = "mousemodel"
    let ParagraphsName = "paragraphs"
    let PathName = "path"
    let ScrollOffsetName = "scrolloff"
    let SectionsName = "sections"
    let SelectionName = "selection"
    let SelectModeName = "selectmode"
    let ShellName = "shell"
    let ShellFlagName = "shellcmdflag"
    let ShowCommandName = "showcmd"
    let SmartCaseName = "smartcase"
    let StartOfLineName = "startofline"
    let StatusLineName = "statusline"
    let TildeOpName = "tildeop"
    let TimeoutExName = "ttimeout"
    let TimeoutName = "timeout"
    let TimeoutLengthName = "timeoutlen"
    let TimeoutLengthExName = "ttimeoutlen"
    let VisualBellName = "visualbell"
    let VirtualEditName = "virtualedit"
    let VimRcName = "vimrc"
    let VimRcPathsName = "vimrcpaths"
    let WhichWrapName = "whichwrap"
    let WrapScanName = "wrapscan"

module LocalSettingNames =

    let AutoIndentName = "autoindent"
    let ExpandTabName = "expandtab"
    let NumberName = "number"
    let NumberFormatsName = "nrformats"
    let RelativeNumberName = "relativenumber"
    let SoftTabStopName = "softtabstop"
    let ShiftWidthName = "shiftwidth"
    let TabStopName = "tabstop"
    let ListName = "list"
    let QuoteEscapeName = "quoteescape"
    let EndOfLineName = "endofline"
    let FixEndOfLineName = "fixendofline"
    let TextWidthName = "textwidth"
    let CommentsName = "comments"
    let IsKeywordName = "iskeyword"
    let IsIdentName = "isident"
    let FileFormatName = "fileformat"

module WindowSettingNames =

    let CursorLineName = "cursorline"
    let ScrollName = "scroll"
    let WrapName = "wrap"

/// Qualifiers that options can have
[<RequireQualifiedAccess>]
type SettingOptions = 
    | None = 0

    /// A string setting that is used as a filename undergoes limited
    /// backslash escaping and expands environment variables
    | FileName = 0x1 

/// Types of number formats supported by CTRL-A CTRL-A
[<RequireQualifiedAccess>]
[<NoComparison>]
type NumberFormat =
    | Alpha
    | Decimal
    | Hex
    | Octal
    | Binary

/// The options which can be set in the 'clipboard' setting
[<RequireQualifiedAccess>]
type ClipboardOptions = 
    | None = 0
    | Unnamed = 0x1 
    | AutoSelect = 0x2
    | AutoSelectMl = 0x4

/// The options which can be set in the 'selectmode' setting
[<RequireQualifiedAccess>]
type SelectModeOptions =
    | None = 0
    | Mouse = 0x1
    | Keyboard = 0x2
    | Command = 0x4

/// The options which can be set in the 'keymodel' setting
[<RequireQualifiedAccess>]
type KeyModelOptions =
    | None = 0
    | StartSelection = 0x1
    | StopSelection = 0x2

/// The type of path values which can appear for 'cdpath' or 'path'
[<RequireQualifiedAccess>]
[<NoComparison>]
[<StructuralEquality>]
type PathOption =

    /// An actual named path
    | Named of Named: string

    /// Use the current directory
    | CurrentDirectory

    /// Use the directory of the current file 
    | CurrentFile

[<RequireQualifiedAccess>]
[<NoComparison>]
type SelectionKind =
    | Inclusive
    | Exclusive

[<RequireQualifiedAccess>]
[<NoComparison>]
type SettingKind =
    | Number
    | String
    | Toggle

/// A concrete value attached to a setting
[<RequireQualifiedAccess>]
[<StructuralEquality>]
[<NoComparison>]
type SettingValue =
    | Number of Number: int
    | String of String: string
    | Toggle of Toggle: bool

    member x.Kind = 
        match x with
        | Number _ -> SettingKind.Number
        | String _ -> SettingKind.String
        | Toggle _ -> SettingKind.Toggle

/// Allows for custom setting sources to be defined.  This is used by the vim host to 
/// add custom settings
type IVimCustomSettingSource =

    abstract GetDefaultSettingValue: name: string -> SettingValue

    abstract GetSettingValue: name: string -> SettingValue

    abstract SetSettingValue: name: string -> settingValue: SettingValue -> unit

/// This pairs both the current setting value and the default value into a single type safe
/// value.  The first value in every tuple is the current value while the second is the 
/// default
[<RequireQualifiedAccess>]
type LiveSettingValue =
    | Number of Value: int * DefaultValue: int
    | String of Value: string * DefaultValue: string
    | Toggle of Value: bool * DefaultValue: bool
    | CalculatedNumber of Value: int option * DefaultValueFunc: (unit -> int)
    | Custom of Value: string * DefaultValueSource: IVimCustomSettingSource

    /// Is this a calculated value
    member x.IsCalculated = 
        match x with 
        | CalculatedNumber _ -> true
        | _ -> false

    member x.Value =
        match x with
        | Number (value, _) -> SettingValue.Number value
        | String (value, _) -> SettingValue.String value
        | Toggle (value, _) -> SettingValue.Toggle value
        | CalculatedNumber (value, func) ->
            match value with
            | Some value -> SettingValue.Number value
            | None -> func() |> SettingValue.Number
        | Custom (name, customSettingSource) -> customSettingSource.GetSettingValue name

    member x.DefaultValue =
        match x with
        | Number (_, defaultValue) -> SettingValue.Number defaultValue
        | String (_, defaultValue) -> SettingValue.String defaultValue
        | Toggle (_, defaultValue) -> SettingValue.Toggle defaultValue
        | CalculatedNumber (_, func) -> func() |> SettingValue.Number
        | Custom (name, customSettingSource) -> customSettingSource.GetDefaultSettingValue name

    /// Is the value currently the default? 
    member x.IsValueDefault = x.Value = x.DefaultValue

    member x.Kind = 
        match x with
        | Number _ -> SettingKind.Number
        | String _ -> SettingKind.String 
        | Toggle _ -> SettingKind.Toggle
        | CalculatedNumber _ -> SettingKind.Number
        | Custom (name, customSettingSource) -> (customSettingSource.GetDefaultSettingValue name).Kind

    member x.UpdateValue value =
        match x, value with 
        | Number (_, defaultValue), SettingValue.Number value -> Number (value, defaultValue) |> Some
        | String (_, defaultValue), SettingValue.String value -> String (value, defaultValue) |> Some
        | Toggle (_, defaultValue), SettingValue.Toggle value -> Toggle (value, defaultValue) |> Some
        | CalculatedNumber (_, func), SettingValue.Number value ->
            if value = 0 then
                CalculatedNumber (None, func)
            else
                CalculatedNumber (Some value, func)
            |> Some
        | Custom (name, customSettingSource), value -> 
            customSettingSource.SetSettingValue name value
            Some x
        | _ -> None

    static member Create value = 
        match value with
        | SettingValue.Number value -> LiveSettingValue.Number (value, value)
        | SettingValue.String value -> LiveSettingValue.String (value, value)
        | SettingValue.Toggle value -> LiveSettingValue.Toggle (value, value)

[<DebuggerDisplay("{Name}={Value}")>]
type Setting = {
    Name: string
    Abbreviation: string
    LiveSettingValue: LiveSettingValue
    IsGlobal: bool
    SettingOptions: SettingOptions
} with 

    member x.Value = x.LiveSettingValue.Value

    member x.DefaultValue = x.LiveSettingValue.DefaultValue

    member x.Kind = x.LiveSettingValue.Kind

    /// Is the value calculated
    member x.IsValueCalculated = x.LiveSettingValue.IsCalculated

    /// Is the setting value currently set to the default value
    member x.IsValueDefault = x.LiveSettingValue.IsValueDefault

    /// Whether the setting has the FileName
    member x.HasFileNameOption = Util.IsFlagSet x.SettingOptions SettingOptions.FileName

type SettingEventArgs(_setting: Setting, _isValueChanged: bool) =
    inherit System.EventArgs()

    /// The affected setting
    member x.Setting = _setting

    /// Determine if the value changed or not.  The event is raised for sets that don't change the
    /// value because there is a lot of vim specific behavior that depends on this (:noh).  This
    /// will help the handlers which want to look for actual changes
    member x.IsValueChanged = _isValueChanged;

/// Represent the setting supported by the Vim implementation.  This class **IS** mutable
/// and the values will change.  Setting names are case sensitive but the exposed property
/// names tend to have more familiar camel case names
type IVimSettings =

    /// Collection of settings owned by this IVimSettings instance
    abstract Settings: Setting list

    /// Try and set a setting to the passed in value.  This can fail if the value does not 
    /// have the correct type.  The provided name can be the full name or abbreviation
    abstract TrySetValue: settingNameOrAbbrev: string -> value: SettingValue -> bool

    /// Try and set a setting to the passed in value which originates in string form.  This 
    /// will fail if the setting is not found or the value cannot be converted to the appropriate
    /// value
    abstract TrySetValueFromString: settingNameOrAbbrev: string -> strValue: string -> bool

    /// Get the value for the named setting.  The name can be the full setting name or an 
    /// abbreviation
    abstract GetSetting: settingNameOrAbbrev: string -> Setting option

    /// Raised when a Setting changes
    [<CLIEvent>]
    abstract SettingChanged: IDelegateEvent<System.EventHandler<SettingEventArgs>>

and IVimGlobalSettings = 

    /// The local settings specified in the vim rc file, if any
    abstract VimRcLocalSettings: IVimLocalSettings option with get, set

    /// The window settings specified in the vim rc file, if any
    abstract VimRcWindowSettings: IVimWindowSettings option with get, set

    /// Add a custom setting to the current collection
    abstract AddCustomSetting: name: string -> abbrevation: string -> customSettingSource: IVimCustomSettingSource -> unit

    /// When set, don't break inserts into multiple inserts when moving the cursor. Applies to all kinds of cursor movements, arrow keys,
    /// automatic movements from intellisense for example or mouse movements.
    abstract AtomicInsert: bool with get, set

    /// The multi-value option for determining backspace behavior.  Valid values include 
    /// indent, eol, start.  Usually accessed through the IsBackSpace helpers
    abstract Backspace: string with get, set

    /// Opacity of the caret.  This must be an integer between values 0 and 100 which
    /// will be converted into a double for the opacity of the caret
    abstract CaretOpacity: int with get, set

    /// List of paths which will be searched by the :cd and :ld commands
    abstract CurrentDirectoryPath: string with get, set

    /// Strongly typed list of paths which will be searched by the :cd and :ld commands
    abstract CurrentDirectoryPathList: PathOption list 

    /// The clipboard option.  Use the IsClipboard helpers for finding out if specific options 
    /// are set
    abstract Clipboard: string with get, set

    /// Whether digraphs are supported in insert mode without <C-k>
    abstract Digraph: bool with get, set

    /// The parsed set of clipboard options
    abstract ClipboardOptions: ClipboardOptions with get, set

    /// Whether or not 'gdefault' is set
    abstract GlobalDefault: bool with get, set

    /// Whether or not to highlight previous search patterns matching cases
    abstract HighlightSearch: bool with get, set

    /// The number of items to keep in the history lists
    abstract History: int with get, set

    /// Whether or not we should be ignoring case in the ITextBuffer
    abstract IgnoreCase: bool with get, set

    /// Whether or not incremental searches should be highlighted and focused 
    /// in the ITextBuffer
    abstract IncrementalSearch: bool with get, set

    /// Whether the command line should be treated as if it were in
    /// insert mode for the purposes of manipulating the IME
    abstract ImeCommand: bool with get, set

    /// Whether the IME coordinator is diabled
    abstract ImeDisable: bool with get, set

    /// The current state of the IME when in insert mode, or the
    /// value that will be restored when insert mode is next entered
    /// 0 - :lmap is off and IM is off
    /// 1 - :lmap is ON and IM is off
    /// 2 - :lmap is off and IM is ON
    abstract ImeInsert: int with get, set

    /// The current state of the IME in search mode, or the
    /// value that will be restored when search mode is next entered
    /// -1 - use the value of 'iminsert' instead
    /// 0 - :lmap is off and IM is off
    /// 1 - :lmap is ON and IM is off
    /// 2 - :lmap is off and IM is ON
    abstract ImeSearch: int with get, set

    /// Is the 'indent' option inside of Backspace set
    abstract IsBackspaceIndent: bool with get

    /// Is the 'eol' option inside of Backspace set
    abstract IsBackspaceEol: bool with get

    /// Is the 'start' option inside of Backspace set
    abstract IsBackspaceStart: bool with get

    /// Is the 'block' option inside of VirtualEdit set
    abstract IsVirtualEditBlock: bool with get

    /// Is the 'insert' option inside of VirtualEdit set
    abstract IsVirtualEditInsert: bool with get

    /// Is the 'all' option inside of VirtualEdit set
    abstract IsVirtualEditAll: bool with get

    /// Is the 'onemore' option inside of VirtualEdit set
    abstract IsVirtualEditOneMore: bool with get

    /// Is the 'b' option inside of WhichWrap set
    abstract IsWhichWrapSpaceLeft: bool with get

    /// Is the 's' option inside of WhichWrap set
    abstract IsWhichWrapSpaceRight: bool with get

    /// Is the 'h' option inside of WhichWrap set
    abstract IsWhichWrapCharLeft: bool with get

    /// Is the 'l' option inside of WhichWrap set
    abstract IsWhichWrapCharRight: bool with get

    /// Is the '<' option inside of WhichWrap set
    abstract IsWhichWrapArrowLeft: bool with get

    /// Is the '>' option inside of WhichWrap set
    abstract IsWhichWrapArrowRight: bool with get

    /// Is the '~' option inside of WhichWrap set
    abstract IsWhichWrapTilde: bool with get

    /// Is the '[' option inside of WhichWrap set
    abstract IsWhichWrapArrowLeftInsert: bool with get

    /// Is the ']' option inside of WhichWrap set
    abstract IsWhichWrapArrowRightInsert: bool with get

    /// Is the Selection setting set to a value which calls for inclusive 
    /// selection.  This does not directly track if Setting = "inclusive" 
    /// although that would cause this value to be true
    abstract IsSelectionInclusive: bool with get

    /// Is the Selection setting set to a value which permits the selection
    /// to extend past the line
    abstract IsSelectionPastLine: bool with get

    /// The characters which represent an identifier
    abstract IsIdent: string with get, set

    /// Type safe representation of IsIdent
    abstract IsIdentCharSet: VimCharSet with get, set

    /// Whether or not to insert two spaces after certain constructs in a 
    /// join operation
    abstract JoinSpaces: bool with get, set

    /// The 'keymodel' setting
    abstract KeyModel: string with get, set

    /// The 'keymodel' in a type safe form
    abstract KeyModelOptions: KeyModelOptions with get, set

    /// The value of this option influences when a window will have a status line
    abstract LastStatus: int with get, set

    /// Whether or not the magic option is set
    abstract Magic: bool with get, set

    /// Maximum number of recursive depths which occur for a mapping
    abstract MaxMapDepth: int with get, set

    /// Whether or not the 'modeline' option is set
    abstract ModeLine: bool with get, set

    /// How many text lines to search for a modeline
    abstract ModeLines: int with get, set

    /// The 'mousemodel' setting
    abstract MouseModel: string with get, set

    /// The nrooff macros that separate paragraphs
    abstract Paragraphs: string with get, set

    /// List of paths which will be searched by the 'gf' :find, etc ... commands
    abstract Path: string with get, set

    /// Strongly typed list of path entries
    abstract PathList: PathOption list 

    /// The nrooff macros that separate sections
    abstract Sections: string with get, set

    /// The name of the shell to use for shell commands
    abstract Shell: string with get, set

    /// The flag which is passed to the shell when executing shell commands
    abstract ShellFlag: string with get, set

    /// The 'showcmd' setting as in Vim
    abstract ShowCommand: bool with get, set
    
    abstract StartOfLine: bool with get, set

    /// This option determines the content of the status line.
    abstract StatusLine: string with get, set
    
    /// Controls the behavior of ~ in normal mode
    abstract TildeOp: bool with get, set

    /// Part of the control for key mapping and code timeout
    abstract Timeout: bool with get, set

    /// Part of the control for key mapping and code timeout
    abstract TimeoutEx: bool with get, set

    /// Timeout for a key mapping in milliseconds
    abstract TimeoutLength: int with get, set

    /// Timeout control for key mapping / code
    abstract TimeoutLengthEx: int with get, set

    /// Holds the scroll offset value which is the number of lines to keep visible
    /// above the cursor after a move operation
    abstract ScrollOffset: int with get, set

    /// Holds the Selection option
    abstract Selection: string with get, set

    /// Get the SelectionKind for the current settings
    abstract SelectionKind: SelectionKind

    /// Options for how select mode is entered
    abstract SelectMode: string with get, set 

    /// The options which are set via select mode
    abstract SelectModeOptions: SelectModeOptions with get, set

    /// Overrides the IgnoreCase setting in certain cases if the pattern contains
    /// any upper case letters
    abstract SmartCase: bool with get, set

    /// Retrieves the location of the loaded VimRC file.  Will be the empty string if the load 
    /// did not succeed or has not been tried
    abstract VimRc: string with get, set

    /// Set of paths considered when looking for a .vimrc file.  Will be the empty string if the 
    /// load has not been attempted yet
    abstract VimRcPaths: string with get, set

    /// Holds the VirtualEdit string.  
    abstract VirtualEdit: string with get, set

    /// Whether or not to use a visual indicator of errors instead of a beep
    abstract VisualBell: bool with get, set

    /// Which operations should wrap in the buffer
    abstract WhichWrap: string with get, set

    /// Whether or not searches should wrap at the end of the file
    abstract WrapScan: bool with get, set

    /// The key binding which will cause all IVimBuffer instances to enter disabled mode
    abstract DisableAllCommand: KeyInput;

    inherit IVimSettings

/// Settings class which is local to a given IVimBuffer.  This will hide the work of merging
/// global settings with non-global ones
and IVimLocalSettings =

    abstract Defaults: IVimLocalSettings with get

    /// Whether or not to auto-indent
    abstract AutoIndent: bool with get, set

    /// Whether or not to expand tabs into spaces
    abstract ExpandTab: bool with get, set

    /// Return the handle to the global IVimSettings instance
    abstract GlobalSettings: IVimGlobalSettings

    /// Whether or not to put the numbers on the left column of the display
    abstract Number: bool with get, set

    /// Formats that vim considers a number for CTRL-A and CTRL-X
    abstract NumberFormats: string with get, set

    /// The characters which represent a keyword / WordKind.NormalWord
    abstract IsKeyword: string with get, set

    /// Type safe representation of IsKeyword
    abstract IsKeywordCharSet: VimCharSet with get, set

    /// Whether or not to put relative line numbers on the left column of the display
    abstract RelativeNumber: bool with get, set

    /// The number of spaces a << or >> command will shift by 
    abstract ShiftWidth: int with get, set

    /// Number of spaces a tab counts for when doing edit operations
    abstract SoftTabStop: int with get, set

    /// How many spaces a tab counts for 
    abstract TabStop: int with get, set

    /// Whether list mode is enabled
    abstract List: bool with get, set

    /// Which characters escape quotes for certain motion types
    abstract QuoteEscape: string with get, set

    /// Whether or not the buffer ends with a newline
    abstract EndOfLine: bool with get, set

    /// Whether or not to fix any missing final newline
    abstract FixEndOfLine: bool with get, set

    /// Text width used when formatting text
    abstract TextWidth: int with get, set

    /// Comma separated list of strings that can start a comment line
    abstract Comments: string with get, set

    /// Is the provided NumberFormat supported by the current options
    abstract IsNumberFormatSupported: NumberFormat -> bool

    /// Which fileformat to convert the file to: unix, dos, mac
    abstract FileFormat: string with get, set

    inherit IVimSettings

/// Settings which are local to a given window.
and IVimWindowSettings = 

    abstract Defaults: IVimWindowSettings with get

    /// Whether or not to highlight the line the cursor is on
    abstract CursorLine: bool with get, set

    /// Return the handle to the global IVimSettings instance
    abstract GlobalSettings: IVimGlobalSettings

    /// The scroll size 
    abstract Scroll: int with get, set

    /// Whether or not the window should be wrapping
    abstract Wrap: bool with get, set

    inherit IVimSettings