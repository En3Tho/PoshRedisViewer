module PoshRedisViewer.UILogic

open System
open System.Threading
open En3Tho.FSharp.Extensions
open PoshRedisViewer.Redis
open PoshRedisViewer.UI
open PoshRedisViewer.UIUtil
open Terminal.Gui

#nowarn "0058"

let makeErrorHandler (views: Views) =
    fun (exn: Exception) ->
        views.ResultsListView.SetSource(exn.ToString().Split(Environment.NewLine))
        true

let getFilter (views: Views) =
    let filterType =
        match views.KeyQueryFilterTypeComboBox.SelectedItem with
        | 0 | 1 as idx ->
            views.KeyQueryFilterTypeComboBox.Source.ToList()[idx] :?> FilterType
        | _ ->
            FilterType.Contains

    match filterType with
    | FilterType.Contains ->
        Filter.stringContains
    | FilterType.Regex ->
        Filter.regex

let filterBy query (views: Views) keys =
    match query with
    | String.NullOrWhiteSpace ->
        keys
    | _ ->
        let filter = getFilter views
        keys |> Array.filter (filter query)

let filterKeyQueryResult results views =
    let query = Ustr.toString views.KeyQueryFilterTextField.Text
    results |> filterBy query views

let filterCommandResult (views: Views) results =
    let query = Ustr.toString views.ResultFilterTextField.Text
    results |> filterBy query views

let updateKeyQueryFieldsWithNewState (uiState: UIState) (views: Views) state =
    uiState.KeyQueryResultState <- state
    views.KeysFrameView.Title <- uiState.KeyQueryResultState |> KeyQueryResultState.toString |> ustr
    views.KeysListView.SetSource state.Keys

let updateResultsFieldsWithNewState (uiState: UIState) (views: Views) state =
    uiState.ResultsState <- state
    views.ResultsFrameView.Title <- state |> ResultsState.toString |> ustr
    views.ResultsListView.SetSource state.Result

module KeyQueryTextField =

    let addKeyDownEvenProcessing (uiState: UIState) (views: Views) (keyQueryTextField: TextField) =
        let filterSourceAndSetKeyQueryResultFromHistory keyQuery keys time =
            keyQueryTextField.Text <- ustr keyQuery
            let newKeyQueryState = { uiState.KeyQueryResultState with
                Keys = views |> filterKeyQueryResult keys
                FromHistory = true
                Time = time
            }
            updateKeyQueryFieldsWithNewState uiState views newKeyQueryState

        keyQueryTextField.add_KeyDown (fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.Enter ->
                uiState.Semaphore |> Semaphore.runTask ^ fun _ -> task {
                     let database =
                         if views.DbPickerCheckBox.Checked then
                             KeySearchDatabase.Range (0, 15)
                         else
                             KeySearchDatabase.Single views.DbPickerComboBox.SelectedItem

                     let pattern = keyQueryTextField.Text.ToString()
                     views.KeysFrameView.Title <- ustr "Keys (processing)"

                     let! keys =
                         pattern
                         |> RedisReader.getKeys uiState.Multiplexer database
                         |> Task.map RedisResult.toStringArray

                     keys |> Array.sortInPlace
                     let time = DateTimeOffset.Now
                     uiState.KeyQueryHistory.Add(pattern, (keys, time))

                     let newKeyQueryResultState = { uiState.KeyQueryResultState with
                         Keys = views |> filterKeyQueryResult keys
                         FromHistory = false
                         Time = time
                     }
                     updateKeyQueryFieldsWithNewState uiState views newKeyQueryResultState
                }
                |> ignore

            | Key.CursorUp ->
                match uiState.KeyQueryHistory.Up() with
                | ValueSome { Key = keyQuery; Value = source, time; } ->
                    filterSourceAndSetKeyQueryResultFromHistory keyQuery source time
                | _ -> ()

            | Key.CursorDown ->
                match uiState.KeyQueryHistory.Down() with
                | ValueSome { Key = keyQuery; Value = source, time; } ->
                    filterSourceAndSetKeyQueryResultFromHistory keyQuery source time
                | _ -> ()

            | _ -> ()
        )
        keyQueryTextField

