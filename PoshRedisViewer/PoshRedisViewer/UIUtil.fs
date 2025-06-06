﻿module PoshRedisViewer.UIUtil

open System
open System.ComponentModel
open System.Runtime.CompilerServices
open System.Text.RegularExpressions
open En3Tho.FSharp.Extensions
open En3Tho.FSharp.ComputationExpressions
open En3Tho.FSharp.ComputationExpressions.SStringBuilderBuilder
open NStack
open PoshRedisViewer.Redis

open Terminal.Gui

type HistorySlot<'a, 'b> = {
    Key: 'a
    Value: 'b
}

type KeyQueryResultState = {
    Keys: string[]
    FromHistory: bool
    Filtered: bool
    Time: DateTimeOffset
}

module KeyQueryResultState =
    let toString (result: KeyQueryResultState) =
        let flags =
            seq {
                toString result.Keys.Length
                if result.FromHistory then "From History"
                if result.Filtered then "Filtered"
                result.Time.ToString()
            } |> String.concat ", "
        $"Keys ({flags})"

type ResultsState = {
    ResultType: string
    Result: string[]
    FromHistory: bool
    Filtered: bool
    Time: DateTimeOffset
}

module ResultsState =
    let toString (result: ResultsState) =
        let flags =
            seq {
                result.ResultType
                if result.FromHistory then "From History"
                if result.Filtered then "Filtered"
                result.Time.ToString()
            } |> String.concat ", "

        $"Results ({flags})"

