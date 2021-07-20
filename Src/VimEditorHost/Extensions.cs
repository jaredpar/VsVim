using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Vim.EditorHost;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Vim.Extensions;
using Vim.Interpreter;
using Xunit;
using Expression = Vim.Interpreter.Expression;
using Size = System.Windows.Size;
using Microsoft.VisualStudio.Text.Tagging;
using System.Threading;
using System.Threading.Tasks;
using Vim.UnitTest.Utilities;
using Xunit.Sdk;
using System.Windows.Threading;
using System.Diagnostics;
using Vim.UnitTest;

namespace Vim.EditorHost
{
    /// <summary>
    /// Contains extension methods common to all unit test
    /// </summary>
    public static partial class Extensions
    {
        #region CommandResult

        public static CommandResult.Completed AsCompleted(this CommandResult result)
        {
            Assert.True(result.IsCompleted);
            return (CommandResult.Completed)result;
        }

        #endregion

        #region CommandData

        public static RegisterName GetRegisterNameOrDefault(this CommandData commandData)
        {
            return commandData.RegisterName.IsSome()
                ? commandData.RegisterName.Value
                : RegisterName.Unnamed;
        }

        public static Register GetRegister(this CommandData commandData, IRegisterMap registerMap)
        {
            return registerMap.GetRegister(commandData.GetRegisterNameOrDefault());
        }

        #endregion

        #region MaintainCaretColumn

        internal static MaintainCaretColumn.Spaces AsSpaces(this MaintainCaretColumn result)
        {
            Assert.True(result.IsSpaces);
            return (MaintainCaretColumn.Spaces)result;
        }

        #endregion

        #region VisualSelection

        public static void SelectAndMoveCaret(this VisualSelection selection, ITextView textView)
        {
            selection.Select(textView);
            TextViewUtil.MoveCaretToVirtualPointRaw(textView, selection.GetCaretVirtualPoint(SelectionKind.Inclusive), MoveCaretFlags.EnsureOnScreen);
        }

        #endregion

        #region LineCommand

        /// <summary>
        /// LineCommand as AddAutoCommand
        /// </summary>
        public static LineCommand.AddAutoCommand AsAddAutoCommand(this LineCommand lineCommand)
        {
            return (LineCommand.AddAutoCommand)lineCommand;
        }

        /// <summary>
        /// LineCommand as ChangeDirectory
        /// </summary>
        public static LineCommand.ChangeDirectory AsChangeDirectory(this LineCommand lineCommand)
        {
            return (LineCommand.ChangeDirectory)lineCommand;
        }

        /// <summary>
        /// LineCommand as ChangeLocalDirectory
        /// </summary>
        public static LineCommand.ChangeLocalDirectory AsChangeLocalDirectory(this LineCommand lineCommand)
        {
            return (LineCommand.ChangeLocalDirectory)lineCommand;
        }

        /// <summary>
        /// LineCommand as Close
        /// </summary>
        public static LineCommand.Close AsClose(this LineCommand lineCommand)
        {
            return (LineCommand.Close)lineCommand;
        }

        /// <summary>
        /// LineCommand as Delete
        /// </summary>
        public static LineCommand.Delete AsDelete(this LineCommand lineCommand)
        {
            return (LineCommand.Delete)lineCommand;
        }

        /// <summary>
        /// LineCommand as If
        /// </summary>
        public static LineCommand.If AsIf(this LineCommand lineCommand)
        {
            return (LineCommand.If)lineCommand;
        }

        /// <summary>
        /// LineCommand as ParseError
        /// </summary>
        public static LineCommand.ParseError AsParseError(this LineCommand lineCommand)
        {
            return (LineCommand.ParseError)lineCommand;
        }

        /// <summary>
        /// LineCommand as ReadCommand
        /// </summary>
        public static LineCommand.ReadCommand AsReadCommand(this LineCommand lineCommand)
        {
            return (LineCommand.ReadCommand)lineCommand;
        }

        /// <summary>
        /// LineCommand as ReadFile
        /// </summary>
        public static LineCommand.ReadFile AsReadFile(this LineCommand lineCommand)
        {
            return (LineCommand.ReadFile)lineCommand;
        }

        /// <summary>
        /// LineCommand as Set
        /// </summary>
        public static LineCommand.Set AsSet(this LineCommand lineCommand)
        {
            return (LineCommand.Set)lineCommand;
        }

        /// <summary>
        /// LineCommand as Source
        /// </summary>
        public static LineCommand.Source AsSource(this LineCommand lineCommand)
        {
            return (LineCommand.Source)lineCommand;
        }

        /// <summary>
        /// LineCommand as Substitute
        /// </summary>
        public static LineCommand.Substitute AsSubstitute(this LineCommand lineCommand)
        {
            return (LineCommand.Substitute)lineCommand;
        }

        /// <summary>
        /// LineCommand as SubstituteRepeat
        /// </summary>
        public static LineCommand.SubstituteRepeat AsSubstituteRepeat(this LineCommand lineCommand)
        {
            return (LineCommand.SubstituteRepeat)lineCommand;
        }

        /// <summary>
        /// LineCommand as MapKeys
        /// </summary>
        public static LineCommand.MapKeys AsMapKeys(this LineCommand lineCommand)
        {
            return (LineCommand.MapKeys)lineCommand;
        }

        /// <summary>
        /// LineCommand as UnmapKeys
        /// </summary>
        public static LineCommand.UnmapKeys AsUnmapKeys(this LineCommand lineCommand)
        {
            return (LineCommand.UnmapKeys)lineCommand;
        }

        /// <summary>
        /// LineCommand as VimGrep
        /// </summary>
        public static LineCommand.VimGrep AsVimGrep(this LineCommand lineCommand)
        {
            return (LineCommand.VimGrep)lineCommand;
        }

        /// <summary>
        /// LineCommand as Write
        /// </summary>
        public static LineCommand.Write AsWrite(this LineCommand lineCommand)
        {
            return (LineCommand.Write)lineCommand;
        }

        /// <summary>
        /// LineCommand as WriteAll
        /// </summary>
        public static LineCommand.WriteAll AsWriteAll(this LineCommand lineCommand)
        {
            return (LineCommand.WriteAll)lineCommand;
        }

        /// <summary>
        /// LineCommand as NavigateToListItem
        /// </summary>
        public static LineCommand.NavigateToListItem AsNavigateToListItem(this LineCommand lineCommand)
        {
            return (LineCommand.NavigateToListItem)lineCommand;
        }

        #endregion

        #region VimResult<T>

        public static T AsResult<T>(this VimResult<T> vimResult)
        {
            return ((VimResult<T>.Result)vimResult).Result;
        }

        public static string AsError<T>(this VimResult<T> vimResult)
        {
            return ((VimResult<T>.Error)vimResult).Error; ;
        }

        #endregion

        #region SetArgument

        /// <summary>
        /// SetArgument as SetArgument
        /// </summary>
        public static SetArgument.DisplaySetting AsDisplaySetting(this SetArgument setArgument)
        {
            return (SetArgument.DisplaySetting)setArgument;
        }

        /// <summary>
        /// SetArgument as UseSetting
        /// </summary>
        public static SetArgument.UseSetting AsUseSetting(this SetArgument setArgument)
        {
            return (SetArgument.UseSetting)setArgument;
        }

        /// <summary>
        /// SetArgument as ToggleOffSetting
        /// </summary>
        public static SetArgument.ToggleOffSetting AsToggleOffSetting(this SetArgument setArgument)
        {
            return (SetArgument.ToggleOffSetting)setArgument;
        }

        /// <summary>
        /// SetArgument as InvertSetting
        /// </summary>
        public static SetArgument.InvertSetting AsInvertSetting(this SetArgument setArgument)
        {
            return (SetArgument.InvertSetting)setArgument;
        }

        /// <summary>
        /// SetArgument as AssignSetting
        /// </summary>
        public static SetArgument.AssignSetting AsAssignSetting(this SetArgument setArgument)
        {
            return (SetArgument.AssignSetting)setArgument;
        }