module KeyQueryFilterTextField =

    let private processEnterKey (uiState: UIState) (views: Views) (keyQueryFilterTextField: TextField) =
        match uiState.KeyQueryHistory.TryReadCurrent() with
        | ValueSome { Value = keys, time } ->
            let filter = Ustr.toString keyQueryFilterTextField.Text
            let newKeyQueryResultState = { uiState.KeyQueryResultState with
                Keys = views |> filterKeyQueryResult keys
                Filtered = not ^ String.IsNullOrEmpty filter
                Time = time
            }
            updateKeyQueryFieldsWithNewState uiState views newKeyQueryResultState
        | _ -> ()

    let addKeyDownEventProcessing (uiState: UIState) (views: Views) (keyQueryFilterTextField: TextField) =
        keyQueryFilterTextField.add_KeyDown (fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.Enter ->
                processEnterKey uiState views keyQueryFilterTextField
                keyDownEvent.Handled <- true
            | _ -> ()
        )
        keyQueryFilterTextField

module KeysListView =

    let private fetchNewKeyInfo (uiState: UIState) (views: Views) key =
        uiState.Semaphore |> Semaphore.runTask ^ fun _ -> task {
            let database, key = key.ToString() |> KeyFormatter.getDatabaseAndOriginalKeyFromFormattedKeyString
            views.ResultsFrameView.Title <- ustr "Results (processing)"

            let! keyValue = key |> RedisReader.getKeyValue uiState.Multiplexer database
            let results = keyValue |> RedisResult.toStringArray

            uiState.ResultsFromKeyQuery <- ValueSome results
            updateResultsFieldsWithNewState uiState views { uiState.ResultsState with
                Result = results |> filterCommandResult views
                ResultType = RedisResult.getInformationText keyValue
                FromHistory = false
                Time = DateTimeOffset.Now
            }
        }
        |> ignore

    let addSelectedItemChangedEventProcessing (uiState: UIState) (views: Views) (keysListView: ListView) =
        keysListView.add_SelectedItemChanged (fun selectedItemChangedEvent ->
            match selectedItemChangedEvent.Value with
            | null ->
               ()
            | value ->
                fetchNewKeyInfo uiState views value
        )
        keysListView

module CommandTextField =

    let private filterSourceAndSetCommandResultFromHistory (uiState: UIState) (views: Views) (commandTextField: TextField)
        command commandResult time =
        commandTextField.Text <- ustr command
        let newResultsState = { uiState.ResultsState with
            Result = commandResult |> filterCommandResult views
            ResultType = "Command"
            FromHistory = true
            Time = time
        }

        updateResultsFieldsWithNewState uiState views newResultsState

    let private processEnterKey (uiState: UIState) (views: Views) (commandTextField: TextField) =
        uiState.Semaphore |> Semaphore.runTask ^ fun _ -> task {
            let database = views.DbPickerComboBox.SelectedItem
            let command = commandTextField.Text.ToString()
            views.ResultsFrameView.Title <- ustr "Results (Processing)"

            let! commandResult =
                command
                |> RedisReader.execCommand uiState.Multiplexer database
                |> Task.map RedisResult.toStringArray

            let time = DateTimeOffset.Now
            uiState.ResultsHistory.Add(command, (commandResult, time))
            uiState.ResultsFromKeyQuery <- ValueNone

            let filter = Ustr.toString views.ResultFilterTextField.Text
            let newResultsState = { uiState.ResultsState with
                Result = commandResult |> filterCommandResult views
                ResultType = "Command"
                Filtered = not ^ String.IsNullOrEmpty filter
                Time = time
            }
            updateResultsFieldsWithNewState uiState views newResultsState
        }
        |> ignore

    let private processCursorUpKey (uiState: UIState) (views: Views) (commandTextField: TextField) =
        match uiState.ResultsHistory.Up() with
        | ValueSome { Key = command; Value = results, time } ->
            filterSourceAndSetCommandResultFromHistory uiState views commandTextField command results time
        | _ -> ()

    let private processCursorDownKey (uiState: UIState) (views: Views) (commandTextField: TextField) =
        match uiState.ResultsHistory.Down() with
        | ValueSome { Key = command; Value = results, time } ->
            filterSourceAndSetCommandResultFromHistory uiState views commandTextField command results time
        | _ -> ()

    let addKeyDownEventProcessing (uiState: UIState) (views: Views) (commandTextField: TextField) =
        commandTextField.add_KeyDown (fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.Enter ->
                processEnterKey uiState views commandTextField
                keyDownEvent.Handled <- true

            | Key.CursorUp ->
                processCursorUpKey uiState views commandTextField
                keyDownEvent.Handled <- true

            | Key.CursorDown ->
                processCursorDownKey uiState views commandTextField
                keyDownEvent.Handled <- true

            | _ -> ()
        )
        commandTextField