[<AbstractClass; Extension>]
type ViewExtensions() =
    [<Extension; EditorBrowsable(EditorBrowsableState.Never)>]
    static member inline Run(value: #View, [<InlineIfLambda>] runExpr: CollectionCode) = runExpr(); value

type ResultHistoryCache<'a, 'b when 'a: equality>(capacity: int) =
    let syncRoot = obj()
    let items = ResizeArray<HistorySlot<'a, 'b>>(capacity)
    let mutable index = 0

    member _.Up() =
        lock syncRoot ^ fun() ->
            match items.Count, index - 1 with
            | 0, _ ->
                ValueNone
            | currentCount, newIndex ->
                if uint newIndex >= uint currentCount then
                    ValueNone
                else
                    index <- newIndex
                    ValueSome items[newIndex]

    member _.Down() =
        lock syncRoot ^ fun() ->
            match items.Count, index + 1 with
            | 0, _ ->
                ValueNone
            | currentCount, newIndex ->
                if uint newIndex >= uint currentCount then
                    ValueNone
                else
                    index <- newIndex
                    ValueSome items[newIndex]

    member _.Add(key, value) =
        lock syncRoot ^ fun() ->
            if items.Count = 0 then
                ()
            else
                match items.FindIndex(fun slot -> slot.Key = key) with
                | -1 -> ()
                | index ->
                    items.RemoveAt index

            items.Add { Key = key; Value = value }
            if items.Count > capacity then
                items.RemoveAt 0
            index <- items.Count - 1

    member _.TryReadCurrent() =
        lock syncRoot ^ fun() ->
            let index = index
            if uint index >= uint items.Count then
                ValueNone
            else
                ValueSome items[index]

type UIState = {
    Multiplexer: StackExchange.Redis.IConnectionMultiplexer
    mutable KeyQueryResultState: KeyQueryResultState
    mutable ResultsState: ResultsState
    mutable ResultsFromKeyQuery: ValueOption<string[]>
    KeyQueryHistory: ResultHistoryCache<string, string array * DateTimeOffset>
    ResultsHistory: ResultHistoryCache<string, string array * DateTimeOffset>
}

module rec RedisResult =

    let toStringArray (value: RedisResult) =
        match value with
        | RedisString str ->
            [| str |]
        | RedisList strings ->
            strings
            |> Array.map ^ fun member' -> $"Index: {member'.Index} | Value: {member'.Value}"
        | RedisError e ->
            e.ToString().Split(Environment.NewLine)
        | RedisHash members ->
            members
            |> Array.map ^ fun member' -> $"Field: {member'.Field} | Value: {member'.Value}"
        | RedisNone ->
            [| "None" |]
        | RedisSet strings ->
            strings
        | RedisSortedSet members ->
            members
            |> Array.map ^ fun member' -> $"Score: {member'.Score} | Value: {member'.Value}"
        | RedisStream ->
            [| "RedisStream is not supported" |]
        | RedisMultiResult values ->
            values
            |> Array.map toStringArray
            |> Array.concat

    let getInformationText (value: RedisResult) =
        match value with
        | RedisString _ ->
            "RedisString"
        | RedisList strings ->
            $"RedisList ({strings.Length})"
        | RedisError _ ->
            "RedisError"
        | RedisHash members ->
            $"RedisHash ({members.Length})"
        | RedisNone ->
            "RedisNone"
        | RedisSet strings ->
            $"RedisSet ({strings.Length})"
        | RedisSortedSet members ->
            $"RedisSortedSet ({members.Length})"
        | RedisStream ->
            "RedisStream"
        | RedisMultiResult values ->
            $"RedisMultiResult ({values.Length})"

let ustr str = icast<string, ustring> str
module Ustr =

    let toString (utext: ustring) =
        match utext with
        | null -> ""
        | _ -> utext.ToString()

    let fromString (text: string) =
        match text with
        | null | "" -> ustring.Empty
        | _ -> ustr text

module Key =
    let private copyKey = Key.CtrlMask ||| Key.C
    let private pasteKey = Key.CtrlMask ||| Key.V

    let private is flag (key: Key) = key |> Enum.hasFlag flag |> Option.ofBool

    let (|CopyCommand|_|) key = key |> is copyKey
    let (|PasteCommand|_|) key = key |> is pasteKey

[<RequireQualifiedAccess>]
type FilterType =
    | Contains
    | Regex

module Filter =
    let stringContains (pattern: string) (value: string) =
        value.Contains(pattern, StringComparison.OrdinalIgnoreCase)

    let regex (pattern: string) (value: string) =
        let regex = Regex(pattern, RegexOptions.Compiled)
        regex.IsMatch(value)

module View =
    let preventKeyPressedEvents (events: Key[]) (view: #View) =
        view.add_KeyPress(fun keyPressEvent ->
            match keyPressEvent.KeyEvent.Key with
            | key when events |> Array.contains key ->
                keyPressEvent.Handled <- true
            | _ -> ()
        )
        view

    let preventCursorUpDownKeyPressedEvents (view: #View) =
        view |> preventKeyPressedEvents [| Key.CursorUp; Key.CursorDown |]

type MiniClipboard(clipboard: IClipboard) =
    let mutable current = ""

    interface IClipboard with
        member this.GetClipboardData() =
            let original = try' { clipboard.GetClipboardData() }
            original |> String.defaultValue current

        member this.SetClipboardData(text) =
            current <- text
            try' { clipboard.SetClipboardData(current) }

        member this.TryGetClipboardData(result) =
            if not (clipboard.TryGetClipboardData(&result)) then
                result <- current
            true

        member this.TrySetClipboardData(text) =
            current <- text
            clipboard.TrySetClipboardData(text) |> ignore
            true

        member this.IsSupported = true

module Clipboard =

    let mutable MiniClipboard = nullRef<IClipboard>

    let saveToClipboard text =
        let text = if Object.ReferenceEquals(text, null) then "" else text.ToString()
        MiniClipboard.TrySetClipboardData text |> ignore

    let getFromClipboard() =
        let mutable text = nullRef
        MiniClipboard.TryGetClipboardData(&text) |> ignore
        text |> String.defaultValue ""

module ListView =

    type ListView with
        member this.TrySelectedItem() =
            match this.Source, this.SelectedItem with
            | (NotNull & source), (GtEq 0 & selectedItem) when source.Count >= 0 ->
                let value = source.ToList()[selectedItem]
                value |> ValueOption.ofObj
            | _ ->
                ValueNone

    let private copySelectedItemTextToClipboard (textMapper: string -> string) (listView: ListView) =
        match listView.TrySelectedItem() with
        | ValueSome selectedItem ->
            selectedItem
            |> toString
            |> textMapper
            |> Clipboard.saveToClipboard
        | _ -> ()

    let addValueCopyOnRightClick textMapper (listView: ListView) =
        listView.add_MouseClick(fun mouseClickEvent ->
        if listView.HasFocus then
            match mouseClickEvent.MouseEvent.Flags with
            | Enum.HasFlag MouseFlags.Button3Released ->
                copySelectedItemTextToClipboard textMapper listView
                mouseClickEvent.Handled <- true
            | _ -> ()
        )
        listView

    let addValueCopyOnCopyHotKey textMapper (listView: ListView) =
        listView.add_KeyDown(fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.CopyCommand ->
                copySelectedItemTextToClipboard textMapper listView
                keyDownEvent.Handled <- true
            | _ -> ()
        )
        listView

    let addDetailedViewOnEnterKey (listView: ListView) =
        listView.add_KeyDown (fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key, listView.TrySelectedItem() with
            | Key.Enter, ValueSome selectedItem ->
                MessageBox.Query("Value", selectedItem.ToString(), "Ok")
                |> ignore
                keyDownEvent.Handled <- true
            | _ -> ()
        )
        listView

module TextField =

    let addCopyPasteSupportWithMiniClipboard (textField: TextField) =
        textField.add_KeyDown(fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.CopyCommand ->
                textField.SelectedText |> Clipboard.saveToClipboard
                keyDownEvent.Handled <- true

            | Key.PasteCommand ->
                let newText, newCursorPosition =
                    let clipboardText = Clipboard.getFromClipboard()

                    match textField.SelectedLength, textField.CursorPosition with
                    | 0, 0 ->
                        clipboardText |> Ustr.fromString,
                        clipboardText.Length

                    | 0, cursor ->
                        let text = textField.Text |> Ustr.toString
                        let left = text.Substring(0, cursor)
                        let right = text.Substring(cursor)

                        left + clipboardText + right |> Ustr.fromString,
                        textField.CursorPosition + clipboardText.Length

                    | _, cursor ->
                        let text = textField.Text
                        text.Replace(textField.SelectedText, clipboardText |> Ustr.fromString, maxReplacements = 1),
                        if textField.SelectedStart < cursor then
                            textField.SelectedStart + clipboardText.Length
                        else
                            cursor + clipboardText.Length

                textField.Text <- newText
                textField.CursorPosition <- newCursorPosition
                keyDownEvent.Handled <- true
            | _ -> ()
        )
        textField