        /// <summary>
        /// SetArgument as AddSetting
        /// </summary>
        public static SetArgument.AddSetting AsAddSetting(this SetArgument setArgument)
        {
            return (SetArgument.AddSetting)setArgument;
        }

        /// <summary>
        /// SetArgument as MultiplySetting
        /// </summary>
        public static SetArgument.MultiplySetting AsMultiplySetting(this SetArgument setArgument)
        {
            return (SetArgument.MultiplySetting)setArgument;
        }

        /// <summary>
        /// SetArgument as SubtractSetting
        /// </summary>
        public static SetArgument.SubtractSetting AsSubtractSetting(this SetArgument setArgument)
        {
            return (SetArgument.SubtractSetting)setArgument;
        }

        #endregion

        #region LineRangeSpecifier

        /// <summary>
        /// LineRange as SingleLine
        /// </summary>
        public static LineRangeSpecifier.SingleLine AsSingleLine(this LineRangeSpecifier lineRange)
        {
            return (LineRangeSpecifier.SingleLine)lineRange;
        }

        /// <summary>
        /// LineRange as Range
        /// </summary>
        public static LineRangeSpecifier.Range AsRange(this LineRangeSpecifier lineRange)
        {
            return (LineRangeSpecifier.Range)lineRange;
        }

        /// <summary>
        /// LineRange as WithEndCount
        /// </summary>
        public static LineRangeSpecifier.WithEndCount AsWithEndCount(this LineRangeSpecifier lineRange)
        {
            return (LineRangeSpecifier.WithEndCount)lineRange;
        }

        #endregion

        #region LineSpecifier

        public static LineSpecifier.Number AsNumber(this LineSpecifier lineSpecifier)
        {
            return (LineSpecifier.Number)lineSpecifier;
        }

        public static bool IsNumber(this LineSpecifier lineSpecifier, int number)
        {
            return lineSpecifier.IsNumber && lineSpecifier.AsNumber().Number == number;
        }

        public static LineSpecifier.NextLineWithPattern AsNextLineWithPattern(this LineSpecifier lineSpecifier)
        {
            return (LineSpecifier.NextLineWithPattern)lineSpecifier;
        }

        public static LineSpecifier.PreviousLineWithPattern AsPreviousLineWithPattern(this LineSpecifier lineSpecifier)
        {
            return (LineSpecifier.PreviousLineWithPattern)lineSpecifier;
        }

        public static LineSpecifier.LineSpecifierWithAdjustment AsLineSpecifierWithAdjustment(this LineSpecifier lineSpecifier)
        {
            return (LineSpecifier.LineSpecifierWithAdjustment)lineSpecifier;
        }

        public static bool IsCurrentLineWithAdjustment(this LineSpecifier lineSpecifier, int count)
        {
            return lineSpecifier.IsLineSpecifierWithAdjustment &&
                lineSpecifier.AsLineSpecifierWithAdjustment().LineSpecifier.IsCurrentLine &&
                lineSpecifier.AsLineSpecifierWithAdjustment().Adjustment == count;
        }

        #endregion

        #region ParseResult

        /// <summary>
        /// Get the suceeded version of the ParseResult
        /// </summary>
        internal static ParseResult<T>.Succeeded AsSucceeded<T>(this ParseResult<T> parseResult)
        {
            return (ParseResult<T>.Succeeded)parseResult;
        }

        /// <summary>
        /// Get the failed version of the ParseResult
        /// </summary>
        internal static ParseResult<T>.Failed AsFailed<T>(this ParseResult<T> parseResult)
        {
            return (ParseResult<T>.Failed)parseResult;
        }

        /// <summary>
        /// Is this a failed ParseResult with the given error message?
        /// </summary>
        internal static bool IsFailed<T>(this ParseResult<T> parseResult, string message)
        {
            return parseResult.IsFailed && message == parseResult.AsFailed().Error;
        }

        #endregion

        #region Expression

        /// <summary>
        /// Get the succeeded version of the Expression as a constant value
        /// </summary>
        public static Expression.ConstantValue AsConstantValue(this Expression expr)
        {
            return (Expression.ConstantValue)expr;
        }

        /// <summary>
        /// Get the succeeded version of the Expression as a list of expressions
        /// </summary>
        public static Expression.List AsList(this Expression expr)
        {
            return (Expression.List)expr;
        }

        public static bool IsParseError(this LineCommand lineCommand, string message)
        {
            return lineCommand.IsParseError && lineCommand.AsParseError().Error == message;
        }

        #endregion

        #region VariableValue

        /// <summary>
        /// Number version of a value
        /// </summary>
        public static VariableValue.Number AsNumber(this VariableValue value)
        {
            return (VariableValue.Number)value;
        }

        /// <summary>
        /// List version of a value
        /// </summary>
        public static VariableValue.List AsList(this VariableValue value)
        {
            return (VariableValue.List)value;
        }

        /// <summary>
        /// String version of a value
        /// </summary>
        public static VariableValue.String AsString(this VariableValue value)
        {
            return (VariableValue.String)value;
        }

        #endregion

        #region ModeSwitch

        public static ModeSwitch.SwitchModeWithArgument AsSwitchModeWithArgument(this ModeSwitch mode)
        {
            Assert.True(mode.IsSwitchModeWithArgument);
            return (ModeSwitch.SwitchModeWithArgument)mode;
        }

        #endregion

        #region ProcessResult

        public static ProcessResult.Handled AsHandled(this ProcessResult res)
        {
            return (ProcessResult.Handled)res;
        }

        public static bool IsSwitchModeOneTimeCommand(this ProcessResult result)
        {
            return result.IsHandled && result.AsHandled().ModeSwitch.IsSwitchModeOneTimeCommand;
        }

        public static bool IsSwitchMode(this ProcessResult result, ModeKind kind)
        {
            return result.IsHandled && result.AsHandled().ModeSwitch.IsSwitchMode(kind);
        }

        public static bool IsSwitchModeWithArgument(this ProcessResult result, ModeKind kind, ModeArgument argument)
        {
            return result.IsHandled && result.AsHandled().ModeSwitch.IsSwitchModeWithArgument(kind, argument);
        }

        public static bool IsSwitchPreviousMode(this ProcessResult result)
        {
            return result.IsHandled && result.AsHandled().ModeSwitch.IsSwitchPreviousMode;
        }

        public static bool IsHandledNoSwitch(this ProcessResult result)
        {
            return result.IsHandled && result.AsHandled().ModeSwitch.IsNoSwitch;
        }

        #endregion

        #region NumberValue

        /// <summary>
        /// Convert the NumberValue to the Decimal form
        /// </summary>
        internal static NumberValue.Decimal AsDecimal(this NumberValue numberValue)
        {
            return (NumberValue.Decimal)numberValue;
        }

        /// <summary>
        /// Convert the NumberValue to the Hex form
        /// </summary>
        internal static NumberValue.Hex AsHex(this NumberValue numberValue)
        {
            return (NumberValue.Hex)numberValue;
        }

        /// <summary>
        /// Convert the NumberValue to the Octal form
        /// </summary>
        internal static NumberValue.Octal AsOctal(this NumberValue numberValue)
        {
            return (NumberValue.Octal)numberValue;
        }

        /// <summary>
        /// Convert the NumberValue to the Alpha form
        /// </summary>
        internal static NumberValue.Alpha AsAlpha(this NumberValue numberValue)
        {
            return (NumberValue.Alpha)numberValue;
        }

        #endregion

        #region ModeSwitch

        public static bool IsSwitchMode(this ModeSwitch mode, ModeKind kind)
        {
            return mode.IsSwitchMode && ((ModeSwitch.SwitchMode)mode).ModeKind == kind;
        }

        public static bool IsSwitchModeWithArgument(this ModeSwitch mode, ModeKind kind, ModeArgument argument)
        {
            if (!mode.IsSwitchModeWithArgument)
            {
                return false;
            }

            var value = (ModeSwitch.SwitchModeWithArgument)mode;
            return value.ModeKind == kind && value.ModeArgument.Equals(argument);
        }

        #endregion

        #region BindResult

