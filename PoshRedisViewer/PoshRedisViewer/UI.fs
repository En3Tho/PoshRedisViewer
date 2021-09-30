module PoshRedisViewer.UI

open System.Threading
open En3Tho.FSharp.Extensions
open En3Tho.FSharp.ComputationExpressions.SCollectionBuilder
open NStack
open PoshRedisViewer.Redis
open PoshRedisViewer.UIUtil
open FSharp.Control.Tasks
open Terminal.Gui

#nowarn "0058"

type IConnectionMultiplexer = StackExchange.Redis.IConnectionMultiplexer

let shutdown() = Application.Shutdown()

let runApp(multiplexer: IConnectionMultiplexer) =
    Application.Init()

    let window = new Window(
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    let keyQueryFrameView = new FrameView(ustr "KeyQuery",
        Width = Dim.Percent(70.f),
        Height = Dim.Sized 3
    )

    let keyQueryTextField = new TextField(
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        Text = ustr "*"
    )

    let keyQueryFilterFrameView = new FrameView(ustr "Keys Filter",
        X = Pos.Right keyQueryFrameView,
        Width = Dim.Percent(25.f),
        Height = Dim.Sized 3
    )

    let keyQueryFilterTextField = new TextField(
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        Text = ustr ""
    )

    let dbPickerFrameView = new FrameView(ustr "DB",
        X = Pos.Right keyQueryFilterFrameView,
        Width = Dim.Percent(5.f),
        Height = Dim.Sized 3
    )

    let dbPickerComboBox = new ComboBox(ustr "0",
        X = Pos.Left dbPickerFrameView + Pos.At 1,
        Y = Pos.Top dbPickerFrameView + Pos.At 1,
        Width = Dim.Width dbPickerFrameView - Dim.Sized 2,
        Height = Dim.Sized 16,
        ReadOnly = true
    )

    dbPickerComboBox.SetSource([|
        for i = 0 to 15 do ustr (string i)
    |])

    let keysFrameView = new FrameView(ustr "Keys",
        Y = Pos.Bottom keyQueryFrameView,
        Width = Dim.Fill(),
        Height = Dim.Percent(50.f) - Dim.Sized 3
    )

    let keysListView = new ListView(
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    let resultsFrameView = new FrameView(ustr "Results",
        Y = Pos.Bottom keysFrameView,
        Width = Dim.Fill(),
        Height = Dim.Fill() - Dim.Sized 3
    )

    let resultsListView = new ListView(
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    let commandFrameView = new FrameView(ustr "Command",
        Y = Pos.Bottom resultsFrameView,
        Width = Dim.Percent(70.f),
        Height = Dim.Sized 3
    )

    let commandTextField = new TextField(
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        Text = ustring.Empty
    )

    let resultFilterFrameView = new FrameView(ustr "Results Filter",
        X = Pos.Right commandFrameView,
        Y = Pos.Bottom resultsFrameView,
        Width = Dim.Percent(30.f),
        Height = Dim.Sized 3
    )

    let resultFilterTextField = new TextField(
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        Text = ustr ""
    )

    let semaphore = new SemaphoreSlim(1)

    View.preventCursorUpDownKeyPressedEvents keyQueryTextField
    let keyQueryHistory = ResultHistoryCache(100)
    keyQueryTextField.add_KeyDown(fun keyDownEvent ->

        let filterSourceAndSetKeyQueryResultFromHistory keyQuery source =
            let filteredSource = source |> StringSource.filter (Ustr.toString keyQueryFilterTextField.Text)
            keyQueryTextField.Text <- ustr keyQuery
            keysFrameView.Title <- ustr "Keys (From History)"
            keysListView.SetSource filteredSource

        match keyDownEvent.KeyEvent.Key with
        | Key.Enter ->
           semaphore |> Semaphore.runTask ^ task {
                let database = dbPickerComboBox.SelectedItem
                let pattern = keyQueryTextField.Text.ToString()
                keysFrameView.Title <- ustr "Keys (processing)"

                let! keys = pattern |> RedisReader.getKeys multiplexer database
                let source = keys |> RedisResult.toStringArray
                source |> Array.sortInPlace
                let filteredSource = source |> StringSource.filter (Ustr.toString keyQueryFilterTextField.Text)

                keyQueryHistory.Add(pattern, source)
                keysListView.SetSource(filteredSource)
                keysFrameView.Title <- ustr "Keys"
            }
           |> ignore
        | Key.CursorUp ->
            match keyQueryHistory.Up() with
            | ValueSome { Key = keyQuery; Value = source } ->
                filterSourceAndSetKeyQueryResultFromHistory keyQuery source
            | _ -> ()
        | Key.CursorDown ->
            match keyQueryHistory.Down() with
            | ValueSome { Key = keyQuery; Value = source } ->
                filterSourceAndSetKeyQueryResultFromHistory keyQuery source
            | _ -> ()
        | Key.CopyCommand ->
            Clipboard.TrySetClipboardData(keyQueryTextField.Text.ToString()) |> ignore
        | _ -> ()
        keyDownEvent.Handled <- true
    )

    View.preventCursorUpDownKeyPressedEvents keyQueryFilterTextField
    keyQueryFilterTextField.add_KeyDown(fun keyDownEvent ->
        match keyDownEvent.KeyEvent.Key with
        | Key.Enter ->
            match keyQueryHistory.TryReadCurrent() with
            | ValueSome { Value = source } ->
                let filteredSource = source |> StringSource.filter (Ustr.toString keyQueryFilterTextField.Text)
                keysListView.SetSource filteredSource
            | _ -> ()
        | Key.CopyCommand ->
            Clipboard.TrySetClipboardData(keyQueryFilterTextField.Text.ToString()) |> ignore
        | _ -> ()
        keyDownEvent.Handled <- true
    )

    View.preventCursorUpDownKeyPressedEvents dbPickerComboBox
    let mutable resultsFromKeyQuery = [||]
    keysListView.add_SelectedItemChanged(fun selectedItemChangedEvent ->
        semaphore |> Semaphore.runTask ^ task {
            match selectedItemChangedEvent.Value with
            | null -> ()
            | value ->
                let key = value.ToString()
                let database = dbPickerComboBox.SelectedItem
                resultsFrameView.Title <- ustr "Results (processing)"
                let! keyValue = key |> RedisReader.getKeyValue multiplexer database
                let source = keyValue |> RedisResult.toStringArray
                let filteredSource = source |> StringSource.filter (Ustr.toString resultFilterTextField.Text)
                resultsFromKeyQuery <- source
                resultsListView.SetSource filteredSource
                resultsFrameView.Title <- ustr $"Results ({Union.getName keyValue})"
        }
        |> ignore
    )

    keysListView.add_KeyDown(fun keyDownEvent ->
        match keyDownEvent.KeyEvent.Key with
        | Key.CopyCommand ->
            let source = keysListView.Source.ToList()
            let selectedItem = source.[keysListView.SelectedItem].ToString()
            Clipboard.TrySetClipboardData(selectedItem.ToString()) |> ignore
        | _ -> ()
        keyDownEvent.Handled <- true
    )

    resultsListView.add_KeyDown(fun keyDownEvent ->
        match keyDownEvent.KeyEvent.Key with
        | Key.CopyCommand ->
            let source = resultsListView.Source.ToList()
            let selectedItem = source.[resultsListView.SelectedItem].ToString()
            Clipboard.TrySetClipboardData(selectedItem.ToString()) |> ignore
        | _ -> ()
        keyDownEvent.Handled <- true
    )

    View.preventCursorUpDownKeyPressedEvents commandTextField
    let resultsHistory = ResultHistoryCache(100)
    commandTextField.add_KeyDown(fun keyDownEvent ->

        let filterSourceAndSetCommandResultFromHistory command source =
            let filteredSource = source |> StringSource.filter (Ustr.toString resultFilterTextField.Text)
            commandTextField.Text <- ustr command
            resultsFrameView.Title <- ustr "Results (From History)"
            resultsListView.SetSource filteredSource

        match keyDownEvent.KeyEvent.Key with
        | Key.Enter ->
            semaphore |> Semaphore.runTask ^ task {
                let database = dbPickerComboBox.SelectedItem
                let command = commandTextField.Text.ToString()
                resultsFrameView.Title <- ustr "Results (processing)"
                let! commandResult =
                    command
                    |> RedisReader.execCommand multiplexer database
                let source =
                    commandResult
                    |> RedisResult.toStringArray

                resultsFromKeyQuery <- [||]
                let filteredSource = source |> StringSource.filter (Ustr.toString resultFilterTextField.Text)

                resultsHistory.Add(command, source)
                resultsListView.SetSource(filteredSource)
                resultsFrameView.Title <- ustr "Results"
            }
            |> ignore
        | Key.CursorUp ->
            match resultsHistory.Up() with
            | ValueSome { Key = command; Value = source } ->
                filterSourceAndSetCommandResultFromHistory command source
            | _ -> ()
        | Key.CursorDown ->
            match resultsHistory.Down() with
            | ValueSome { Key = command; Value = source } ->
                filterSourceAndSetCommandResultFromHistory command source
            | _ -> ()
        | _ -> ()
        keyDownEvent.Handled <- true
    )

    View.preventCursorUpDownKeyPressedEvents resultFilterTextField
    resultFilterTextField.add_KeyDown(fun keyDownEvent ->
        match keyDownEvent.KeyEvent.Key with
        | Key.Enter ->
            match resultsFromKeyQuery, resultsHistory.TryReadCurrent() with
            | Array.NotNullOrEmpty as source, _
            | _, ValueSome { Value = source } ->
                let filteredSource = source |> StringSource.filter (Ustr.toString resultFilterTextField.Text)
                resultsListView.SetSource filteredSource
            | _ -> ()
        | Key.CopyCommand ->
            Clipboard.TrySetClipboardData(resultFilterTextField.Text.ToString()) |> ignore
        | _ -> ()
        keyDownEvent.Handled <- true
    )

    Application.Top {
        window {
            keyQueryFrameView {
                keyQueryTextField
            }
            keyQueryFilterFrameView {
                keyQueryFilterTextField
            }
            dbPickerFrameView
            dbPickerComboBox
            keysFrameView {
                keysListView
            }
            resultsFrameView {
                resultsListView
            }
            commandFrameView {
                commandTextField
            }
            resultFilterFrameView {
                resultFilterTextField
            }
        }
    } |> ignore

    window.Subviews.[0].BringSubviewToFront(dbPickerComboBox)

    Application.Run Application.Top