module ResultFilterTextField =

    let private processEnterKey (uiState: UIState) (views: Views) (resultFilterTextField: TextField) =
        match uiState.ResultsFromKeyQuery, uiState.ResultsHistory.TryReadCurrent() with
        | ValueSome commandResult, _
        | _, ValueSome { Value = commandResult, _ } ->
            let filter = Ustr.toString resultFilterTextField.Text
            let newResultState = { uiState.ResultsState with
                Result = commandResult |> filterCommandResult views
                Filtered = not ^ String.IsNullOrEmpty filter
            }
            updateResultsFieldsWithNewState uiState views newResultState
        | _ -> ()

    let addKeyDownEventProcessing (uiState: UIState) (views: Views) (resultFilterTextField: TextField) =
        resultFilterTextField.add_KeyDown (fun keyDownEvent ->
            match keyDownEvent.KeyEvent.Key with
            | Key.Enter ->
                processEnterKey uiState views resultFilterTextField
                keyDownEvent.Handled <- true
            | _ -> ()
        )
        resultFilterTextField

let setupViewsLogic multiplexer (views: Views) =

    let uiState = {
        UIState.Semaphore = new SemaphoreSlim(1)
        Multiplexer = multiplexer
        KeyQueryResultState = { KeyQueryResultState.Keys = [||]; Filtered = false; FromHistory = false; Time = DateTimeOffset() }
        ResultsState = { ResultsState.Result = [||]; ResultType = ""; Filtered = false; FromHistory = false; Time = DateTimeOffset() }
        ResultsFromKeyQuery = ValueSome [||]
        KeyQueryHistory = ResultHistoryCache(100)
        ResultsHistory = ResultHistoryCache(100)
    }

    views.KeyQueryTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> KeyQueryTextField.addKeyDownEvenProcessing uiState views
    |> ignore

    views.KeyQueryFilterTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> KeyQueryFilterTextField.addKeyDownEventProcessing uiState views
    |> ignore

    views.DbPickerComboBox
    |> View.preventCursorUpDownKeyPressedEvents
    |> ignore

    views.KeysListView
    |> ListView.addValueCopyOnRightClick KeyFormatter.trimDatabaseHeader
    |> ListView.addValueCopyOnCopyHotKey KeyFormatter.trimDatabaseHeader
    |> KeysListView.addSelectedItemChangedEventProcessing uiState views
    |> ignore

    views.ResultsListView
    |> ListView.addValueCopyOnRightClick id
    |> ListView.addValueCopyOnCopyHotKey id
    |> ListView.addDetailedViewOnEnterKey
    |> ignore

    views.CommandTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> CommandTextField.addKeyDownEventProcessing uiState views
    |> ignore

    views.ResultFilterTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> ResultFilterTextField.addKeyDownEventProcessing uiState views
    |> ignore

    views