        public static BindResult<T>.Complete AsComplete<T>(this BindResult<T> res)
        {
            Assert.True(res.IsComplete);
            return (BindResult<T>.Complete)res;
        }

        public static BindResult<T>.NeedMoreInput AsNeedMoreInput<T>(this BindResult<T> res)
        {
            Assert.True(res.IsNeedMoreInput);
            return (BindResult<T>.NeedMoreInput)res;
        }

        public static BindResult<TResult> Convert<T, TResult>(this BindResult<T> res, Func<T, TResult> func)
        {
            var func2 = func.ToFSharpFunc();
            return res.Convert(func2);
        }

        #endregion

        #region UndoRedoData

        internal static UndoRedoData.Normal AsNormal(this UndoRedoData data)
        {
            return (UndoRedoData.Normal)data;
        }

        internal static UndoRedoData.Linked AsLinked(this UndoRedoData data)
        {
            return (UndoRedoData.Linked)data;
        }

        #endregion

        #region Command

        public static Command.VisualCommand AsVisualCommand(this Command command)
        {
            Assert.True(command.IsVisualCommand);
            return (Command.VisualCommand)command;
        }

        public static Command.NormalCommand AsNormalCommand(this Command command)
        {
            Assert.True(command.IsNormalCommand);
            return (Command.NormalCommand)command;
        }

        public static Command.InsertCommand AsInsertCommand(this Command command)
        {
            Assert.True(command.IsInsertCommand);
            return (Command.InsertCommand)command;
        }

        #endregion

        #region InsertCommand

        public static InsertCommand.Insert AsInsert(this InsertCommand command)
        {
            return (InsertCommand.Insert)command;
        }

        public static InsertCommand.InsertLiteral AsInsertLiteral(this InsertCommand command)
        {
            return (InsertCommand.InsertLiteral)command;
        }

        public static bool IsInsert(this InsertCommand command, char c)
        {
            return IsInsert(command, c.ToString());
        }

        public static bool IsInsert(this InsertCommand command, string text)
        {
            return command.IsInsert && command.AsInsert().Text == text;
        }

        public static bool TryGetInsertionText(this InsertCommand command, out string text)
        {
            text = null;
            if (command.IsInsert)
            {
                text = command.AsInsert().Text;
            }
            else if (command.IsInsertLiteral)
            {
                text = command.AsInsertLiteral().Text;
            }
            else if (command.IsInsertNewLine)
            {
                text = Environment.NewLine;
            }
            else if (command.IsInsertTab)
            {
                text = "\t";
            }
            return text != null;
        }

        #endregion

        #region IMotionCapture

        public static BindResult<Tuple<Motion, FSharpOption<int>>> GetMotionAndCount(this IMotionCapture capture, char c)
        {
            return capture.GetMotionAndCount(KeyInputUtil.CharToKeyInput(c));
        }

        #endregion

        #region ModeUtil.Result

        public static Result.Failed AsFailed(this Result res)
        {
            return (Result.Failed)res;
        }

        #endregion

        #region IKeyMap

        public static KeyMappingResult Map(this IVimLocalKeyMap keyMap, KeyInput ki, KeyRemapMode mode)
        {
            return keyMap.Map(new KeyInputSet(ki), mode);
        }

        public static KeyMappingResult Map(this IVimLocalKeyMap keyMap, char c, KeyRemapMode mode)
        {
            return Map(keyMap, KeyInputUtil.CharToKeyInput(c), mode);
        }

        public static KeyMappingResult Map(this IVimLocalKeyMap keyMap, string str, KeyRemapMode mode)
        {
            return keyMap.Map(KeyNotationUtil.StringToKeyInputSet(str), mode);
        }

        public static bool RemoveKeyMapping(this IVimKeyMap keyMap, string lhs, KeyRemapMode mode)
        {
            var left = KeyNotationUtil.TryStringToKeyInputSet(lhs);
            if (left.IsNone())
            {
                return false;
            }

            return keyMap.RemoveKeyMapping(left.Value, mode);
        }

        public static bool AddKeyMapping(this IVimKeyMap keyMap, string lhs, string rhs, bool allowRemap, KeyRemapMode mode)
        {
            var left = keyMap.ParseKeyNotation(lhs);
            var right = keyMap.ParseKeyNotation(rhs);
            if (left.IsNone() || right.IsNone())
            {
                return false;
            }

            keyMap.AddKeyMapping(left.Value, right.Value, allowRemap, mode);
            return true;
        }

        #endregion

        #region KeyMappingResult

        public static KeyMappingResult.Mapped AsMapped(this KeyMappingResult res)
        {
            Assert.True(res.IsMapped);
            return (KeyMappingResult.Mapped)res;
        }

        public static KeyMappingResult.Unmapped AsUnmapped(this KeyMappingResult res)
        {
            Assert.True(res.IsUnmapped);
            return (KeyMappingResult.Unmapped)res;
        }

        public static KeyMappingResult.PartiallyMapped AsPartiallyMapped(this KeyMappingResult res)
        {
            Assert.True(res.IsPartiallyMapped);
            return (KeyMappingResult.PartiallyMapped)res;
        }

        public static KeyInputSet GetMappedKeyInputs(this KeyMappingResult res)
        {
            if (res.IsMapped)
            {
                return res.AsMapped().KeyInputSet;
            }

            if (res.IsUnmapped)
            {
                return res.AsUnmapped().KeyInputSet;
            }

            var partialMap = res.AsPartiallyMapped();
            return KeyInputSetUtil.Combine(partialMap.MappedKeyInputSet, partialMap.RemainingKeyInputSet);
        }

        #endregion

        #region SettingValue

        public static SettingValue.String AsString(this SettingValue value)
        {
            Assert.True(value.IsString);
            return (SettingValue.String)value;
        }

        public static SettingValue.Toggle AsToggle(this SettingValue value)
        {
            Assert.True(value.IsToggle);
            return (SettingValue.Toggle)value;
        }

        public static SettingValue.Number AsNumber(this SettingValue value)
        {
            Assert.True(value.IsNumber);
            return (SettingValue.Number)value;
        }

        #endregion

        #region IMode

        public static bool CanProcess(this IMode mode, VimKey key)
        {
            return mode.CanProcess(KeyInputUtil.VimKeyToKeyInput(key));
        }

        public static ProcessResult Process(this IMode mode, KeyInput keyInput)
        {
            return mode.Process(KeyInputData.Create(keyInput, false));
        }

        public static ProcessResult Process(this IMode mode, VimKey key)
        {
            return mode.Process(KeyInputUtil.VimKeyToKeyInput(key));
        }

        public static ProcessResult Process(this IMode mode, char c)
        {
            return mode.Process((KeyInputUtil.CharToKeyInput(c)));
        }

        public static ProcessResult Process(this IMode mode, string input, bool enter = false)
        {
            ProcessResult last = null;
            foreach (var c in input)
            {
                var i = KeyInputUtil.CharToKeyInput(c);
                last = mode.Process(c);
            }

            if (enter)
            {
                last = mode.Process(KeyInputUtil.EnterKey);
            }

            return last;
        }

        /// <summary>
        /// Process the full notation as a series of KeyInput values
        /// </summary>
        public static void ProcessNotation(this IMode mode, string notation, bool enter = false)
        {
            var keyInputSet = KeyNotationUtil.StringToKeyInputSet(notation);
            foreach (var keyInput in keyInputSet.KeyInputs)
            {
                mode.Process(keyInput);
            }

            if (enter)
            {
                mode.Process(KeyInputUtil.EnterKey);
            }
        }

        #endregion

        #region IVimBufferFactory

        internal static IVimBuffer CreateVimBuffer(this IVimBufferFactory vimBufferFactory, ITextView textView, IVimTextBuffer vimTextBuffer)
        {
            var vimBufferData = vimBufferFactory.CreateVimBufferData(vimTextBuffer, textView);
            return vimBufferFactory.CreateVimBuffer(vimBufferData);
        }

        #endregion

        #region IVimBuffer

