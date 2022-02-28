module PoshRedisViewer.UI

open System
open System.Threading
open En3Tho.FSharp.Extensions
open En3Tho.FSharp.ComputationExpressions.SCollectionBuilder
open NStack
open PoshRedisViewer.Redis
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
        Width = Dim.Percent(60.f),
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
        Width = Dim.Percent(15.f),
        Height = Dim.Sized 3
    )

    let dbPickerComboBox = new ComboBox(ustr "0",
        X = Pos.Left dbPickerFrameView + Pos.At 1,
        Y = Pos.Top dbPickerFrameView + Pos.At 1,
        Width = Dim.Width dbPickerFrameView - Dim.Sized 15,
        Height = Dim.Sized 16,
        ReadOnly = true
    )

    let dbPickerCheckBox = new CheckBox(ustr "Query All",
        X = Pos.Right dbPickerComboBox + Pos.At 1,
        Y = Pos.Top dbPickerComboBox
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

    let mutable keyQueryResultState = { KeyQueryResultState.Keys = [||]; Filtered = false; FromHistory = false; Time = DateTimeOffset() }
    let filterKeyQueryResult keys = keys |> StringSource.filter (Ustr.toString keyQueryFilterTextField.Text)
    let updateKeyQueryFieldsWithNewState state =
        keyQueryResultState <- state
        keysFrameView.Title <- keyQueryResultState |> KeyQueryResultState.toString |> ustr
        keysListView.SetSource state.Keys

    let mutable resultState = { ResultsState.Result = [||]; ResultType = ""; Filtered = false; FromHistory = false; Time = DateTimeOffset() }
    let filterCommandResult keys = keys |> StringSource.filter (Ustr.toString resultFilterTextField.Text)
    let updateResultsFieldsWithNewState state =
        resultState <- state
        resultsFrameView.Title <- resultState |> ResultsState.toString |> ustr
        resultsListView.SetSource state.Result

    let keyQueryHistory = ResultHistoryCache(100)

    keyQueryTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> fun keyQueryTextField ->
        keyQueryTextField.add_KeyDown(fun keyDownEvent ->

            let filterSourceAndSetKeyQueryResultFromHistory keyQuery keys time =
                keyQueryTextField.Text <- ustr keyQuery
                updateKeyQueryFieldsWithNewState { keyQueryResultState with
                   Keys = filterKeyQueryResult keys
                   FromHistory = true
                   Time = time
                }

            match keyDownEvent.KeyEvent.Key with
            | Key.Enter ->
               semaphore |> Semaphore.runTask ^ fun _ -> task {
                    let database =
                        if dbPickerCheckBox.Checked then
                            KeySearchDatabase.Range (0, 15)
                        else
                            KeySearchDatabase.Single dbPickerComboBox.SelectedItem

                    let pattern = keyQueryTextField.Text.ToString()
                    keysFrameView.Title <- ustr "Keys (processing)"

                    let! keys =
                        pattern
                        |> RedisReader.getKeys multiplexer database
                        |> Task.map RedisResult.toStringArray

                    let time = DateTimeOffset.Now
                    keys |> Array.sortInPlace
                    keyQueryHistory.Add(pattern, (keys, time))

                    updateKeyQueryFieldsWithNewState { keyQueryResultState with
                       Keys = filterKeyQueryResult keys
                       FromHistory = false
                       Time = time
                    }
               }
               |> ignore
            | Key.CursorUp ->
                match keyQueryHistory.Up() with
                | ValueSome { Key = keyQuery; Value = source, time; } ->
                    filterSourceAndSetKeyQueryResultFromHistory keyQuery source time
                | _ -> ()
            | Key.CursorDown ->
                match keyQueryHistory.Down() with
                | ValueSome { Key = keyQuery; Value = source, time; } ->
                    filterSourceAndSetKeyQueryResultFromHistory keyQuery source time
                | _ -> ()
            | _ -> ()
        )

    keyQueryFilterTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> fun keyQueryFilterTextField ->
        keyQueryFilterTextField.add_KeyDown(fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.Enter ->
                match keyQueryHistory.TryReadCurrent() with
                | ValueSome { Value = keys, time } ->
                    let filter = Ustr.toString keyQueryFilterTextField.Text
                    updateKeyQueryFieldsWithNewState { keyQueryResultState with
                       Keys = filterKeyQueryResult keys
                       Filtered = not ^ String.IsNullOrEmpty filter
                       Time = time
                    }
                | _ -> ()
            | _ -> ()
        )

    dbPickerComboBox |> View.preventCursorUpDownKeyPressedEvents |> ignore

    let mutable resultsFromKeyQuery = ValueSome [||]
    keysListView
    |> ListView.addValueCopyOnRightClick KeyFormatter.trimDatabaseHeader
    |> ListView.addValueCopyOnCopyHotKey KeyFormatter.trimDatabaseHeader
    |> fun keysListView ->
        keysListView.add_SelectedItemChanged(fun selectedItemChangedEvent ->
            semaphore |> Semaphore.runTask ^ fun _ -> task {
                match selectedItemChangedEvent.Value with
                | null -> ()
                | value ->
                    let database, key = value.ToString() |> KeyFormatter.getDatabaseAndOriginalKeyFromFormattedKeyString
                    resultsFrameView.Title <- ustr "Results (processing)"

                    let! keyValue = key |> RedisReader.getKeyValue multiplexer database
                    let result = keyValue |> RedisResult.toStringArray

                    resultsFromKeyQuery <- ValueSome result
                    updateResultsFieldsWithNewState { resultState with
                        Result = filterCommandResult result
                        ResultType = Union.getName keyValue
                        FromHistory = false
                        Time = DateTimeOffset.Now
                    }
            }
            |> ignore
        )

    resultsListView
    |> ListView.addValueCopyOnRightClick id
    |> ListView.addValueCopyOnCopyHotKey id
    |> ignore

    let resultsHistory = ResultHistoryCache(100)
    commandTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> fun commandTextField ->
        commandTextField.add_KeyDown(fun keyDownEvent ->

            let filterSourceAndSetCommandResultFromHistory command commandResult time =
                commandTextField.Text <- ustr command
                updateResultsFieldsWithNewState { resultState with
                    Result = filterCommandResult commandResult
                    ResultType = "Command"
                    FromHistory = true
                    Time = time
                }

            match keyDownEvent.KeyEvent.Key with
            | Key.Enter ->
                semaphore |> Semaphore.runTask ^ fun _ -> task {
                    let database = dbPickerComboBox.SelectedItem
                    let command = commandTextField.Text.ToString()
                    resultsFrameView.Title <- ustr "Results (Processing)"

                    let! commandResult =
                        command
                        |> RedisReader.execCommand multiplexer database
                        |> Task.map RedisResult.toStringArray

                    let time = DateTimeOffset.Now
                    resultsHistory.Add(command, (commandResult, time))
                    resultsFromKeyQuery <- ValueNone

                    let filter = Ustr.toString resultFilterTextField.Text
                    updateResultsFieldsWithNewState { resultState with
                      Result = filterCommandResult commandResult
                      ResultType = "Command"
                      Filtered = not ^ String.IsNullOrEmpty filter
                      Time = time
                    }
                }
                |> ignore
            | Key.CursorUp ->
                match resultsHistory.Up() with
                | ValueSome { Key = command; Value = results, time } ->
                    filterSourceAndSetCommandResultFromHistory command results time
                | _ -> ()
            | Key.CursorDown ->
                match resultsHistory.Down() with
                | ValueSome { Key = command; Value = results, time } ->
                    filterSourceAndSetCommandResultFromHistory command results time
                | _ -> ()
            | _ -> ()
            keyDownEvent.Handled <- true
        )

    resultFilterTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> fun resultFilterTextField ->
        resultFilterTextField.add_KeyDown(fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.Enter ->
                match resultsFromKeyQuery, resultsHistory.TryReadCurrent() with
                | ValueSome commandResult, _
                | _, ValueSome { Value = commandResult, _ } ->
                    let filter = Ustr.toString resultFilterTextField.Text
                    updateResultsFieldsWithNewState { resultState with
                        Result = filterCommandResult commandResult
                        Filtered = not ^ String.IsNullOrEmpty filter
                    }
                | _ -> ()
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
            dbPickerCheckBox
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
        window.Subviews.[0].BringSubviewToFront(dbPickerCheckBox)
        top |> Application.Run