#light

namespace Vim
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Text.Operations
open Microsoft.VisualStudio.Text.Outlining
open Microsoft.VisualStudio.Text.Classification
open Microsoft.VisualStudio.Text.Tagging
open Microsoft.VisualStudio.Utilities
open System.Diagnostics
open System.IO
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Threading.Tasks
open Vim.Interpreter
open System

[<RequireQualifiedAccess>]
[<NoComparison>]
type CaretMovement =
    | Up
    | Right
    | Down
    | Left
    | Home
    | End
    | PageUp
    | PageDown
    | ControlUp
    | ControlRight
    | ControlDown
    | ControlLeft
    | ControlHome
    | ControlEnd

    with 

    static member OfDirection direction =
        match direction with
        | Direction.Up -> CaretMovement.Up
        | Direction.Right -> CaretMovement.Right
        | Direction.Down -> CaretMovement.Down
        | Direction.Left -> CaretMovement.Left
        | _ -> raise (Contract.GetInvalidEnumException direction)

type TextViewEventArgs(_textView: ITextView) =
    inherit System.EventArgs()

    member x.TextView = _textView

type VimRcKind =
    | VimRc     = 0
    | VsVimRc   = 1

type VimRcPath = { 

    /// Which type of file was loaded 
    VimRcKind: VimRcKind 

    /// Full path to the file which the contents were loaded from
    FilePath: string
}

[<RequireQualifiedAccess>]
type VimRcState =
    /// The VimRc file has not been processed at this point
    | None

    /// The load succeeded of the specified file.  If there were any errors actually
    /// processing the load they will be captured in the string[] parameter.
    /// The load succeeded and the specified file was used 
    | LoadSucceeded of VimRcPath: VimRcPath * Errors: string[]

    /// The load failed 
    | LoadFailed

[<RequireQualifiedAccess>]
[<NoComparison>]
type ChangeCharacterKind =
    /// Switch the characters to upper case
    | ToUpperCase

    /// Switch the characters to lower case
    | ToLowerCase

    /// Toggle the case of the characters
    | ToggleCase

    /// Rot13 encode the letters
    | Rot13

type IStatusUtil =

    /// Raised when there is a special status message that needs to be reported
    abstract OnStatus: string -> unit

    /// Raised when there is a long status message that needs to be reported
    abstract OnStatusLong: string seq -> unit 

    /// Raised when there is an error message that needs to be reported
    abstract OnError: string -> unit 

    /// Raised when there is a warning message that needs to be reported
    abstract OnWarning: string -> unit 

/// Abstracts away VsVim's interaction with the file system to facilitate testing
type IFileSystem =

    /// Create the specified directory, returns true if it was actually created
    abstract CreateDirectory: path: string -> bool 

    /// Get the directories to probe for RC files
    abstract GetVimRcDirectories: unit -> string[]

    /// Get the possible paths for a vimrc file in the order they should be 
    /// considered 
    abstract GetVimRcFilePaths: unit -> VimRcPath[]

    /// Attempt to read all of the lines from the given file 
    abstract ReadAllLines: filePath: string -> string[] option

    /// Read the contents of the directory 
    abstract ReadDirectoryContents: directoryPath: string -> string[] option

    abstract Read: filePath: string -> Stream option

    abstract Write: filePath: string -> stream: Stream -> bool

/// Used for manipulating multiple selections
type ISelectionUtil =

    /// Whether multi-selection is supported
    abstract IsMultiSelectionSupported: bool

    /// Get all the selected spans for the specified text view
    abstract GetSelectedSpans: unit -> SelectionSpan seq

    /// Set all the selected spans for the specified text view
    abstract SetSelectedSpans: selectedSpans: SelectionSpan seq -> unit

/// Factory service for creating ISelectionUtil instances
type ISelectionUtilFactory = 

    /// Get the selection utility interface
    abstract GetSelectionUtil: textView: ITextView -> ISelectionUtil

/// Service for creating ISelectionUtilFactory instances
type ISelectionUtilFactoryService = 

    /// Get the selection utility interface
    abstract GetSelectionUtilFactory: unit -> ISelectionUtilFactory

/// Used to display a word completion list to the user
type IWordCompletionSession =

    inherit IPropertyOwner

    /// Is the session dismissed
    abstract IsDismissed: bool

    /// The associated ITextView instance
    abstract TextView: ITextView

    /// Select the next word in the session
    abstract MoveNext: unit -> bool

    /// Select the previous word in the session.
    abstract MovePrevious: unit -> bool

    /// Commit the current session
    abstract Commit: unit -> unit

    /// Dismiss the completion session 
    abstract Dismiss: unit -> unit

    /// Raised when the session is dismissed
    [<CLIEvent>]
    abstract Dismissed: IDelegateEvent<System.EventHandler>

type WordCompletionSessionEventArgs(_wordCompletionSession: IWordCompletionSession) =
    inherit System.EventArgs()

    member x.WordCompletionSession = _wordCompletionSession

/// Factory for creating a IWordCompletionSession instance.
type IWordCompletionSessionFactory = 

    /// Create a session with the given set of words
    abstract CreateWordCompletionSession: textView: ITextView -> wordSpan: SnapshotSpan -> words: string seq -> isForward: bool -> IWordCompletionSession option

/// Factory service for creating IWordCompletionSession instances. This type is available as 
/// a MEF import
type IWordCompletionSessionFactoryService = 

    /// Create a session with the given set of words
    abstract CreateWordCompletionSession: textView: ITextView -> wordSpan: SnapshotSpan -> words: string seq -> isForward: bool -> IWordCompletionSession option

    /// Raised when the session is created
    [<CLIEvent>]
    abstract Created: IDelegateEvent<System.EventHandler<WordCompletionSessionEventArgs>>

/// Wraps an ITextUndoTransaction so we can avoid all of the null checks
type IUndoTransaction =

    /// Call when it completes
    abstract Complete: unit -> unit

    /// Cancels the transaction
    abstract Cancel: unit -> unit

    inherit System.IDisposable

/// This is a IUndoTransaction that is specific to a given ITextView instance
type ITextViewUndoTransaction =

    /// Adds an ITextUndoPrimitive which will reset the selection to the current
    /// state when redoing this edit
    abstract AddAfterTextBufferChangePrimitive: unit -> unit

    /// Adds an ITextUndoPrimitive which will reset the selection to the current
    /// state when undoing this change
    abstract AddBeforeTextBufferChangePrimitive: unit -> unit

    inherit IUndoTransaction

/// Flags controlling the linked undo transaction behavior
[<System.Flags>]
type LinkedUndoTransactionFlags = 
    | None = 0x0

    | CanBeEmpty = 0x1

    | EndsWithInsert = 0x2

/// Wraps a set of IUndoTransaction items such that they undo and redo as a single
/// entity.
type ILinkedUndoTransaction =

    /// Complete the linked operation
    abstract Complete: unit -> unit

    inherit System.IDisposable