        /// <summary>
        /// Helper for the CanProcess function which maps the char to a KeyInput value
        /// </summary>
        public static bool CanProcess(this IVimBuffer buffer, char c)
        {
            var keyInput = KeyInputUtil.CharToKeyInput(c);
            return buffer.CanProcess(keyInput);
        }

        /// <summary>
        /// Helper for the CanProcess function which maps the VimKey to a KeyInput value
        /// </summary>
        public static bool CanProcess(this IVimBuffer buffer, VimKey key)
        {
            var keyInput = KeyInputUtil.VimKeyToKeyInput(key);
            return buffer.CanProcess(keyInput);
        }

        /// <summary>
        /// Helper for the CanProcessAsCommand function which maps the char to a KeyInput value
        /// </summary>
        public static bool CanProcessAsCommand(this IVimBuffer buffer, char c)
        {
            var keyInput = KeyInputUtil.CharToKeyInput(c);
            return buffer.CanProcessAsCommand(keyInput);
        }

        /// <summary>
        /// Helper for the CanProcessAsCommand function which maps the VimKey to a KeyInput value
        /// </summary>
        public static bool CanProcessAsCommand(this IVimBuffer buffer, VimKey key)
        {
            var keyInput = KeyInputUtil.VimKeyToKeyInput(key);
            return buffer.CanProcessAsCommand(keyInput);
        }

        /// <summary>
        /// Process the VimKey values in sequence
        /// </summary>
        public static bool Process(this IVimBuffer buf, params VimKey[] keys)
        {
            var ret = false;
            foreach (var key in keys)
            {
                ret = buf.Process(KeyInputUtil.VimKeyToKeyInput(key)).IsAnyHandled;
            }
            return ret;
        }

        public static bool Process(this IVimBuffer buf, char c)
        {
            return buf.Process(KeyInputUtil.CharToKeyInput(c)).IsAnyHandled;
        }

        public static void Process(this IVimBuffer buf, string input, bool enter = false)
        {
            foreach (var c in input)
            {
                var i = KeyInputUtil.CharToKeyInput(c);
                buf.Process(i);
            }

            if (enter)
            {
                buf.Process(KeyInputUtil.EnterKey);
            }
        }

        /// <summary>
        /// Process the full notation as a series of KeyInput values
        /// </summary>
        public static void ProcessNotation(this IVimBuffer vimBuffer, string notation, bool enter = false)
        {
            var keyInputSet = KeyNotationUtil.StringToKeyInputSet(notation);
            foreach (var keyInput in keyInputSet.KeyInputs)
            {
                vimBuffer.Process(keyInput);
            }

            if (enter)
            {
                vimBuffer.Process(KeyInputUtil.EnterKey);
            }
        }

        public static Register GetRegister(this IVimBuffer buffer, char c)
        {
            var name = RegisterNameUtil.CharToRegister(c).Value;
            return buffer.RegisterMap.GetRegister(name);
        }

        public static BlockSpan GetSelectionBlockSpan(this IVimBuffer vimBuffer)
        {
            return GetSelectionBlockSpan(vimBuffer.TextView, vimBuffer.LocalSettings.TabStop);
        }

        public static BlockSpan GetBlockSpan(this IVimBuffer vimBuffer, int column, int length, int startLine = 0, int lineCount = 1)
        {
            return GetBlockSpan(vimBuffer.TextBuffer, column, length, startLine, lineCount, vimBuffer.LocalSettings.TabStop);
        }

        public static async Task GetSearchCompleteAsync(this IVimBuffer vimBuffer) => await vimBuffer.IncrementalSearch.GetSearchCompleteAsync();

        #endregion

        public static SelectionSpan GetSelectedSpan(
            this SnapshotPoint point,
            int startOffset,
            int endOffset,
            bool isReversed)
        {
            if (startOffset > endOffset)
            {
                throw new InvalidOperationException("start is after end");
            }

            // This is different than just converting the point to a virtual
            // point and getting the virtual selected span because point
            // addition adds within the buffer, but virtual point addition adds
            // on the same line.
            if (!isReversed)
            {
                return new SelectionSpan(
                    new VirtualSnapshotPoint(point),
                    new VirtualSnapshotPoint(point.Add(startOffset)),
                    new VirtualSnapshotPoint(point.Add(endOffset)));
            }
            else
            {
                return new SelectionSpan(
                    new VirtualSnapshotPoint(point),
                    new VirtualSnapshotPoint(point.Add(endOffset)),
                    new VirtualSnapshotPoint(point.Add(startOffset)));
            }
        }

        #region VirtualSnapshotPoint

        public static VirtualSnapshotPoint Add(this VirtualSnapshotPoint point, int offset)
        {
            if (offset == 0)
            {
                return point;
            }
            var line = point.Position.GetContainingLine();
            var realPointOffset = point.Position.Position - line.Start.Position;
            var newOffset = realPointOffset + point.VirtualSpaces + offset;
            return new VirtualSnapshotPoint(line, newOffset);
        }

        public static SelectionSpan GetSelectedSpan(this VirtualSnapshotPoint point)
        {
            return new SelectionSpan(point);
        }

        public static SelectionSpan GetSelectedSpan(
            this VirtualSnapshotPoint point,
            int startOffset,
            int endOffset,
            bool isReversed)
        {
            if (startOffset > endOffset)
            {
                throw new InvalidOperationException("start is after end");
            }

            if (!isReversed)
            {
                return new SelectionSpan(point, point.Add(startOffset), point.Add(endOffset));
            }
            else
            {
                return new SelectionSpan(point, point.Add(endOffset), point.Add(startOffset));
            }
        }

        #endregion

        #region ITextSnapshot

        public static ITextSnapshotLine GetLine(this ITextSnapshot snapshot, int lineNumber)
        {
            return snapshot.GetLineFromLineNumber(lineNumber);
        }

        public static ITextSnapshotLine GetFirstLine(this ITextSnapshot snapshot)
        {
            return GetLine(snapshot, 0);
        }

        public static ITextSnapshotLine GetLastLine(this ITextSnapshot snapshot)
        {
            return GetLine(snapshot, snapshot.LineCount - 1);
        }

        public static SnapshotColumn GetColumn(this ITextSnapshot snapshot, int lineNumber, int columnNumber, bool? includeLineBreak = true)
        {
            var option = FSharpOption.CreateForNullable(includeLineBreak);
            var column = SnapshotColumn.GetForLineAndColumnNumber(snapshot, lineNumber, columnNumber, option);
            return column.Value;
        }

        public static SnapshotColumn GetColumnFromPosition(this ITextSnapshot snapshot, int position)
        {
            var point = new SnapshotPoint(snapshot, position);
            return new SnapshotColumn(point);
        }

        public static VirtualSnapshotColumn GetVirtualColumn(this ITextSnapshot snapshot, int lineNumber, int columnNumber)
        {
            var line = snapshot.GetLine(lineNumber);
            return VirtualSnapshotColumn.GetForColumnNumber(line, columnNumber);
        }

        public static VirtualSnapshotColumn GetVirtualColumnFromPosition(this ITextSnapshot snapshot, int position, int virtualSpaces = 0)
        {
            var point = new SnapshotPoint(snapshot, position);
            var column = new SnapshotColumn(point);
            return new VirtualSnapshotColumn(column, virtualSpaces);
        }

        public static SnapshotCodePoint GetCodePoint(this ITextSnapshot snapshot, int lineNumber, int columnNumber)
        {
            var point = snapshot.GetPointInLine(lineNumber, columnNumber);
            return new SnapshotCodePoint(point);
        }

        public static SnapshotCodePoint GetCodePointFromPosition(this ITextSnapshot snapshot, int position)
        {
            var point = new SnapshotPoint(snapshot, position);
            return new SnapshotCodePoint(point);
        }

        public static SnapshotColumn GetEndColumn(this ITextSnapshot snapshot) => SnapshotColumn.GetEndColumn(snapshot);

        public static SnapshotColumn GetStartColumn(this ITextSnapshot snapshot) => SnapshotColumn.GetStartColumn(snapshot);

        #endregion

        #region ITextBuffer

