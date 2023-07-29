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

let setupViewsLogic multiplexer (views: Views) =

    let semaphore = new SemaphoreSlim(1)

    let mutable keyQueryResultState = { KeyQueryResultState.Keys = [||]; Filtered = false; FromHistory = false; Time = DateTimeOffset() }

    let getFilter() =
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

    let filterBy query keys =
        match query with
        | String.NullOrWhiteSpace ->
            keys
        | _ ->
            let filter = getFilter()
            keys |> StringSource.filter (filter query)

    let filterKeyQueryResult results =
        let query = Ustr.toString views.KeyQueryFilterTextField.Text
        filterBy query results

    let updateKeyQueryFieldsWithNewState state =
        keyQueryResultState <- state
        views.KeysFrameView.Title <- keyQueryResultState |> KeyQueryResultState.toString |> ustr
        views.KeysListView.SetSource state.Keys

    let mutable resultState = { ResultsState.Result = [||]; ResultType = ""; Filtered = false; FromHistory = false; Time = DateTimeOffset() }

    let filterCommandResult results =
        let query = Ustr.toString views.ResultFilterTextField.Text
        filterBy query results

    let updateResultsFieldsWithNewState state =
        resultState <- state
        views.ResultsFrameView.Title <- state |> ResultsState.toString |> ustr
        views.ResultsListView.SetSource state.Result

    let keyQueryHistory = ResultHistoryCache(100)

    views.KeyQueryTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> fun keyQueryTextField ->
        keyQueryTextField.add_KeyDown (fun keyDownEvent ->

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
                         if views.DbPickerCheckBox.Checked then
                             KeySearchDatabase.Range (0, 15)
                         else
                             KeySearchDatabase.Single views.DbPickerComboBox.SelectedItem

                     let pattern = keyQueryTextField.Text.ToString()
                     views.KeysFrameView.Title <- ustr "Keys (processing)"

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

    views.KeyQueryFilterTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> fun keyQueryFilterTextField ->
        keyQueryFilterTextField.add_KeyDown (fun keyDownEvent ->
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

    views.DbPickerComboBox |> View.preventCursorUpDownKeyPressedEvents |> ignore

    let mutable resultsFromKeyQuery = ValueSome [||]
    views.KeysListView
    |> ListView.addValueCopyOnRightClick KeyFormatter.trimDatabaseHeader
    |> ListView.addValueCopyOnCopyHotKey KeyFormatter.trimDatabaseHeader
    |> fun keysListView ->
        keysListView.add_SelectedItemChanged (fun selectedItemChangedEvent ->
           semaphore |> Semaphore.runTask ^ fun _ -> task {
               match selectedItemChangedEvent.Value with
               | null -> ()
               | value ->
                   let database, key = value.ToString() |> KeyFormatter.getDatabaseAndOriginalKeyFromFormattedKeyString
                   views.ResultsFrameView.Title <- ustr "Results (processing)"

                   let! keyValue = key |> RedisReader.getKeyValue multiplexer database
                   let result = keyValue |> RedisResult.toStringArray

                   resultsFromKeyQuery <- ValueSome result
                   updateResultsFieldsWithNewState { resultState with
                       Result = filterCommandResult result
                       ResultType = RedisResult.getInformationText keyValue
                       FromHistory = false
                       Time = DateTimeOffset.Now
                   }
           }
           |> ignore
       )


    views.ResultsListView
    |> ListView.addValueCopyOnRightClick id
    |> ListView.addValueCopyOnCopyHotKey id
    |> ListView.addDetailedViewOnEnterKey
    |> ignore    

    let resultsHistory = ResultHistoryCache(100)
    views.CommandTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> fun commandTextField ->
        commandTextField.add_KeyDown (fun keyDownEvent ->

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
                   let database = views.DbPickerComboBox.SelectedItem
                   let command = commandTextField.Text.ToString()
                   views.ResultsFrameView.Title <- ustr "Results (Processing)"

                   let! commandResult =
                       command
                       |> RedisReader.execCommand multiplexer database
                       |> Task.map RedisResult.toStringArray

                   let time = DateTimeOffset.Now
                   resultsHistory.Add(command, (commandResult, time))
                   resultsFromKeyQuery <- ValueNone

                   let filter = Ustr.toString views.ResultFilterTextField.Text
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

    views.ResultFilterTextField
    |> View.preventCursorUpDownKeyPressedEvents
    |> TextField.addCopyPasteSupportWithMiniClipboard
    |> fun resultFilterTextField ->
        resultFilterTextField.add_KeyDown (fun keyDownEvent ->
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

    views