/// Wraps all of the undo and redo operations
type IUndoRedoOperations = 

    abstract TextUndoHistory: ITextUndoHistory option

    /// Is there an open linked undo transaction
    abstract InLinkedUndoTransaction: bool

    /// StatusUtil instance that is used to report errors
    abstract StatusUtil: IStatusUtil

    /// Close the IUndoRedoOperations and remove any attached event handlers
    abstract Close: unit -> unit

    /// Creates an Undo Transaction
    abstract CreateUndoTransaction: name: string -> IUndoTransaction

    /// Creates an Undo Transaction specific to the given ITextView.  Use when operations
    /// like caret position need to be a part of the undo / redo stack
    abstract CreateTextViewUndoTransaction: name: string -> textView: ITextView -> ITextViewUndoTransaction

    /// Creates a linked undo transaction
    abstract CreateLinkedUndoTransaction: name: string -> ILinkedUndoTransaction

    /// Creates a linked undo transaction with flags
    abstract CreateLinkedUndoTransactionWithFlags: name: string -> flags: LinkedUndoTransactionFlags -> ILinkedUndoTransaction

    /// Wrap the passed in "action" inside an undo transaction.  This is needed
    /// when making edits such as paste so that the cursor will move properly 
    /// during an undo operation
    abstract EditWithUndoTransaction: name: string -> textView: ITextView -> action: (unit -> 'T) -> 'T

    /// Redo the last "count" operations
    abstract Redo: count:int -> unit

    /// Undo the last "count" operations
    abstract Undo: count:int -> unit

/// Represents a set of changes to a contiguous region. 
[<RequireQualifiedAccess>]
type TextChange = 
    | DeleteLeft of Count: int
    | DeleteRight of Count: int
    | Insert of Text: string
    | Combination of Left: TextChange * Right: TextChange

    with 

    /// Get the insert text resulting from the change if there is any
    member x.InsertText = 
        let rec inner textChange (text: string) = 
            match textChange with 
            | Insert data -> text + data |> Some
            | DeleteLeft count -> 
                if count > text.Length then
                    None
                else 
                    text.Substring(0, text.Length - count) |> Some
            | DeleteRight _ -> None
            | Combination (left, right) ->
                match inner left text with
                | None -> None
                | Some text -> inner right text

        inner x StringUtil.Empty

    /// Get the last / most recent change in the TextChange tree
    member x.LastChange = 
        match x with
        | DeleteLeft _ -> x
        | DeleteRight _ -> x
        | Insert _ -> x
        | Combination (_, right) -> right.LastChange

    member x.IsEmpty = 
        match x with 
        | Insert text -> StringUtil.IsNullOrEmpty text
        | DeleteLeft count -> count = 0
        | DeleteRight count -> count = 0
        | Combination (left, right) -> left.IsEmpty && right.IsEmpty

    member x.Reduce = 
        match x with 
        | Combination (left, right) -> 
            match TextChange.ReduceCore left right with
            | Some reduced -> reduced
            | None -> x
        | _ -> x

    /// Merge two TextChange values together.  The goal is to produce a the smallest TextChange
    /// value possible.  This will return Some if a reduction is made and None if there is 
    /// no possible reduction
    static member private ReduceCore left right =

        // This is called for combination merges.  Some progress was made but it's possible further
        // progress could be made by reducing the specified values.  If further progress can be made
        // keep it else keep at least the progress already made
        let tryReduceAgain left right = 
            Some (TextChange.CreateReduced left right)

        // Insert can only merge with a previous insert operation.  It can't 
        // merge with any deletes that came before it
        let reduceInsert before (text: string) =
            match before with
            | Insert otherText -> Some (Insert (otherText + text))
            | _ -> None 

        // DeleteLeft can merge with deletes and previous insert operations. 
        let reduceDeleteLeft before count = 
            match before with
            | Insert otherText -> 
                if count <= otherText.Length then
                    let text = otherText.Substring(0, otherText.Length - count)
                    Some (Insert text)
                else
                    let count = count - otherText.Length
                    Some (DeleteLeft count)
            | DeleteLeft beforeCount -> Some (DeleteLeft (count + beforeCount))
            | _ -> None

        // Delete right can merge only with other DeleteRight operations
        let reduceDeleteRight before count = 
            match before with 
            | DeleteRight beforeCount -> Some (DeleteRight (beforeCount + count))
            | _ -> None 

        // The item on the left isn't a Combination item.  So do simple merge semantics
        let simpleMerge () = 
            match right with
            | Insert text -> reduceInsert left text
            | DeleteLeft count -> reduceDeleteLeft left count
            | DeleteRight count -> reduceDeleteRight left count 
            | Combination (rightSubLeft, rightSubRight) ->
                // First just see if the combination itself can be reduced 
                match TextChange.ReduceCore rightSubLeft rightSubRight with
                | Some reducedTextChange -> tryReduceAgain left reducedTextChange
                | None -> 
                    match TextChange.ReduceCore left rightSubLeft with
                    | None -> None
                    | Some reducedTextChange -> tryReduceAgain reducedTextChange rightSubRight

        // The item on the left is a Combination item.  
        let complexMerge leftSubLeft leftSubRight = 

            // First check if the left can be merged against itself.  This can easily happen
            // for hand built trees
            match TextChange.ReduceCore leftSubLeft leftSubRight with
            | Some reducedTextChange -> tryReduceAgain reducedTextChange right
            | None -> 
                // It can't be reduced.  Still a change that the right can be reduced against 
                // the subRight value 
                match TextChange.ReduceCore leftSubRight right with
                | None -> None
                | Some reducedTextChange -> tryReduceAgain leftSubLeft reducedTextChange

        if left.IsEmpty then
            Some right
        elif right.IsEmpty then
            Some left
        else
            match left with
            | Insert _ -> simpleMerge ()
            | DeleteLeft _ -> simpleMerge ()
            | DeleteRight _ -> simpleMerge ()
            | Combination (leftSubLeft, leftSubRight) -> complexMerge leftSubLeft leftSubRight

    static member Replace str =
        let left = str |> StringUtil.Length |> TextChange.DeleteLeft
        let right = TextChange.Insert str
        TextChange.Combination (left, right)

    static member CreateReduced left right = 
        match TextChange.ReduceCore left right with
        | None -> Combination (left, right)
        | Some textChange -> textChange

type TextChangeEventArgs(_textChange: TextChange) =
    inherit System.EventArgs()

    member x.TextChange = _textChange

[<System.Flags>]
type SearchOptions = 
    | None = 0x0

    /// Consider the "ignorecase" option when doing the search
    | ConsiderIgnoreCase = 0x1

    /// Consider the "smartcase" option when doing the search
    | ConsiderSmartCase = 0x2

    /// Whether to include the start point when doing the search
    | IncludeStartPoint = 0x4

    /// ConsiderIgnoreCase ||| ConsiderSmartCase
    | Default = 0x3

/// Information about a search of a pattern
type PatternData = {

    /// The Pattern to search for
    Pattern: string

    /// The direction in which the pattern was searched for
    Path: SearchPath
}

type PatternDataEventArgs(_patternData: PatternData) =
    inherit System.EventArgs()

    member x.PatternData = _patternData

/// An incremental search can be augmented with a offset of characters or a line
/// count.  This is described in full in :help searh-offset'
[<RequireQualifiedAccess>]
[<StructuralEquality>]
[<NoComparison>]
[<DebuggerDisplay("{ToString(),nq}")>]
type SearchOffsetData =
    | None
    | Line of Line: int
    | Start of Start: int
    | End of End: int
    | Search of PatternData: PatternData

    with 

    static member private ParseCore (offset: string) =
        Contract.Requires (offset.Length > 0)

        let index = ref 0
        let isForward = ref true
        let movePastPlusOrMinus () = 
            if index.Value < offset.Length then
                match offset.[index.Value] with
                | '+' -> 
                    isForward := true
                    index := index.Value + 1
                    true
                | '-' ->
                    isForward := false
                    index := index.Value + 1
                    true
                | _ -> false
            else
                false

        let parseNumber () = 
            if movePastPlusOrMinus () && index.Value = offset.Length then
                // a single + or - counts as a value if it's not followed by
                // a number 
                if isForward.Value then 1
                else -1
            else
                // parse out the digits after the value
                let mutable num = 0
                let mutable isBad = index.Value >= offset.Length
                while index.Value < offset.Length do
                    num <- num * 10
                    match CharUtil.GetDigitValue (offset.[index.Value]) with
                    | Option.None -> 
                        isBad <- true
                        index := offset.Length
                    | Option.Some d ->
                        num <- num + d
                        index := index.Value + 1
                if isBad then
                    0
                elif isForward.Value then
                    num
                else    
                    -num

        let parseLine () = 
            let number = parseNumber ()
            SearchOffsetData.Line number

        let parseEnd () = 
            index := index.Value + 1
            let number = parseNumber ()
            SearchOffsetData.End number
            
        let parseStart () = 
            index := index.Value + 1
            let number = parseNumber ()
            SearchOffsetData.Start number

        let parseSearch () = 
            index := index.Value + 1
            match StringUtil.CharAtOption index.Value offset with
            | Option.Some '/' -> 
                let path = SearchPath.Forward
                let pattern = offset.Substring(index.Value + 1)
                SearchOffsetData.Search ({ Pattern = pattern; Path = path})
            | Option.Some '?' -> 
                let path = SearchPath.Backward
                let pattern = offset.Substring(index.Value + 1)
                SearchOffsetData.Search ({ Pattern = pattern; Path = path})
            | _ ->
                SearchOffsetData.None

        if CharUtil.IsDigit (offset.[0]) then
            parseLine ()
        else
            match offset.[0] with
            | '-' -> parseLine ()
            | '+' -> parseLine ()
            | 'e' -> parseEnd ()
            | 's' -> parseStart ()
            | 'b' -> parseStart ()
            | ';' -> parseSearch () 
            | _ -> SearchOffsetData.None

    static member Parse (offset: string) =
        if StringUtil.IsNullOrEmpty offset then
            SearchOffsetData.None
        else
            SearchOffsetData.ParseCore offset

[<Sealed>]
[<DebuggerDisplay("{ToString(),nq}")>]
type SearchData
    (
        _pattern: string,
        _offset: SearchOffsetData,
        _kind: SearchKind,
        _options: SearchOptions
    ) = 

    new (pattern: string, path: SearchPath, isWrap: bool) =
        let kind = SearchKind.OfPathAndWrap path isWrap
        SearchData(pattern, SearchOffsetData.None, kind, SearchOptions.Default)

    new (pattern: string, path: SearchPath) = 
        let kind = SearchKind.OfPathAndWrap path true
        SearchData(pattern, SearchOffsetData.None, kind, SearchOptions.Default)

    /// The pattern being searched for in the buffer
    member x.Pattern = _pattern

    /// The offset that is applied to the search
    member x.Offset = _offset

    member x.Kind = _kind

    member x.Options = _options

    member x.Path = x.Kind.Path

    /// The PatternData which was searched for.  This does not include the pattern searched
    /// for in the offset
    member x.PatternData = { Pattern = x.Pattern; Path = x.Kind.Path }

    /// The SearchData which should be used for IVimData.LastSearchData if this search needs
    /// to update that value.  It takes into account the search pattern in an offset string
    /// as specified in ':help //;'
    member x.LastSearchData =
        let path = x.Path
        let pattern = 
            match x.Offset with
            | SearchOffsetData.Search patternData -> patternData.Pattern
            | _ -> x.Pattern
        SearchData(pattern, x.Offset, x.Kind, x.Options)

    member x.Equals(other: SearchData) =
        if obj.ReferenceEquals(other, null) then
            false
        else
            _pattern = other.Pattern &&
            _offset = other.Offset &&
            _kind = other.Kind &&
            _options = other.Options

    override x.Equals(other: obj) = 
        match other with
        | :? SearchData as otherSearchData -> x.Equals(otherSearchData)
        | _ -> false 

    override x.GetHashCode() =
        _pattern.GetHashCode()

    override x.ToString() =
        x.Pattern

    static member op_Equality(this, other) = System.Collections.Generic.EqualityComparer<SearchData>.Default.Equals(this, other)
    static member op_Inequality(this, other) = not (System.Collections.Generic.EqualityComparer<SearchData>.Default.Equals(this, other))

    /// Parse out a SearchData value given the specific pattern and SearchKind.  The pattern should
    /// not include a beginning / or ?.  That should be removed by this point
    static member Parse (pattern: string) (searchKind: SearchKind) searchOptions =
        let mutable index = -1
        let mutable i = 1
        let targetChar = 
            if searchKind.IsAnyForward then '/'
            else '?'
        while i < pattern.Length do
            if pattern.[i] = targetChar && pattern.[i-1] <> '\\' then
                index <- i
                i <- pattern.Length
            else
                i <- i + 1

        if index < 0 then
            SearchData(pattern, SearchOffsetData.None, searchKind, searchOptions)
        else
            let offset = SearchOffsetData.Parse (pattern.Substring(index + 1))
            let pattern = pattern.Substring(0, index)
            SearchData(pattern, offset, searchKind, searchOptions)

    interface System.IEquatable<SearchData> with
        member x.Equals other = x.Equals(other)

type SearchDataEventArgs(_searchData: SearchData) =
    inherit System.EventArgs()

    member x.SearchData = _searchData

/// Result of an individual search
[<RequireQualifiedAccess>]
type SearchResult =

    /// The pattern was found.  The two spans here represent the following pieces of data
    /// respectively 
    ///
    ///     - the span of the pattern + the specified offset
    ///     - the span of the found pattern 
    ///
    /// In general the first should be used by consumers.  The second is only interesting
    /// to items that need to tag the value 
    ///
    /// The bool at the end of the tuple represents whether not
    /// a wrap occurred while searching for the value
    | Found of SearchData: SearchData * SpanWithOffset: SnapshotSpan * Span: SnapshotSpan * DidWrap: bool

    /// The pattern was not found.  The bool is true if the word was present in the ITextBuffer
    /// but wasn't found do to the lack of a wrap in the SearchData value
    | NotFound of SeachData: SearchData * CanFindWithWrap: bool

    /// The search was cancelled
    | Cancelled of SearchData: SearchData

    /// There was an error converting the pattern to a searchable value.  The string value is the
    /// error message
    | Error of SearchData: SearchData * Error: string

    with

    /// Returns the SearchData which was searched for
    member x.SearchData = 
        match x with 
        | SearchResult.Found (searchData, _, _, _) -> searchData
        | SearchResult.NotFound (searchData, _) -> searchData
        | SearchResult.Cancelled (searchData) -> searchData
        | SearchResult.Error (searchData, _) -> searchData

type SearchResultEventArgs(_searchResult: SearchResult) = 
    inherit System.EventArgs()

    member x.SearchResult = _searchResult

/// Global information about searches within Vim.  
///
/// This interface is usable from any thread
[<UsedInBackgroundThread()>]
type ISearchService = 

    /// Find the next occurrence of the pattern in the buffer starting at the 
    /// given SnapshotPoint
    abstract FindNext: searchPoint: SnapshotPoint -> searchData: SearchData -> navigator: SnapshotWordNavigator -> SearchResult

    /// Find the next 'count' occurrence of the specified pattern.  Note: The first occurrence won't
    /// match anything at the provided start point.  That will be adjusted appropriately
    abstract FindNextPattern: searchPoint: SnapshotPoint -> searchPoint: SearchData -> navigator: SnapshotWordNavigator -> count: int -> SearchResult

/// Column information about the caret in relation to this Motion Result
[<RequireQualifiedAccess>]
[<NoComparison>]
type CaretColumn = 

    /// No column information was provided
    | None

    /// Caret should be placed in the specified character on the last line in 
    /// the MotionResult
    ///
    /// This column should be specified in terms of a character offset in the ITextBuffer
    /// and shouldn't consider items like how wide a tab is.  A tab should be a single
    /// character
    | InLastLine of ColumnNumber: int

    /// Caret should be placed in the specified column on the last line in 
    /// the MotionResult
    ///
    /// This column should be specified in terms number of screen columns, where 
    /// some characters like tabs may span many columns.
    | ScreenColumn of ColumnNumber: int

    /// Caret should be placed at the start of the line after the last line
    /// in the motion
    | AfterLastLine

/// These are the types of motions which must be handled separately
[<RequireQualifiedAccess>]
type MotionResultFlags = 

    /// No special information for this motion
    | None = 0

    /// Any of the word motions
    | AnyWord = 0x1

    /// This motion was promoted under rule #2 to a line wise motion
    | ExclusiveLineWise = 0x2

    /// This motion when used as a movement should maintain the caret column
    /// setting.
    | MaintainCaretColumn = 0x4

    /// This flag is needed to disambiguate the cases which come up when there
    /// is an empty last line in the buffer.  When that happens it's ambiguous 
    /// if a line wise motion meant to include the last line or merely just 
    /// the line above.  The End is the same in both cases.
    | IncludeEmptyLastLine = 0x8

    /// Marker for the end of line motion.  This affects how the caret column
    /// is maintained
    | EndOfLine = 0x10

    /// When used as a delete argument force a big delete
    | BigDelete = 0x20

    /// Suppress 'exclusive linewise' adjustment 
    | SuppressAdjustment = 0x40

/// Information about the type of the motion this was.
[<RequireQualifiedAccess>]
[<NoComparison>]
type MotionKind =

    | CharacterWiseInclusive

    | CharacterWiseExclusive

    | LineWise

/// Data about a complete motion operation. 
type MotionResult = {

    /// Span of the motion.
    Span: SnapshotSpan

    /// In the case this MotionResult is the result of an exclusive promotion, this will 
    /// hold the original SnapshotSpan
    SpanBeforeExclusivePromotion: SnapshotSpan option

    /// A linewise motion may also have a logical characterwise counter part. Consider for 
    /// example motions like j and k. The motion can be described by the caret point and 
    /// column above / below. This is useful when converting between linewise and characterwise
    /// motions using v or V (:help o_v).
    SpanBeforeLineWise: SnapshotSpan option

    /// Is the motion forward
    IsForward: bool

    /// Kind of the motion
    MotionKind: MotionKind

    /// The flags on the MotionRelult
    MotionResultFlags: MotionResultFlags

    /// In addition to recording the Span, certain motions like j, k, and | also
    /// record data about the desired column within the span.  This value may or may not
    /// be a valid point within the line
    CaretColumn: CaretColumn

} with

    member x.ColumnSpan = SnapshotColumnSpan(x.Span) 

    /// The Span as an EditSpan value
    member x.EditSpan = EditSpan.Single x.ColumnSpan

    /// The OperationKind of the MotionResult
    member x.OperationKind = 
        match x.MotionKind with
        | MotionKind.CharacterWiseExclusive -> OperationKind.CharacterWise
        | MotionKind.CharacterWiseInclusive -> OperationKind.CharacterWise
        | MotionKind.LineWise _ -> OperationKind.LineWise

    /// Is this a word motion 
    member x.IsAnyWordMotion = Util.IsFlagSet x.MotionResultFlags MotionResultFlags.AnyWord

    /// Is this an exclusive motion
    member x.IsExclusive =
        match x.MotionKind with
        | MotionKind.CharacterWiseExclusive -> true
        | MotionKind.CharacterWiseInclusive -> false
        | MotionKind.LineWise _ -> false

    /// Is this an inclusive motion
    member x.IsInclusive = not x.IsExclusive

    /// The Span as a SnapshotLineRange value 
    member x.LineRange = SnapshotLineRangeUtil.CreateForSpan x.Span

    /// The Start or Last line depending on whether the motion is forward or not.  The returned
    /// line will be in the document <see cref="ITextSnapshot">.  
    member x.DirectionLastLine = 
        if x.IsForward then
            // Need to handle the empty last line case here.  If the flag to include
            // the empty last line is set and we are at the empty line then it gets
            // included as the last line
            if SnapshotPointUtil.IsEndPoint x.Span.End && Util.IsFlagSet x.MotionResultFlags MotionResultFlags.IncludeEmptyLastLine then
                SnapshotPointUtil.GetContainingLine x.Span.End
            else
                SnapshotSpanUtil.GetLastLine x.Span
        else
            SnapshotSpanUtil.GetStartLine x.Span

    member x.Start = x.Span.Start

    member x.End = x.Span.End

    member x.Last = SnapshotSpanUtil.GetLastIncludedPoint x.Span

    member x.LastOrStart = x.Last |> OptionUtil.getOrDefault x.Start

    /// This will map every Span inside the MotionResult using the provided mapFunc value and 
    /// return the resulting MotionResult.
    member x.MapSpans mapFunc = 
        let map s =
            match s with
            | None -> (true, None)
            | Some s -> 
                match mapFunc s with
                | Some s -> (true, Some s)
                | None -> (false, None)

        match mapFunc x.Span, map x.SpanBeforeExclusivePromotion, map x.SpanBeforeLineWise with
        | Some s, (true, e), (true, l) -> Some { x with Span = s; SpanBeforeExclusivePromotion = e; SpanBeforeLineWise = l }
        | _ -> None

    static member Create(span: SnapshotSpan, motionKind: MotionKind, ?isForward, ?motionResultFlags, ?caretColumn): MotionResult =
        let isForward = defaultArg isForward true
        let motionResultFlags = defaultArg motionResultFlags MotionResultFlags.None
        let caretColumn = defaultArg caretColumn CaretColumn.None
        {
            Span = span
            SpanBeforeExclusivePromotion = None
            SpanBeforeLineWise = None
            IsForward = isForward
            MotionKind = motionKind
            MotionResultFlags = motionResultFlags
            CaretColumn = caretColumn
        }

    static member Create(span: SnapshotSpan, motionKind: MotionKind, isForward: bool) = MotionResult.Create(span, motionKind, isForward, MotionResultFlags.None, CaretColumn.None)

    static member CreateCharacterWise(span, ?isForward, ?isExclusive, ?motionResultFlags, ?caretColumn) = 
        let isForward = defaultArg isForward true
        let isExclusive = defaultArg isExclusive true
        let motionResultFlags = defaultArg motionResultFlags MotionResultFlags.None
        let caretColumn = defaultArg caretColumn CaretColumn.None
        let kind = 
            if isExclusive then MotionKind.CharacterWiseExclusive
            else MotionKind.CharacterWiseInclusive
        MotionResult.Create(span, kind, isForward, motionResultFlags, caretColumn)

    static member CreateLineWise(span, ?spanBeforeLineWise, ?isForward, ?motionResultFlags, ?caretColumn) =
        let isForward = defaultArg isForward true
        let motionResultFlags = defaultArg motionResultFlags MotionResultFlags.None 
        let caretColumn = defaultArg caretColumn CaretColumn.None
        {
            Span = span
            SpanBeforeExclusivePromotion = None
            SpanBeforeLineWise = spanBeforeLineWise
            IsForward = isForward
            MotionKind = MotionKind.LineWise
            MotionResultFlags = motionResultFlags
            CaretColumn = caretColumn
        }

/// Context on how the motion is being used.  Several motions (]] for example)
/// change behavior based on how they are being used
[<RequireQualifiedAccess>]
[<NoComparison>]
type MotionContext =
    | Movement
    | AfterOperator

/// Arguments necessary to building a Motion
type MotionArgument
    (
        motionContext: MotionContext,
        operatorCount: int option,
        motionCount: int option
    ) =

    /// Context of the Motion
    member x.MotionContext = motionContext

    /// Count passed to the operator
    member x.OperatorCount = operatorCount

    /// Count passed to the motion 
    member x.MotionCount = motionCount

    /// Provides the raw count which is a combination of the OperatorCount
    /// and MotionCount values.  
    member x.Count = 
        match x.MotionCount, x.OperatorCount with
        | None, None -> None
        | Some c, None -> Some c
        | None, Some c  -> Some c
        | Some l, Some r -> Some (l*r)

    /// Resolves the count to a value.  It will use the default 1 for 
    /// any count which is not provided 
    member x.CountOrDefault = 
        match x.Count with
        | Some c -> c
        | None -> 1

    new(motionContext) = MotionArgument(motionContext, None, None)

/// Char searches are interesting because they are defined in one IVimBuffer
/// and can be repeated in any IVimBuffer.  Use a discriminated union here 
/// to name the motion without binding it to a given IVimBuffer or ITextView 
/// which would increase the chance of an accidental memory leak
[<RequireQualifiedAccess>]
[<NoComparison>]
type CharSearchKind  =

    /// Used for the 'f' and 'F' motion.  To the specified char 
    | ToChar

    /// Used for the 't' and 'T' motion.  Till the specified char
    | TillChar

[<RequireQualifiedAccess>]
type BlockKind =

    /// A [] block
    | Bracket 

    /// A () block
    | Paren

    /// A <> block
    | AngleBracket

    /// A {} block
    | CurlyBracket

    with

    member x.Characters = 
        match x with 
        | Bracket -> '[', ']'
        | Paren -> '(', ')'
        | AngleBracket -> '<', '>'
        | CurlyBracket -> '{', '}'

[<RequireQualifiedAccess>]
[<NoComparison>]
type UnmatchedTokenKind =
    | Paren
    | CurlyBracket

[<RequireQualifiedAccess>]
type TagBlockKind =
    | Inner
    | All

/// A discriminated union of the Motion types supported.  These are the primary
/// repeat mechanisms for Motion arguments so it's very important that these 
/// are ITextView / IVimBuffer agnostic.  It will be very common for a Motion 
/// item to be stored and then applied to many IVimBuffer instances.
[<RequireQualifiedAccess>]
type Motion =

    /// Implement the all block motion
    | AllBlock of BlockKind: BlockKind

    /// Implement the 'aw' motion.  This is called once the a key is seen.
    | AllWord of WordKind: WordKind

    /// Implement the 'ap' motion
    | AllParagraph 

    /// Gets count full sentences from the cursor.  If used on a blank line this will
    /// not return a value
    | AllSentence

    /// Move to the beginning of the line.  Interestingly since this command is bound to the '0' it 
    /// can't be associated with a count.  Doing a command like 30 binds as count 30 vs. count 3 
    /// for command '0'
    | BeginingOfLine

    /// Implement the 'ge' / 'gE' motion.  Goes backward to the end of the previous word 
    | BackwardEndOfWord of WordKind: WordKind

    /// The left motion for h
    | CharLeft 

    /// The right motion for l
    | CharRight

    /// The backspace motion
    | SpaceLeft 

    /// The space motion
    | SpaceRight

    /// The arrow motion for <Left>
    | ArrowLeft 

    /// The arrow motion for <Right>
    | ArrowRight

    /// Implements the f, F, t and T motions
    | CharSearch of CharSearchKind: CharSearchKind * SearchPath: SearchPath * Character: char

    /// Get the span of "count" display lines upward.  Display lines can differ when
    /// wrap is enabled
    | DisplayLineUp

    /// Get the span of "count" display lines downward.  Display lines can differ when
    /// wrap is enabled
    | DisplayLineDown

    /// Start of the display line 
    | DisplayLineStart 

    /// End of the display line 
    | DisplayLineEnd

    /// First non blank character on the display line
    | DisplayLineFirstNonBlank

    /// Get the point in the middle of the screen.  This looks at the entire screen not just 
    /// the width of the current line
    | DisplayLineMiddleOfScreen

    /// Implement the 'e' motion.  This goes to the end of the current word.  If we're
    /// not currently on a word it will find the next word and then go to the end of that
    | EndOfWord of WordKind: WordKind
    
    /// Implement an end of line motion.  Typically in response to the $ key.  Even though
    /// this motion deals with lines, it's still a character wise motion motion. 
    | EndOfLine

    /// Find the first non-blank character as the start of the span.  This is an exclusive
    /// motion so be careful we don't go to far forward.  Providing a count to this motion has
    /// no effect
    | FirstNonBlankOnCurrentLine

    /// Find the first non-blank character on the (count - 1) line below this line
    | FirstNonBlankOnLine

    /// Forces a line wise version of the specified motion 
    | ForceLineWise of Motion: Motion

    /// Forces a characterwise version of the specified motion
    | ForceCharacterWise of Motion: Motion

    /// Inner word motion
    | InnerWord of WordKind: WordKind

    /// Inner paragraph motion
    | InnerParagraph

    /// Inner block motion
    | InnerBlock of BlockKind: BlockKind

    /// Find the last non-blank character on the line.  Count causes it to go "count" lines
    /// down and perform the search
    | LastNonBlankOnLine

    /// Find the next occurrence of the last search.  The bool parameter is true if the search
    /// should be in the opposite direction
    | LastSearch of UseOppositeDirection: bool

    /// Handle the lines down to first non-blank motion.  This is one of the motions which 
    /// can accept a count of 0.
    | LineDownToFirstNonBlank

    /// Handle the - motion
    | LineUpToFirstNonBlank

    /// Get the span of "count" lines upward careful not to run off the beginning of the
    /// buffer.  Implementation of the "k" motion
    | LineUp

    /// Get the span of "count" lines downward careful not to run off the end of the
    /// buffer.  Implementation of the "j" motion
    | LineDown

    /// Go to the specified line number or the first line if no line number is provided 
    | LineOrFirstToFirstNonBlank

    /// Go to the specified line number or the last line of no line number is provided
    | LineOrLastToFirstNonBlank

    /// Go to the "count - 1" line from the top of the visible window.  If the count exceeds
    /// the number of visible lines it will end on the last visible line
    | LineFromTopOfVisibleWindow

    /// Go to the "count -1" line from the bottom of the visible window.  If the count 
    /// exceeds the number of visible lines it will end on the first visible line
    | LineFromBottomOfVisibleWindow

    /// Go to the middle line in the visible window.  
    | LineInMiddleOfVisibleWindow

    /// Get the motion to the specified mark.  This is typically accessed via
    /// the ` (back tick) operator and results in an exclusive motion
    | Mark of LocalMark: LocalMark

    /// Get the motion to the line of the specified mark.  This is typically
    /// accessed via the ' (single quote) operator and results in a 
    /// line wise motion
    | MarkLine of LocalMark: LocalMark

    /// Get the matching token from the next token on the line.  This is used to implement
    /// the % motion
    /// If a number is specified, go to {count} percentage in the file
    | MatchingTokenOrDocumentPercent 

    /// Move the caret to the position of the mouse
    | MoveCaretToMouse

    /// Get the motion to the nearest lowercase mark in the specified direction
    | NextMark of SearchPath: SearchPath

    /// Get the motion to the nearest lowercase mark line in the specified direction
    | NextMarkLine of SearchPath: SearchPath

    /// Operate on the next match for last pattern searched for
    | NextMatch of SearchPath: SearchPath

    /// Search for the next occurrence of the word under the caret
    | NextWord of SearchPath: SearchPath

    /// Search for the next partial occurrence of the word under the caret
    | NextPartialWord of SearchPath: SearchPath

    /// Count paragraphs backwards
    | ParagraphBackward

    /// Count paragraphs forward
    | ParagraphForward

    /// The quoted string including the quotes
    | QuotedString of Character: char

    /// The quoted string excluding the quotes
    | QuotedStringContents of Character: char

    /// Repeat the last CharSearch value
    | RepeatLastCharSearch

    /// Repeat the last CharSearch value in the opposite direction
    | RepeatLastCharSearchOpposite

    /// A search for the specified pattern
    | Search of SearchData: SearchData

    /// Backward a section in the editor or to a close brace
    | SectionBackwardOrCloseBrace
    
    /// Backward a section in the editor or to an open brace
    | SectionBackwardOrOpenBrace

    /// Forward a section in the editor or to a close brace
    | SectionForwardOrCloseBrace

    /// Forward a section in the editor
    | SectionForward

    /// Count sentences backward 
    | SentenceBackward

    /// Count sentences forward
    | SentenceForward

    /// Move the the specific column of the current line. Typically in response to the | key. 
    | ScreenColumn

    /// Matching xml / html tags
    | TagBlock of TagBlockKind: TagBlockKind

    /// The [(, ]), ]}, [{ motions
    | UnmatchedToken of SearchPattern: SearchPath * UnmatchedTokenKind: UnmatchedTokenKind

    /// Implement the b/B motion
    | WordBackward of WordKind: WordKind

    /// Implement the w/W motion
    | WordForward of WordKind: WordKind 

/// Interface for running Motion instances against an ITextView
and IMotionUtil =

    /// The associated ITextView instance
    abstract TextView: ITextView

    /// Get the specified Motion value 
    abstract GetMotion: motion: Motion -> motionArgument: MotionArgument -> MotionResult option 

    /// Get the specific text object motion from the given SnapshotPoint
    abstract GetTextObject: motion: Motion -> point: SnapshotPoint -> MotionResult option

    /// Get the expanded tag point for the given tag block kind
    abstract GetExpandedTagBlock: startPoint: SnapshotPoint -> endPoint: SnapshotPoint -> kind: TagBlockKind -> SnapshotSpan option

type ModeKind = 

    /// Initial mode for an IVimBuffer.  It will maintain this mode until the
    /// underlying ITextView completes it's initialization and allows the
    /// IVimBuffer to properly  transition to the mode matching it's
    /// underlying IVimTextBuffer
    | Uninitialized = 0

    | Normal = 1
    | Insert = 2
    | Command = 3
    | VisualCharacter = 4
    | VisualLine = 5
    | VisualBlock = 6 
    | Replace = 7
    | SubstituteConfirm = 8
    | SelectCharacter = 9
    | SelectLine = 10 
    | SelectBlock = 11
    | ExternalEdit = 12

    /// Mode when Vim is disabled.  It won't interact with events it otherwise
    /// would such as selection changes
    | Disabled = 42

[<RequireQualifiedAccess>]
type VisualKind =
    | Character
    | Line
    | Block
    with 

    /// The TextSelectionMode this VisualKind would require
    member x.TextSelectionMode = 
        match x with
        | Character _ -> TextSelectionMode.Stream
        | Line _ -> TextSelectionMode.Stream
        | Block _ -> TextSelectionMode.Box

    member x.VisualModeKind = 
        match x with
        | Character _ -> ModeKind.VisualCharacter
        | Line _ -> ModeKind.VisualLine
        | Block _ -> ModeKind.VisualBlock

    member x.SelectModeKind = 
        match x with
        | Character _ -> ModeKind.SelectCharacter
        | Line _ -> ModeKind.SelectLine
        | Block _ -> ModeKind.SelectBlock

    static member All = [ Character; Line; Block ] |> Seq.ofList

    static member OfModeKind kind =
        match kind with
        | ModeKind.VisualCharacter | ModeKind.SelectCharacter -> VisualKind.Character |> Some
        | ModeKind.VisualLine | ModeKind.SelectLine -> VisualKind.Line |> Some
        | ModeKind.VisualBlock | ModeKind.SelectBlock -> VisualKind.Block |> Some
        | _ -> None

    static member IsAnyVisual kind =
        match kind with
        | ModeKind.VisualCharacter -> true
        | ModeKind.VisualLine -> true
        | ModeKind.VisualBlock -> true
        | _ -> false


    static member IsAnySelect kind =
        match kind with
        | ModeKind.SelectCharacter -> true
        | ModeKind.SelectLine -> true
        | ModeKind.SelectBlock -> true
        | _ -> false

    static member IsAnyVisualOrSelect kind = VisualKind.IsAnyVisual kind || VisualKind.IsAnySelect kind

/// The actual command name.  This is a wrapper over the collection of KeyInput 
/// values which make up a command name.  
///
/// The intent of this type is that two values are equal if the sequence of 
/// KeyInputs are Equal.  
///
/// It is not possible to simple store this as a string as it is possible, and 
/// in fact likely due to certain virtual key codes which are unable to be mapped,
/// for KeyInput values will map to a single char.  Hence to maintain proper semantics
/// we have to use KeyInput values directly.
[<DebuggerDisplay("{ToString(),nq}")>]
type KeyInputSet
    (
        _keyInputs: KeyInput list
    ) =

    let _length = _keyInputs.Length 

    static let s_empty = KeyInputSet([])

    new (keyInput: KeyInput) = 
        KeyInputSet([keyInput])

    new (keyInput1: KeyInput, keyInput2: KeyInput) = 
        let list = keyInput1 :: [keyInput2]
        KeyInputSet(list)

    /// Get the list of KeyInput which represent this KeyInputSet
    member x.KeyInputs = _keyInputs

    /// Length of the contained KeyInput's
    member x.Length = _length

    /// Returns the first KeyInput if present
    member x.FirstKeyInput = 
        match x.KeyInputs with
        | head :: _ -> Some head
        | [] -> None

    /// Returns the rest of the KeyInput values after the first
    member x.Rest = 
        match x.KeyInputs with 
        | [] -> KeyInputSet.Empty
        | _ :: tail -> KeyInputSet(tail)

    /// A string representation of the name.  It is unreliable to use this for anything
    /// other than display as two distinct KeyInput values can map to a single char
    member x.Name = x.KeyInputs |> Seq.map (fun ki -> ki.Char) |> StringUtil.OfCharSeq

    /// Add a KeyInput to the end of this KeyInputSet and return the 
    /// resulting value
    member x.Add keyInput =
        let list = x.KeyInputs @ [keyInput] 
        KeyInputSet(list)

    member x.AddRange (keyInputSet: KeyInputSet) =
        let list = x.KeyInputs @ keyInputSet.KeyInputs
        KeyInputSet(list)

    /// Does the name start with the given KeyInputSet
    member x.StartsWith (keyInputSet: KeyInputSet) = 
        let left = keyInputSet.KeyInputs 
        let right = x.KeyInputs
        if left.Length <= right.Length then
            SeqUtil.contentsEqual (left |> Seq.ofList) (right |> Seq.ofList |> Seq.take left.Length)
        else false

    static member Empty = s_empty

    member x.CompareTo (other: KeyInputSet) = 
        if x.Length <> other.Length then
            x.Length - other.Length 
        else
            let mutable left = x.KeyInputs
            let mutable right = other.KeyInputs
            let mutable value = 0
            while not left.IsEmpty do
                value <- left.Head.CompareTo right.Head
                if value <> 0 then
                    left <- []
                else
                    left <- left.Tail
                    right <- right.Tail
            value

    override x.GetHashCode() = 
        let mutable hashCode = 1
        let mutable current = x.KeyInputs
        while not current.IsEmpty do
            hashCode <- 
                if hashCode = 1 then current.Head.GetHashCode()
                else HashUtil.Combine2 hashCode (current.Head.GetHashCode())
            current <- current.Tail
        hashCode

    override x.Equals(yobj) =
        match yobj with
        | :? KeyInputSet as y -> (x.CompareTo y) = 0
        | _ -> false

    static member op_Equality(this,other) = System.Collections.Generic.EqualityComparer<KeyInputSet>.Default.Equals(this,other)
    static member op_Inequality(this,other) = not (System.Collections.Generic.EqualityComparer<KeyInputSet>.Default.Equals(this,other))

    override x.ToString() =
        x.KeyInputs
        |> Seq.map (fun ki ->
            if ki.Key = VimKey.RawCharacter then ki.Char.ToString()
            elif ki.Key = VimKey.None then "<None>"
            else System.String.Format("<{0}>", ki.Key)  )
        |> StringUtil.OfStringSeq

    interface System.IComparable with
        member x.CompareTo yobj = 
            match yobj with
            | :? KeyInputSet as y -> x.CompareTo y
            | _ -> failwith "Cannot compare values of different types"

module KeyInputSetUtil =

    let Empty = KeyInputSet([])

    let OfSeq sequence = 
        let list = List.ofSeq sequence
        KeyInputSet(list)

    let OfList (list: KeyInput list) = 
        KeyInputSet(list)

    let OfChar c = 
        let keyInput = KeyInputUtil.CharToKeyInput c
        KeyInputSet([keyInput])

    let OfCharArray ([<System.ParamArray>] arr) = 
        let list = 
            arr
            |> Seq.ofArray
            |> Seq.map KeyInputUtil.CharToKeyInput
            |> List.ofSeq
        KeyInputSet(list)

    let OfString (str:string) = str |> Seq.map KeyInputUtil.CharToKeyInput |> OfSeq

    let OfVimKeyArray ([<System.ParamArray>] arr) = 
        arr 
        |> Seq.ofArray 
        |> Seq.map KeyInputUtil.VimKeyToKeyInput
        |> OfSeq

    let Single (keyInput: KeyInput) =
        KeyInputSet(keyInput)

    let Combine (left: KeyInputSet) (right: KeyInputSet) =
        let all = left.KeyInputs @ right.KeyInputs
        OfList all

[<RequireQualifiedAccess>]
[<NoComparison>]
type KeyMappingResult =

    /// The values were not mapped
    | Unmapped of KeyInputSet: KeyInputSet

    /// The values were mapped completely and require no further mapping
    | Mapped of KeyInputSet: KeyInputSet

    /// The values were partially mapped but further mapping is required once the
    /// keys which were mapped are processed.
    ///
    /// The majority of successful mappings will come back as PartiallyMapped because
    /// key mappings can't be evaluated in isolation. The completely mapped keys need 
    /// to be processed before the remaining can be evaluated for further remappings. 
    ///
    /// Consider the following example:
    ///     :nmap qq if
    ///     :imap f dog
    /// Typing 'qq' in normal mode should produce insert mode with the text 'dog'. The 'f'
    /// in the mapping of 'qq' can't be evaluated until the 'i' has gone back to normal 
    /// mode, the mode changed and then insert mode can evaluate the mapping for 'f'
    | PartiallyMapped of MappedKeyInputSet: KeyInputSet * RemainingKeyInputSet: KeyInputSet

    /// The mapping encountered a recursive element that had to be broken 
    | Recursive

    /// More input is needed to resolve this mapping.
    | NeedsMoreInput of KeyInputSet: KeyInputSet

    with 

    /// This will returned the mapped KeyInputSet or KeyInputSet.Empty if no mapping
    /// was possible 
    member x.KeyInputSet = 
        match x with
        | Unmapped keyInputSet -> keyInputSet
        | Mapped keyInputSet -> keyInputSet
        | PartiallyMapped (keyInputSet, _) -> keyInputSet
        | Recursive -> KeyInputSet.Empty
        | NeedsMoreInput _ -> KeyInputSet.Empty

/// Represents the span for a Visual Character mode selection.  If it weren't for the
/// complications of tracking a visual character selection across edits to the buffer
/// there would really be no need for this and we could instead just represent it as 
/// a SnapshotSpan
///
/// The other reason this type is necessary is to differentiate several empty line 
/// cases.  Essentially how empty line should be handled when the span includes the 
/// line break of the line above 
[<StructuralEquality>]
[<NoComparison>]
[<Struct>]
[<DebuggerDisplay("{ToString()}")>]
type CharacterSpan = 

    val private _start: VirtualSnapshotPoint
    val private _lineCount: int
    val private _lastLineMaxPositionCount: int
    val private _useVirtualSpace: bool

    new (start: SnapshotPoint, lineCount: int, lastLineMaxPositionCount: int) =
        let virtualStart = VirtualSnapshotPointUtil.OfPoint start
        CharacterSpan(virtualStart, lineCount, lastLineMaxPositionCount, false)

    new (start: VirtualSnapshotPoint, lineCount: int, lastLineMaxPositionCount: int, useVirtualSpace: bool) =
        Contract.Requires(lineCount > 0)
        { 
            _start = start
            _lineCount = lineCount
            _lastLineMaxPositionCount = lastLineMaxPositionCount
            _useVirtualSpace = useVirtualSpace }

    new (span: SnapshotSpan) = 
        let lineCount = SnapshotSpanUtil.GetLineCount span
        let lastLine = SnapshotSpanUtil.GetLastLine span
        let startPoint =
            if lineCount = 1 then
                span.Start
            else
                lastLine.Start
        let endPoint =

            // Don't let the last line of the CharacterSpan end partially into a line
            // break.  Encompass the entire line break instead
            if span.End.Position > lastLine.End.Position then
                lastLine.EndIncludingLineBreak
            else
                span.End
        let lastLineLength = endPoint.Position - startPoint.Position
        CharacterSpan(span.Start, lineCount, lastLineLength)

    new (span: VirtualSnapshotSpan, useVirtualSpace: bool) =
        if span.Start.IsInVirtualSpace || span.End.IsInVirtualSpace then
            let lineCount = VirtualSnapshotSpanUtil.GetLineCount span
            let lastLine = VirtualSnapshotSpanUtil.GetLastLine span
            let endOffset = VirtualSnapshotPointUtil.GetLineOffset span.End
            let lastLineLength =
                if lineCount = 1 then
                    let startOffset = VirtualSnapshotPointUtil.GetLineOffset span.Start
                    let endOffsetInLastLine =
                        if span.Length <> 0 && endOffset = 0 then
                            lastLine.LengthIncludingLineBreak
                        else
                            endOffset
                    endOffsetInLastLine - startOffset
                else
                    endOffset
            CharacterSpan(span.Start, lineCount, lastLineLength, useVirtualSpace)
        else
            CharacterSpan(span.SnapshotSpan)

    new (startPoint: SnapshotPoint, endPoint: SnapshotPoint) =
        let span = SnapshotSpan(startPoint, endPoint)
        CharacterSpan(span)

    new (startPoint: VirtualSnapshotPoint, endPoint: VirtualSnapshotPoint, useVirtualSpace: bool) =
        let span = VirtualSnapshotSpan(startPoint, endPoint)
        CharacterSpan(span, useVirtualSpace)

    member x.UseVirtualSpace = x._useVirtualSpace

    member x.Snapshot = x._start.Position.Snapshot

    member x.StartLine = SnapshotPointUtil.GetContainingLine x.Start

    member x.Start =  x._start.Position

    member x.VirtualStart =  x._start

    member x.LineCount = x._lineCount

    /// The maximum number of position elements this character span should occupy on the last line. This
    /// is maintained to allowed characters spans to maintain their ideal width across initial position
    /// changes in the buffer.
    member x.LastLineMaxPositionCount = x._lastLineMaxPositionCount

    /// Actual number of positions in the last line
    member x.LastLinePositionCount = 
        let lastLineStart = 
            let lastLine = x.LastLine
            if x.LineCount = 1 then x.Start
            else lastLine.Start
        x.End.Position - lastLineStart.Position

    /// The last line in the CharacterSpan
    member x.LastLine: ITextSnapshotLine = 
        let number = x.StartLine.LineNumber + (x._lineCount - 1)
        SnapshotUtil.GetLineOrLast x.Snapshot number

    /// The last point included in the CharacterSpan
    member x.Last = 
        let endPoint: SnapshotPoint = x.End
        if endPoint.Position = x.Start.Position then
            None
        else
            SnapshotPoint(x.Snapshot, endPoint.Position - 1) |> Some

    /// The last virtual point included in the CharacterSpan
    member x.VirtualLast =
        let startPoint = x.VirtualStart
        let endPoint = x.VirtualEnd
        if endPoint = startPoint then
            None
        else
            let point =
                endPoint
                |> VirtualSnapshotPointUtil.GetPreviousCharacterSpanWithWrap
            if point.Position.Position < startPoint.Position.Position then
                None
            else
                Some point

    /// Get the End point of the Character Span.
    member x.End: SnapshotPoint =
        let lastLine = x.LastLine
        let offset = 
            if x._lineCount = 1 then
                // For a single line we need to apply the offset past the start point
                SnapshotPointUtil.GetLineOffset x._start.Position + x.LastLineMaxPositionCount
            else
                x.LastLineMaxPositionCount

        // The original SnapshotSpan could extend into the line break and hence we must
        // consider that here.  The most common case for this occurring is when the caret
        // in visual mode is on the first column of an empty line.  In that case the caret
        // is really in the line break so End is one past that
        let endPoint = SnapshotLineUtil.GetOffsetOrEndIncludingLineBreak offset lastLine

        // Make sure that we don't create a negative SnapshotSpan.  Really we should
        // be verifying the arguments to ensure we don't but until we do fix up
        // potential errors here
        if x._start.Position.Position <= endPoint.Position then
            endPoint
        else
            x._start.Position

    /// Get the End point of the Character Span.
    member x.VirtualEnd =
        if x.UseVirtualSpace then
            let lastLine = x.LastLine
            let offset =
                if x._lineCount = 1 then
                    // For a single line we need to apply the offset past the start point
                    VirtualSnapshotPointUtil.GetLineOffset x._start + x.LastLineMaxPositionCount
                else
                    x.LastLineMaxPositionCount

            let virtualColumn = VirtualSnapshotColumn.GetForColumnNumber(lastLine, offset)
            virtualColumn.VirtualStartPoint
        else
            x.End |> VirtualSnapshotPointUtil.OfPoint

    member x.Span = SnapshotSpan(x.Start, x.End)

    member x.VirtualSpan = VirtualSnapshotSpan(x.VirtualStart, x.VirtualEnd)

    member x.ColumnSpan = SnapshotColumnSpan(x.Span)

    member x.VirtualColumnSpan = VirtualSnapshotColumnSpan(x.VirtualSpan)

    member x.Length = x.Span.Length

    member x.VirtualLength = x.VirtualSpan.Length

    member x.IncludeLastLineLineBreak = x.End.Position > x.LastLine.End.Position

    member internal x.MaybeAdjustToIncludeLastLineLineBreak() = 
        if x.UseVirtualSpace then
            x
        elif x.End.Position >= x.LastLine.End.Position then
            let endPoint = x.LastLine.EndIncludingLineBreak
            CharacterSpan(x.Start, endPoint)
        else
            x

    override x.ToString() =
        x.VirtualSpan.ToString()

    static member op_Equality(this,other) = System.Collections.Generic.EqualityComparer<CharacterSpan>.Default.Equals(this,other)
    static member op_Inequality(this,other) = not (System.Collections.Generic.EqualityComparer<CharacterSpan>.Default.Equals(this,other))

/// Represents the span for a Visual Block mode selection. Because Visual
/// Studio cannot display a ragged box selection, when 'end-of-line' is true,
/// we still use a non-zero spaces width to visually represent the block span
[<StructuralEquality>]
[<NoComparison>]
[<Struct>]
type BlockSpan =

    val private _startColumn: VirtualSnapshotColumn
    val private _tabStop: int
    val private _spaces: int
    val private _height: int
    val private _endOfLine: bool

    new(startPoint: SnapshotPoint, tabStop, spaces, height) =
        let startColumn = VirtualSnapshotColumn(startPoint)
        { _startColumn = startColumn; _tabStop = tabStop; _spaces = spaces; _height = height; _endOfLine = false }

    new(startPoint: SnapshotPoint, tabStop, spaces, height, endOfLine) =
        let startColumn = VirtualSnapshotColumn(startPoint)
        { _startColumn = startColumn; _tabStop = tabStop; _spaces = spaces; _height = height; _endOfLine = endOfLine }

    new(startPoint: VirtualSnapshotPoint, tabStop, spaces, height, endOfLine) =
        let startColumn = VirtualSnapshotColumn(startPoint)
        { _startColumn = startColumn; _tabStop = tabStop; _spaces = spaces; _height = height; _endOfLine = endOfLine }

    new(startColumn: VirtualSnapshotColumn, tabStop, spaces, height, endOfLine) =
        { _startColumn = startColumn; _tabStop = tabStop; _spaces = spaces; _height = height; _endOfLine = endOfLine }

    /// Create a BlockSpan for the given SnapshotSpan.  The returned BlockSpan
    /// will have a minimum of 1 for height and width.  The start of the
    /// BlockSpan is not necessarily the Start of the SnapshotSpan as an End
    /// column which occurs before the start could cause the BlockSpan start to
    /// be before the SnapshotSpan start
    new(span: VirtualSnapshotColumnSpan, tabStop: int) =

        // The start of the span is by definition before the end of the span
        // but we also have to handle upper-left/lower-right vs.
        // upper-right/lower-left.
        let startColumn, width =
            let startColumnSpaces = span.Start.GetSpacesToColumn tabStop
            let endColumnSpaces = span.End.GetSpacesToColumn tabStop
            let width = endColumnSpaces - startColumnSpaces

            if width = 0 then
                span.Start, 1
            elif width > 0 then
                span.Start, width
            else
                VirtualSnapshotColumn.GetColumnForSpaces(span.Start.Line, endColumnSpaces, tabStop), -width

        let height = VirtualSnapshotSpanUtil.GetLineCount span.VirtualSpan
        BlockSpan(startColumn, tabStop = tabStop, spaces = width, height = height, endOfLine = false)

    /// Create a BlockSpan for the given SnapshotSpan.  The returned BlockSpan
    /// will have a minimum of 1 for height and width.  The start of the
    /// BlockSpan is not necessarily the Start of the SnapshotSpan as an End
    /// column which occurs before the start could cause the BlockSpan start to
    /// be before the SnapshotSpan start
    new(span: VirtualSnapshotSpan, tabStop: int) =
        let span = VirtualSnapshotColumnSpan(span)
        BlockSpan(span, tabStop)

    /// Get the virtual start point of the BlockSpan
    member x.VirtualStart = x._startColumn

    /// The SanpshotColumn which begins this BlockSpan
    member x.Start = x.VirtualStart.Column

    /// In what space does this BlockSpan begin
    member x.BeforeSpaces = x.Start.GetSpacesToColumn x.TabStop

    /// In what space does this BlockSpan begin
    member x.VirtualBeforeSpaces = x.VirtualStart.GetSpacesToColumn x.TabStop

    /// How many spaces does this BlockSpan occupy?  Be careful to treat this
    /// value as spaces, not columns.  The different being that tabs count as
    /// 'tabStop' spaces but only 1 column
    member x.SpacesLength = x._spaces

    /// How many lines does this BlockSpan encompass
    member x.Height = x._height

    /// Whether this block span extends to the end of line
    member x.EndOfLine = x._endOfLine

    member x.OverlapEnd =
        let line =
            let lineNumber = x.Start.LineNumber
            SnapshotUtil.GetLineOrLast x.Snapshot (lineNumber + (x._height - 1))
        let totalSpaces = x.BeforeSpaces + x.SpacesLength
        SnapshotOverlapColumn.GetColumnForSpacesOrEnd(line, totalSpaces, x.TabStop)

    /// Get the end column (exclusive) of the BlockSpan
    member x.End =
        let column = x.OverlapEnd
        if column.SpacesBefore > 0 then column.Column.AddOneOrCurrent()
        else column.Column

    /// Get the virtual end point (exclusive) of the BlockSpan
    member x.VirtualEnd =
        let column = VirtualSnapshotColumn(x.End)
        if column.Column.IsLineBreak then
            let realSpaces = SnapshotColumn.GetSpacesOnLine(column.Line, x.TabStop)
            let virtualSpaces = x.BeforeSpaces + x.SpacesLength - realSpaces
            column.AddInLine(virtualSpaces)
        else
            column

    /// What is the tab stop this BlockSpan is created off of
    member x.TabStop = x._tabStop

    member x.Snapshot = x.Start.Snapshot

    member x.TextBuffer =  x.Start.Snapshot.TextBuffer

    member private x.GetBlockSpansCore (makeSpan: ITextSnapshotLine -> int -> 'T): NonEmptyCollection<'T> =
        let x = x
        seq {
            for i = 0 to x.Height - 1 do
                let lineNumber = x.Start.LineNumber + i
                match SnapshotUtil.TryGetLine x.Snapshot lineNumber with
                | None -> ()
                | Some line ->
                    yield makeSpan line x.BeforeSpaces
        }
        |> NonEmptyCollectionUtil.OfSeq
        |> Option.get

    member private x.GetEndColumn line = SnapshotColumn(line, line.End)

    /// Get the NonEmptyCollection<SnapshotSpan> for the given block
    /// information
    member x.BlockColumnSpans: NonEmptyCollection<SnapshotColumnSpan> =
        let x = x
        x.GetBlockSpansCore (fun line beforeSpaces ->
            let startColumn = SnapshotColumn.GetColumnForSpacesOrEnd(line, beforeSpaces, x.TabStop)
            let endColumn =
                if x.EndOfLine then
                    x.GetEndColumn line
                else
                    SnapshotColumn.GetColumnForSpacesOrEnd(line, beforeSpaces + x.SpacesLength, x.TabStop)
            SnapshotColumnSpan(startColumn, endColumn))

    /// Get the NonEmptyCollection<VirtualSnapshotSpan> for the given block
    /// information
    member x.BlockVirtualColumnSpans: NonEmptyCollection<VirtualSnapshotColumnSpan> =
        let x = x
        x.GetBlockSpansCore (fun line beforeSpaces ->
            let startColumn = VirtualSnapshotColumn.GetColumnForSpaces(line, beforeSpaces, x.TabStop)
            let endColumn =
                if x.EndOfLine then
                    VirtualSnapshotColumn(x.GetEndColumn line)
                else
                    VirtualSnapshotColumn.GetColumnForSpaces(line, beforeSpaces + x.SpacesLength, x.TabStop)
            VirtualSnapshotColumnSpan(startColumn, endColumn))

    /// Get a NonEmptyCollection indicating of the SnapshotOverlapColumnSpan
    /// that each line of this block spans, along with the offset (measured in
    /// cells) of the block with respect to the start point and end point
    member x.BlockOverlapColumnSpans: NonEmptyCollection<SnapshotOverlapColumnSpan> =
        let x = x
        x.GetBlockSpansCore (fun line beforeSpaces ->
            let startColumn = SnapshotOverlapColumn.GetColumnForSpacesOrEnd(line, beforeSpaces, x.TabStop)
            let endColumn =
                if x.EndOfLine then
                    SnapshotOverlapColumn(x.GetEndColumn line, x.TabStop)
                else
                    SnapshotOverlapColumn.GetColumnForSpacesOrEnd(line, beforeSpaces + x.SpacesLength, x.TabStop)
            SnapshotOverlapColumnSpan(startColumn, endColumn, x.TabStop))

    member x.BlockSpans = x.BlockColumnSpans |> NonEmptyCollectionUtil.Map (fun s -> s.Span)

    member x.BlockVirtualSpans = x.BlockVirtualColumnSpans |> NonEmptyCollectionUtil.Map (fun s -> s.VirtualSpan)

    /// Adjust the block span for the specified 'end-of-line' setting
    member x.AdjustWithEndOfLine endOfLine =
        BlockSpan(x.VirtualStart, x.TabStop, x.SpacesLength, x.Height, endOfLine)

    override x.ToString() =
        sprintf "Point: %s Spaces: %d Height: %d EndOfLine: %b" (x.Start.ToString()) x._spaces x._height x._endOfLine

    static member op_Equality(this,other) = System.Collections.Generic.EqualityComparer<BlockSpan>.Default.Equals(this,other)
    static member op_Inequality(this,other) = not (System.Collections.Generic.EqualityComparer<BlockSpan>.Default.Equals(this,other))

    static member CreateForSpan (span: SnapshotSpan) tabStop =
        let virtualSpan = VirtualSnapshotSpanUtil.OfSpan span
        BlockSpan(virtualSpan, tabStop)

[<RequireQualifiedAccess>]
[<NoComparison>]
type BlockCaretLocation =
    | TopLeft
    | TopRight
    | BottomLeft
    | BottomRight

/// Represents a visual span of text in the form Vim understands.  This type understands 
/// nothing about the intricacies of Visual Mode selection.  It simply understands how
/// to represent the Spans it can occupy.
///
/// Note: There is no use of inclusive or exclusive in this type.  That is intentional.  This
/// type is simply a measurement.  The context in which it was measured is important for
/// the types which care about the context
[<RequireQualifiedAccess>]
[<StructuralEquality>]
[<NoComparison>]
[<DebuggerDisplay("{ToString()}")>]
type VisualSpan =

    /// A character wise span.  The 'End' of the span is not selected.
    | Character of CharacterSpan: CharacterSpan

    /// A line wise span
    | Line of LineRange: SnapshotLineRange

    /// A block span.  
    | Block of BlockSpan: BlockSpan

    with

    /// Return the Spans which make up this VisualSpan instance
    member x.Spans = 
        match x with 
        | VisualSpan.Character characterSpan -> [characterSpan.Span] |> Seq.ofList
        | VisualSpan.Line range -> [range.ExtentIncludingLineBreak] |> Seq.ofList
        | VisualSpan.Block blockSpan -> blockSpan.BlockSpans :> SnapshotSpan seq

    /// Return the per line spans which make up this VisualSpan instance
    member x.PerLineSpans =
        match x with
        | VisualSpan.Character characterSpan ->
            seq {
                let leadingEdge, middle, trailingEdge =
                    SnapshotSpanUtil.GetLinesAndEdges characterSpan.Span
                match leadingEdge with
                | Some span -> yield span
                | None -> ()
                match middle with
                | Some lineRange ->
                    let spans =
                        lineRange.Lines
                        |> Seq.map SnapshotLineUtil.GetExtentIncludingLineBreak
                    yield! spans
                | None -> ()
                match trailingEdge with
                | Some span -> yield span
                | None -> ()
            }
        | VisualSpan.Line lineRange ->
            lineRange.Lines
            |> Seq.map SnapshotLineUtil.GetExtentIncludingLineBreak
        | VisualSpan.Block blockSpan ->
            blockSpan.BlockSpans
            :> SnapshotSpan seq

    member x.GetOverlapColumnSpans(tabStop: int): SnapshotOverlapColumnSpan seq =
        match x with
        | VisualSpan.Character characterSpan -> Seq.singleton (SnapshotOverlapColumnSpan(characterSpan.ColumnSpan, tabStop))
        | VisualSpan.Line range -> Seq.singleton (SnapshotOverlapColumnSpan(range.ColumnExtentIncludingLineBreak, tabStop))
        | VisualSpan.Block blockSpan -> blockSpan.BlockOverlapColumnSpans :> SnapshotOverlapColumnSpan seq

    /// Returns the EditSpan for this VisualSpan
    member x.EditSpan = 
        match x with
        | VisualSpan.Character characterSpan -> EditSpan.Single characterSpan.ColumnSpan
        | VisualSpan.Line range -> EditSpan.Single range.ColumnExtentIncludingLineBreak
        | VisualSpan.Block blockSpan -> EditSpan.Block blockSpan.BlockOverlapColumnSpans

    /// Returns the SnapshotLineRange for the VisualSpan.  For Character this will
    /// just expand out the Span.  For Line this is an identity.  For Block it will
    /// return the overarching span
    member x.LineRange = 
        match x with
        | VisualSpan.Character characterSpan -> SnapshotLineRangeUtil.CreateForSpan characterSpan.Span
        | VisualSpan.Line range -> range
        | VisualSpan.Block _ -> x.EditSpan.OverarchingSpan |> SnapshotLineRangeUtil.CreateForSpan

    /// Returns the start point of the Visual Span.  This can be None in the case
    /// of an empty Block selection.
    member x.Start =
        match x with
        | Character characterSpan -> characterSpan.Start
        | Line range ->  range.Start
        | Block blockSpan -> blockSpan.Start.StartPoint

    /// Get the end of the Visual Span
    member x.End = 
        match x with
        | VisualSpan.Character characterSpan -> characterSpan.End
        | VisualSpan.Line lineRange -> lineRange.End
        | VisualSpan.Block blockSpan -> blockSpan.End.StartPoint

    /// What type of OperationKind does this VisualSpan represent
    member x.OperationKind =
        match x with
        | VisualSpan.Character _ -> OperationKind.CharacterWise
        | VisualSpan.Line _ -> OperationKind.LineWise
        | VisualSpan.Block _ -> OperationKind.CharacterWise

    /// What type of ModeKind does this VisualSpan represent
    member x.ModeKind =
        match x with
        | VisualSpan.Character _ -> ModeKind.VisualCharacter
        | VisualSpan.Line _ -> ModeKind.VisualLine
        | VisualSpan.Block _ -> ModeKind.VisualBlock

    /// VisualKind of the VisualSpan
    member x.VisualKind = 
        match x with
        | VisualSpan.Character _ -> VisualKind.Character
        | VisualSpan.Block _ -> VisualKind.Block
        | VisualSpan.Line _ -> VisualKind.Line

    member x.AdjustForExtendIntoLineBreak extendIntoLineBreak = 
        if extendIntoLineBreak then
            match x with
            | Character characterSpan -> characterSpan.MaybeAdjustToIncludeLastLineLineBreak() |> VisualSpan.Character
            | Line _ -> x
            | Block _ -> x
        else
            x

    /// Adjust the visual span for a new 'end-of-line' setting
    member x.AdjustWithEndOfLine endOfLine =
        match x with
        | Block blockSpan ->
            let blockSpan = blockSpan.AdjustWithEndOfLine endOfLine
            VisualSpan.Block blockSpan
        | _ ->
            x

    /// Select the given VisualSpan in the ITextView
    member x.Select (textView: ITextView) path =

        // Select the given VirtualSnapshotSpan
        let selectVirtualSpan startPoint endPoint =

            textView.Selection.Mode <- TextSelectionMode.Stream

            let caretPoint = TextViewUtil.GetCaretVirtualPoint textView
            let anchorPoint, activePoint =
                match path with
                | SearchPath.Forward -> startPoint, endPoint
                | SearchPath.Backward -> endPoint, startPoint

            TextViewUtil.Select textView caretPoint anchorPoint activePoint

        // Select the given SnapshotSpan
        let selectSpan startPoint endPoint = 
            let startPoint = startPoint |> VirtualSnapshotPointUtil.OfPointConsiderLineBreak
            let endPoint = endPoint |> VirtualSnapshotPointUtil.OfPointConsiderLineBreak
            selectVirtualSpan startPoint endPoint

        match x with
        | Character characterSpan ->
            if characterSpan.UseVirtualSpace then
                selectVirtualSpan characterSpan.VirtualStart characterSpan.VirtualEnd
            else
                let endPoint =
                    if characterSpan.IncludeLastLineLineBreak then
                        SnapshotPointUtil.AddOneOrCurrent characterSpan.LastLine.End
                    else
                        characterSpan.End
                selectSpan characterSpan.Start endPoint
        | Line lineRange ->
            selectSpan lineRange.Start lineRange.EndIncludingLineBreak
        | Block blockSpan ->
            // In general the Start and End accurately represent the selection points.  The cases where
            // it doesn't is when the caret ends inside of a point when spaces are considered. The
            // clearest example of this is when the caret is inside a tab (assume tabstop = 4)
            //
            //  truck
            //  \t  cat
            //
            // In this example if the anchor point is 'r' and the caret is under 'u' (2 space selection) then 
            // the block span of the second line is the 2nd and 3rd space.  The tab itself is a single 
            // SnapshotPoint numbering 4 spaces.  Hence the caret will be in the middle of a SnapshotPoint
            //
            // When this happens the anchor point is reset to min of the column of the SnapshotPoint the
            // caret resides in or the column of the anchor point
            let endPoint = blockSpan.OverlapEnd
            let startPoint =
                if endPoint.SpacesBefore > 0 then
                    let startOffset = SnapshotPointUtil.GetLineOffset blockSpan.Start.StartPoint
                    let endOffset = SnapshotPointUtil.GetLineOffset endPoint.Column.StartPoint
                    let offset = min startOffset endOffset
                    let startLine = blockSpan.Start.Line
                    SnapshotLineUtil.GetOffsetOrEnd offset startLine
                    |> VirtualSnapshotPointUtil.OfPoint
                else
                    blockSpan.VirtualStart.VirtualStartPoint
            textView.Selection.Mode <- TextSelectionMode.Box
            let caretPoint = TextViewUtil.GetCaretVirtualPoint textView
            TextViewUtil.Select textView caretPoint startPoint blockSpan.VirtualEnd.VirtualStartPoint

    override x.ToString() =
        match x with
        | VisualSpan.Character characterSpan -> sprintf "Character: %O" characterSpan
        | VisualSpan.Line lineRange -> sprintf "Line: %O" lineRange
        | VisualSpan.Block blockSpan -> sprintf "Block: %O" blockSpan

    /// Create the VisualSpan based on the specified points.  The activePoint is assumed
    /// to be the end of the selection and hence not included (exclusive) just as it is 
    /// in ITextSelection
    static member CreateForVirtualSelectionPoints visualKind (anchorPoint: VirtualSnapshotPoint) (activePoint: VirtualSnapshotPoint) tabStop useVirtualSpace =

        match visualKind with
        | VisualKind.Character ->
            let startPoint, endPoint = VirtualSnapshotPointUtil.OrderAscending anchorPoint activePoint
            let span = VirtualSnapshotSpan(startPoint, endPoint)
            let characterSpan = CharacterSpan(span, useVirtualSpace)
            Character characterSpan
        | VisualKind.Line ->

            let startPoint, endPoint = SnapshotPointUtil.OrderAscending anchorPoint.Position activePoint.Position
            let startLine = SnapshotPointUtil.GetContainingLine startPoint

            // If endPoint is EndIncludingLineBreak we would get the line after and be 
            // one line too big.  Go back on point to ensure we don't expand the span
            let endLine = 
                if startPoint = endPoint then
                    startLine
                else
                    let endPoint = SnapshotPointUtil.SubtractOneOrCurrent endPoint
                    SnapshotPointUtil.GetContainingLine endPoint
            SnapshotLineRangeUtil.CreateForLineRange startLine endLine |> Line

        | VisualKind.Block -> 
            let startPoint, endPoint = VirtualSnapshotPointUtil.OrderAscending anchorPoint activePoint
            let span = VirtualSnapshotSpan(startPoint, endPoint)
            BlockSpan(span, tabStop) |> Block

    static member CreateForSelectionPoints visualKind (anchorPoint: SnapshotPoint) (activePoint: SnapshotPoint) tabStop =
        let virtualAnchorPoint = VirtualSnapshotPointUtil.OfPoint anchorPoint
        let virtualActivePoint = VirtualSnapshotPointUtil.OfPoint activePoint
        VisualSpan.CreateForVirtualSelectionPoints visualKind virtualAnchorPoint virtualActivePoint tabStop false

    /// Create a VisualSelection based off of the current selection.  If no selection is present
    /// then an empty VisualSpan will be created at the caret
    static member CreateForVirtualSelection (textView: ITextView) visualKind tabStop useVirtualSpace =
        let selection = textView.Selection
        if selection.IsEmpty then
            let caretPoint = TextViewUtil.GetCaretVirtualPoint textView
            VisualSpan.CreateForVirtualSelectionPoints visualKind caretPoint caretPoint tabStop useVirtualSpace
        else
            let anchorPoint = selection.AnchorPoint
            let activePoint = selection.ActivePoint

            // Need to special case the selection ending in, and encompassing, an empty line.  Once you 
            // get rid of the virtual points here it's impossible to distinguish from the case where the 
            // selection ends in the line above instead.  
            if
                not useVirtualSpace
                && visualKind = VisualKind.Character
                && selection.End.Position.GetContainingLine().Length = 0
            then
                let endPoint = SnapshotPointUtil.AddOneOrCurrent selection.End.Position 
                let characterSpan = CharacterSpan(selection.Start.Position, endPoint)
                Character characterSpan
            else
                let visualSpan = VisualSpan.CreateForVirtualSelectionPoints visualKind anchorPoint activePoint tabStop useVirtualSpace
                if visualKind <> VisualKind.Line && useVirtualSpace then
                    visualSpan
                else
                    visualSpan.AdjustForExtendIntoLineBreak selection.End.IsInVirtualSpace

    static member CreateForSelection (textView: ITextView) visualKind tabStop =
        VisualSpan.CreateForVirtualSelection textView visualKind tabStop false

    static member CreateForSpan (span: SnapshotSpan) visualKind tabStop =
        match visualKind with
        | VisualKind.Character -> CharacterSpan(span) |> Character
        | VisualKind.Line -> span |> SnapshotLineRangeUtil.CreateForSpan |> Line
        | VisualKind.Block -> BlockSpan.CreateForSpan span tabStop |> Block

/// Represents the information for a visual mode selection.  All of the values are expressed
/// in terms of an inclusive selection.
///
/// Note: It's intentional that inclusive and exclusive are not included in this particular
/// structure.  Whether or not the selection is inclusive or exclusive doesn't change the 
/// anchor / caret point.  It just changes what is operated on and what is actually 
/// physically selected by visual mode
[<RequireQualifiedAccess>] 
[<StructuralEquality>]
[<NoComparison>] 
type VisualSelection =

    /// The underlying span and whether or not this is a forward looking span.  
    | Character of CharacterSpan: CharacterSpan * SearchPath: SearchPath

    /// The underlying range, whether or not is forwards or backwards and the int 
    /// is which column in the range the caret should be placed in
    | Line of LineRange: SnapshotLineRange * SearchPath: SearchPath * ColumnNumber: int 

    /// Just keep the BlockSpan and the caret information for the block
    | Block of BlockSpan: BlockSpan * BlockCaretLocation: BlockCaretLocation

    with

    member x.IsForward =
        match x with
        | Character (_, path) -> path.IsSearchPathForward
        | Line (_, path, _) -> path.IsSearchPathForward
        | Block _ -> true

    member x.IsCharacterForward =
        match x with
        | Character (_, path) -> path.IsSearchPathForward
        | _ -> false

    member x.IsLineForward = 
        match x with
        | Line (_, path, _) -> path.IsSearchPathForward
        | _ -> false

    member x.VisualKind = 
        match x with
        | Character _ -> VisualKind.Character
        | Line _ -> VisualKind.Line
        | Block _ -> VisualKind.Block

    /// The underlying VisualSpan
    member x.VisualSpan =
        match x with 
        | Character (characterSpan, _) -> VisualSpan.Character characterSpan
        | Line (lineRange, _, _) -> VisualSpan.Line lineRange
        | Block (blockSpan, _) -> VisualSpan.Block blockSpan

    member x.AdjustForExtendIntoLineBreak extendIntoLineBreak = 
        if extendIntoLineBreak then
            match x with
            | Character (characterSpan, path) -> 
                let characterSpan = characterSpan.MaybeAdjustToIncludeLastLineLineBreak()
                VisualSelection.Character (characterSpan, path)
            | Line _ -> x
            | Block _ -> x
        else
            x

    /// Get the VisualSelection information adjusted for the given selection kind.  This is only useful 
    /// when a VisualSelection is created from the caret position and the selection needs to be adjusted
    /// to include or exclude the caret.  
    /// 
    /// It's incorrect to use when creating from an actual physical selection
    member x.AdjustForSelectionKind selectionKind = 
        match selectionKind with
        | SelectionKind.Inclusive -> x
        | SelectionKind.Exclusive -> 
            match x with
            | Character (characterSpan, path) -> 
                if
                    not characterSpan.UseVirtualSpace
                    && SnapshotPointUtil.IsEndPoint characterSpan.End
                then
                    x
                else
                    // The span decreases by a single character in exclusive
                    let characterSpan =
                        let endPoint = characterSpan.VirtualLast |> OptionUtil.getOrDefault characterSpan.VirtualStart
                        CharacterSpan(VirtualSnapshotSpan(characterSpan.VirtualStart, endPoint), characterSpan.UseVirtualSpace)
                    VisualSelection.Character (characterSpan, path)
            | Line _ ->
                // The span isn't effected
                x
            | Block (blockSpan, blockCaretLocation) -> 
                // The width of a block span decreases by 1 in exclusive.  The minimum though
                // is still 1
                let width = max (blockSpan.SpacesLength - 1) 1
                let blockSpan = BlockSpan(blockSpan.VirtualStart, blockSpan.TabStop, width, blockSpan.Height, blockSpan.EndOfLine)
                VisualSelection.Block (blockSpan, blockCaretLocation)

    /// Adjust the visual selection for a new 'end-of-line' setting
    member x.AdjustWithEndOfLine endOfLine =
        match x with
        | Block (blockSpan, blockCaretLocation) ->
            let blockSpan = blockSpan.AdjustWithEndOfLine endOfLine
            VisualSelection.Block (blockSpan, blockCaretLocation)
        | _ ->
            x

    /// Gets the SnapshotPoint for the caret as it should appear in the given VisualSelection with the 
    /// specified SelectionKind.  
    member x.GetCaretVirtualPoint selectionKind =

        let getAdjustedEnd (span: SnapshotSpan) = 
            if span.Length = 0 then
                span.Start
            else
                match selectionKind with
                | SelectionKind.Exclusive -> span.End
                | SelectionKind.Inclusive -> 
                    if span.Length > 0 then
                        SnapshotPointUtil.SubtractOne span.End
                    else
                        span.End

        let getAdjustedVirtualEnd (span: VirtualSnapshotSpan) =
            if span.Length = 0 then
                span.Start
            else
                match selectionKind with
                | SelectionKind.Exclusive -> span.End
                | SelectionKind.Inclusive ->
                    if span.Length > 0 then
                        VirtualSnapshotPointUtil.SubtractOneOrCurrent span.End
                    else
                        span.End

        match x with
        | Character (characterSpan, path) ->
            // The caret is either positioned at the start or the end of the selected
            // SnapshotSpan
            match path with
            | SearchPath.Forward ->
                if selectionKind = SelectionKind.Inclusive && characterSpan.LastLine.Length = 0 then
                    // Need to special case the empty last line because there is no character which
                    // isn't inside the line break here.  Just return the start as the caret position
                    characterSpan.LastLine.Start
                else
                    getAdjustedEnd characterSpan.Span
            | SearchPath.Backward -> characterSpan.Start
            |> VirtualSnapshotPointUtil.OfPoint

        | Line (snapshotLineRange, path, column) ->

            // The caret is either positioned at the start or the end of the selected range
            // and can be on any column in either
            let line = 
                if path.IsSearchPathForward then
                    snapshotLineRange.LastLine
                else
                    snapshotLineRange.StartLine

            if column <= line.LengthIncludingLineBreak then
                SnapshotPointUtil.Add column line.Start
            else
                line.End
            |> VirtualSnapshotPointUtil.OfPoint

        | Block (blockSpan, blockCaretLocation) ->

            match blockCaretLocation with
            | BlockCaretLocation.TopLeft ->
                blockSpan.BlockVirtualSpans
                |> SeqUtil.head
                |> VirtualSnapshotSpanUtil.GetStartPoint
            | BlockCaretLocation.TopRight ->
                blockSpan.BlockVirtualSpans
                |> SeqUtil.head
                |> getAdjustedVirtualEnd
            | BlockCaretLocation.BottomLeft ->
                blockSpan.BlockVirtualSpans
                |> SeqUtil.last
                |> VirtualSnapshotSpanUtil.GetStartPoint
            | BlockCaretLocation.BottomRight ->
                blockSpan.BlockVirtualSpans
                |> SeqUtil.last
                |> getAdjustedVirtualEnd

    /// Gets the SnapshotPoint for the caret as it should appear in the given VisualSelection with the
    /// specified SelectionKind.
    member x.GetCaretPoint selectionKind =
        let virtualCaretPoint = x.GetCaretVirtualPoint selectionKind
        virtualCaretPoint.Position

    /// Get the primary selected span for the visual selection
    member x.GetPrimarySelectedSpan selectionKind =
        let caretPoint =
            x.GetCaretVirtualPoint selectionKind
        let span =
            x.VisualSpan.Spans
            |> Seq.head
            |> VirtualSnapshotSpanUtil.OfSpan
        if x.IsForward then
            SelectionSpan(caretPoint, span.Start, span.End)
        else
            SelectionSpan(caretPoint, span.End, span.Start)

    /// Select the given VisualSpan in the ITextView
    member x.Select (textView: ITextView) =
        let path =
            match x with
            | Character (_, path) -> path
            | Line (_, path, _) -> path
            | Block _ -> SearchPath.Forward
        x.VisualSpan.Select textView path

    /// Create for the given VisualSpan.  Assumes this was a forward created VisualSpan
    static member CreateForward visualSpan = 
        match visualSpan with
        | VisualSpan.Character span -> 
            VisualSelection.Character (span, SearchPath.Forward)
        | VisualSpan.Line lineRange ->
            let offset = SnapshotPointUtil.GetLineOffset lineRange.LastLine.End
            VisualSelection.Line (lineRange, SearchPath.Forward, offset)
        | VisualSpan.Block blockSpan ->
            VisualSelection.Block (blockSpan, BlockCaretLocation.BottomRight)

    /// Create the VisualSelection over the VisualSpan with the specified caret location
    static member Create (visualSpan: VisualSpan) path (caretPoint: VirtualSnapshotPoint) =
        match visualSpan with
        | VisualSpan.Character characterSpan ->
            Character (characterSpan, path)
        | VisualSpan.Line lineRange ->
            let offset = VirtualSnapshotPointUtil.GetLineOffset caretPoint
            Line (lineRange, path, offset)

        | VisualSpan.Block blockSpan ->

            // Need to calculate the caret location.  Do this based on the initial anchor and
            // caret locations
            let blockCaretLocation = 
                let startLineNumber, startOffset = SnapshotPointUtil.GetLineNumberAndOffset blockSpan.Start.StartPoint
                let caretLineNumber, caretOffset = VirtualSnapshotPointUtil.GetLineNumberAndOffset caretPoint
                match caretLineNumber > startLineNumber, caretOffset > startOffset with
                | true, true -> BlockCaretLocation.BottomRight
                | true, false -> BlockCaretLocation.BottomLeft
                | false, true -> BlockCaretLocation.TopRight
                | false, false -> BlockCaretLocation.TopLeft

            Block (blockSpan, blockCaretLocation)

    /// Create a VisualSelection for the given anchor point and caret.  The position, anchorPoint or 
    /// caretPoint, which is greater position wise is the last point included in the selection.  It
    /// is inclusive
    static member CreateForVirtualPoints visualKind (anchorPoint: VirtualSnapshotPoint) (caretPoint: VirtualSnapshotPoint) tabStop useVirtualSpace =

        let addOne point =
            if visualKind <> VisualKind.Line && useVirtualSpace then
                point
                |> VirtualSnapshotPointUtil.AddOneOnSameLine
            else
                point.Position
                |> SnapshotPointUtil.AddOneOrCurrent
                |> VirtualSnapshotPointUtil.OfPoint

        let createBlock () =
            let anchorSpaces = VirtualSnapshotPointUtil.GetSpacesToPoint anchorPoint tabStop
            let caretSpaces = VirtualSnapshotPointUtil.GetSpacesToPoint caretPoint tabStop
            let spaces = (abs (caretSpaces - anchorSpaces)) + 1
            let column = min anchorSpaces caretSpaces
            
            let startColumn = 
                let first, _ = VirtualSnapshotPointUtil.OrderAscending anchorPoint caretPoint
                let line = VirtualSnapshotPointUtil.GetContainingLine first
                VirtualSnapshotColumn.GetColumnForSpaces(line, column, tabStop)

            let height = 
                let anchorLine = anchorPoint.Position.GetContainingLine()
                let caretLine = caretPoint.Position.GetContainingLine()
                (abs (anchorLine.LineNumber - caretLine.LineNumber)) + 1

            let path = 
                if anchorSpaces <= caretSpaces then SearchPath.Forward
                else SearchPath.Backward

            let blockSpan = BlockSpan(startColumn, tabStop, spaces, height, endOfLine = false)
            VisualSpan.Block blockSpan, path

        let createNormal () = 

            let isForward = anchorPoint.Position.Position <= caretPoint.Position.Position
            let anchorPoint, activePoint = 
                if isForward then
                    let activePoint = addOne caretPoint
                    anchorPoint, activePoint
                else
                    let activePoint = addOne anchorPoint
                    caretPoint, activePoint

            let path = SearchPath.Create isForward
            VisualSpan.CreateForVirtualSelectionPoints visualKind anchorPoint activePoint tabStop useVirtualSpace, path

        let visualSpan, path = 
            match visualKind with
            | VisualKind.Block -> createBlock ()
            | VisualKind.Line -> createNormal ()
            | VisualKind.Character -> createNormal ()

        VisualSelection.Create visualSpan path caretPoint

    /// Create a VisualSelection for the given anchor point and caret.  The position, anchorPoint or 
    /// caretPoint, which is greater position wise is the last point included in the selection.  It
    /// is inclusive
    static member CreateForPoints (visualKind : VisualKind) (anchorPoint: SnapshotPoint) (caretPoint: SnapshotPoint) (tabStop: int) =
        let virtualAnchorPoint = VirtualSnapshotPointUtil.OfPoint anchorPoint
        let virtualCaretPoint = VirtualSnapshotPointUtil.OfPoint caretPoint
        let useVirtualSpace = false
        VisualSelection.CreateForVirtualPoints visualKind virtualAnchorPoint virtualCaretPoint tabStop useVirtualSpace

    /// Create a VisualSelection based off of the current selection and position of the caret.  The
    /// SelectionKind should specify what the current mode is (or the mode which produced the 
    /// active ITextSelection)
    static member CreateForVirtualSelection (textView: ITextView) visualKind selectionKind tabStop useVirtualSpace =
        let caretPoint = TextViewUtil.GetCaretVirtualPoint textView
        let visualSpan = VisualSpan.CreateForVirtualSelection textView visualKind tabStop useVirtualSpace

        // Get the proper VisualSpan based off of the way in which it was created.  VisualSelection
        // represents all values internally as inclusive
        let visualSpan = 
            match selectionKind with
            | SelectionKind.Inclusive -> visualSpan
            | SelectionKind.Exclusive ->
                match visualSpan with
                | VisualSpan.Character characterSpan ->
                    let endPoint = SnapshotPointUtil.AddOneOrCurrent characterSpan.End
                    CharacterSpan(characterSpan.Start, endPoint) |> VisualSpan.Character
                | VisualSpan.Line _ -> visualSpan
                | VisualSpan.Block blockSpan ->
                    let width = blockSpan.SpacesLength + 1
                    let blockSpan = BlockSpan(blockSpan.VirtualStart, blockSpan.TabStop, width, blockSpan.Height, endOfLine = false)
                    VisualSpan.Block blockSpan

        let path = 
            if textView.Selection.IsReversed then
                SearchPath.Backward
            else
                SearchPath.Forward

        VisualSelection.Create visualSpan path caretPoint 

    static member CreateForSelection (textView: ITextView) visualKind selectionKind tabStop =
        VisualSelection.CreateForVirtualSelection textView visualKind selectionKind tabStop false

    /// Create the initial Visual Selection information for the specified Kind started at 
    /// the specified point
    static member CreateInitial visualKind (caretVirtualPoint: VirtualSnapshotPoint) tabStop selectionKind useVirtualSpace =
        let caretPoint = caretVirtualPoint.Position
        match visualKind with
        | VisualKind.Character ->
            let characterSpan = 
                let endPoint = 
                    match selectionKind with
                    | SelectionKind.Inclusive ->
                        if useVirtualSpace then
                            VirtualSnapshotPointUtil.AddOneOnSameLine caretVirtualPoint
                        else
                            SnapshotPointUtil.AddOneOrCurrent caretPoint
                            |> VirtualSnapshotPointUtil.OfPoint
                    | SelectionKind.Exclusive -> caretVirtualPoint
                CharacterSpan(caretVirtualPoint, endPoint, useVirtualSpace)
            VisualSelection.Character (characterSpan, SearchPath.Forward)
        | VisualKind.Line ->
            let lineRange = 
                let line = SnapshotPointUtil.GetContainingLine caretPoint
                SnapshotLineRangeUtil.CreateForLine line
            let offset = SnapshotPointUtil.GetLineOffset caretPoint
            VisualSelection.Line (lineRange, SearchPath.Forward, offset)
        | VisualKind.Block ->
            let blockSpan = BlockSpan(caretVirtualPoint, tabStop, 1, 1, endOfLine = false)
            VisualSelection.Block (blockSpan, BlockCaretLocation.BottomRight)

/// This is used for commands like [count]v and [count]V to hold a visual selection
/// without any specific buffer information.
[<RequireQualifiedAccess>]
[<NoComparison>]
type StoredVisualSelection =
    | Character of Width: int
    | CharacterLine of LineCount: int * LastLineMaxOffset : int 
    | Line of LineCount: int

    with 

    member x.GetVisualSelection (point: SnapshotPoint) count =

        // Get the point which is 'count' columns forward from the passed in point (exclusive). If the point
        // extends past the line break the point past the line break will be returned if possible.
        let addOrEntireLine (point: SnapshotPoint) count = 
            let column = SnapshotColumn(point)
            match column.TryAddInLine(count, includeLineBreak = true) with
            | Some column -> column.StartPoint
            | None -> 
                match SnapshotPointUtil.TryAddOne column.Line.EndIncludingLineBreak with 
                | Some p -> p
                | None -> column.Line.EndIncludingLineBreak

        match x with
        | StoredVisualSelection.Character width ->
            // For a single line count only moves the caret on the current line
            let endPoint = addOrEntireLine point (width * count)
            let characterSpan = CharacterSpan(point, endPoint)
            VisualSelection.Character (characterSpan, SearchPath.Forward)
        | StoredVisualSelection.CharacterLine (lineCount, offset)  ->
            let startColumn = SnapshotColumn(point)
            let range = SnapshotLineRangeUtil.CreateForLineAndMaxCount startColumn.Line (count * lineCount)
            let lastLine = range.LastLine
            let endPoint =
                let column = 
                    if offset >= 0 then startColumn.ColumnNumber + offset + 1
                    else startColumn.ColumnNumber + offset
                addOrEntireLine lastLine.Start column
                        
            let characterSpan, searchPath = 
                if point.Position < endPoint.Position then 
                    CharacterSpan(point, endPoint), SearchPath.Forward
                else 
                    let point = SnapshotPointUtil.AddOneOrCurrent point
                    CharacterSpan(endPoint, point), SearchPath.Backward

            VisualSelection.Character (characterSpan, searchPath)
        | StoredVisualSelection.Line c ->
            let line = SnapshotPointUtil.GetContainingLine point
            let count = c * count
            let range = SnapshotLineRangeUtil.CreateForLineAndMaxCount line count
            VisualSelection.Line (range, SearchPath.Forward, 0)

    static member CreateFromVisualSpan visualSpan =
        match visualSpan with
        | VisualSpan.Character span ->
            if span.LineCount = 1 then StoredVisualSelection.Character span.Length |> Some
            else
                let startOffset = SnapshotPointUtil.GetLineOffset span.Start
                let endOffset = max 0 (span.LastLineMaxPositionCount - 1)
                let offset = endOffset - startOffset
                StoredVisualSelection.CharacterLine (LineCount = span.LineCount, LastLineMaxOffset = offset) |> Some
        | VisualSpan.Line range -> StoredVisualSelection.Line range.Count |> Some
        | VisualSpan.Block _ -> None

/// Most text object entries have specific effects on Visual Mode.  They are 
/// described below
[<RequireQualifiedAccess>]
[<NoComparison>]
type TextObjectKind = 
    | None
    | LineToCharacter
    | AlwaysCharacter
    | AlwaysLine

/// The kind of visual insertion to perform
[<RequireQualifiedAccess>]
[<NoComparison>]
type VisualInsertKind =

    /// The visual insert should begin at the start of the primary span
    | Start

    /// The visual insert should begin at the end of the primary span
    | End

    /// The visual insert should begin at the end of the line that contains the
    /// start of the primary span
    | EndOfLine

[<RequireQualifiedAccess>]
type ModeArgument =
    | None

    /// Passed to visual mode to indicate what the initial selection should be.  The SnapshotPoint
    /// option provided is meant to be the initial caret point.  If not provided the actual 
    /// caret point is used
    | InitialVisualSelection of Selection: VisualSelection * CaretPoint: SnapshotPoint option

    /// Begins a block insertion.  This can possibly have a linked undo transaction that needs
    /// to be carried forward through the insert
    | InsertBlock of BlockSpan: BlockSpan * VisualInsertKind: VisualInsertKind * LinkedUndoTransaction: ILinkedUndoTransaction

    /// Begins insert mode with a specified count.  This means the text inserted should
    /// be repeated a total of 'count - 1' times when insert mode exits
    | InsertWithCount of Count: int

    /// Begins insert mode with a specified count.  This means the text inserted should
    /// be repeated a total of 'count - 1' times when insert mode exits.  Each extra time
    /// should be on a new line
    | InsertWithCountAndNewLine of Count: int * LinkedUndoTransaction: ILinkedUndoTransaction

    /// Begins insert mode with an existing UndoTransaction.  This is used to link 
    /// change commands with text changes.  For example C, c, etc ...
    | InsertWithTransaction of LinkedUndoTransaction: ILinkedUndoTransaction

    /// Passing the substitute to confirm to Confirm mode.  The SnapshotSpan is the first
    /// match to process and the range is the full range to consider for a replace
    | Substitute of Span: SnapshotSpan * LineRange: SnapshotLineRange * SubstituteData: SubstituteData

    /// Enter command mode with a partially entered command and then return to normal mode
    | PartialCommand of Command: string

    /// Cancel any operation that is in-progress, such as refactoring
    | CancelOperation

with

    /// Extract any linked undo transaction from the mode argument
    member x.LinkedUndoTransaction =
        match x with
        | ModeArgument.None -> Option.None
        | ModeArgument.InitialVisualSelection _ -> Option.None
        | ModeArgument.InsertBlock (_, _, transaction) -> Some transaction
        | ModeArgument.InsertWithCount _ -> Option.None
        | ModeArgument.InsertWithCountAndNewLine (_, transaction) -> Some transaction
        | ModeArgument.InsertWithTransaction transaction -> Some transaction
        | ModeArgument.Substitute _ -> Option.None
        | ModeArgument.PartialCommand _ -> Option.None
        | ModeArgument.CancelOperation _ -> Option.None


    /// Complete any embedded linked undo transaction
    member x.CompleteAnyTransaction () =
        match x.LinkedUndoTransaction with
        | Some transaction -> transaction.Complete()
        | Option.None -> ()

[<RequireQualifiedAccess>]
[<NoComparison>]
[<NoEquality>]
type ModeSwitch =
    | NoSwitch
    | SwitchMode of ModeKind: ModeKind
    | SwitchModeWithArgument of ModeKind: ModeKind * ModeArgument: ModeArgument
    | SwitchPreviousMode 

    /// Switch to the given mode for a single command.  After the command is processed switch
    /// back to the original mode
    | SwitchModeOneTimeCommand of ModeKind: ModeKind

[<RequireQualifiedAccess>]
[<NoComparison>]
type CommandResult =   

    /// The command completed and requested a switch to the provided Mode which 
    /// may just be a no-op
    | Completed of ModeSwitch: ModeSwitch

    /// An error was encountered and the command was unable to run.  If this is encountered
    /// during a macro run it will cause the macro to stop executing
    | Error

[<RequireQualifiedAccess>]
[<NoComparison>]
[<NoEquality>]
type VimResult<'T> =
    | Result of Result: 'T
    | Error of Error: string

/// Information about the attributes of Command
[<System.Flags>]
type CommandFlags =
    | None = 0x0000

    /// Relates to the movement of the cursor.  A movement command does not alter the 
    /// last command
    | Movement = 0x0001

    /// A Command which can be repeated
    | Repeatable = 0x0002

    /// A Command which should not be considered when looking at last changes
    | Special = 0x0004

    /// Can handle the escape key if provided as part of a Motion or Long command extra
    /// input
    | HandlesEscape = 0x0008

    /// For the purposes of change repeating the command is linked with the following
    /// text change
    | LinkedWithNextCommand = 0x0010

    /// For the purposes of change repeating the command is linked with the previous
    /// text change if it exists
    | LinkedWithPreviousCommand = 0x0020

    /// For Visual mode commands which should reset the cursor to the original point
    /// after completing
    | ResetCaret = 0x0040

    /// For Visual mode commands which should reset the anchor point to the current
    /// anchor point of the selection
    | ResetAnchorPoint = 0x0080

    /// Vim allows for special handling of the 'd' command in normal mode such that it can
    /// have the pattern 'd#d'.  This flag is used to tag the 'd' command to allow such
    /// a pattern
    | Delete = 0x0100

    /// Vim allows for special handling of the 'y' command in normal mode such that it can
    /// have the pattern 'y#y'.  This flag is used to tag the 'y' command to allow such
    /// a pattern
    | Yank = 0x0200

    /// Vim allows for special handling of the '>' command in normal mode such that it can
    /// have the pattern '>#>'.  This flag is used to tag the '>' command to allow such
    /// a pattern
    | ShiftRight = 0x0400

    /// Vim allows for special handling of the '<' command in normal mode such that it can
    /// have the pattern '<#<'.  This flag is used to tag the '<' command to allow such
    /// a pattern
    | ShiftLeft = 0x0800

    /// Vim allows for special handling of the 'c' command in normal mode such that it can
    /// have the pattern 'c#c'.  This flag is used to tag the 'c' command to allow such
    /// a pattern
    | Change = 0x1000

    /// Represents an insert edit action which can be linked with other insert edit actions and
    /// hence acts with them in a repeat
    | InsertEdit = 0x2000

    /// Whether the command depends on the context, e.g. a tab insertion
    | ContextSensitive = 0x4000

/// Data about the run of a given MotionResult
type MotionData = {

    /// The associated Motion value
    Motion: Motion

    /// The argument which should be supplied to the given Motion
    MotionArgument: MotionArgument
}

/// Data needed to execute a command
type CommandData = {

    /// The raw count provided to the command
    Count: int option 

    /// The register name specified for the command 
    RegisterName: RegisterName option

} with

    /// Return the provided count or the default value of 1
    member x.CountOrDefault = Util.CountOrDefault x.Count

    static member Default = { Count = None; RegisterName = None }

/// We want the NormalCommand discriminated union to have structural equality in order
/// to ease testing requirements.  In order to do this and support Ping we need a 
/// separate type here to wrap the Func to be comparable.  Does so in a reference 
/// fashion
type PingData (_func: CommandData -> CommandResult) = 

    member x.Function = _func

    static member op_Equality (this, other) = System.Object.ReferenceEquals(this, other)
    static member op_Inequality (this, other) = not (System.Object.ReferenceEquals(this, other))
    override x.GetHashCode() = 1
    override x.Equals(obj) = System.Object.ReferenceEquals(x, obj)
    interface System.IEquatable<PingData> with
        member x.Equals other = x.Equals(other)

/// Normal mode commands which can be executed by the user
[<RequireQualifiedAccess>]
[<NoComparison>]
[<StructuralEquality>]
type NormalCommand = 

    /// Add a new caret at the mouse point
    | AddCaretAtMousePoint

    /// Add a new caret on an adjacent line in the specified direction
    | AddCaretOnAdjacentLine of Direction: Direction

    /// Add 'count' to the word close to the caret
    | AddToWord

    /// Cancel any in-progress operation such as external edit
    | CancelOperation

    /// Deletes the text specified by the motion and begins insert mode. Implements the "c" 
    /// command
    | ChangeMotion of MotionData: MotionData

    /// Change the characters on the caret line 
    | ChangeCaseCaretLine of ChangeCharacterKind: ChangeCharacterKind

    /// Change the characters on the caret line 
    | ChangeCaseCaretPoint of ChangeCharacterKind: ChangeCharacterKind

    /// Change case of the specified motion
    | ChangeCaseMotion of ChangeCharacterKind: ChangeCharacterKind * MotionData: MotionData

    /// Delete 'count' lines and begin insert mode
    | ChangeLines

    /// Delete the text till the end of the line in the same manner as DeleteTillEndOfLine
    /// and start Insert Mode
    | ChangeTillEndOfLine

    /// Close all folds in the buffer
    | CloseAllFolds

    /// Close all folds under the caret
    | CloseAllFoldsUnderCaret

    /// Close the IVimBuffer and don't bother to save
    | CloseBuffer

    /// Close the window unless the buffer is dirty
    | CloseWindow

    /// Close 'count' folds under the caret
    | CloseFoldUnderCaret

    /// Delete all of the folds that are in the ITextBuffer
    | DeleteAllFoldsInBuffer

    /// Delete the character at the current cursor position.  Implements the "x" command
    | DeleteCharacterAtCaret

    /// Delete the character before the cursor. Implements the "X" command
    | DeleteCharacterBeforeCaret

    /// Delete the fold under the caret
    | DeleteFoldUnderCaret

    /// Delete all folds under the caret
    | DeleteAllFoldsUnderCaret

    /// Delete lines from the buffer: dd
    | DeleteLines

    /// Delete the specified motion of text
    | DeleteMotion of MotionData: MotionData

    /// Delete till the end of the line and 'count - 1' more lines down
    | DeleteTillEndOfLine

    /// Display the bytes of the current character in the status bar
    | DisplayCharacterBytes

    /// Display the ascii (or really code point) value of the current character
    | DisplayCharacterCodePoint

    /// Fold 'count' lines in the ITextBuffer
    | FoldLines

    /// Filter the specified lines
    | FilterLines

    /// Filter the specified motion
    | FilterMotion of MotionData: MotionData

    /// Create a fold over the specified motion 
    | FoldMotion of MotionData: MotionData

    /// Format the code in the specified lines
    | FormatCodeLines

    /// Format the code in the specified motion
    | FormatCodeMotion of MotionData: MotionData

    /// Format the text in the specified lines, optionally preserving the caret position
    | FormatTextLines of PreserveCaretPosition: bool

    /// Format the text in the specified motion
    | FormatTextMotion of PreserveCaretPosition: bool * MotionData: MotionData

    /// Go to the definition of the word under the caret
    | GoToDefinition

    /// Go to the definition of the word under the mouse
    | GoToDefinitionUnderMouse

    /// GoTo the file under the cursor.  The bool represents whether or not this should occur in
    /// a different window
    | GoToFileUnderCaret of UseNewWindow: bool

    /// Go to the global declaration of the word under the caret
    | GoToGlobalDeclaration

    /// Go to the local declaration of the word under the caret
    | GoToLocalDeclaration

    /// Go to the next tab in the specified direction
    | GoToNextTab of SearchPath: SearchPath

    /// Go to the window of the specified kind
    | GoToWindow of WindowKind: WindowKind

    /// Go to the nth most recent view
    | GoToRecentView

    /// Switch to insert after the caret position
    | InsertAfterCaret

    /// Switch to insert mode
    | InsertBeforeCaret

    /// Switch to insert mode at the end of the line
    | InsertAtEndOfLine

    /// Insert text at the first non-blank line in the current line
    | InsertAtFirstNonBlank

    /// Insert text at the start of a line (column 0)
    | InsertAtStartOfLine

    /// Insert a line above the cursor and begin insert mode
    | InsertLineAbove

    /// Insert a line below the cursor and begin insert mode
    | InsertLineBelow

    /// Join the specified lines
    | JoinLines of JoinKind: JoinKind

    /// Jump to the specified mark 
    | JumpToMark of Mark: Mark

    /// Jump to the start of the line for the specified mark
    | JumpToMarkLine of Mark: Mark

    /// Jump to the next older item in the tag list
    | JumpToOlderPosition

    /// Jump to the next new item in the tag list
    | JumpToNewerPosition

    /// Move the caret to the result of the given Motion.
    | MoveCaretToMotion of Motion: Motion

    /// Move the caret to position of the mouse cursor
    | MoveCaretToMouse

    /// Perform no operation
    | NoOperation

    /// Undo count operations in the ITextBuffer
    | Undo

    /// Undo all recent changes made to the current line
    | UndoLine

    /// Open all folds in the buffer
    | OpenAllFolds

    /// Open all of the folds under the caret
    | OpenAllFoldsUnderCaret

    /// Open link under caret
    | OpenLinkUnderCaret

    /// Open a fold under the caret
    | OpenFoldUnderCaret

    /// Toggle a fold under the caret
    | ToggleFoldUnderCaret

    /// Toggle all folds under the caret
    | ToggleAllFolds

    /// Not actually a Vim Command.  This is a simple ping command which makes 
    /// testing items like complex repeats significantly easier
    | Ping of PingData: PingData

    /// Put the contents of the register into the buffer after the cursor.  The bool is 
    /// whether or not the caret should be placed after the inserted text
    | PutAfterCaret of PlaceCaretAfterInsertedText: bool

    /// Put the contents of the register into the buffer after the cursor and respecting 
    /// the indent of the current line
    | PutAfterCaretWithIndent

    /// Put the contents of the register after the current mouse position.  This will move
    /// the mouse to that position before inserting 
    | PutAfterCaretMouse

    /// Put the contents of the register into the buffer before the cursor.  The bool is 
    /// whether or not the caret should be placed after the inserted text
    | PutBeforeCaret of PlaceCaretAfterInsertedText: bool

    /// Put the contents of the register into the buffer before the cursor and respecting 
    /// the indent of the current line
    | PutBeforeCaretWithIndent

    /// Print out the current file information
    | PrintFileInformation

    /// Start the recording of a macro to the specified Register
    | RecordMacroStart of RegisterName: char

    /// Stop the recording of a macro to the specified Register
    | RecordMacroStop

    /// Redo count operations in the ITextBuffer
    | Redo

    /// Repeat the last command
    | RepeatLastCommand

    /// Repeat the last substitute command.  The first bool value is for
    /// whether or not the flags from the last substitute should be reused as
    /// well. The second bool value is for whether the substitute should
    /// operate on the whole buffer
    | RepeatLastSubstitute of UseSameFlags: bool * UseWholeBuffer: bool

    /// Replace the text starting at the text by starting insert mode
    | ReplaceAtCaret

    /// Replace the char under the cursor with the given char
    | ReplaceChar of KeyInput: KeyInput

    /// Restore the most recent set of multiple selections
    | RestoreMultiSelection

    /// Run an 'at' command for the specified character
    | RunAtCommand of Character: char

    /// Set the specified mark to the current value of the caret
    | SetMarkToCaret of Character: char

    /// Scroll the caret in the specified direciton.  The bool is whether to use
    /// the 'scroll' option or 'count'
    | ScrollLines of ScrollDirection: ScrollDirection * UseScrollOption: bool

    /// Move the display a single page in the specified direction
    | ScrollPages of ScrollDirection: ScrollDirection

    /// Scroll the window in the specified direction by 'count' lines
    | ScrollWindow of ScrollDirection: ScrollDirection

    /// Scroll the current line to the top of the ITextView.  The bool is whether or not
    /// to leave the caret in the same column
    | ScrollCaretLineToTop of MaintainCaretColumn: bool

    /// Scroll the caret line to the middle of the ITextView.  The bool is whether or not
    /// to leave the caret in the same column
    | ScrollCaretLineToMiddle of MaintainCaretColumn: bool

    /// Scroll the caret line to the bottom of the ITextView.  The bool is whether or not
    /// to leave the caret in the same column
    | ScrollCaretLineToBottom of MaintainCaretColumn: bool

    /// Scroll the window horizontally so that the caret is at the left edge
    /// of the screen
    | ScrollCaretColumnToLeft

    /// Scroll the window horizontally so that the caret is at the right edge
    /// of the screen
    | ScrollCaretColumnToRight

    /// Scroll the window horizontally in the specified direction
    | ScrollColumns of Direction: Direction

    /// Scroll half the width of the window in the specified direction
    | ScrollHalfWidth of Direction: Direction

    /// Select the current block
    | SelectBlock

    /// Select the current line
    | SelectLine

    /// Select the next match for the last pattern searched for
    | SelectNextMatch of SearchPath: SearchPath

    /// Select text for a mouse click
    | SelectTextForMouseClick

    /// Select text for a mouse drag
    | SelectTextForMouseDrag

    /// Select text for a mouse release
    | SelectTextForMouseRelease

    /// Select the word or matching token under the caret
    | SelectWordOrMatchingToken

    /// Select the word or matching token at the mouse point
    | SelectWordOrMatchingTokenAtMousePoint

    /// Shift 'count' lines from the cursor left
    | ShiftLinesLeft

    /// Shift 'count' lines from the cursor right
    | ShiftLinesRight

    /// Shift 'motion' lines from the cursor left
    | ShiftMotionLinesLeft of MotionData: MotionData

    /// Shift 'motion' lines from the cursor right
    | ShiftMotionLinesRight of MotionData: MotionData

    /// Split the view horizontally
    | SplitViewHorizontally

    /// Split the view vertically
    | SplitViewVertically

    /// Substitute the character at the cursor
    | SubstituteCharacterAtCaret

    /// Subtract 'count' from the word at the caret
    | SubtractFromWord

    /// Switch modes with the specified information
    | SwitchMode of ModeKind: ModeKind * ModeArgument: ModeArgument

    /// Switch to the specified kind of visual mode
    | SwitchModeVisualCommand of VisualKind: VisualKind

    /// Switch to the previous Visual Mode selection
    | SwitchPreviousVisualMode

    /// Switch to a selection dictated by the given caret movement
    | SwitchToSelection of CaretMovement: CaretMovement

    /// Write out the ITextBuffer and quit
    | WriteBufferAndQuit

    /// Yank the given motion into a register
    | Yank of MotionData: MotionData

    /// Yank the specified number of lines
    | YankLines

    with 

    member x.MotionData = 
        match x.GetMotionDataCore() with
        | Some (_, motionData) -> Some motionData
        | None -> None

    member private x.GetMotionDataCore() = 
        match x with
        | NormalCommand.ChangeCaseMotion (changeCharacterKind, motion) -> Some ((fun motion -> NormalCommand.ChangeCaseMotion (changeCharacterKind, motion)), motion)
        | NormalCommand.ChangeMotion motion -> Some (NormalCommand.ChangeMotion, motion)
        | NormalCommand.DeleteMotion motion -> Some (NormalCommand.DeleteMotion, motion)
        | NormalCommand.FilterMotion motion -> Some (NormalCommand.FilterMotion, motion)
        | NormalCommand.FoldMotion motion -> Some (NormalCommand.FoldMotion, motion)
        | NormalCommand.FormatCodeMotion motion -> Some (NormalCommand.FormatCodeMotion, motion)
        | NormalCommand.FormatTextMotion (preserveCaretPosition, motion) -> Some ((fun motion -> NormalCommand.FormatTextMotion (preserveCaretPosition, motion)), motion)
        | NormalCommand.ShiftMotionLinesLeft motion -> Some (NormalCommand.ShiftMotionLinesLeft, motion)
        | NormalCommand.ShiftMotionLinesRight motion -> Some (NormalCommand.ShiftMotionLinesRight, motion)
        | NormalCommand.Yank motion -> Some (NormalCommand.Yank, motion)

        // Non-motion commands
        | NormalCommand.AddCaretAtMousePoint -> None
        | NormalCommand.AddCaretOnAdjacentLine _ -> None
        | NormalCommand.AddToWord _ -> None
        | NormalCommand.CancelOperation -> None
        | NormalCommand.ChangeCaseCaretLine _ -> None
        | NormalCommand.ChangeCaseCaretPoint _ -> None
        | NormalCommand.ChangeLines -> None
        | NormalCommand.ChangeTillEndOfLine -> None
        | NormalCommand.CloseAllFolds -> None
        | NormalCommand.CloseAllFoldsUnderCaret -> None
        | NormalCommand.CloseBuffer -> None
        | NormalCommand.CloseWindow -> None
        | NormalCommand.CloseFoldUnderCaret -> None
        | NormalCommand.DeleteAllFoldsInBuffer -> None
        | NormalCommand.DeleteCharacterAtCaret -> None
        | NormalCommand.DeleteCharacterBeforeCaret -> None
        | NormalCommand.DeleteFoldUnderCaret -> None
        | NormalCommand.DeleteAllFoldsUnderCaret -> None
        | NormalCommand.DeleteLines -> None
        | NormalCommand.DeleteTillEndOfLine -> None
        | NormalCommand.DisplayCharacterBytes -> None
        | NormalCommand.DisplayCharacterCodePoint ->None
        | NormalCommand.FilterLines -> None
        | NormalCommand.FoldLines -> None
        | NormalCommand.FormatCodeLines -> None
        | NormalCommand.FormatTextLines _ -> None
        | NormalCommand.GoToDefinition -> None
        | NormalCommand.GoToDefinitionUnderMouse -> None
        | NormalCommand.GoToFileUnderCaret _ -> None
        | NormalCommand.GoToGlobalDeclaration -> None
        | NormalCommand.GoToLocalDeclaration -> None
        | NormalCommand.GoToNextTab _ -> None
        | NormalCommand.GoToWindow _ -> None
        | NormalCommand.GoToRecentView _ -> None
        | NormalCommand.InsertAfterCaret -> None
        | NormalCommand.InsertBeforeCaret -> None
        | NormalCommand.InsertAtEndOfLine -> None
        | NormalCommand.InsertAtFirstNonBlank -> None
        | NormalCommand.InsertAtStartOfLine -> None
        | NormalCommand.InsertLineAbove -> None
        | NormalCommand.InsertLineBelow -> None
        | NormalCommand.JoinLines _ -> None
        | NormalCommand.JumpToMark _ -> None
        | NormalCommand.JumpToMarkLine _ -> None
        | NormalCommand.JumpToOlderPosition -> None
        | NormalCommand.JumpToNewerPosition -> None
        | NormalCommand.MoveCaretToMotion _ -> None
        | NormalCommand.MoveCaretToMouse -> None
        | NormalCommand.NoOperation -> None
        | NormalCommand.Undo -> None
        | NormalCommand.UndoLine -> None
        | NormalCommand.OpenAllFolds -> None
        | NormalCommand.OpenAllFoldsUnderCaret -> None
        | NormalCommand.OpenLinkUnderCaret -> None
        | NormalCommand.OpenFoldUnderCaret -> None
        | NormalCommand.ToggleFoldUnderCaret -> None
        | NormalCommand.ToggleAllFolds -> None
        | NormalCommand.Ping _ -> None
        | NormalCommand.PutAfterCaret _ -> None
        | NormalCommand.PutAfterCaretWithIndent -> None
        | NormalCommand.PutAfterCaretMouse -> None
        | NormalCommand.PutBeforeCaret _ -> None
        | NormalCommand.PutBeforeCaretWithIndent -> None
        | NormalCommand.PrintFileInformation -> None
        | NormalCommand.RecordMacroStart _ -> None
        | NormalCommand.RecordMacroStop -> None
        | NormalCommand.Redo -> None
        | NormalCommand.RepeatLastCommand -> None
        | NormalCommand.RepeatLastSubstitute _ -> None 
        | NormalCommand.ReplaceAtCaret -> None
        | NormalCommand.ReplaceChar _ -> None
        | NormalCommand.RestoreMultiSelection -> None
        | NormalCommand.RunAtCommand _ -> None
        | NormalCommand.SetMarkToCaret _ -> None
        | NormalCommand.ScrollColumns _ -> None
        | NormalCommand.ScrollHalfWidth _ -> None
        | NormalCommand.ScrollLines _ -> None
        | NormalCommand.ScrollPages _ -> None
        | NormalCommand.ScrollWindow _ -> None
        | NormalCommand.ScrollCaretLineToTop _ -> None
        | NormalCommand.ScrollCaretLineToMiddle _ -> None
        | NormalCommand.ScrollCaretLineToBottom _ -> None
        | NormalCommand.ScrollCaretColumnToLeft -> None
        | NormalCommand.ScrollCaretColumnToRight -> None
        | NormalCommand.SelectBlock -> None
        | NormalCommand.SelectLine -> None
        | NormalCommand.SelectNextMatch _ -> None
        | NormalCommand.SelectTextForMouseClick -> None
        | NormalCommand.SelectTextForMouseDrag -> None
        | NormalCommand.SelectTextForMouseRelease -> None
        | NormalCommand.SelectWordOrMatchingToken -> None
        | NormalCommand.SelectWordOrMatchingTokenAtMousePoint -> None
        | NormalCommand.ShiftLinesLeft -> None
        | NormalCommand.ShiftLinesRight -> None
        | NormalCommand.SplitViewHorizontally -> None
        | NormalCommand.SplitViewVertically -> None
        | NormalCommand.SubstituteCharacterAtCaret -> None
        | NormalCommand.SubtractFromWord -> None
        | NormalCommand.SwitchMode _ -> None
        | NormalCommand.SwitchModeVisualCommand _ -> None
        | NormalCommand.SwitchPreviousVisualMode -> None
        | NormalCommand.SwitchToSelection _ -> None
        | NormalCommand.WriteBufferAndQuit -> None
        | NormalCommand.YankLines -> None

    /// Change the MotionData associated with this command if it's a part of the command. Otherwise it 
    /// returns the original command unchanged
    member x.ChangeMotionData changeMotionFunc = 
        match x.GetMotionDataCore() with
        | Some (createCommandFunc, motionData) ->
            let motionData = changeMotionFunc motionData
            createCommandFunc motionData
        | None -> x

/// Visual mode commands which can be executed by the user 
[<RequireQualifiedAccess>]
[<NoComparison>]
[<StructuralEquality>]
type VisualCommand = 

    /// Add a new caret at the mouse point
    | AddCaretAtMousePoint

    /// Add a new selection on an adjacent line in the specified direction
    | AddSelectionOnAdjacentLine of Direction: Direction

    /// Add count to the word in each line of the selection, optionally progressively
    | AddToSelection of IsProgressive: bool

    /// Add word or matching token at the current mouse point to the selection
    | AddWordOrMatchingTokenAtMousePointToSelection

    /// Cancel any in-progress operation
    | CancelOperation

    /// Change the case of the selected text in the specified manner
    | ChangeCase of ChangeCharacterKind: ChangeCharacterKind

    /// Delete the selection and begin insert mode.  Implements the 'c' and 's' commands
    | ChangeSelection

    /// Delete the selected lines and begin insert mode ('S' and 'C' commands).  The bool parameter
    /// is whether or not to treat block selection as a special case
    | ChangeLineSelection of SpecialCaseBlockSelection: bool

    /// Close a fold in the selection
    | CloseFoldInSelection

    /// Close all folds in the selection
    | CloseAllFoldsInSelection

    /// Delete all folds in the selection
    | DeleteAllFoldsInSelection

    /// Delete the selected lines
    | DeleteLineSelection

    /// Delete the selected text and put it into a register
    | DeleteSelection

    /// Extend the selection for a mouse click
    | ExtendSelectionForMouseClick

    /// Extend the selection for a mouse drag
    | ExtendSelectionForMouseDrag

    /// Extend the selection for a mouse release
    | ExtendSelectionForMouseRelease

    /// Extend the selection to the next match for the last pattern searched for
    | ExtendSelectionToNextMatch of SearchPath: SearchPath

    /// Filter the selected text
    | FilterLines

    /// Fold the current selected lines
    | FoldSelection

    /// Format the selected code lines
    | FormatCodeLines

    /// Format the selected text lines, optionally preserving the caret position
    | FormatTextLines of PreserveCaretPosition: bool

    /// GoTo the file under the cursor in a new window
    | GoToFileInSelectionInNewWindow

    /// GoTo the file under the cursor in this window
    | GoToFileInSelection

    /// Join the selected lines
    | JoinSelection of JoinKind: JoinKind

    /// Invert the selection by swapping the caret and anchor points.  When true it means that block mode should
    /// be special cased to invert the column only 
    | InvertSelection of ColumnOnlyInBlock: bool

    /// Move the caret to the mouse position
    | MoveCaretToMouse

    /// Move the caret to the result of the given Motion.  This movement is from a 
    /// text-object selection.  Certain motions 
    | MoveCaretToTextObject of Motion: Motion * TextObjectKind: TextObjectKind

    /// Open all folds in the selection
    | OpenAllFoldsInSelection

    /// Open link in selection
    | OpenLinkInSelection

    /// Open one fold in the selection
    | OpenFoldInSelection

    /// Put the contents af the register after the selection.  The bool is for whether or not the
    // caret should be placed after the inserted text
    | PutOverSelection of PlaceCaretAfterInsertedText: bool

    /// Replace the visual span with the provided character
    | ReplaceSelection of KeyInput: KeyInput

    /// Select current block
    | SelectBlock

    /// Select current line
    | SelectLine

    /// Select current word or matching token
    | SelectWordOrMatchingTokenAtMousePoint

    /// Shift the selected lines left
    | ShiftLinesLeft

    /// Shift the selected lines to the right
    | ShiftLinesRight

    /// Add the next occurrence of the primary selection or split the
    /// selection into carets
    | StartMultiSelection

    /// Subtract count from the word in each line of the selection, optionally progressively
    | SubtractFromSelection of IsProgressive: bool

    /// Switch from a visual mode to insert mode using the specified visual
    /// insert kind to determine the kind of insertion to perform
    | SwitchModeInsert of VisualInsertKind: VisualInsertKind

    /// Switch to the previous mode
    | SwitchModePrevious

    /// Switch to the specified visual mode
    | SwitchModeVisual of VisualKind: VisualKind

    /// Toggle one fold in the selection
    | ToggleFoldInSelection

    /// Toggle all folds in the selection
    | ToggleAllFoldsInSelection

    /// Yank the lines which are specified by the selection
    | YankLineSelection

    /// Yank the selection into the specified register
    | YankSelection

    /// Switch to the other visual mode, visual or select
    | SwitchModeOtherVisual

    /// Cut selection
    | CutSelection

    /// Copy selection
    | CopySelection

    /// Cut selection and paste
    | CutSelectionAndPaste

    /// Select the whole document
    | SelectAll

/// Insert mode commands that can be executed by the user
[<RequireQualifiedAccess>]
[<NoComparison>]
[<StructuralEquality>]
type InsertCommand  =

    /// Backspace at the current caret position
    | Back

    /// Block edit of the specified TextChange value.  The first bool
    /// specifies whether short lines should be padded. The second bool
    /// specifieds whether the insert is at the end of the line. The int
    /// represents the number of  lines on which this block insert should take
    /// place
    | BlockInsert of InsertCommand: InsertCommand * PadShortLines: bool * AtEndOfLine: bool * Height: int

    /// This is an insert command which is a combination of other insert commands
    | Combined of Left: InsertCommand * Right: InsertCommand

    /// Complete the Insert Mode session.  This is done as a command so that it will 
    /// be a bookend of insert mode for the repeat infrastructure
    ///
    /// The bool value represents whether or not the caret needs to be moved to the
    /// left
    | CompleteMode of MoveCaretLeft: bool

    /// Delete the character under the caret
    | Delete

    /// Delete count characters to the left of the caret
    | DeleteLeft of ColumnCount: int 

    /// Delete count characters to the right of the caret
    | DeleteRight of ColumnCount: int

    /// Delete all indentation on the current line
    | DeleteAllIndent

    /// Delete the word before the cursor
    | DeleteWordBeforeCursor

    /// Insert the character which is immediately above the caret
    | InsertCharacterAboveCaret

    /// Insert the character which is immediately below the caret
    | InsertCharacterBelowCaret

    /// Insert a new line into the ITextBuffer
    | InsertNewLine

    /// Insert previously inserted text, optionally stopping insert
    | InsertPreviouslyInsertedText of StopInsert: bool

    /// Insert a tab into the ITextBuffer
    | InsertTab

    /// Insert of text into the ITextBuffer at the caret position 
    | Insert of Text: string

    /// Insert of literal text into the ITextBuffer at the caret position 
    | InsertLiteral of Text: string

    /// Move the caret in the given direction
    | MoveCaret of Direction: Direction

    /// Move the caret in the given direction with an arrow key
    | MoveCaretWithArrow of Direction: Direction

    /// Move the caret in the given direction by a whole word
    | MoveCaretByWord of Direction: Direction

    /// Move the caret to the end of the line
    | MoveCaretToEndOfLine

    /// Replace the character under the caret with the specified value
    | Replace of Character: char

    /// Replace the character which is immediately above the caret
    | ReplaceCharacterAboveCaret

    /// Replace the character which is immediately below the caret
    | ReplaceCharacterBelowCaret

    /// Overwrite the characters under the caret with the specified string
    | Overwrite of Text: string

    /// Shift the current line one indent width to the left
    | ShiftLineLeft 

    /// Shift the current line one indent width to the right
    | ShiftLineRight

    /// Undo replace
    | UndoReplace

    /// Delete non-blank characters before cursor on current line
    | DeleteLineBeforeCursor

    /// Paste clipboard
    | Paste

    | LiteralPaste of Register: char
    /// | LiteralPaste of Register: char * Text: string

    with

    member x.RightMostCommand =
        match x with
        | InsertCommand.Combined (_, right) -> right.RightMostCommand
        | _ -> x

    member x.SecondRightMostCommand =
        match x with
        | InsertCommand.Combined (left, right) ->
            match right with
            | InsertCommand.Combined (_, right) -> right.SecondRightMostCommand
            | _ -> Some left
        | _ -> None

    /// Convert a TextChange value into the appropriate InsertCommand structure
    static member OfTextChange textChange = 
        match textChange with
        | TextChange.Insert text -> InsertCommand.Insert text
        | TextChange.DeleteLeft count -> InsertCommand.DeleteLeft count
        | TextChange.DeleteRight count -> InsertCommand.DeleteRight count
        | TextChange.Combination (left, right) ->
            let leftCommand = InsertCommand.OfTextChange left
            let rightCommand = InsertCommand.OfTextChange right
            InsertCommand.Combined (leftCommand, rightCommand)

    /// Convert this InsertCommand to a TextChange object
    member x.TextChange editorOptions textBuffer = 
        match x with 
        | InsertCommand.Back ->  Some (TextChange.DeleteLeft 1)
        | InsertCommand.BlockInsert _ -> None
        | InsertCommand.Combined (left, right) -> 
            match left.TextChange editorOptions textBuffer, right.TextChange editorOptions textBuffer with
            | Some l, Some r -> TextChange.Combination (l, r) |> Some
            | _ -> None
        | InsertCommand.CompleteMode _ -> None
        | InsertCommand.Delete -> Some (TextChange.DeleteRight 1)
        | InsertCommand.DeleteLeft count -> Some (TextChange.DeleteLeft count)
        | InsertCommand.DeleteRight count -> Some (TextChange.DeleteRight count)
        | InsertCommand.DeleteAllIndent -> None
        | InsertCommand.DeleteWordBeforeCursor -> None
        | InsertCommand.Insert text -> Some (TextChange.Insert text)
        | InsertCommand.InsertLiteral text -> Some (TextChange.Insert text)
        | InsertCommand.InsertCharacterAboveCaret -> None
        | InsertCommand.InsertCharacterBelowCaret -> None
        | InsertCommand.InsertNewLine -> Some (TextChange.Insert (EditUtil.NewLine editorOptions textBuffer))
        | InsertCommand.InsertPreviouslyInsertedText _ -> None
        | InsertCommand.InsertTab -> Some (TextChange.Insert "\t")
        | InsertCommand.MoveCaret _ -> None
        | InsertCommand.MoveCaretWithArrow _ -> None
        | InsertCommand.MoveCaretByWord _ -> None
        | InsertCommand.MoveCaretToEndOfLine -> None
        | InsertCommand.Replace c -> Some (TextChange.Combination ((TextChange.DeleteRight 1), (TextChange.Insert (c.ToString()))))
        | InsertCommand.ReplaceCharacterAboveCaret -> None
        | InsertCommand.ReplaceCharacterBelowCaret -> None
        | InsertCommand.Overwrite s -> Some (TextChange.Replace s)
        | InsertCommand.ShiftLineLeft -> None
        | InsertCommand.ShiftLineRight -> None
        | InsertCommand.UndoReplace -> None
        | InsertCommand.DeleteLineBeforeCursor -> None
        | InsertCommand.Paste -> None
        | InsertCommand.LiteralPaste _ -> None

/// Commands which can be executed by the user
[<RequireQualifiedAccess>]
[<StructuralEquality>]
[<NoComparison>]
type Command =

    /// A Normal Mode Command
    | NormalCommand of NormalCommand: NormalCommand * CommandData: CommandData

    /// A Visual Mode Command
    | VisualCommand of VisualCommand: VisualCommand * CommandData: CommandData * VisualSpan: VisualSpan

    /// An Insert / Replace Mode Command
    | InsertCommand of InsertCommand: InsertCommand

/// This is the result of attemping to bind a series of KeyInput values into a Motion
/// Command, etc ... 
[<RequireQualifiedAccess>]
type BindResult<'T> = 

    /// Successfully bound to a value
    | Complete of Result: 'T 

    /// More input is needed to complete the binding operation
    | NeedMoreInput of BindData: BindData<'T>

    /// There was an error completing the binding operation
    | Error

    /// Motion was cancelled via user input
    | Cancelled

    with

    /// Used to compose to BindResult<'T> functions together by forwarding from
    /// one to the other once the value is completed
    member x.Map (mapFunc: 'T -> BindResult<'U>): BindResult<'U> =
        match x with
        | Complete value -> mapFunc value 
        | NeedMoreInput (bindData: BindData<'T>) -> NeedMoreInput (bindData.Map mapFunc)
        | Error -> Error
        | Cancelled -> Cancelled

    /// Used to convert a BindResult<'T>.Completed to BindResult<'U>.Completed through a conversion
    /// function
    member x.Convert (convertFunc: 'T -> 'U): BindResult<'U> = 
        x.Map (fun value -> convertFunc value |> BindResult.Complete)

and BindData<'T> = {

    /// The optional KeyRemapMode which should be used when binding
    /// the next KeyInput in the sequence
    KeyRemapMode: KeyRemapMode 

    /// Function to call to get the BindResult for this data
    BindFunction: KeyInput -> BindResult<'T>

} with

    member x.CreateBindResult() = BindResult.NeedMoreInput x

    /// Used for BindData where there can only be a complete result for a given 
    /// KeyInput.
    static member CreateForKeyInput keyRemapMode valueFunc =
        let bindFunc keyInput = 
            let value = valueFunc keyInput
            BindResult<_>.Complete value
        { KeyRemapMode = keyRemapMode; BindFunction = bindFunc } 

    /// Used for BindData where there can only be a complete result for a given 
    /// char
    static member CreateForChar keyRemapMode valueFunc =
        BindData<_>.CreateForKeyInput keyRemapMode (fun keyInput -> valueFunc keyInput.Char)

    /// Very similar to the Convert function.  This will instead map a BindData<'T>.Completed
    /// to a BindData<'U> of any form 
    member x.Map<'U> (mapFunc: 'T -> BindResult<'U>): BindData<'U> = 
        let originalBindFunc = x.BindFunction
        let bindFunc keyInput = 
            match originalBindFunc keyInput with
            | BindResult.Cancelled -> BindResult.Cancelled
            | BindResult.Complete value -> mapFunc value
            | BindResult.Error -> BindResult.Error
            | BindResult.NeedMoreInput bindData -> BindResult.NeedMoreInput (bindData.Map mapFunc)
        { KeyRemapMode = x.KeyRemapMode; BindFunction = bindFunc }

    /// Often types bindings need to compose together because we need an inner binding
    /// to succeed so we can create a projected value.  This function will allow us
    /// to translate a BindResult<'T>.Completed -> BindResult<'U>.Completed
    member x.Convert (convertFunc: 'T -> 'U): BindData<'U> = 
        x.Map (fun value -> convertFunc value |> BindResult.Complete)

/// Several types of BindData<'T> need to take an action when a binding begins against
/// themselves. This action needs to occur before the first KeyInput value is processed
/// and hence they need a jump start. The most notable is IncrementalSearch which 
/// needs to enter 'Search' mode before processing KeyInput values so the cursor can
/// be updated
[<RequireQualifiedAccess>]
type BindDataStorage<'T> =

    /// Simple BindData<'T> which doesn't require activation
    | Simple of BindData: BindData<'T> 

    /// Complex BindData<'T> which does require activation
    | Complex of CreateBindDataFunc: (unit -> BindData<'T>)

    with

    /// Creates the BindData
    member x.CreateBindData () = 
        match x with
        | Simple bindData -> bindData
        | Complex func -> func()

    /// Convert from a BindDataStorage<'T> -> BindDataStorage<'U>.  The 'mapFunc' value
    /// will run on the final 'T' data if it eventually is completed
    member x.Convert mapFunc = 
        match x with
        | Simple bindData -> Simple (bindData.Convert mapFunc)
        | Complex func -> Complex (fun () -> func().Convert mapFunc)

/// This is the result of attemping to bind a series of KeyInputData values
/// into a Motion Command, etc ...
[<RequireQualifiedAccess>]
type MappedBindResult<'T> =

    /// Successfully bound to a value
    | Complete of Result: 'T

    /// More input is needed to complete the binding operation
    | NeedMoreInput of MappedBindData: MappedBindData<'T>

    /// There was an error completing the binding operation
    | Error

    /// Motion was cancelled via user input
    | Cancelled

    with

    /// Used to compose to MappedBindResult<'T> functions together by
    /// forwarding from one to the other once the value is completed
    member x.Map (mapFunc: 'T -> MappedBindResult<'U>): MappedBindResult<'U> =
        match x with
        | Complete value -> mapFunc value
        | NeedMoreInput (bindData: MappedBindData<'T>) -> NeedMoreInput (bindData.Map mapFunc)
        | Error -> Error
        | Cancelled -> Cancelled

    /// Used to convert a MappedBindResult<'T>.Completed to
    /// MappedBindResult<'U>.Completed through a conversion function
    member x.Convert (convertFunc: 'T -> 'U): MappedBindResult<'U> =
        x.Map (fun value -> convertFunc value |> MappedBindResult.Complete)

    /// Convert this MappedBindResult<'T> to a BindResult<'T>
    member x.ConvertToBindResult (): BindResult<'T> =
        match x with
        | MappedBindResult.Complete result -> BindResult.Complete result
        | MappedBindResult.NeedMoreInput mappedBindData ->
            BindResult.NeedMoreInput (mappedBindData.ConvertToBindData())
        | MappedBindResult.Error -> BindResult.Error
        | MappedBindResult.Cancelled -> BindResult.Cancelled

and MappedBindData<'T> = {

    /// The optional KeyRemapMode which should be used when binding the next
    /// KeyInput in the sequence
    KeyRemapMode: KeyRemapMode

    /// Function to call to get the MappedBindResult for this data
    MappedBindFunction: KeyInputData -> MappedBindResult<'T>

} with

    member x.CreateBindResult() = MappedBindResult.NeedMoreInput x

    /// Very similar to the Convert function.  This will instead map a
    /// MappedBindData<'T>.Completed to a MappedBindData<'U> of any form
    member x.Map<'U> (mapFunc: 'T -> MappedBindResult<'U>): MappedBindData<'U> =
        let originalBindFunc = x.MappedBindFunction
        let bindFunc keyInputData =
            match originalBindFunc keyInputData with
            | MappedBindResult.Complete value -> mapFunc value
            | MappedBindResult.NeedMoreInput mappedBindData ->
                MappedBindResult.NeedMoreInput (mappedBindData.Map mapFunc)
            | MappedBindResult.Error -> MappedBindResult.Error
            | MappedBindResult.Cancelled -> MappedBindResult.Cancelled
        { KeyRemapMode = x.KeyRemapMode; MappedBindFunction = bindFunc }

    /// Often types bindings need to compose together because we need an inner
    /// binding to succeed so we can create a projected value.  This function
    /// will allow us to translate a MappedBindResult<'T>.Completed ->
    /// MappedBindResult<'U>.Completed
    member x.Convert (convertFunc: 'T -> 'U): MappedBindData<'U> =
        x.Map (fun value -> convertFunc value |> MappedBindResult.Complete)

    /// Convert this MappedBindData<'T> to a BindData<'T> (note that as a
    /// result of the conversion all key inputs will all appear to be unmapped)
    member x.ConvertToBindData (): BindData<'T> =
        let bindFunc keyInput =
            KeyInputData.Create keyInput false
            |> x.MappedBindFunction
            |> (fun mappedBindResult -> mappedBindResult.ConvertToBindResult())
        { KeyRemapMode = x.KeyRemapMode; BindFunction = bindFunc }

/// Several types of MappedBindData<'T> need to take an action when a binding
/// begins against themselves. This action needs to occur before the first
/// KeyInput value is processed and hence they need a jump start. The most
/// notable is IncrementalSearch which  needs to enter 'Search' mode before
/// processing KeyInput values so the cursor can be updated
[<RequireQualifiedAccess>]
type MappedBindDataStorage<'T> =

    /// Simple MappedBindData<'T> which doesn't require activation
    | Simple of MappedBindData: MappedBindData<'T> 

    /// Complex MappedBindData<'T> which does require activation
    | Complex of CreateBindDataFunc: (unit -> MappedBindData<'T>)

    with

    /// Creates the MappedBindData
    member x.CreateMappedBindData () = 
        match x with
        | Simple bindData -> bindData
        | Complex func -> func()

    /// Convert from a MappedBindDataStorage<'T> -> MappedBindDataStorage<'U>.
    /// The 'mapFunc' value will run on the final 'T' data if it eventually is
    /// completed
    member x.Convert mapFunc = 
        match x with
        | Simple bindData -> Simple (bindData.Convert mapFunc)
        | Complex func -> Complex (fun () -> func().Convert mapFunc)

/// Representation of binding of Command's to KeyInputSet values and flags which correspond
/// to the execution of the command
[<DebuggerDisplay("{ToString(),nq}")>]
[<RequireQualifiedAccess>]
type CommandBinding = 

    /// KeyInputSet bound to a particular NormalCommand instance
    | NormalBinding of KeyInputSet: KeyInputSet * CommandFlags: CommandFlags * NormalCommand: NormalCommand

    /// KeyInputSet bound to a complex NormalCommand instance
    | ComplexNormalBinding of KeyInputSet: KeyInputSet * CommandFlags: CommandFlags * BindDataStorage: BindDataStorage<NormalCommand>

    /// KeyInputSet bound to a particular NormalCommand instance which takes a Motion Argument
    | MotionBinding of KeyInputSet: KeyInputSet * CommandFlags: CommandFlags * Func: (MotionData -> NormalCommand)

    /// KeyInputSet bound to a particular VisualCommand instance
    | VisualBinding of KeyInputSet: KeyInputSet * CommandFlags: CommandFlags * VisualCommand: VisualCommand

    /// KeyInputSet bound to an insert mode command
    | InsertBinding of KeyInputSet: KeyInputSet * CommandFlags: CommandFlags * InsertCommand: InsertCommand

    /// KeyInputSet bound to a complex VisualCommand instance
    | ComplexVisualBinding of KeyInputSet: KeyInputSet * CommandFlags: CommandFlags * BindDataStorage: BindDataStorage<VisualCommand>

    with 

    /// The raw command inputs
    member x.KeyInputSet = 
        match x with
        | NormalBinding (value, _, _) -> value
        | MotionBinding (value, _, _) -> value
        | VisualBinding (value, _, _) -> value
        | InsertBinding (value, _, _) -> value
        | ComplexNormalBinding (value, _, _) -> value
        | ComplexVisualBinding (value, _, _) -> value

    /// The kind of the Command
    member x.CommandFlags =
        match x with
        | NormalBinding (_, value, _) -> value
        | MotionBinding (_, value, _) -> value
        | VisualBinding (_, value, _) -> value
        | InsertBinding (_, value, _) -> value
        | ComplexNormalBinding (_, value, _) -> value
        | ComplexVisualBinding (_, value, _) -> value

    /// Is the Repeatable flag set
    member x.IsRepeatable = Util.IsFlagSet x.CommandFlags CommandFlags.Repeatable

    /// Is the HandlesEscape flag set
    member x.HandlesEscape = Util.IsFlagSet x.CommandFlags CommandFlags.HandlesEscape

    /// Is the Movement flag set
    member x.IsMovement = Util.IsFlagSet x.CommandFlags CommandFlags.Movement

    /// Is the Special flag set
    member x.IsSpecial = Util.IsFlagSet x.CommandFlags CommandFlags.Special

    override x.ToString() = System.String.Format("{0} -> {1}", x.KeyInputSet, x.CommandFlags)

/// Used to execute commands
and ICommandUtil = 

    /// Run a normal command
    abstract RunNormalCommand: command: NormalCommand -> commandData: CommandData -> CommandResult

    /// Run a visual command
    abstract RunVisualCommand: command: VisualCommand -> commandData: CommandData -> visualSpan: VisualSpan -> CommandResult

    /// Run a insert command
    abstract RunInsertCommand: command: InsertCommand -> CommandResult

    /// Run a command
    abstract RunCommand: command: Command -> CommandResult

type internal IInsertUtil = 

    /// Run a insert command
    abstract RunInsertCommand: insertCommand: InsertCommand -> CommandResult

    /// Repeat the given edit series. 
    abstract RepeatEdit: textChange: TextChange -> addNewLines: bool -> count: int -> unit

    /// Repeat the given edit series. 
    abstract RepeatBlock: command: InsertCommand -> visualInsertKind: VisualInsertKind -> blockSpan: BlockSpan -> string option

    /// Signal that a new undo sequence is in effect
    abstract NewUndoSequence: unit -> unit

/// Contains the stored information about a Visual Span.  This instance *will* be 
/// stored for long periods of time and used to repeat a Command instance across
/// multiple IVimBuffer instances so it must be buffer agnostic
[<RequireQualifiedAccess>]
type StoredVisualSpan = 

    /// Storing a character wise span.  Need to know the line count and the offset 
    /// in the last line for the end.  
    | Character of LineCount: int * LastLineMaxPositionCount: int

    /// Storing a line wise span just stores the count of lines
    | Line of Count: int

    /// Storing of a block span records the length of the span and the number of
    /// lines which should be affected by the Span
    | Block of Width: int * Height: int * EndOfLine: bool

    with

    /// Create a StoredVisualSpan from the provided VisualSpan value
    static member OfVisualSpan visualSpan = 
        match visualSpan with
        | VisualSpan.Character characterSpan -> StoredVisualSpan.Character (characterSpan.LineCount, characterSpan.LastLineMaxPositionCount)
        | VisualSpan.Line range -> StoredVisualSpan.Line range.Count
        | VisualSpan.Block blockSpan -> StoredVisualSpan.Block (blockSpan.SpacesLength, blockSpan.Height, blockSpan.EndOfLine)

/// Contains information about an executed Command.  This instance *will* be stored
/// for long periods of time and used to repeat a Command instance across multiple
/// IVimBuffer instances so it simply cannot store any state specific to an 
/// ITextView instance.  It must be completely agnostic of such information 
[<RequireQualifiedAccess>]
type StoredCommand =

    /// The stored information about a NormalCommand
    | NormalCommand of NormalCommand: NormalCommand * CommandData: CommandData * CommandFlags: CommandFlags

    /// The stored information about a VisualCommand
    | VisualCommand of VisualCommand: VisualCommand * CommandData: CommandData * StoredVisualSpan: StoredVisualSpan * CommandFlags: CommandFlags

    /// The stored information about a InsertCommand
    | InsertCommand of InsertCommand: InsertCommand * CommandFlags: CommandFlags

    /// A Linked Command links together 2 other StoredCommand objects so they
    /// can be repeated together.
    | LinkedCommand of Left: StoredCommand * Right: StoredCommand

    with

    /// The CommandFlags associated with this StoredCommand
    member x.CommandFlags =
        match x with 
        | NormalCommand (_, _, flags) -> flags
        | VisualCommand (_, _, _, flags) -> flags
        | InsertCommand (_, flags) -> flags
        | LinkedCommand (_, rightCommand) -> rightCommand.CommandFlags

    /// Returns the last command.  For most StoredCommand values this is just an identity 
    /// function but for LinkedCommand values it returns the right most
    member x.LastCommand =
        match x with
        | NormalCommand _ -> x
        | VisualCommand _ -> x
        | InsertCommand _ -> x
        | LinkedCommand (_, right) -> right.LastCommand

    /// Create a StoredCommand instance from the given Command value
    static member OfCommand command (commandBinding: CommandBinding) = 
        match command with 
        | Command.NormalCommand (command, data) -> 
            StoredCommand.NormalCommand (command, data, commandBinding.CommandFlags)
        | Command.VisualCommand (command, data, visualSpan) ->
            let storedVisualSpan = StoredVisualSpan.OfVisualSpan visualSpan
            StoredCommand.VisualCommand (command, data, storedVisualSpan, commandBinding.CommandFlags)
        | Command.InsertCommand command ->
            StoredCommand.InsertCommand (command, commandBinding.CommandFlags)

/// Flags about specific motions
[<RequireQualifiedAccess>]
[<System.Flags>]
type MotionFlags =

    | None = 0x0

    /// This type of motion can be used to move the caret
    | CaretMovement = 0x1 

    /// The motion function wants to specially handle the esape function.  This is used 
    /// on Complex motions such as / and ? 
    | HandlesEscape = 0x2

    /// Text object selection motions.  These can be used for cursor movement inside of 
    /// Visual Mode but otherwise need to be used only after operators.  
    /// :help text-objects
    | TextObject = 0x4

    /// Text object with line to character.  Requires TextObject
    | TextObjectWithLineToCharacter = 0x8

    /// Text object with always character.  Requires TextObject
    | TextObjectWithAlwaysCharacter = 0x10

    /// Text objcet with always line.  Requires TextObject
    | TextObjectWithAlwaysLine = 0x20

/// Represents the types of MotionCommands which exist
[<RequireQualifiedAccess>]
type MotionBinding =

    /// Simple motion which comprises of a single KeyInput and a function which given 
    /// a start point and count will produce the motion.  None is returned in the 
    /// case the motion is not valid
    | Static of KeyInputSet: KeyInputSet * MotionFlags: MotionFlags * Motion: Motion

    /// Complex motion commands take more than one KeyInput to complete.  For example 
    /// the f,t,F and T commands all require at least one additional input.
    | Dynamic of KeyInputSet: KeyInputSet * MotionFlags: MotionFlags * BindDataStorage: BindDataStorage<Motion>

    with

    member x.KeyInputSet = 
        match x with
        | Static (keyInputSet, _, _) -> keyInputSet
        | Dynamic (keyInputSet, _, _) -> keyInputSet

    member x.MotionFlags =
        match x with 
        | Static (_, flags, _) -> flags
        | Dynamic (_, flags, _) -> flags

/// The information about the particular run of a Command
type CommandRunData = {

    /// The binding which the command was invoked from
    CommandBinding: CommandBinding

    /// The Command which was run
    Command: Command

    /// The result of the Command Run
    CommandResult: CommandResult
}

type CommandRunDataEventArgs(_commandRunData: CommandRunData) =
    inherit System.EventArgs()

    member x.CommandRunData = _commandRunData

type CommandEventArgs(_rawCommand: string, _command: string, _wasMapped: bool, _lineCommand: LineCommand) =
    inherit System.EventArgs()

    /// This is provided text that was executed. This can be both the abbreviated form of a command, say 'e',
    /// or the expanded form 'edit'.
    member x.RawCommand = _rawCommand

    /// This is the expanded command name which was executed. It will never be the abbreviated form.
    member x.Command = _command

    member x.LineCommand = _lineCommand

    member x.WasMapped = _wasMapped

/// Responsible for binding key input to a Motion and MotionArgument tuple.  Does
/// not actually run the motions
type IMotionCapture =

    /// Associated ITextView
    abstract TextView: ITextView
    
    /// Set of MotionBinding values supported
    abstract MotionBindings: MotionBinding list

    /// Get the motion and count starting with the given KeyInput
    abstract GetMotionAndCount: KeyInput -> BindResult<Motion * int option>

    /// Get the motion with the provided KeyInput
    abstract GetMotion: KeyInput -> BindResult<Motion>

/// Responsible for managing a set of Commands and running them
type ICommandRunner =

    /// Set of Commands currently supported.
    abstract Commands: CommandBinding seq

    /// Count of commands currently supported.
    abstract CommandCount: int

    /// In certain circumstances a specific type of key remapping needs to occur for input.  This 
    /// option will have the appropriate value in those circumstances.  For example while processing
    /// the {char} argument to f,F,t or T the Language mapping will be used
    abstract KeyRemapMode: KeyRemapMode 

    /// True when in the middle of a count operation
    abstract InCount: bool

    /// Is the command runner currently binding a command which needs to explicitly handle escape
    abstract IsHandlingEscape: bool

    /// True if waiting on more input
    abstract IsWaitingForMoreInput: bool

    /// True if the current command has a register associated with it 
    abstract HasRegisterName: bool 

    /// True if the current command has a count associated with it 
    abstract HasCount: bool

    /// When HasRegister is true this has the associated RegisterName 
    abstract RegisterName: RegisterName

    /// When HasCount is true this has the associated count
    abstract Count: int 

    /// List of processed KeyInputs in the order in which they were typed
    abstract Inputs: KeyInput list
    
    /// Whether a command starts with the specified key input
    abstract DoesCommandStartWith: KeyInput -> bool

    /// Add a Command.  If there is already a Command with the same name an exception will
    /// be raised
    abstract Add: CommandBinding -> unit

    /// Remove a command with the specified name
    abstract Remove: KeyInputSet -> unit

    /// Process the given KeyInput.  If the command completed it will return a result.  A
    /// None value implies more input is needed to finish the operation
    abstract Run: KeyInput -> BindResult<CommandRunData>

    /// If currently waiting for more input on a Command, reset to the 
    /// initial state
    abstract ResetState: unit -> unit

    /// Raised when a command is successfully run
    [<CLIEvent>]
    abstract CommandRan: IDelegateEvent<System.EventHandler<CommandRunDataEventArgs>>

[<Struct>]
type KeyMapping =
    val private _left: KeyInputSet
    val private _right: KeyInputSet
    val private _allowRemap: bool
    val private _mode: KeyRemapMode

    new (left: KeyInputSet, right: KeyInputSet, allowRemap: bool, mode: KeyRemapMode) =
        { _left = left; _right = right; _allowRemap = allowRemap; _mode = mode }

    member x.Left = x._left
    member x.Right = x._right
    member x.AllowRemap = x._allowRemap
    member x.Mode = x._mode

    override x.ToString() = sprintf "%O - %O" x.Left x.Right

type IVimKeyMap =

    abstract KeyMappings: KeyMapping seq

    /// Get the specified key mapping if it exists
    abstract GetKeyMapping: lhs: KeyInputSet * mode: KeyRemapMode -> KeyMapping option

    /// Get all mappings for the specified mode
    abstract GetKeyMappings: mode: KeyRemapMode -> KeyMapping seq

    /// Map the given key sequence without allowing for remaping
    abstract AddKeyMapping: lhs: KeyInputSet * rhs: KeyInputSet * allowRemap: bool * mode: KeyRemapMode -> unit

    /// Unmap the specified key sequence for the specified mode
    abstract RemoveKeyMapping: lhs: KeyInputSet * mode: KeyRemapMode -> bool

    /// Clear the Key mappings for the specified mode
    abstract ClearKeyMappings: mode: KeyRemapMode -> unit

    /// Clear the Key mappings for all modes
    abstract ClearKeyMappings: unit -> unit

    /// This operates similar to KeyNotationUtil.TryStringToKeyInputSet except that it will consider 
    /// the <leader> notation as well.
    abstract ParseKeyNotation: notation: string -> KeyInputSet option

type IVimGlobalKeyMap =
    inherit IVimKeyMap

type IVimLocalKeyMap =
    inherit IVimKeyMap

    abstract GlobalKeyMap: IVimGlobalKeyMap

    /// Is the mapping of the 0 key currently enabled
    abstract IsZeroMappingEnabled: bool with get, set 

    /// Get the specified key mapping if it exists
    abstract GetKeyMapping: lhs: KeyInputSet * mode: KeyRemapMode * includeGlobal: bool -> KeyMapping option

    /// Get the specified key mappings if they exists
    abstract GetKeyMappings: mode: KeyRemapMode * includeGlobal: bool -> KeyMapping seq

    /// Map the provided KeyInputSet for the given mode.
    abstract Map: lhs: KeyInputSet * mode: KeyRemapMode -> KeyMappingResult

    /// Map the provided KeyInputSet for the given mode. The allowRemap parameter will override
    /// the defined remapping behavior.
    abstract Map: lhs: KeyInputSet * allowRemap: bool * mode:KeyRemapMode -> KeyMappingResult

/// Manages the digraph map for Vim
type IDigraphMap =

    abstract Map: char -> char -> int -> unit

    abstract Unmap: char -> char -> unit

    abstract GetMapping: char -> char -> int option

    abstract Mappings: (char * char * int) seq

    abstract Clear: unit -> unit

[<NoEquality>]
[<NoComparison>]
[<Struct>]
type Abbreviation =
    val private _abbreviation: KeyInputSet
    val private _replacement: KeyInputSet
    val private _allowRemap: bool
    val private _mode: AbbreviationMode

    new (abbreviation: KeyInputSet, replacement: KeyInputSet, allowRemap: bool, mode: AbbreviationMode) =
        { _abbreviation = abbreviation; _replacement = replacement; _allowRemap = allowRemap; _mode = mode }

    member x.Abbreviation = x._abbreviation
    member x.Replacement = x._replacement
    member x.AllowRemap = x._allowRemap
    member x.Mode = x._mode

    override x.ToString() = sprintf "%O - %O" x.Abbreviation x.Replacement

/// Information about a single abbreviation replace
[<NoComparison>]
[<NoEquality>]
type AbbreviationResult = {
    /// The found Abbreviation that was replaced
    Abbreviation: Abbreviation

    /// The AbbreviationKind of the found abbrevitaion
    AbbreviationKind: AbbreviationKind

    /// The set of KeyInput values that should be used to replace the abbreviated text. 
    /// Note: this can be different than Abbreviation.Replacement when key mappings are 
    /// in play. AbbreviationResult.Replacement should be preferred.
    Replacement: KeyInputSet

    /// This contains the result of running the remap operation on Abbreviation.Replacement.
    ReplacementRemapResult: KeyMappingResult option

    /// The original text which was considered for the abbreviation
    OriginalText: string

    /// The KeyInput which triggered the abbreviation attempt. This will always be a non-keyword
    /// character.
    TriggerKeyInput: KeyInput

    /// The Span inside OriginalText which should be replaced by the abbreviation
    ReplacedSpan: Span
} with

    member x.Mode = x.Abbreviation.Mode

    override x.ToString() =
        let key = x.Abbreviation.ToString()
        let value = (x.Replacement.Add x.TriggerKeyInput).ToString()
        let text = x.OriginalText.Substring(0, x.OriginalText.Length - key.Length)
        text + value

type IVimAbbreviationMap =

    abstract Abbreviations: Abbreviation seq

    abstract AddAbbreviation: lhs: KeyInputSet * rhs: KeyInputSet * allowRemap: bool * mode: AbbreviationMode -> unit

    abstract GetAbbreviation: lhs: KeyInputSet * mode: AbbreviationMode -> Abbreviation option

    abstract GetAbbreviations: mode: AbbreviationMode -> Abbreviation seq

    abstract RemoveAbbreviation: lhs: KeyInputSet * mode: AbbreviationMode -> bool

    abstract ClearAbbreviations: mode: AbbreviationMode -> unit

    abstract ClearAbbreviations: unit -> unit

type IVimGlobalAbbreviationMap =
    inherit IVimAbbreviationMap

type IVimLocalAbbreviationMap =
    inherit IVimAbbreviationMap

    abstract GlobalAbbreviationMap: IVimGlobalAbbreviationMap 

    abstract GetAbbreviation: lhs: KeyInputSet * mode: AbbreviationMode * includeGlobal: bool -> Abbreviation option

    abstract GetAbbreviations: mode: AbbreviationMode * includeGlobal: bool -> Abbreviation seq

    abstract Parse: text: string -> AbbreviationKind option

    abstract Abbreviate: text: string * triggerKeyInput: KeyInput * mode: AbbreviationMode -> AbbreviationResult option

type MarkTextBufferEventArgs (_mark: Mark, _textBuffer: ITextBuffer) =
    inherit System.EventArgs()

    member x.Mark = _mark
    member x.TextBuffer = _textBuffer

    override x.ToString() = _mark.ToString()

type MarkTextViewEventArgs (_mark: Mark, _textView: ITextView) =
    inherit System.EventArgs()

    member x.Mark = _mark
    member x.TextView = _textView

    override x.ToString() = _mark.ToString()

/// Jump list information associated with an IVimBuffer.  This is maintained as a forward
/// and backwards traversable list of points with which to navigate to
///
/// Technically Vim's implementation of a jump list can span across different
/// buffers  This is limited to just a single ITextBuffer.  This is mostly due to Visual 
/// Studio's limitations in swapping out an ITextBuffer contents for a different file.  It
/// is possible but currently not a high priority here
type IJumpList = 

    /// Associated ITextView instance
    abstract TextView: ITextView

    /// Current value in the jump list.  Will be None if we are not currently traversing the
    /// jump list
    abstract Current: VirtualSnapshotPoint option

    /// Current index into the jump list.  Will be None if we are not currently traversing
    /// the jump list
    abstract CurrentIndex: int option

    /// True if we are currently traversing the list
    abstract IsTraversing: bool

    /// Get all of the jumps in the jump list.  Returns in order of most recent to oldest
    abstract Jumps: VirtualSnapshotPoint list

    /// The SnapshotPoint when the last jump occurred
    abstract LastJumpLocation: VirtualSnapshotPoint option

    /// Add a given SnapshotPoint to the jump list.  This will reset Current to point to 
    /// the begining of the jump list
    abstract Add: VirtualSnapshotPoint -> unit

    /// Clear out all of the stored jump information.  Removes all tracking information from
    /// the IJumpList
    abstract Clear: unit -> unit

    /// Move to the previous point in the jump list.  This will fail if we are not traversing
    /// the list or at the end 
    abstract MoveOlder: int -> bool

    /// Move to the next point in the jump list.  This will fail if we are not traversing
    /// the list or at the start
    abstract MoveNewer: int -> bool

    /// Set the last jump location to the given line and column
    abstract SetLastJumpLocation: lineNumber: int -> offset: int -> unit

    /// Start a traversal of the list
    abstract StartTraversal: unit -> unit

    /// Raised when a mark is set
    [<CLIEvent>]
    abstract MarkSet: IDelegateEvent<System.EventHandler<MarkTextViewEventArgs>>

type IIncrementalSearchSession = 

    /// Key that uniquely identifies this session
    abstract Key: obj

    /// Whether or not the search has started
    abstract IsStarted: bool

    /// Whether or not the session is complete. True when the session has finished the search
    /// or was cancelled.
    abstract IsCompleted: bool

    /// When in the middle of a search this will return the SearchData for 
    /// the search
    abstract SearchData: SearchData 

    /// When a search is complete within the session this will hold the result
    abstract SearchResult: SearchResult option

    /// Start an incremental search in the ITextView
    abstract Start: unit -> BindData<SearchResult>

    /// Reset the search to the specified text
    abstract ResetSearch: searchText: string -> unit

    /// Cancel the session without completing
    abstract Cancel: unit -> unit

    /// This will resolve to SearchResult for the current value of SearchData once the
    /// search is complete
    abstract GetSearchResultAsync: unit -> Task<SearchResult>

    [<CLIEvent>]
    abstract SearchStart: IDelegateEvent<System.EventHandler<SearchDataEventArgs>>

    [<CLIEvent>]
    abstract SearchEnd: IDelegateEvent<System.EventHandler<SearchResultEventArgs>>

    [<CLIEvent>]
    abstract SessionComplete: IDelegateEvent<System.EventHandler<EventArgs>>

type IncrementalSearchSessionEventArgs(_session: IIncrementalSearchSession) = 
    inherit System.EventArgs()

    member x.Session = _session

type IIncrementalSearch = 

    /// The active IIncrementalSearchSession
    abstract ActiveSession: IIncrementalSearchSession option

    /// True when there is an IIncrementalSearchSession in progress
    abstract HasActiveSession: bool

    /// True when the search is in a paste wait state
    abstract InPasteWait: bool

    /// The ITextStructureNavigator used for finding 'word' values in the ITextBuffer
    abstract WordNavigator: ITextStructureNavigator

    abstract CurrentSearchData: SearchData

    abstract CurrentSearchText: string

    abstract CreateSession: searchPath: SearchPath -> IIncrementalSearchSession

    /// Cancel the active session if there is one.
    abstract CancelSession: unit -> unit

    [<CLIEvent>]
    abstract SessionCreated: IDelegateEvent<System.EventHandler<IncrementalSearchSessionEventArgs>>

type RecordRegisterEventArgs(_register: Register, _isAppend: bool) =
    inherit System.EventArgs()
    
    member x.Register = _register

    member x.IsAppend = _isAppend

/// Used to record macros in a Vim 
type IMacroRecorder =

    /// The current recording 
    abstract CurrentRecording: KeyInput list option

    /// Is a macro currently recording
    abstract IsRecording: bool

    /// Start recording a macro into the specified Register.  Will fail if the recorder
    /// is already recording
    abstract StartRecording: register: Register -> isAppend: bool -> unit

    /// Stop recording a macro.  Will fail if it's not actually recording
    abstract StopRecording: unit -> unit

    /// Raised when a macro recording is started.  Passes the Register where the recording
    /// will take place.  The bool is whether the record is an append or not
    [<CLIEvent>]
    abstract RecordingStarted: IDelegateEvent<System.EventHandler<RecordRegisterEventArgs>>

    /// Raised when a macro recording is completed.
    [<CLIEvent>]
    abstract RecordingStopped: IDelegateEvent<System.EventHandler>

[<RequireQualifiedAccess>]
type ProcessResult = 

    /// The input was processed and provided the given ModeSwitch
    | Handled of ModeSwitch: ModeSwitch

    /// The input was processed but more input is needed in order to complete
    /// an operation
    | HandledNeedMoreInput

    /// The operation did not handle the input
    | NotHandled

    /// The input was processed and resulted in an error
    | Error

    /// Is this any type of mode switch
    member x.IsAnySwitch =
        match x with
        | Handled modeSwitch ->
            match modeSwitch with
            | ModeSwitch.NoSwitch -> false
            | ModeSwitch.SwitchMode _ -> true
            | ModeSwitch.SwitchModeWithArgument _ -> true
            | ModeSwitch.SwitchPreviousMode -> true
            | ModeSwitch.SwitchModeOneTimeCommand _ -> true
        | HandledNeedMoreInput ->
            false
        | NotHandled -> 
            false
        | Error -> 
            false

    /// Is this any type of switch to a visual mode
    member x.IsAnySwitchToVisual =
        match x with
        | ProcessResult.Handled modeSwitch ->
            match modeSwitch with
            | ModeSwitch.SwitchMode modeKind ->
                VisualKind.IsAnyVisualOrSelect modeKind
            | ModeSwitch.SwitchModeWithArgument(modeKind, _) ->
                VisualKind.IsAnyVisualOrSelect modeKind
            | ModeSwitch.SwitchModeOneTimeCommand modeKind ->
                VisualKind.IsAnyVisualOrSelect modeKind
            | _ ->
                false
        | _ ->
            false

    // Is this a switch to command mode?
    member x.IsAnySwitchToCommand =
        match x with
        | ProcessResult.Handled modeSwitch ->
            match modeSwitch with
            | ModeSwitch.SwitchMode modeKind -> modeKind = ModeKind.Command
            | ModeSwitch.SwitchModeWithArgument (modeKind, _) -> modeKind = ModeKind.Command
            | _ -> false
        | _ ->
            false

    /// Did this actually handle the KeyInput
    member x.IsAnyHandled = 
        match x with
        | Handled _ -> true
        | HandledNeedMoreInput -> true
        | Error -> true
        | NotHandled -> false

    /// Is this a successfully handled value?
    member x.IsHandledSuccess =
        match x with
        | Handled _ -> true
        | HandledNeedMoreInput -> true
        | Error -> false
        | NotHandled -> false

    static member OfModeKind kind = 
        let switch = ModeSwitch.SwitchMode kind
        Handled switch

    /// Create a ProcessResult from the given CommandResult value
    static member OfCommandResult commandResult = 
        match commandResult with
        | CommandResult.Completed modeSwitch -> Handled modeSwitch
        | CommandResult.Error -> Error

    /// Create a CommandResult from the given ProcessResult value
    static member ToCommandResult processResult = 
        match processResult with
        | Handled modeSwitch -> CommandResult.Completed modeSwitch
        | _ -> CommandResult.Error

type StringEventArgs(_message: string) =
    inherit System.EventArgs()

    member x.Message = _message

    override x.ToString() = _message

type KeyInputEventArgs (_keyInput: KeyInput) = 
    inherit System.EventArgs()

    member x.KeyInput = _keyInput

    override x.ToString() = _keyInput.ToString()

type KeyInputStartEventArgs (_keyInput: KeyInput) =
    inherit KeyInputEventArgs(_keyInput)

    let mutable _handled = false

    member x.Handled 
        with get() = _handled 
        and set value = _handled <- value

    override x.ToString() = _keyInput.ToString()

type KeyInputSetEventArgs (_keyInputSet: KeyInputSet) = 
    inherit System.EventArgs()

    member x.KeyInputSet = _keyInputSet

    override x.ToString() = _keyInputSet.ToString()

type KeyInputProcessedEventArgs(_keyInput: KeyInput, _processResult: ProcessResult) =
    inherit System.EventArgs()

    member x.KeyInput = _keyInput

    member x.ProcessResult = _processResult

    override x.ToString() = _keyInput.ToString()

/// Implements a list for storing history items.  This is used for the 5 types
/// of history lists in Vim (:help history).  
type HistoryList () = 

    let mutable _list: string list = List.empty
    let mutable _limit = VimConstants.DefaultHistoryLength
    let mutable _totalCount = 0

    /// Limit of the items stored in the list
    member x.Limit 
        with get () = 
            _limit
        and set value = 
            _limit <- value
            x.MaybeTruncateList()

    /// The count of actual items currently stored in the collection
    member x.Count = _list.Length

    /// This is a truncating list.  As items exceed the set Limit the eeriest items will
    /// be removed from the collection.  The total count represents the number of items
    /// that have ever been added, not the current count
    member x.TotalCount = _totalCount

    member x.Items = _list

    /// Adds an item to the top of the history list
    member x.Add value = 
        if not (StringUtil.IsNullOrEmpty value) then
            let list =
                _list
                |> Seq.filter (fun x -> not (StringUtil.IsEqual x value))
                |> Seq.truncate (_limit - 1)
                |> List.ofSeq
            _list <- value :: list
            _totalCount <- _totalCount + 1

    /// Remove an item from the history list
    member x.Remove value = 
        if not (StringUtil.IsNullOrEmpty value) then
            _list <-
                _list
                |> Seq.filter (fun x -> not (StringUtil.IsEqual x value))
                |> List.ofSeq

    /// Reset the list back to it's original state
    member x.Reset () = 
        _list <- List.empty
        _totalCount <- 0
        _limit <- VimConstants.DefaultHistoryLength

    member private x.MaybeTruncateList () = 
        if _list.Length > _limit then
            _list <-
                _list
                |> Seq.truncate _limit
                |> List.ofSeq

    interface System.Collections.IEnumerable with
        member x.GetEnumerator () = 
            let seq = _list :> string seq
            seq.GetEnumerator() :> System.Collections.IEnumerator

    interface System.Collections.Generic.IEnumerable<string> with
        member x.GetEnumerator () = 
            let seq = _list :> string seq
            seq.GetEnumerator()

/// Represents a single line of text being edited
[<StructuralEquality>]
[<NoComparison>]
[<Struct>]
[<DebuggerDisplay("{ToString(),nq}")>]
type EditableCommand =

    val private _text: string
    val private _caretPosition: int

    /// Constructor for initial text with the caret at the end
    new (text: string) =
        { _text = text; _caretPosition = text.Length }

    /// Constructor for text and caret position
    new (text: string, caretPosition: int) =
        { _text = text; _caretPosition = caretPosition }

    /// The text of the command
    member x.Text = x._text

    /// The caret position of the command
    member x.CaretPosition = x._caretPosition

    /// Home key operation
    member x.Home () =
        if x.CaretPosition <> 0 then
            EditableCommand(x.Text, 0)
        else
            x

    /// End key operation
    member x.End () =
        if x.CaretPosition <> x.Text.Length then
            EditableCommand(x.Text, x.Text.Length)
        else
            x

    /// Left arrow key operation
    member x.Left () =
        if x.CaretPosition > 0 then
            EditableCommand(x.Text, x.CaretPosition - 1)
        else
            x

    /// Right arrow key operation
    member x.Right () =
        if x.CaretPosition < x.Text.Length then
            EditableCommand(x.Text, x.CaretPosition + 1)
        else
            x

    /// Backspace key operation
    member x.Backspace () =
        if x.CaretPosition > 0 then
            let before = x.Text.Substring(0, x.CaretPosition - 1)
            let after = x.Text.Substring(x.CaretPosition)
            EditableCommand(before + after, x.CaretPosition  - 1)
        else
            x

    /// Delete key operation
    member x.Delete () =
        if x.CaretPosition < x.Text.Length - 1 then
            let before = x.Text.Substring(0, x.CaretPosition)
            let after = x.Text.Substring(x.CaretPosition + 1)
            EditableCommand(before + after, x.CaretPosition)
        else
            x

    /// Insert text at the caret
    member x.InsertText text =
        let before = x.Text.Substring(0, x.CaretPosition)
        let after = x.Text.Substring(x.CaretPosition)
        EditableCommand(before + text + after, x.CaretPosition + text.Length)

    /// Display representation of an editable command
    override x.ToString() =
        sprintf "Text = %s, CaretPosition = %i" x.Text x.CaretPosition

    /// The empty editable command
    static member Empty =
        EditableCommand(StringUtil.Empty, 0)

/// Used for helping history editing 
type internal IHistoryClient<'TData, 'TResult> =

    /// History list used by this client
    abstract HistoryList: HistoryList

    /// Get the register map 
    abstract RegisterMap: IRegisterMap

    /// What remapping mode if any should be used for key input
    abstract RemapMode: KeyRemapMode

    /// Beep
    abstract Beep: unit -> unit

    /// Process the new command with the previous TData value
    abstract ProcessCommand: data: 'TData -> command: EditableCommand -> 'TData

    /// Called when the command is completed.  The last valid TData and command
    /// string will be provided
    abstract Completed: data: 'TData -> command: EditableCommand -> wasMapped: bool -> 'TResult

    /// Called when the command is cancelled.  The last valid TData value will
    /// be provided
    abstract Cancelled: data: 'TData -> unit

/// An active use of an IHistoryClient instance 
type internal IHistorySession<'TData, 'TResult> =

    /// The IHistoryClient this session is using 
    abstract HistoryClient: IHistoryClient<'TData, 'TResult>

    /// The current command that is being edited 
    abstract EditableCommand: EditableCommand 

    /// Is the session currently waiting for a register paste operation to complete
    abstract InPasteWait: bool

    /// The current client data 
    abstract ClientData: 'TData

    /// Cancel the IHistorySession
    abstract Cancel: unit -> unit

    /// Reset the command to the current value
    abstract ResetCommand: EditableCommand -> unit

    /// Create an BindDataStorage for this session which will process relevant KeyInput values
    /// as manipulating the current history
    abstract CreateBindDataStorage: unit -> MappedBindDataStorage<'TResult>

/// Represents shared state which is available to all IVimBuffer instances.
type IVimData = 

    /// The set of supported auto command groups
    abstract AutoCommandGroups: AutoCommandGroup list with get, set

    /// The set of auto commands
    abstract AutoCommands: AutoCommand list with get, set

    /// The current directory Vim is positioned in
    abstract CurrentDirectory: string with get, set

    /// The history of the: command list
    abstract CommandHistory: HistoryList with get, set

    /// The file history list
    abstract FileHistory: HistoryList with get, set

    /// This is the pattern for which all occurences should be highlighted in the visible
    /// IVimBuffer instances.  When this value is empty then no pattern should be highlighted
    abstract DisplayPattern: string

    /// The ordered list of incremental search values
    abstract SearchHistory: HistoryList with get, set

    /// Motion function used with the last f, F, t or T motion.  The 
    /// first item in the tuple is the forward version and the second item
    /// is the backwards version
    abstract LastCharSearch: (CharSearchKind * SearchPath * char) option with get, set

    /// The last line command which was run 
    abstract LastLineCommand: LineCommand option with get, set

    /// The last command which was run 
    abstract LastCommand: StoredCommand option with get, set

    /// The last command line which was run
    abstract LastCommandLine: string with get, set

    /// The last shell command that was run
    abstract LastShellCommand: string option with get, set

    /// The last macro register which was run
    abstract LastMacroRun: char option with get, set

    /// Last pattern searched for in any buffer.
    abstract LastSearchData: SearchData with get, set

    /// Data for the last substitute command performed
    abstract LastSubstituteData: SubstituteData option with get, set

    /// Last text inserted into any buffer. Used for the '.' register
    abstract LastTextInsert: string option with get, set

    /// Last, unescaped, visual selection that occurred
    abstract LastVisualSelection: StoredVisualSelection option with get, set

    /// The previous value of the current directory Vim is positioned in
    abstract PreviousCurrentDirectory: string

    /// Suspend the display of patterns in the visible IVimBuffer instances.  This is usually
    /// associated with the use of the :nohl command
    abstract SuspendDisplayPattern: unit -> unit

    /// Resume the display of patterns in the visible IVimBuffer instance.  If the display
    /// isn't currently suspended then tihs command will have no effect on the system
    abstract ResumeDisplayPattern: unit -> unit

    /// Raised when the DisplayPattern property changes
    [<CLIEvent>]
    abstract DisplayPatternChanged: IDelegateEvent<System.EventHandler>

type TextViewChangedEventArgs
    (
        _oldTextView: ITextView option,
        _newTextView: ITextView option
    ) =

    inherit System.EventArgs()

    member x.OldTextView = _oldTextView

    member x.NewTextView = _newTextView

/// What settings default should VsVim use when there is not a _vimrc file present 
/// on the users machine?  There is a significant difference between gVim 7.3 and 
/// 7.4.  
[<RequireQualifiedAccess>]
type DefaultSettings =
    | GVim73 = 0
    | GVim74 = 1

type RunCommandResults
    (
        _exitCode: int,
        _output: string,
        _error: string
    ) =

    member x.ExitCode = _exitCode

    member x.Output = _output

    member x.Error = _error

/// Information associated with a mark
type MarkInfo
    (
        _ident: char,
        _name: string,
        _line: int,
        _column: int
    ) =

    /// The character used to identify the mark
    member x.Ident = _ident

    /// The name of the buffer the mark is in
    member x.Name = _name

    /// The line of the mark
    member x.Line = _line

    /// The column of the mark
    member x.Column = _column

/// When maintaining the caret column for motion moves this represents the desired 
/// column to jump to if there is enough space on the line
///
[<RequireQualifiedAccess>]
[<NoComparison>]
[<NoEquality>]
type MaintainCaretColumn = 

    /// There is no saved caret column. 
    | None

    /// This number is kept as a count of spaces.  Tabs need to be adjusted for when applying
    /// this setting to a motion
    | Spaces of Count: int

    /// The caret was moved with the $ motion and the further moves should move to the end of 
    /// the line 
    | EndOfLine

    with 

    /// Whether we are maintaining end-of-line
    member x.IsMaintainingEndOfLine = 
        match x with
        | EndOfLine -> true
        | _ -> false

type IVimHost =

    /// Should vim automatically start synchronization of IVimBuffer instances when they are 
    /// created
    abstract AutoSynchronizeSettings: bool 

    /// What settings defaults should be used when there is no vimrc file present
    abstract DefaultSettings: DefaultSettings

    /// Is auto-command enabled for this host
    abstract IsAutoCommandEnabled: bool

    /// Is undo / redo expected at this point in time due to a host operation.
    abstract IsUndoRedoExpected: bool 

    /// Get the count of window tabs that are active in the host. This refers to tabs for actual 
    /// edit windows, not anything to do with tabs in the text file.  If window tabs are not supported 
    /// then -1 should be returned
    abstract TabCount: int

    /// Whether to use the Visual Studio caret or the VsVim caret in insert mode
    abstract UseDefaultCaret: bool

    /// Ensure that the VsVim package is loaded
    abstract EnsurePackageLoaded: unit -> unit

    abstract Beep: unit -> unit

    /// Called at the start of a bulk operation such as a macro replay or a repeat of
    /// a last command
    abstract BeginBulkOperation: unit -> unit

    /// Close the provided view
    abstract Close: ITextView -> unit

    /// Close all tabs but this one
    abstract CloseAllOtherTabs: ITextView -> unit

    /// Close all windows but this one within this tab
    abstract CloseAllOtherWindows: ITextView -> unit

    /// Create a hidden ITextView instance.  This is primarily used to load the contents
    /// of the vimrc
    abstract CreateHiddenTextView: unit -> ITextView

    /// Perform the specified action when the specified text view is ready
    abstract DoActionWhenTextViewReady: action: (unit -> unit) -> textView: ITextView -> unit

    /// Called at the end of a bulk operation such as a macro replay or a repeat of
    /// a last command
    abstract EndBulkOperation: unit -> unit

    /// Ensure that the given point is visible
    abstract EnsureVisible: textView: ITextView -> point: SnapshotPoint -> unit

    /// Perform the "find in files" operation
    abstract FindInFiles: pattern: string -> matchCase: bool -> filesOfType: string -> flags: VimGrepFlags -> action: (unit -> unit) -> unit

    /// Format the provided lines
    abstract FormatLines: textView: ITextView -> range: SnapshotLineRange -> unit

    /// Get the ITextView which currently has keyboard focus
    abstract GetFocusedTextView: unit -> ITextView option

    /// Get the tab index of the tab containing the given ITextView.  A number less
    /// than 0 indicates the value couldn't be determined
    abstract GetTabIndex: textView: ITextView -> int

    /// Get the indent for the new line.  This has precedence over the 'autoindent'
    /// setting
    abstract GetNewLineIndent: textView: ITextView -> contextLine: ITextSnapshotLine -> newLine: ITextSnapshotLine -> localSettings: IVimLocalSettings -> int option

    /// Get the WordWrap style which should be used for the specified ITextView if word 
    /// wrap is enabled
    abstract GetWordWrapStyle: textView: ITextView -> WordWrapStyles

    /// Go to the definition of the value under the cursor
    abstract GoToDefinition: unit -> bool

    /// Peek at the definition of the value under the cursor
    abstract PeekDefinition: unit -> bool

    /// Go to the local declaration of the value under the cursor
    abstract GoToLocalDeclaration: textView: ITextView -> identifier: string -> bool

    /// Go to the local declaration of the value under the cursor
    abstract GoToGlobalDeclaration: tetxView: ITextView -> identifier: string -> bool

    /// Go to the nth tab in the tab list.  This value is always a 0 based index 
    /// into the set of tabs.  It does not correspond to vim's handling of tab
    /// values which is not a standard 0 based index
    abstract GoToTab: index: int -> unit

    /// Go to the specified item in the specified list
    abstract NavigateToListItem: listKind: ListKind -> navigationKind: NavigationKind -> argument: int option -> hasBang: bool -> ListItem option

    /// Get the name of the given ITextBuffer
    abstract GetName: textBuffer: ITextBuffer -> string

    /// Is the ITextBuffer in a dirty state?
    abstract IsDirty: textBuffer: ITextBuffer -> bool

    /// Is the ITextBuffer read only
    abstract IsReadOnly: textBuffer: ITextBuffer -> bool

    /// Is the ITextView visible to the user
    abstract IsVisible: textView: ITextView -> bool

    /// Is the ITextView in focus
    abstract IsFocused: textView: ITextView -> bool

    /// Loads the new file into the existing window
    abstract LoadFileIntoExistingWindow: filePath: string -> textView: ITextView -> bool

    /// Load a file into a new window, optionally moving the caret to the first
    /// non-blank on a specific line or to a specific line and column
    abstract LoadFileIntoNewWindow: filePath: string -> line: int option -> column: int option -> ITextView option

    /// Run the host specific make operation
    abstract Make: jumpToFirstError: bool -> arguments: string -> unit

    /// Move the focus to the ITextView in the open document in the specified direction
    abstract GoToWindow: textView: ITextView -> direction: WindowKind -> count: int -> unit

    abstract NavigateTo: point: VirtualSnapshotPoint -> bool

    // Open the specified kind of list window (:cwindow, :lwindow)
    abstract OpenListWindow: listKind: ListKind -> unit

    /// Open the the specified link
    abstract OpenLink: link: string -> bool

    /// Quit the application
    abstract Quit: unit -> unit

    /// Reload the contents of the ITextView discarding any changes
    abstract Reload: textView: ITextView -> bool

    /// Run the specified command with the given arguments and return the textual
    /// output
    abstract RunCommand: workingDirectory: string -> file: string -> arguments: string -> input: string -> RunCommandResults

    /// Run C# Script
    abstract RunCSharpScript: vimBuffer:IVimBuffer -> callInfo: CallInfo -> createEachTime: bool -> unit

    /// Run the Visual studio command in the context of the given ITextView
    abstract RunHostCommand: textView: ITextView -> commandName: string -> argument: string -> unit

    /// Save the provided ITextBuffer instance
    abstract Save: textBuffer: ITextBuffer -> bool 

    /// Save the current document as a new file with the specified name
    abstract SaveTextAs: text: string -> filePath: string -> bool 

    /// Called by Vim when it encounters a new ITextView and needs to know if it should 
    /// create an IVimBuffer for it
    abstract ShouldCreateVimBuffer: textView: ITextView -> bool

    /// Called by Vim when it is loading vimrc files.  This gives the host the chance to
    /// filter out vimrc files it doesn't want to consider
    abstract ShouldIncludeRcFile: vimRcPath: VimRcPath -> bool

    /// Split the views horizontally
    abstract SplitViewHorizontally: ITextView -> unit

    /// Split the views horizontally
    abstract SplitViewVertically: ITextView -> unit

    /// Start a shell window
    abstract StartShell: workingDirectory: string -> file: string -> arguments: string -> unit

    /// Called when IVim is fully created.  This callback gives the host the oppurtunity
    /// to customize various aspects of vim including IVimGlobalSettings, IVimData, etc ...
    abstract VimCreated: vim: IVim -> unit

    /// Called when VsVim attempts to load the user _vimrc file.  If the load succeeded 
    /// then the resulting settings are passed into the method.  If the load failed it is 
    /// the defaults.  Either way, they are the default settings used for new buffers
    abstract VimRcLoaded: vimRcState: VimRcState -> localSettings: IVimLocalSettings -> windowSettings: IVimWindowSettings -> unit

    /// Allow the host to custom process the insert command.  Hosts often have
    /// special non-vim semantics for certain types of edits (Enter for 
    /// example).  This override allows them to do this processing
    abstract TryCustomProcess: textView: ITextView -> command: InsertCommand -> bool

    /// Raised when the visibility of an ITextView changes
    [<CLIEvent>]
    abstract IsVisibleChanged: IDelegateEvent<System.EventHandler<TextViewEventArgs>>

    /// Raised when the active ITextView changes
    [<CLIEvent>]
    abstract ActiveTextViewChanged: IDelegateEvent<System.EventHandler<TextViewChangedEventArgs>>

    /// Raised before an ITextBuffer is saved
    [<CLIEvent>]
    abstract BeforeSave: IDelegateEvent<System.EventHandler<BeforeSaveEventArgs>>

/// Core parts of an IVimBuffer.  Used for components which make up an IVimBuffer but
/// need the same data provided by IVimBuffer.
and IVimBufferData =

    /// The current caret index
    abstract CaretIndex: int with get, set

    /// The caret register map
    abstract CaretRegisterMap: IRegisterMap

    /// The current directory for this particular buffer
    abstract CurrentDirectory: string option with get, set

    /// The current (rooted) file path for this buffer
    abstract CurrentFilePath : string option

    /// The current file path for this buffer, relative to CurrentDirectory
    abstract CurrentRelativeFilePath : string option
    
    /// The working directory to use for this particular buffer,
    /// either the current directory for the buffer, if set, or the
    /// global current directory
    abstract WorkingDirectory: string

    /// This is the caret point at the start of the most recent visual mode session. It's
    /// the actual location of the caret vs. the anchor point.
    abstract VisualCaretStartPoint: ITrackingPoint option with get, set

    /// This is the anchor point for the visual mode selection.  It is different than the anchor
    /// point in ITextSelection.  The ITextSelection anchor point is always the start or end
    /// of the visual selection.  While the anchor point for visual mode selection may be in 
    /// the middle (in say line wise mode)
    abstract VisualAnchorPoint: ITrackingPoint option with get, set

    /// The last multi-selection recorded for this buffer
    abstract LastMultiSelection: (ModeKind * SelectionSpan array) option with get, set

    /// The currently maintained caret column for up / down caret movements in
    /// the buffer
    abstract MaintainCaretColumn: MaintainCaretColumn with get, set

    /// The IJumpList associated with the IVimBuffer
    abstract JumpList: IJumpList

    /// The ITextView associated with the IVimBuffer
    abstract TextView: ITextView

    /// The ITextBuffer associated with the IVimBuffer
    abstract TextBuffer: ITextBuffer

    /// The IStatusUtil associated with the IVimBuffer
    abstract StatusUtil: IStatusUtil

    /// The IUndoRedOperations associated with the IVimBuffer
    abstract UndoRedoOperations: IUndoRedoOperations

    /// The IVimTextBuffer associated with the IVimBuffer
    abstract VimTextBuffer: IVimTextBuffer

    /// The IVimWindowSettings associated with the ITextView 
    abstract WindowSettings: IVimWindowSettings

    /// The IWordUtil associated with the IVimBuffer
    abstract WordUtil: WordUtil

    /// The ISelectionUtil associated with the IVimBuffer
    abstract SelectionUtil: ISelectionUtil

    /// The IVimLocalSettings associated with the ITextBuffer
    abstract LocalSettings: IVimLocalSettings

    abstract Vim: IVim

/// Vim instance.  Global for a group of buffers
and IVim =

    /// Buffer actively processing input.  This has no relation to the IVimBuffer
    /// which has focus 
    abstract ActiveBuffer: IVimBuffer option

    /// The IStatusUtil for the active IVimBuffer.  If there is currently no active IVimBuffer
    /// then a silent one will be returned 
    abstract ActiveStatusUtil: IStatusUtil

    /// Whether to auto load digraphs
    abstract AutoLoadDigraphs: bool with get, set

    /// Whether or not the vimrc file should be autoloaded before the first IVimBuffer
    /// is created
    abstract AutoLoadVimRc: bool with get, set

    /// Whether or not saved data like macros shuold be autoloaded before the first IVimBuffer 
    // is created
    abstract AutoLoadSessionData: bool with get, set

    /// Get the set of tracked IVimBuffer instances
    abstract VimBuffers: IVimBuffer list

    /// Get the IVimBuffer which currently has KeyBoard focus
    abstract FocusedBuffer: IVimBuffer option

    /// Is Vim currently disabled 
    abstract IsDisabled: bool with get, set

    /// In the middle of a bulk operation such as a macro replay or repeat last command
    abstract InBulkOperation: bool

    /// Digraph map for this IVim instance
    abstract DigraphMap: IDigraphMap

    /// IMacroRecorder for the IVim instance
    abstract MacroRecorder: IMacroRecorder

    /// IMarkMap for the IVim instance
    abstract MarkMap: IMarkMap

    /// IRegisterMap for the IVim instance
    abstract RegisterMap: IRegisterMap

    /// ISearchService for this IVim instance
    abstract SearchService: ISearchService

    abstract GlobalAbbreviationMap: IVimGlobalAbbreviationMap

    /// IVimGlobalKeyMap for this IVim instance
    abstract GlobalKeyMap: IVimGlobalKeyMap

    /// IVimGlobalSettings for this IVim instance
    abstract GlobalSettings: IVimGlobalSettings

    /// The variable map for this IVim instance
    abstract VariableMap: VariableMap

    abstract VimData: IVimData 

    abstract VimHost: IVimHost

    /// The state of the VimRc file
    abstract VimRcState: VimRcState

    /// Create an IVimBuffer for the given ITextView
    abstract CreateVimBuffer: textView: ITextView -> IVimBuffer

    /// Create an IVimBuffer for the specified iVimBufferData
    abstract CreateVimBufferWithData: vimBufferData: IVimBufferData -> IVimBuffer

    /// Create an IVimTextBuffer for the given ITextBuffer
    abstract CreateVimTextBuffer: textBuffer: ITextBuffer -> IVimTextBuffer

    /// Close all IVimBuffer instances in the system
    abstract CloseAllVimBuffers: unit -> unit

    /// Get the IVimInterpreter for the specified IVimBuffer
    abstract GetVimInterpreter: vimBuffer: IVimBuffer -> IVimInterpreter

    /// Get or create an IVimBuffer for the given ITextView
    abstract GetOrCreateVimBuffer: textView: ITextView -> IVimBuffer

    /// Get or create an IVimTextBuffer for the given ITextBuffer
    abstract GetOrCreateVimTextBuffer: textBuffer: ITextBuffer -> IVimTextBuffer

    /// Load the VimRc file.  If the file was previously loaded a new load will be 
    /// attempted.  Returns true if a VimRc was actually loaded.
    abstract LoadVimRc: unit -> VimRcState

    /// Load the saved data file. 
    abstract LoadSessionData: unit -> unit

    /// Save out the current session data.
    abstract SaveSessionData: unit -> unit

    /// Remove the IVimBuffer associated with the given view.  This will not actually close
    /// the IVimBuffer but instead just removes it's association with the given view
    abstract RemoveVimBuffer: ITextView -> bool

    /// Get the IVimBuffer associated with the given ITextView
    abstract TryGetVimBuffer: textView: ITextView * vimBuffer: outref<IVimBuffer> -> bool

    /// Get the IVimTextBuffer associated with the given ITextBuffer
    abstract TryGetVimTextBuffer: textBuffer: ITextBuffer * vimTextBuffer: outref<IVimTextBuffer> -> bool

    /// This implements a cached version of IVimHost::ShouldCreateVimBuffer.  Code should prefer 
    /// this method wherever possible 
    abstract ShouldCreateVimBuffer: textView: ITextView -> bool

    /// Get or create an IVimBuffer for the given ITextView.  The creation of the IVimBuffer will
    /// only occur if the host returns true from IVimHost::ShouldCreateVimBuffer.  
    ///
    /// MEF component load ordering isn't defined and it's very possible that components like the 
    /// ITagger implementations will be called long before the host has a chance to create the 
    /// IVimBuffer instance.  This method removes the ordering concerns and maintains control of 
    /// creation in the IVimHost
    abstract TryGetOrCreateVimBufferForHost: textView: ITextView * vimBuffer: outref<IVimBuffer> -> bool

    /// Get the nth most recent IVimBuffer
    abstract TryGetRecentBuffer: n: int -> IVimBuffer option

and BeforeSaveEventArgs
    (
        _textBuffer: ITextBuffer
    ) =
    inherit System.EventArgs()

    member x.TextBuffer = _textBuffer

and SwitchModeKindEventArgs
    (
        _modeKind: ModeKind,
        _modeArgument: ModeArgument
    ) =
    inherit System.EventArgs()

    member x.ModeKind = _modeKind

    member x.ModeArgument = _modeArgument

and SwitchModeEventArgs 
    (
        _previousMode: IMode,
        _currentMode: IMode,
        _modeArgument: ModeArgument
    ) = 

    inherit System.EventArgs()

    /// Current IMode 
    member x.CurrentMode = _currentMode

    /// Previous IMode
    member x.PreviousMode = _previousMode

    /// Mode argument for the current mode
    member x.ModeArgument = _modeArgument

and IMarkMap =

    /// The set of active global marks
    abstract GlobalMarks: (Letter * VirtualSnapshotPoint) seq

    /// Get the mark for the given char for the IVimTextBuffer
    abstract GetMark: mark: Mark -> vimBufferData: IVimBufferData -> VirtualSnapshotPoint option

    /// Get the mark info for the given mark for the IVimTextBuffer
    abstract GetMarkInfo: mark: Mark -> vimBufferData: IVimBufferData -> MarkInfo option

    /// Get the current value of the specified global mark
    abstract GetGlobalMark: letter: Letter -> VirtualSnapshotPoint option

    /// Set the global mark to the given line and column in the provided IVimTextBuffer
    abstract SetGlobalMark: letter: Letter -> vimTextBuffer: IVimTextBuffer -> lineNumber: int -> offset: int -> unit

    /// Set the mark for the given char for the IVimTextBuffer
    abstract SetMark: mark: Mark -> vimBufferData: IVimBufferData -> lineNumber: int -> offset: int -> bool

    /// Delete the mark for the IVimTextBuffer
    abstract DeleteMark: mark: Mark -> vimBufferData: IVimBufferData -> bool

    /// Unload the buffer recording the last exited position
    abstract UnloadBuffer: vimBufferData: IVimBufferData -> name: string -> lineNumber: int -> offset: int -> bool

    /// Reload the marks associated with a buffer
    abstract ReloadBuffer: vimBufferData: IVimBufferData -> name: string -> bool

    /// Remove the specified mark and return whether or not a mark was actually
    /// removed
    abstract RemoveGlobalMark: letter: Letter -> bool

    /// Delete all of the global marks 
    abstract Clear: unit -> unit

    /// Raised when a mark is set
    [<CLIEvent>]
    abstract MarkSet: IDelegateEvent<System.EventHandler<MarkTextBufferEventArgs>>

    /// Raised when a mark is deleted
    [<CLIEvent>]
    abstract MarkDeleted: IDelegateEvent<System.EventHandler<MarkTextBufferEventArgs>>

/// This is the interface which represents the parts of a vim buffer which are shared amongst all
/// of it's views
and IVimTextBuffer = 

    /// The associated ITextBuffer instance
    abstract TextBuffer: ITextBuffer

    /// The associated IVimGlobalSettings instance
    abstract GlobalSettings: IVimGlobalSettings

    /// The associated IVimGlobalAbbreviationMap instance
    abstract GlobalAbbreviationMap: IVimGlobalAbbreviationMap

    /// The 'start' point of the current insert session.  This is relevant for settings like 
    /// 'backspace'
    abstract InsertStartPoint: SnapshotPoint option with get, set

    /// True when 'softtabstop' setting should be considered during backspace operations in insert
    /// mode
    abstract IsSoftTabStopValidForBackspace: bool with get, set

    /// The point the caret occupied when Insert mode was exited 
    abstract LastInsertExitPoint: SnapshotPoint option with get, set

    /// The last VisualSpan selection for the IVimTextBuffer.  This is a combination of a VisualSpan
    /// and the SnapshotPoint within the span where the caret should be positioned
    abstract LastVisualSelection: VisualSelection option with get, set

    /// The point the caret occupied when the last edit occurred
    abstract LastEditPoint: SnapshotPoint option with get, set

    /// The start point of the last change or yank
    abstract LastChangeOrYankStart: SnapshotPoint option with get, set

    /// The end point of the last change or yank
    abstract LastChangeOrYankEnd: SnapshotPoint option with get, set

    /// If we are in the middle of processing a "one time command" (<c-o>) then this will
    /// hold the ModeKind which will be switched back to after it's completed
    abstract InOneTimeCommand: ModeKind option with get, set
    
    /// True if we are processing a "one time command" initiated from a select mode,
    /// or from a select mode initiated from within another "one time command", e.g. "(insert) SELECT".
    abstract InSelectModeOneTimeCommand: bool with get, set

    /// The set of active local marks in the ITextBuffer
    abstract LocalMarks: (LocalMark * VirtualSnapshotPoint) seq

    /// The associated IVimLocalSettings instance
    abstract LocalSettings: IVimLocalSettings

    /// The associated abbreviation map
    abstract LocalAbbreviationMap: IVimLocalAbbreviationMap

    /// The associated key map
    abstract LocalKeyMap: IVimLocalKeyMap

    /// ModeKind of the current mode of the IVimTextBuffer.  It may seem odd at first to put ModeKind
    /// at this level but it is indeed shared amongst all views.  This can be demonstrated by opening
    /// the same file in multiple tabs, switch to insert in one and then move to the other via the
    /// mouse and noting it is also in Insert mode.  Actual IMode values are ITextView specific though
    /// and only live at the ITextView level
    abstract ModeKind: ModeKind

    /// Name of the buffer.  Used for items like Marks
    abstract Name: string

    /// The IUndoRedoOperations associated with the IVimTextBuffer
    abstract UndoRedoOperations: IUndoRedoOperations

    /// The associated IVim instance
    abstract Vim: IVim

    /// The WordUtil for this IVimTextBuffer
    abstract WordUtil: WordUtil

    /// The ITextStructureNavigator for word values in the ITextBuffer
    abstract WordNavigator: ITextStructureNavigator

    /// Whether to use virtual space
    abstract UseVirtualSpace: bool

    /// Check the contents of the buffer for a modeline, returning a tuple of
    /// the line we used as a modeline, if any, and a string representing the
    /// first sub-option that produced an error if any
    abstract CheckModeLine: WindowSettings: IVimWindowSettings -> string option * string option

    /// Clear out all of the cached information in the IVimTextBuffer.  It will reset to it's startup
    /// state 
    abstract Clear: unit -> unit

    /// Get the local mark value 
    abstract GetLocalMark: localMark: LocalMark -> VirtualSnapshotPoint option

    /// Set the local mark value to the specified line and column.  Returns false if the given 
    /// mark cannot be set
    abstract SetLocalMark: localMark: LocalMark -> lineNumber: int -> offset: int -> bool

    /// Remove the specified local mark.  Returns whether a mark was actually removed
    abstract RemoveLocalMark: localMark: LocalMark -> bool

    /// Switch the current mode to the provided value
    abstract SwitchMode: ModeKind -> ModeArgument -> unit

    /// Raised when the mode is switched.  Returns the old and new mode 
    [<CLIEvent>]
    abstract SwitchedMode: IDelegateEvent<System.EventHandler<SwitchModeKindEventArgs>>

    /// Raised when a mark is set
    [<CLIEvent>]
    abstract MarkSet: IDelegateEvent<System.EventHandler<MarkTextBufferEventArgs>>

/// Main interface for the Vim editor engine so to speak. 
and IVimBuffer =

    /// Sequence of available Modes
    abstract AllModes: seq<IMode>

    /// Buffered KeyInput list.  When a key remapping has multiple source elements the input 
    /// is buffered until it is completed or the ambiguity is removed.  
    abstract BufferedKeyInputs: KeyInput list

    /// The current directory for this particular buffer
    abstract CurrentDirectory: string option with get, set

    /// The ICommandUtil for this IVimBuffer
    abstract CommandUtil: ICommandUtil

    /// Global settings for the buffer
    abstract GlobalSettings: IVimGlobalSettings

    /// IIncrementalSearch instance associated with this IVimBuffer
    abstract IncrementalSearch: IIncrementalSearch

    /// Whether or not the IVimBuffer is currently processing a KeyInput value
    abstract IsProcessingInput: bool

    /// Whether or not the IVimBuffer is currently switching modes
    abstract IsSwitchingMode: bool

    /// Is this IVimBuffer instance closed
    abstract IsClosed: bool

    /// Jump list
    abstract JumpList: IJumpList

    /// The last status message produced, if any
    abstract LastMessage: string option

    /// Local settings for the buffer
    abstract LocalSettings: IVimLocalSettings

    /// The associated abbreviation map
    abstract LocalAbbreviationMap: IVimLocalAbbreviationMap

    /// The associated key map
    abstract LocalKeyMap: IVimLocalKeyMap

    /// Associated IMarkMap
    abstract MarkMap: IMarkMap

    /// Current mode of the buffer
    abstract Mode: IMode

    /// ModeKind of the current IMode in the buffer
    abstract ModeKind: ModeKind

    /// Name of the buffer.  Used for items like Marks
    abstract Name: string

    /// If we are in the middle of processing a "one time command" (<c-o>) then this will
    /// hold the ModeKind which will be switched back to after it's completed
    abstract InOneTimeCommand: ModeKind option

    /// Register map for IVim.  Global to all IVimBuffer instances but provided here
    /// for convenience
    abstract RegisterMap: IRegisterMap

    /// Underlying ITextBuffer Vim is operating under
    abstract TextBuffer: ITextBuffer

    /// Current ITextSnapshot of the ITextBuffer
    abstract TextSnapshot: ITextSnapshot

    /// View of the file
    abstract TextView: ITextView

    /// The IMotionUtil associated with this IVimBuffer instance
    abstract MotionUtil: IMotionUtil

    /// The IUndoRedoOperations associated with this IVimBuffer instance
    abstract UndoRedoOperations: IUndoRedoOperations

    /// Owning IVim instance
    abstract Vim: IVim

    /// Associated IVimTextBuffer
    abstract VimTextBuffer: IVimTextBuffer

    /// VimBufferData for the given IVimBuffer
    abstract VimBufferData: IVimBufferData

    /// The ITextStructureNavigator for word values in the buffer
    abstract WordNavigator: ITextStructureNavigator

    /// Associated IVimWindowSettings
    abstract WindowSettings: IVimWindowSettings

    /// Associated IVimData instance
    abstract VimData: IVimData

    /// INormalMode instance for normal mode
    abstract NormalMode: INormalMode

    /// ICommandMode instance for command mode
    abstract CommandMode: ICommandMode 

    /// IDisabledMode instance for disabled mode
    abstract DisabledMode: IDisabledMode

    /// IVisualMode for visual character mode
    abstract VisualCharacterMode: IVisualMode

    /// IVisualMode for visual line mode
    abstract VisualLineMode: IVisualMode

    /// IVisualMode for visual block mode
    abstract VisualBlockMode: IVisualMode

    /// IInsertMode instance for insert mode
    abstract InsertMode: IInsertMode

    /// IInsertMode instance for replace mode
    abstract ReplaceMode: IInsertMode

    /// ISelectMode instance for character mode
    abstract SelectCharacterMode: ISelectMode

    /// ISelectMode instance for line mode
    abstract SelectLineMode: ISelectMode

    /// ISelectMode instance for block mode
    abstract SelectBlockMode: ISelectMode

    /// ISubstituteConfirmDoe instance for substitute confirm mode
    abstract SubstituteConfirmMode: ISubstituteConfirmMode

    /// IMode instance for external edits
    abstract ExternalEditMode: IMode

    /// Get the register of the given name
    abstract GetRegister: RegisterName -> Register

    /// Get the specified Mode
    abstract GetMode: ModeKind -> IMode

    /// Get the KeyInput value produced by this KeyInput in the current state of the
    /// IVimBuffer.  This will consider any buffered KeyInput values.
    abstract GetKeyInputMapping: keyInput: KeyInput -> KeyMappingResult

    /// Process the KeyInput and return whether or not the input was completely handled
    abstract Process: KeyInput -> ProcessResult

    /// Process all of the buffered KeyInput values.
    abstract ProcessBufferedKeyInputs: unit -> unit

    /// Can the passed in KeyInput be processed by the current state of IVimBuffer.  The
    /// provided KeyInput will participate in remapping based on the current mode
    abstract CanProcess: KeyInput -> bool

    /// Can the passed in KeyInput be processed as a Vim command by the current state of
    /// the IVimBuffer.  The provided KeyInput will participate in remapping based on the
    /// current mode
    ///
    /// This is very similar to CanProcess except it will return false for any KeyInput
    /// which would be processed as a direct insert.  In other words commands like 'a',
    /// 'b' when handled by insert / replace mode
    abstract CanProcessAsCommand: KeyInput -> bool

    /// Switch the current mode to the provided value
    abstract SwitchMode: ModeKind -> ModeArgument -> IMode

    /// Switch the buffer back to the previous mode which is returned
    abstract SwitchPreviousMode: unit -> IMode

    /// Add a processed KeyInput value.  This is a way for a host which is intercepting 
    /// KeyInput and custom processing it to still participate in items like Macro 
    /// recording.  The provided value will not go through any remapping
    abstract SimulateProcessed: KeyInput -> unit

    /// Called when the view is closed and the IVimBuffer should uninstall itself
    /// and it's modes
    abstract Close: unit -> unit
    
    /// Whether the buffer is readonly
    abstract IsReadOnly: bool with get

    /// Raised when the mode is switched.  Returns the old and new mode 
    [<CLIEvent>]
    abstract SwitchedMode: IDelegateEvent<System.EventHandler<SwitchModeEventArgs>>

    /// Raised when a KeyInput is received by the buffer.  This will be raised for the 
    /// KeyInput which was received and does not consider any mappings
    [<CLIEvent>]
    abstract KeyInputStart: IDelegateEvent<System.EventHandler<KeyInputStartEventArgs>>

    /// This is raised just before the IVimBuffer attempts to process a KeyInput 
    /// value.  This will not necessarily be the KeyInput which was raised in KeyInputStart
    /// because a mapping could have changed it to one or many other different KeyInput
    /// values.  
    ///
    /// If this event is marked as Handled then the KeyInput will never actually be 
    /// processed by the IVimBuffer.  It will instead immediately move to the 
    /// KeyInputProcessed event
    [<CLIEvent>]
    abstract KeyInputProcessing: IDelegateEvent<System.EventHandler<KeyInputStartEventArgs>>

    /// Raised when a key is processed.  This is raised when the KeyInput is actually
    /// processed by Vim, not when it is received.  
    ///
    /// Typically this occurs immediately after a Start command and is followed by an
    /// End command.  One case this doesn't happen is in a key remapping where the source 
    /// mapping contains more than one key.  In this case the input is buffered until the 
    /// second key is read and then the inputs are processed
    [<CLIEvent>]
    abstract KeyInputProcessed: IDelegateEvent<System.EventHandler<KeyInputProcessedEventArgs>>

    /// Raised when a key is received but not immediately processed.  Occurs when a
    /// key remapping has more than one source key strokes
    [<CLIEvent>]
    abstract KeyInputBuffered: IDelegateEvent<System.EventHandler<KeyInputSetEventArgs>>

    /// Raised when a KeyInput is completed processing within the IVimBuffer.  This happens 
    /// if the KeyInput is buffered or processed.  This will be raised for the KeyInput which
    /// was initially considered (came from KeyInputStart).  It won't consider any mappings
    [<CLIEvent>]
    abstract KeyInputEnd: IDelegateEvent<System.EventHandler<KeyInputEventArgs>>

    /// Raised when a warning is encountered
    [<CLIEvent>]
    abstract WarningMessage: IDelegateEvent<System.EventHandler<StringEventArgs>>

    /// Raised when an error is encountered
    [<CLIEvent>]
    abstract ErrorMessage: IDelegateEvent<System.EventHandler<StringEventArgs>>

    /// Raised when a status message is encountered
    [<CLIEvent>]
    abstract StatusMessage: IDelegateEvent<System.EventHandler<StringEventArgs>>

    /// Raised when the IVimBuffer is being closed
    [<CLIEvent>]
    abstract Closing: IDelegateEvent<System.EventHandler>

    /// Raised when the IVimBuffer is closed
    [<CLIEvent>]
    abstract Closed: IDelegateEvent<System.EventHandler>

    /// Raised after the buffer is closed AND all pending KeyInputEnd events have been raised.
    /// A buffer can be closed internally (i.e. while processing its own keyboard input), which will raise Closed -> KeyInputEnd -> PostClosed.
    /// It can also be closed externally, which will raise Closed -> PostClosed.
    [<CLIEvent>]
    abstract PostClosed: IDelegateEvent<System.EventHandler>
    
    inherit IPropertyOwner

/// Interface for a given Mode of Vim.  For example normal, insert, etc ...
and IMode =

    /// Associated IVimTextBuffer
    abstract VimTextBuffer: IVimTextBuffer 

    /// What type of Mode is this
    abstract ModeKind: ModeKind

    /// Sequence of commands handled by the Mode.  
    abstract CommandNames: seq<KeyInputSet>

    /// Can the mode process this particular KeyIput at the current time
    abstract CanProcess: KeyInput -> bool

    /// Process the given KeyInput
    abstract Process: KeyInputData -> ProcessResult

    /// Called when the mode is entered
    abstract OnEnter: ModeArgument -> unit

    /// Called when the mode is left
    abstract OnLeave: unit -> unit

    /// Called when the owning IVimBuffer is closed so that the mode can free up 
    /// any resources including event handlers
    abstract OnClose: unit -> unit

and INormalMode =

    /// Buffered input for the current command
    abstract Command: string 

    /// The ICommandRunner implementation associated with NormalMode
    abstract CommandRunner: ICommandRunner 

    /// Mode keys need to be remapped with currently
    abstract KeyRemapMode: KeyRemapMode

    /// Is normal mode in the middle of a count operation
    abstract InCount: bool

    /// Is normal mode in the middle of a character replace operation
    abstract InReplace: bool

    inherit IMode

/// This is the interface implemented by Insert and Replace mode
and IInsertMode =

    /// The active IWordCompletionSession if one is active
    abstract ActiveWordCompletionSession: IWordCompletionSession option

    /// The paste indication character
    abstract PasteCharacter: char option

    /// Is insert mode currently in a paste operation
    abstract IsInPaste: bool

    /// Is this KeyInput value considered to be a direct insert command in the current
    /// state of the IVimBuffer.  This does not apply to commands which edit the buffer
    /// like 'CTRL-D' but instead commands like 'a', 'b' which directly edit the 
    /// ITextBuffer
    abstract IsDirectInsert: KeyInput -> bool

    /// Raised when a command is successfully run
    [<CLIEvent>]
    abstract CommandRan: IDelegateEvent<System.EventHandler<CommandRunDataEventArgs>>

    inherit IMode

and ICommandMode = 

    /// Buffered editable input for the current command
    abstract EditableCommand: EditableCommand with get, set

    /// The text of the current command
    abstract Command: string with get, set

    /// Is command mode currently waiting for a register paste operation to complete
    abstract InPasteWait: bool

    /// Run the specified command
    abstract RunCommand: string -> unit

    /// Raised when the command string is changed
    [<CLIEvent>]
    abstract CommandChanged: IDelegateEvent<System.EventHandler>

    /// Raised when the command is run
    [<CLIEvent>]
    abstract CommandRan: IDelegateEvent<System.EventHandler<CommandEventArgs>>

    inherit IMode

and IVisualMode = 

    /// The ICommandRunner implementation associated with NormalMode
    abstract CommandRunner: ICommandRunner 

    /// Mode keys need to be remapped with currently
    abstract KeyRemapMode: KeyRemapMode 

    /// Is visual mode in the middle of a count operation
    abstract InCount: bool

    /// The current Visual Selection
    abstract VisualSelection: VisualSelection

    /// Asks Visual Mode to reset what it perceives to be the original selection.  Instead it 
    /// views the current selection as the original selection for entering the mode
    abstract SyncSelection: unit -> unit

    inherit IMode 
    
and IDisabledMode =
    
    /// Help message to display 
    abstract HelpMessage: string 

    inherit IMode

and ISelectMode = 

    /// Sync the selection with the current state
    abstract SyncSelection: unit -> unit

    inherit IMode

and ISubstituteConfirmMode =

    /// The SnapshotSpan of the current matching piece of text
    abstract CurrentMatch: SnapshotSpan option

    /// The string which will replace the current match
    abstract CurrentSubstitute: string option

    /// Raised when the current match changes
    [<CLIEvent>]
    abstract CurrentMatchChanged: IEvent<SnapshotSpan option> 

    inherit IMode 

[<Extension>]
module VimExtensions = 
    
    /// Is this ModeKind any type of Insert: Insert or Replace
    [<Extension>]
    let IsAnyInsert modeKind = 
        modeKind = ModeKind.Insert ||
        modeKind = ModeKind.Replace

    /// Is this ModeKind any type of Visual
    [<Extension>]
    let IsAnyVisual modeKind = 
        modeKind = ModeKind.VisualCharacter ||
        modeKind = ModeKind.VisualLine ||
        modeKind = ModeKind.VisualBlock

    /// Is this ModeKind any type of Select
    [<Extension>]
    let IsAnySelect modeKind = 
        modeKind = ModeKind.SelectCharacter ||
        modeKind = ModeKind.SelectLine ||
        modeKind = ModeKind.SelectBlock

module internal VimCoreExtensions =
    
    type IVim with
        member x.GetVimBuffer textView =
            let found, vimBuffer = x.TryGetVimBuffer textView
            if found then
                Some vimBuffer
            else
                None

        member x.GetOrCreateVimBufferForHost textView =
            let found, vimBuffer = x.TryGetOrCreateVimBufferForHost textView
            if found then
                Some vimBuffer
            else
                None

/// Alternate method of dispatching calls.  This wraps the Dispatcher type and will
/// gracefully handle dispatch errors.  Without this layer exceptions coming from a 
/// dispatched operation will go directly to the dispatch loop and crash the host
/// application
type IProtectedOperations = 

    /// Get an Action delegate which invokes the original action and handles any
    /// thrown Exceptions by passing them off the the available IExtensionErrorHandler
    /// values
    abstract member GetProtectedAction: action: Action -> Action

    /// Get an EventHandler delegate which invokes the original action and handles any
    /// thrown Exceptions by passing them off the the available IExtensionErrorHandler
    /// values
    abstract member GetProtectedEventHandler: eventHandler: EventHandler -> EventHandler

    /// Report an Exception to the IExtensionErrorHandlers
    abstract member Report: ex: Exception -> unit