        public static SnapshotColumn GetStartColumn(this ITextBuffer textBuffer) => textBuffer.CurrentSnapshot.GetStartColumn();

        public static SnapshotColumn GetEndColumn(this ITextBuffer textBuffer) => textBuffer.CurrentSnapshot.GetEndColumn();

        public static SnapshotColumn GetColumn(this ITextBuffer textBuffer, int lineNumber, int columnNumber, bool? includeLineBreak = null)
        {
            return textBuffer.CurrentSnapshot.GetColumn(lineNumber, columnNumber, includeLineBreak);
        }

        public static SnapshotColumn GetColumnFromPosition(this ITextBuffer textBuffer, int position)
        {
            return GetColumnFromPosition(textBuffer.CurrentSnapshot, position);
        }

        public static VirtualSnapshotColumn GetVirtualColumn(this ITextBuffer textBuffer, int lineNumber, int columnNumber)
        {
            return GetVirtualColumn(textBuffer.CurrentSnapshot, lineNumber, columnNumber);
        }

        public static VirtualSnapshotColumn GetVirtualColumnFromPosition(this ITextBuffer textBuffer, int position, int virtualSpaces = 0)
        {
            return GetVirtualColumnFromPosition(textBuffer.CurrentSnapshot, position, virtualSpaces);
        }

        public static SnapshotCodePoint GetCodePoint(this ITextBuffer textBuffer, int lineNumber, int columnNumber)
        {
            return textBuffer.CurrentSnapshot.GetCodePoint(lineNumber, columnNumber);
        }

        public static SnapshotCodePoint GetCodePointFromPosition(this ITextBuffer textBuffer, int position)
        {
            return GetCodePointFromPosition(textBuffer.CurrentSnapshot, position);
        }

        public static SnapshotPoint GetStartPoint(this ITextBuffer textBuffer)
        {
            return textBuffer.CurrentSnapshot.GetStartPoint();
        }

        public static SnapshotPoint GetEndPoint(this ITextBuffer textBuffer)
        {
            return textBuffer.CurrentSnapshot.GetEndPoint();
        }

        public static ITextSnapshotLine GetLineFromLineNumber(this ITextBuffer buffer, int line)
        {
            return buffer.CurrentSnapshot.GetLineFromLineNumber(line);
        }

        public static BlockSpan GetBlockSpan(this ITextBuffer textBuffer, int column, int length, int startLine = 0, int lineCount = 1, int tabStop = 4)
        {
            var line = textBuffer.GetLine(startLine);
            var startPoint = line.Start.Add(column);
            return new BlockSpan(startPoint, tabStop, length, lineCount);
        }

        public static NonEmptyCollection<SnapshotSpan> GetBlock(this ITextBuffer textBuffer, int column, int length, int startLine = 0, int lineCount = 1)
        {
            return GetBlockSpan(textBuffer, column, length, startLine, lineCount).BlockSpans;
        }

        public static VisualSpan GetVisualSpanBlock(this ITextBuffer textBuffer, int column, int length, int startLine = 0, int lineCount = 1, int tabStop = 4)
        {
            var blockSpanData = GetBlockSpan(textBuffer, column, length, startLine, lineCount, tabStop);
            return VisualSpan.NewBlock(blockSpanData);
        }

        public static IEnumerable<string> GetLines(this ITextBuffer textBuffer)
        {
            foreach (var line in textBuffer.CurrentSnapshot.Lines)
            {
                yield return line.GetText();
            }
        }

        public static string GetLineText(this ITextBuffer textBuffer, int lineNumber, bool includeLineBreak = false)
        {
            var line = textBuffer.GetLine(lineNumber);
            return includeLineBreak ? line.GetTextIncludingLineBreak() : line.GetText();
        }

        public static SnapshotSpan GetLineSpan(this ITextBuffer buffer, int lineNumber, int column, int length)
        {
            var line = buffer.GetLine(lineNumber);
            return new SnapshotSpan(line.Start.Add(column), length);
        }

        #endregion

        #region ITextView

        public static SnapshotPoint GetStartPoint(this ITextView textView)
        {
            return textView.TextBuffer.CurrentSnapshot.GetStartPoint();
        }

        public static SnapshotPoint GetEndPoint(this ITextView textView)
        {
            return textView.TextBuffer.CurrentSnapshot.GetEndPoint();
        }

        public static SnapshotPoint GetPoint(this ITextView textView, int position)
        {
            return textView.TextBuffer.GetPoint(position);
        }

        public static SnapshotPoint GetPointInLine(this ITextView textView, int lineNumber, int column)
        {
            return textView.TextBuffer.GetPointInLine(lineNumber, column);
        }

        public static VirtualSnapshotPoint GetVirtualPointInLine(this ITextView textView, int lineNumber, int column)
        {
            return textView.TextBuffer.GetVirtualPointInLine(lineNumber, column);
        }

        public static SnapshotLineRange GetLineRange(this ITextView textView, int startLine, int endLine = -1)
        {
            return textView.TextBuffer.GetLineRange(startLine, endLine);
        }

        public static SnapshotSpan GetLineSpan(this ITextView textView, int lineNumber, int length)
        {
            return GetLineSpan(textView, lineNumber, 0, length);
        }

        public static SnapshotSpan GetLineSpan(this ITextView textView, int lineNumber, int column, int length)
        {
            return GetLineSpan(textView.TextBuffer, lineNumber, column, length);
        }

        /// <summary>
        /// Change the selection to be the specified SnapshotSpan value and update the caret to be on the
        /// last included point in the SnapshotSpan.  
        /// </summary>
        public static void SelectAndMoveCaret(this ITextView textView, SnapshotSpan span)
        {
            var characterSpan = new CharacterSpan(span);
            var visualSelection = VisualSelection.CreateForward(VisualSpan.NewCharacter(characterSpan));
            visualSelection.SelectAndMoveCaret(textView);
        }

        public static VisualSpan GetVisualSpanBlock(this ITextView textView, int column, int length, int startLine = 0, int lineCount = 1, int tabStop = 4)
        {
            return GetVisualSpanBlock(textView.TextBuffer, column, length, startLine, lineCount, tabStop);
        }

        public static BlockSpan GetBlockSpan(this ITextView textView, int column, int length, int startLine = 0, int lineCount = 1)
        {
            return textView.TextBuffer.GetBlockSpan(column, length, startLine, lineCount);
        }

        public static NonEmptyCollection<SnapshotSpan> GetBlock(this ITextView textView, int column, int length, int startLine = 0, int lineCount = 1)
        {
            return GetBlock(textView.TextBuffer, column, length, startLine, lineCount);
        }

        public static ITextSnapshotLine GetLastLine(this ITextView textView)
        {
            return textView.TextSnapshot.GetLastLine();
        }

        public static ITextSnapshotLine GetFirstLine(this ITextView textView)
        {
            return textView.TextSnapshot.GetFirstLine();
        }

        public static void SetText(this ITextView textView, string text, int? caret)
        {
            textView.TextBuffer.SetText(text);
            if (caret.HasValue)
            {
                textView.MoveCaretTo(caret.Value);
            }
        }

        public static void ScrollToTop(this ITextView textView)
        {
            textView.ViewScroller.ScrollViewportVerticallyByLines(ScrollDirection.Up, textView.TextBuffer.CurrentSnapshot.LineCount);
        }

        public static void ScrollToBottom(this ITextView textView)
        {
            textView.ViewScroller.ScrollViewportVerticallyByLines(ScrollDirection.Down, textView.TextBuffer.CurrentSnapshot.LineCount);
        }

        public static int GetFirstVisibleLineNumber(this ITextView textView)
        {
            return textView.TextViewLines.FirstVisibleLine.Start.GetContainingLine().LineNumber;
        }

        public static int GetLastVisibleLineNumber(this ITextView textView)
        {
            return textView.TextViewLines.LastVisibleLine.Start.GetContainingLine().LineNumber;
        }

        #endregion

        #region IWpfTextView

