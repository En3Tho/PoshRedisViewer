module PoshRedisViewer.UIUtil

open System
open System.ComponentModel
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open En3Tho.FSharp.Extensions
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
}

module KeyQueryResultState =
    let toString (result: KeyQueryResultState) =
        let flags =
            seq {
                toString result.Keys.Length
                if result.FromHistory then "From History"
                if result.Filtered then "Filtered"
            } |> String.concat ", "
        $"Keys ({flags})"

type ResultsState = {
    ResultType: string
    Result: string[]
    FromHistory: bool
    Filtered: bool
}

module ResultsState =
    let toString (result: ResultsState) =
        let flags =
            seq {
                result.ResultType
                if result.FromHistory then "From History"
                if result.Filtered then "Filtered"
            } |> String.concat ", "

        $"Results ({flags})"

[<AbstractClass; Extension>]
type ViewExtensions() =
    [<Extension; EditorBrowsable(EditorBrowsableState.Never)>]
    static member inline Run(value: #View, [<InlineIfLambda>] runExpr: RunExpression) = runExpr(); value

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
                    ValueSome items.[newIndex]

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
                    ValueSome items.[newIndex]

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
                ValueSome items.[index]

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
            |> Array.map ^ fun member' -> $"Score: {member'.Score} | Value : {member'.Value}"
        | RedisStream ->
            [| "RedisStream is not supported" |]
        | RedisMultiResult values ->
            values
            |> Array.map toStringArray
            |> Array.concat

module Semaphore =
    let runTask (job: Task<'a>) (semaphore: SemaphoreSlim) = task {
        do! semaphore.WaitAsync()
        try
            return! job
        finally
            semaphore.Release() |> ignore
    }

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
    let private copyCommandKey =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Key.CtrlMask ||| Key.C
        else
            Key.CtrlMask ||| Key.Y

    let private pasteFromMiniClipboardKey = Key.CtrlMask ||| Key.B

    let private is flag (key: Key) = key |> Enum.hasFlag flag |> Option.ofBool

    let (|CopyCommand|_|) key = key |> is copyCommandKey
    let (|PasteFromMiniClipboardCommand|_|) key = key |> is pasteFromMiniClipboardKey

module StringSource =
    let filter filter (source: string[]) =
        match filter with
        | "" -> source
        | filter ->
            source |> Array.filter ^ fun x -> x.Contains(filter, StringComparison.OrdinalIgnoreCase)

module View =
    let preventCursorUpDownKeyPressedEvents (view: #View) =
        view.add_KeyPress(fun keyPressEvent ->
            match keyPressEvent.KeyEvent.Key with
            | Key.CursorUp
            | Key.CursorDown ->
                keyPressEvent.Handled <- true
            | _ -> ()
        )
        view

module Clipboard =
    let mutable private miniClipboard = ""
    let saveToClipboard text =
        let text = if Object.ReferenceEquals(text, null) then "" else text.ToString()
        Clipboard.TrySetClipboardData text |> ignore
        miniClipboard <- text

    let getFromMiniClipboard() = miniClipboard


module ListView =
    let private copySelectedItemTextToClipboard textMapper (listView: ListView) =
        let source = listView.Source.ToList()
        match source.[listView.SelectedItem].ToString() with
        | NotNull as selectedItem ->
            selectedItem
            |> textMapper
            |> Clipboard.saveToClipboard
        | _ -> ()

    let addValueCopyOnRightClick textMapper (listView: ListView) =
        listView.add_MouseClick(fun mouseClickEvent ->
        if listView.HasFocus then
            match mouseClickEvent.MouseEvent.Flags with
            | Enum.HasFlag MouseFlags.Button3Released ->
                copySelectedItemTextToClipboard textMapper listView
            | _ -> ()
        )
        listView

    let addValueCopyOnCopyHotKey textMapper (listView: ListView) =
        listView.add_KeyDown(fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.CopyCommand ->
                copySelectedItemTextToClipboard textMapper listView
            | _ -> ()
        )
        listView

module TextField =

    let addCopyPasteSupportWithMiniClipboard (textField: TextField) =
        textField.add_KeyDown(fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.CopyCommand ->
                textField.SelectedText |> Clipboard.saveToClipboard
            | Key.PasteFromMiniClipboardCommand ->
                let newText, newCursorPosition =
                    let clipboardText = Clipboard.getFromMiniClipboard()

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
            | _ -> ()
        )
        textField