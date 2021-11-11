module PoshRedisViewer.UI

open System
open System.Threading
open En3Tho.FSharp.Extensions
open En3Tho.FSharp.ComputationExpressions.SCollectionBuilder
open NStack
open PoshRedisViewer.Redis
open PoshRedisViewer.UIUtil
open PoshRedisViewer.UIUtil
open PoshRedisViewer.UIUtil
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

    let mutable keyQueryResultState = { KeyQueryResultState.Keys = [||]; Filtered = false; FromHistory = false }
    let filterKeyQueryResult keys = keys |> StringSource.filter (Ustr.toString keyQueryFilterTextField.Text)
    let updateKeyQueryFieldsWithNewState state =
        keyQueryResultState <- state
        keysFrameView.Title <- keyQueryResultState |> KeyQueryResultState.toString |> ustr
        keysListView.SetSource state.Keys

    let mutable resultState = { ResultsState.Result = [||]; ResultType = ""; Filtered = false; FromHistory = false }
    let filterCommandResult keys = keys |> StringSource.filter (Ustr.toString resultFilterTextField.Text)
    let updateResultsFieldsWithNewState state =
        resultState <- state
        resultsFrameView.Title <- resultState |> ResultsState.toString |> ustr
        resultsListView.SetSource state.Result

    View.preventCursorUpDownKeyPressedEvents keyQueryTextField
    let keyQueryHistory = ResultHistoryCache(100)

    keyQueryTextField.add_KeyDown(fun keyDownEvent ->

        let filterSourceAndSetKeyQueryResultFromHistory keyQuery keys =
            keyQueryTextField.Text <- ustr keyQuery
            updateKeyQueryFieldsWithNewState { keyQueryResultState with Keys = filterKeyQueryResult keys; FromHistory = true }

        match keyDownEvent.KeyEvent.Key with
        | Key.Enter ->
           semaphore |> Semaphore.runTask ^ task {
                let database = dbPickerComboBox.SelectedItem
                let pattern = keyQueryTextField.Text.ToString()
                keysFrameView.Title <- ustr "Keys (processing)"

                let! keys = pattern |> RedisReader.getKeys multiplexer database
                let keys = keys |> RedisResult.toStringArray
                keys |> Array.sortInPlace
                keyQueryHistory.Add(pattern, keys)

                updateKeyQueryFieldsWithNewState { keyQueryResultState with Keys = filterKeyQueryResult keys; FromHistory = false }
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
    )

    View.preventCursorUpDownKeyPressedEvents keyQueryFilterTextField
    keyQueryFilterTextField.add_KeyDown(fun keyDownEvent ->
        match keyDownEvent.KeyEvent.Key with
        | Key.Enter ->
            match keyQueryHistory.TryReadCurrent() with
            | ValueSome { Value = keys } ->
                let filter = Ustr.toString keyQueryFilterTextField.Text
                updateKeyQueryFieldsWithNewState { keyQueryResultState with Keys = filterKeyQueryResult keys; Filtered = not ^ String.IsNullOrEmpty filter }
            | _ -> ()
        | Key.CopyCommand ->
            Clipboard.TrySetClipboardData(keyQueryFilterTextField.Text.ToString()) |> ignore
        | _ -> ()
    )

    View.preventCursorUpDownKeyPressedEvents dbPickerComboBox
    let mutable resultsFromKeyQuery = ValueSome [||]
    keysListView.add_SelectedItemChanged(fun selectedItemChangedEvent ->
        semaphore |> Semaphore.runTask ^ task {
            match selectedItemChangedEvent.Value with
            | null -> ()
            | value ->
                let key = value.ToString()
                let database = dbPickerComboBox.SelectedItem
                resultsFrameView.Title <- ustr "Results (processing)"
                let! keyValue = key |> RedisReader.getKeyValue multiplexer database
                let result = keyValue |> RedisResult.toStringArray

                resultsFromKeyQuery <- ValueSome result
                updateResultsFieldsWithNewState { resultState with Result = filterCommandResult result; ResultType = Union.getName keyValue; FromHistory = false }
        }
        |> ignore
    )

    ListView.addValueCopyOnRightClick keysListView
    ListView.addValueCopyOnCopyHotKey keysListView

    ListView.addValueCopyOnRightClick resultsListView
    ListView.addValueCopyOnCopyHotKey resultsListView

    View.preventCursorUpDownKeyPressedEvents commandTextField
    let resultsHistory = ResultHistoryCache(100)
    commandTextField.add_KeyDown(fun keyDownEvent ->

        let filterSourceAndSetCommandResultFromHistory command commandResult =
            commandTextField.Text <- ustr command
            updateResultsFieldsWithNewState { resultState with Result = filterCommandResult commandResult; ResultType = "Command"; FromHistory = true }

        match keyDownEvent.KeyEvent.Key with
        | Key.Enter ->
            semaphore |> Semaphore.runTask ^ task {
                let database = dbPickerComboBox.SelectedItem
                let command = commandTextField.Text.ToString()
                resultsFrameView.Title <- ustr "Results (Processing)"
                let! commandResult =
                    command
                    |> RedisReader.execCommand multiplexer database
                let commandResult =
                    commandResult
                    |> RedisResult.toStringArray

                resultsHistory.Add(command, commandResult)
                resultsFromKeyQuery <- ValueNone

                let filter = Ustr.toString resultFilterTextField.Text
                updateResultsFieldsWithNewState { resultState with Result = filterCommandResult commandResult; ResultType = "Command"; Filtered = not ^ String.IsNullOrEmpty filter }
            }
            |> ignore
        | Key.CursorUp ->
            match resultsHistory.Up() with
            | ValueSome { Key = command; Value = results } ->
                filterSourceAndSetCommandResultFromHistory command results
            | _ -> ()
        | Key.CursorDown ->
            match resultsHistory.Down() with
            | ValueSome { Key = command; Value = results } ->
                filterSourceAndSetCommandResultFromHistory command results
            | _ -> ()
        | _ -> ()
        keyDownEvent.Handled <- true
    )

    View.preventCursorUpDownKeyPressedEvents resultFilterTextField
    resultFilterTextField.add_KeyDown(fun keyDownEvent ->
        match keyDownEvent.KeyEvent.Key with
        | Key.Enter ->
            match resultsFromKeyQuery, resultsHistory.TryReadCurrent() with
            | ValueSome commandResult, _
            | _, ValueSome { Value = commandResult } ->
                let filter = Ustr.toString resultFilterTextField.Text
                updateResultsFieldsWithNewState { resultState with Result = filterCommandResult commandResult; Filtered = not ^ String.IsNullOrEmpty filter }
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
    } |> fun top ->
        window.Subviews.[0].BringSubviewToFront(dbPickerComboBox)
        top |> Application.Run