        /// <summary>
        /// Set the number of visible lines to the specified count.  This will control the 
        /// range between <see cref="ITextViewLineCollection.FirstVisibleLine"/> and 
        /// <see cref="ITextViewLineCollection.LastVisibleLine"/>.  The <see cref="ITextViewLineCollection.Count"/>
        /// value can potentially be greater than the specified <param name="count"/>.
        /// </summary>
        public static void SetVisibleLineCount(this IWpfTextView wpfTextView, int count)
        {
            var oldSize = wpfTextView.VisualElement.RenderSize;
            var height = wpfTextView.TextViewLines.FirstVisibleLine.Height * (double)count;

            do
            {
                var size = new Size(oldSize.Width, height);
                wpfTextView.VisualElement.RenderSize = size;
                ForceLayout(wpfTextView);

                var startLine = wpfTextView.TextViewLines.FirstVisibleLine.Start.GetContainingLine();
                var lastLine = wpfTextView.TextViewLines.LastVisibleLine.Start.GetContainingLine();
                var visibleCount = (lastLine.LineNumber - startLine.LineNumber) + 1;

                if (visibleCount == count)
                {
                    break;
                }
                else if (visibleCount < count)
                {
                    height += 5;
                }
                else
                {
                    height -= 5;
                }
            }
            while (true);
        }

        /// <summary>
        /// Set the <see cref="ITextViewLineCollection.Count"/> value.
        /// </summary>
        public static void SetTextViewLineCount(this IWpfTextView wpfTextView, int count)
        {
            var oldSize = wpfTextView.VisualElement.RenderSize;
            var height = wpfTextView.TextViewLines.FirstVisibleLine.Height * (double)count;

            do
            {
                var size = new Size(oldSize.Width, height);
                wpfTextView.VisualElement.RenderSize = size;
                ForceLayout(wpfTextView);

                var visibleCount = wpfTextView.TextViewLines.Count;
                if (visibleCount == count)
                {
                    break;
                }
                else if (visibleCount < count)
                {
                    height += 5;
                }
                else
                {
                    height -= 5;
                }
            }
            while (true);
        }

        /// <summary>
        /// Make only the specified line range visible.
        /// </summary>
        public static void SetVisibleLineRange(this IWpfTextView wpfTextView, int start, int length)
        {
            var startLine = wpfTextView.TextSnapshot.GetLineFromLineNumber(start);
            wpfTextView.DisplayTextLineContainingBufferPosition(startLine.Start, 0, ViewRelativePosition.Top);
            SetVisibleLineCount(wpfTextView, length);
        }

        public static void ForceLayout(this IWpfTextView wpfTextView)
        {
#if VS_SPECIFIC_2022
            var method = wpfTextView
                .GetType()
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(x => x.Name == "PerformLayout" && x.GetParameters().Length == 0);
            method.Invoke(wpfTextView, Array.Empty<object>());
            Dispatcher.CurrentDispatcher.DoEvents();
#elif VS_SPECIFIC_2017 || VS_SPECIFIC_2019
            var method = wpfTextView
                .GetType()
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(x => x.Name == "PerformLayout" && x.GetParameters().Length == 2);
            method.Invoke(wpfTextView, new[] { wpfTextView.TextSnapshot, wpfTextView.VisualSnapshot });
#else
#error Unsupported configuration
#endif
        }

#endregion

#region SelectionSpan

        public static SelectionSpan AdjustCaretForInclusive(this SelectionSpan selectedSpan)
        {
            if (selectedSpan.CaretPoint == selectedSpan.Start)
            {
                return selectedSpan;
            }
            else
            {
                return new SelectionSpan(selectedSpan.End.Add(-1), selectedSpan.AnchorPoint, selectedSpan.ActivePoint);
            }
        }

        public static SelectionSpan AdjustEndForInclusive(this SelectionSpan selectedSpan)
        {
            if (selectedSpan.CaretPoint == selectedSpan.Start && !selectedSpan.IsEmpty)
            {
                return new SelectionSpan(selectedSpan.CaretPoint, selectedSpan.AnchorPoint.Add(1), selectedSpan.ActivePoint);
            }
            else
            {
                return new SelectionSpan(selectedSpan.CaretPoint, selectedSpan.AnchorPoint, selectedSpan.ActivePoint.Add(1));
            }
        }

#endregion SelectionSpan

#region VisualSpan

        public static VisualSpan.Character AsCharacter(this VisualSpan span)
        {
            Assert.True(span.IsCharacter);
            return (VisualSpan.Character)span;
        }

        public static VisualSpan.Line AsLine(this VisualSpan span)
        {
            Assert.True(span.IsLine);
            return (VisualSpan.Line)span;
        }

        public static VisualSpan.Block AsBlock(this VisualSpan span)
        {
            Assert.True(span.IsBlock);
            return (VisualSpan.Block)span;
        }

#endregion

#region VisualSelection

        public static VisualSelection.Character AsCharacter(this VisualSelection span)
        {
            Assert.True(span.IsCharacter);
            return (VisualSelection.Character)span;
        }

        public static VisualSelection.Line AsLine(this VisualSelection span)
        {
            Assert.True(span.IsLine);
            return (VisualSelection.Line)span;
        }

        public static VisualSelection.Block AsBlock(this VisualSelection span)
        {
            Assert.True(span.IsBlock);
            return (VisualSelection.Block)span;
        }

#endregion

#region ICommandRunner

        public static BindResult<CommandRunData> Run(this ICommandRunner runner, VimKey key)
        {
            return runner.Run(KeyInputUtil.VimKeyToKeyInput(key));
        }

        public static BindResult<CommandRunData> Run(this ICommandRunner runner, char c)
        {
            return runner.Run(KeyInputUtil.CharToKeyInput(c));
        }

        /// <summary>
        /// Run the multi-input command
        /// </summary>
        public static BindResult<CommandRunData> Run(this ICommandRunner runner, string command)
        {
            BindResult<CommandRunData> result = null;
            for (var i = 0; i < command.Length; i++)
            {
                result = runner.Run(command[i]);
                if (i + 1 < command.Length)
                {
                    Assert.True(result.IsNeedMoreInput, "Needs more input");
                }
            }

            return result;
        }

#endregion

#region ICommonOperations

        public static void SetRegisterValue(this ICommonOperations operations, RegisterName name, RegisterOperation operation, RegisterValue value)
        {
            var opt = FSharpOption.Create(name);
            operations.SetRegisterValue(opt, operation, value);
        }

        public static void ShiftLineRangeLeft(this ICommonOperations operations, int count)
        {
            var number = operations.TextView.GetCaretLine().LineNumber;
            var range = operations.TextView.GetLineRange(number, number + (count - 1));
            operations.ShiftLineRangeLeft(range, 1);
        }

        public static void ShiftLineRangeRight(this ICommonOperations operations, int count)
        {
            var number = operations.TextView.GetCaretLine().LineNumber;
            var range = operations.TextView.GetLineRange(number, number + (count - 1));
            operations.ShiftLineRangeRight(range, 1);
        }

#endregion

#region BindResult<T>

        public static BindResult<T> Run<T>(this BindResult<T> result, KeyInput keyInput)
        {
            Assert.True(result.IsNeedMoreInput);
            return result.AsNeedMoreInput().BindData.BindFunction.Invoke(keyInput);
        }

        public static BindResult<T> Run<T>(this BindResult<T> result, VimKey vimKey) =>
            Run(result, KeyInputUtil.VimKeyToKeyInput(vimKey));

        public static BindResult<T> Run<T>(this BindResult<T> result, params KeyInput[] keyInputs)
        {
            foreach (var keyInput in keyInputs)
            {
                result = result.Run(keyInput);
            }

            return result;
        }

        public static BindResult<T> Run<T>(this BindResult<T> result, params VimKey[] vimKeys) =>
            Run(result, VimUtil.ConvertVimKeysToKeyInput(vimKeys));

        public static BindResult<T> Run<T>(this BindResult<T> result, string text, bool enter = false) =>
            Run(result, VimUtil.ConvertTextToKeyInput(text, enter));

#endregion

#region BindData<T>
        public static BindResult<T> Run<T>(this BindData<T> data, KeyInput keyInput) =>
            data.BindFunction.Invoke(keyInput);

        public static BindResult<T> Run<T>(this BindData<T> data, VimKey vimKey) =>
            Run(data, KeyInputUtil.VimKeyToKeyInput(vimKey));

        public static BindResult<T> Run<T>(this BindData<T> data, params KeyInput[] keyInputs)
        {
            BindResult<T> result = data.CreateBindResult();
            foreach (var keyInput in keyInputs)
            {
                result = result.Run(keyInput);
            }

            return result;
        }

        public static BindResult<T> Run<T>(this BindData<T> data, params VimKey[] vimKeys) =>
            Run(data, VimUtil.ConvertVimKeysToKeyInput(vimKeys));

        public static BindResult<T> Run<T>(this BindData<T> data, string text, bool enter = false) =>
            Run(data, VimUtil.ConvertTextToKeyInput(text, enter));

#endregion

#region SearchResult

        public static SearchResult.Found AsFound(this SearchResult result)
        {
            Assert.True(result.IsFound);
            return (SearchResult.Found)result;
        }

        public static SearchResult.NotFound AsNotFound(this SearchResult result)
        {
            Assert.True(result.IsNotFound);
            return (SearchResult.NotFound)result;
        }

        public static bool IsFound(this SearchResult result, int startPosition)
        {
            return result.IsFound && result.AsFound().SpanWithOffset.Start == startPosition;
        }

#endregion

#region CaretColumn

        public static CaretColumn.InLastLine AsInLastLine(this CaretColumn column)
        {
            return (CaretColumn.InLastLine)column;
        }

#endregion

#region IIncrementalSearchSession

        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearchSession session, params KeyInput[] keyInputs)
        {
            // Even though the Enter key will force the completion of the search on one branch there
            // will still be messages that need to be pumped.
            var result = session.Start().CreateBindResult();
            foreach (var keyInput in keyInputs)
            {
                result = result.Run(keyInput);
                await session.GetSearchResultAsync();
            }

            return result;
        }

        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearchSession session, params VimKey[] vimKeys) =>
            await DoSearchAsync(session, VimUtil.ConvertVimKeysToKeyInput(vimKeys));

        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearchSession session, string text, bool enter = true) =>
            await DoSearchAsync(session, VimUtil.ConvertTextToKeyInput(text, enter));

#endregion

#region IIncrementalSearch

        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearch search, SearchPath searchPath, params KeyInput[] keyInputs)
        {
            var session = search.CreateSession(searchPath);
            return await session.DoSearchAsync(keyInputs);
        }
        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearch search, params KeyInput[] keyInputs) =>
            await DoSearchAsync(search, SearchPath.Forward, keyInputs);

        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearch search, string text, bool enter = true) =>
            await DoSearchAsync(search, SearchPath.Forward, VimUtil.ConvertTextToKeyInput(text, enter));

        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearch search, SearchPath searchPath, string text, bool enter = true) =>
            await DoSearchAsync(search, searchPath, VimUtil.ConvertTextToKeyInput(text, enter));

        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearch search, params VimKey[] vimKeys) =>
            await DoSearchAsync(search, SearchPath.Forward, VimUtil.ConvertVimKeysToKeyInput(vimKeys));

        public static async Task<BindResult<SearchResult>> DoSearchAsync(this IIncrementalSearch search, SearchPath searchPath, params VimKey[] vimKeys) =>
            await DoSearchAsync(search, searchPath, VimUtil.ConvertVimKeysToKeyInput(vimKeys));

        public static void OnSearchStart(this IIncrementalSearch search, Action<SearchData> action)
        {
            search.SessionCreated += (_, args) =>
            {
                args.Session.SearchStart += (_2, args2) => action(args2.SearchData);
            };
        }

        public static void OnSearchEnd(this IIncrementalSearch search, Action<SearchResult> action)
        {
            search.SessionCreated += (_, args) =>
            {
                args.Session.SearchEnd += (_2, args2) => action(args2.SearchResult);
            };
        }

        public static async Task GetSearchCompleteAsync(this IIncrementalSearch search)
        {
            if (search.HasActiveSession)
            {
                var session = search.ActiveSession.Value;
                await session.GetSearchResultAsync();
            }
        }

#endregion

#region TextChange

        public static TextChange.Insert AsInsert(this TextChange change)
        {
            return (TextChange.Insert)change;
        }

        public static TextChange.DeleteLeft AsDeleteLeft(this TextChange change)
        {
            return (TextChange.DeleteLeft)change;
        }

        public static TextChange.DeleteRight AsDeleteRight(this TextChange change)
        {
            return (TextChange.DeleteRight)change;
        }

        public static TextChange.Combination AsCombination(this TextChange change)
        {
            return (TextChange.Combination)change;
        }

        public static bool IsInsert(this TextChange change, string text)
        {
            return change.IsInsert && change.AsInsert().Text == text;
        }

        public static bool IsDeleteLeft(this TextChange change, int count)
        {
            return change.IsDeleteLeft && change.AsDeleteLeft().Count == count;
        }

        public static bool IsDeleteRight(this TextChange change, int count)
        {
            return change.IsDeleteRight && change.AsDeleteRight().Count == count;
        }

#endregion

#region SnapshotSpan

        /// <summary>
        /// Convert the SnapshotSpan into an EditSpan
        /// </summary>
        public static EditSpan ToEditSpan(this SnapshotSpan span) => ToEditSpan(new SnapshotColumnSpan(span));

        /// <summary>
        /// Convert the SnapshotSpan into an EditSpan
        /// </summary>
        public static EditSpan ToEditSpan(this SnapshotColumnSpan span) => EditSpan.NewSingle(span);

#endregion

#region IMarkMap

        public static void SetLocalMark(this IMarkMap markMap, char c, IVimBufferData vimBufferData, int line, int column)
        {
            var letter = Letter.OfChar(c).Value;
            var mark = Mark.NewLocalMark(LocalMark.NewLetter(letter));
            markMap.SetMark(mark, vimBufferData, line, column);
        }

#endregion

#region VisualElement

        public static TextComposition CreateTextComposition(this FrameworkElement frameworkElement, string text, InputManager inputManager = null)
        {
            inputManager = inputManager ?? InputManager.Current;
            var textComposition = new TextComposition(inputManager, frameworkElement, text);
            if (text.Length == 1)
            {
                var c = text[0];
                if (char.IsControl(c))
                {
                    var type = typeof(TextComposition);
                    var method = type.GetMethod("MakeControl", BindingFlags.Instance | BindingFlags.NonPublic);
                    method.Invoke(textComposition, new object[] { });
                    Assert.True(string.IsNullOrEmpty(textComposition.Text));
                    Assert.Equal(text, textComposition.ControlText);
                }
                else if (0 != (c & 0x80))
                {
                    var type = typeof(TextComposition);
                    var method = type.GetMethod("MakeSystem", BindingFlags.Instance | BindingFlags.NonPublic);
                    method.Invoke(textComposition, new object[] { });
                    Assert.True(string.IsNullOrEmpty(textComposition.Text));
                    Assert.Equal(text, textComposition.SystemText);
                }
            }

            return textComposition;
        }

        public static TextCompositionEventArgs CreateTextCompositionEventArgs(this FrameworkElement frameworkElement, string text, InputDevice inputDevice, InputManager inputManager = null)
        {
            var textComposition = CreateTextComposition(frameworkElement, text, inputManager);
            var args = new TextCompositionEventArgs(inputDevice, textComposition)
            {
                RoutedEvent = UIElement.TextInputEvent
            };
            return args;
        }

#endregion

#region HistoryList

        public static void AddRange(this HistoryList historyList, params string[] values)
        {
            foreach (var cur in values)
            {
                historyList.Add(cur);
            }
        }

#endregion

#region IVimData

        public static void AddAutoCommand(this IVimData vimData, EventKind eventKind, string pattern, string command)
        {
            var autoCommand = new AutoCommand(
                AutoCommandGroup.Default,
                eventKind,
                command,
                pattern);
            vimData.AutoCommands = vimData.AutoCommands.Concat(new[] { autoCommand }).ToFSharpList();
        }

#endregion

#region SynchronizationContext

        public static SynchronizationContext GetEffectiveSynchronizationContext(this SynchronizationContext context)
        {
            if (context is AsyncTestSyncContext asyncTestSyncContext)
            {
                SynchronizationContext innerSynchronizationContext = null;
                asyncTestSyncContext.Send(
                    _ =>
                    {
                        innerSynchronizationContext = SynchronizationContext.Current;
                    },
                    null);

                return innerSynchronizationContext;
            }
            else
            {
                return context;
            }
        }

#endregion

        /// <summary>
        /// Run the specified motion with default arguments
        /// </summary>
        public static FSharpOption<MotionResult> GetMotion(this IMotionUtil motionUtil, Motion motion)
        {
            var arg = new MotionArgument(MotionContext.AfterOperator, FSharpOption<int>.None, FSharpOption<int>.None);
            return motionUtil.GetMotion(motion, arg);
        }

        public static SnapshotSpan GetSpan(this ITextSelection selection)
        {
            var span = new SnapshotSpan(selection.Start.Position, selection.End.Position);
            return span;
        }

        public static void Select(this ITextSelection selection, int start, int length)
        {
            var snapshotSpan = new SnapshotSpan(selection.TextView.TextSnapshot, start, length);
            selection.Select(snapshotSpan);
        }

        /// <summary>
        /// If you don't explicitly use VirtualSnapshotPoint values then the selection APIs will truncate
        /// line break selections.  
        /// </summary>
        public static void Select(this ITextSelection selection, SnapshotPoint startPoint, SnapshotPoint endPoint)
        {
            selection.Select(new VirtualSnapshotPoint(startPoint), new VirtualSnapshotPoint(endPoint));
        }

        public static void Select(this ITextSelection selection, params SnapshotSpan[] spans)
        {
            if (spans.Length == 1)
            {
                selection.Mode = TextSelectionMode.Stream;
                selection.Clear();
                selection.Select(spans[0], false);
            }
            else
            {
                selection.Mode = TextSelectionMode.Box;
                foreach (var span in spans)
                {
                    selection.Select(span, false);
                }
            }
        }

        public static void UpdateValue(this Register reg, string value)
        {
            UpdateValue(reg, value, OperationKind.CharacterWise);
        }

        /// <summary>
        /// Update the value with the string followed by the specified VimKey values
        /// </summary>
        public static void UpdateValue(this Register reg, string value, params VimKey[] keys)
        {
            var left = value.Select(KeyInputUtil.CharToKeyInput);
            var right = keys.Select(KeyInputUtil.VimKeyToKeyInput);
            var all = left.Concat(right).ToFSharpList();
            reg.RegisterValue = new RegisterValue(all);
        }

        /// <summary>
        /// Update the value with the spcefied set of VimKey values
        /// </summary>
        public static void UpdateValue(this Register reg, params VimKey[] keys)
        {
            var all = keys.Select(KeyInputUtil.VimKeyToKeyInput).ToFSharpList();
            reg.RegisterValue = new RegisterValue(all);
        }

        /// <summary>
        /// Update the value with the specified set of KeyInput values
        /// </summary>
        public static void UpdateValue(this Register reg, params KeyInput[] keys)
        {
            reg.RegisterValue = new RegisterValue(keys.ToFSharpList());
        }

        public static void UpdateValue(this Register reg, string value, OperationKind kind)
        {
            reg.RegisterValue = new RegisterValue(value, kind);
        }

        public static void UpdateBlockValues(this Register reg, params string[] value)
        {
            var col = NonEmptyCollectionUtil.OfSeq(value).Value;
            var data = StringData.NewBlock(col);
            reg.RegisterValue = new RegisterValue(data, OperationKind.CharacterWise);
        }

        public static VirtualSnapshotPoint GetCaretVirtualPoint(this ITextView view)
        {
            return view.Caret.Position.VirtualBufferPosition;
        }

        public static SnapshotColumn GetCaretColumn(this ITextView textView)
        {
            return new SnapshotColumn(textView.GetCaretPoint());
        }

        public static VirtualSnapshotSpan GetVirtualSelectionSpan(this ITextView textView)
        {
            return textView.Selection.StreamSelectionSpan;
        }

        public static SnapshotSpan GetSelectionSpan(this ITextView textView)
        {
            return textView.Selection.StreamSelectionSpan.SnapshotSpan;
        }

        public static BlockSpan GetSelectionBlockSpan(this ITextView textView, int tabStop)
        {
            Assert.Equal(TextSelectionMode.Box, textView.Selection.Mode);
            var spans = textView.Selection.VirtualSelectedSpans;
            var first = spans[0];
            return new BlockSpan(first.Start, tabStop, first.Length, spans.Count, false);
        }

        public static Register GetRegister(this IRegisterMap map, char c)
        {
            var name = RegisterNameUtil.CharToRegister(c).Value;
            return map.GetRegister(name);
        }

        public static string GetRegisterText(this IRegisterMap map, char c)
        {
            var register = GetRegister(map, c);
            return register.StringValue;
        }

        public static Register GetRegister(this IRegisterMap map, int number)
        {
            var name = RegisterNameUtil.NumberToRegister(number).Value;
            return map.GetRegister(name);
        }

        public static void Clear(this IRegisterMap map)
        {
            foreach (var name in NamedRegister.All)
            {
                map.SetRegisterValue(name.Char, string.Empty);
            }
        }

        public static void SetRegisterValue(this IRegisterMap map, char c, string value)
        {
            var register = GetRegister(map, c);
            register.UpdateValue(value);
        }

        public static void SetRegisterValue(this IRegisterMap map, int n, string value)
        {
            var register = GetRegister(map, n);
            register.UpdateValue(value);
        }

        public static bool IsSome<T>(this FSharpOption<T> option, T value)
        {
            Assert.True(option.IsSome(), "Option is None");
            Assert.Equal(value, option.Value);
            return true;
        }

        public static bool IsSome<T>(this FSharpOption<T> option, Func<T, bool> func)
        {
            Assert.True(option.IsSome());
            Assert.True(func(option.Value));
            return true;
        }

        public static SnapshotColumn GetColumn(this SnapshotPoint point)
        {
            return new SnapshotColumn(point);
        }

        /// <summary>
        /// Return the overaching SnapshotLineRange for the visible lines in the ITextView
        /// </summary>
        public static SnapshotLineRange? GetVisibleSnapshotLineRange(this ITextView textView)
        {
            if (textView.InLayout)
            {
                return null;
            }
            var snapshot = textView.TextSnapshot;
            var lines = textView.TextViewLines;
            var startLine = lines.FirstVisibleLine.Start.GetContainingLine().LineNumber;
            var lastLine = lines.LastVisibleLine.End.GetContainingLine().LineNumber;
            return SnapshotLineRange.CreateForLineNumberRange(textView.TextSnapshot, startLine, lastLine);
        }

        public static SnapshotLineRange GetLineRange(this ITextBuffer textBuffer, int startLine, int endLine = -1)
        {
            return textBuffer.CurrentSnapshot.GetLineRange(startLine, endLine);
        }

        internal static void WaitForBackgroundToComplete<TData, TTag>(this AsyncTagger<TData, TTag> asyncTagger, TestableSynchronizationContext synchronizationContext)
            where TTag : ITag
        {
            while (asyncTagger.AsyncBackgroundRequestData.IsSome())
            {
                synchronizationContext.RunAll();
                Thread.Yield();
            }
        }

        public static SnapshotLineRange GetLineRange(this ITextSnapshot snapshot, int startLine, int endLine = -1)
        {
            endLine = endLine >= 0 ? endLine : startLine;
            return SnapshotLineRange.CreateForLineNumberRange(snapshot, startLine, endLine).Value;
        }

        /// <summary>
        /// Convenient one liner to assert the option has a value and then return it.
        /// </summary>
        public static T AssertSome<T>(this FSharpOption<T> option)
        {
            Assert.True(option.IsSome());
            return option.Value;
        }

        public static string Substring(this string s, Span span) => s.Substring(span.Start, span.Length);
    }
}
