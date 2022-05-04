module PoshRedisViewer.UI

open FSharp.Reflection
open En3Tho.FSharp.Extensions
open En3Tho.FSharp.ComputationExpressions.SCollectionBuilder
open NStack
open PoshRedisViewer.UIUtil
open Terminal.Gui

#nowarn "0058"

type Views = {
    CommandFrameView: FrameView
    CommandTextField: TextField
    DbPickerCheckBox: CheckBox
    DbPickerComboBox: ComboBox
    DbPickerFrameView: FrameView
    KeyQueryFilterFrameView: FrameView
    KeyQueryFilterTextField: TextField
    KeyQueryFilterTypeComboBox: ComboBox
    KeyQueryFilterTypeFrameView: FrameView
    KeyQueryFrameView: FrameView
    KeyQueryTextField: TextField
    KeysFrameView: FrameView
    KeysListView: ListView
    ResultFilterFrameView: FrameView
    ResultFilterTextField: TextField
    ResultsFrameView: FrameView
    ResultsListView: ListView
    Top: Toplevel
    Window: Window
}

let makeViews() =
    let window = new Window(
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    let keyQueryFrameView = new FrameView(ustr "KeyQuery",
        Width = Dim.Percent(45f),
        Height = Dim.Sized 3
    )

    let keyQueryTextField = new TextField(
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        Text = ustr "*"
    )

    let keyQueryFilterFrameView = new FrameView(ustr "Keys Filter",
        X = Pos.Right keyQueryFrameView,
        Width = Dim.Percent(30f),
        Height = Dim.Sized 3
    )

    let keyQueryFilterTextField = new TextField(
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        Text = ustr ""
    )

    let keyQueryFilterTypeFrameView = new FrameView(ustr "Filter Type",
        X = Pos.Right keyQueryFilterFrameView,
        Width = Dim.Percent(10f),
        Height = Dim.Sized 3
    )

    let keyQueryFilterTypeComboBox = new ComboBox(ustr "Contains",
        X = Pos.Left keyQueryFilterTypeFrameView + Pos.At 1,
        Y = Pos.Top keyQueryFilterTypeFrameView + Pos.At 1,
        Width = Dim.Width keyQueryFilterTypeFrameView - Dim.Sized 2,
        Height = Dim.Sized (FSharpType.GetUnionCases(typeof<FilterType>).Length + 1),
        ReadOnly = true
    )

    keyQueryFilterTypeComboBox.SetSource([|
        FilterType.Contains
        FilterType.Regex
    |])

    let dbPickerFrameView = new FrameView(ustr "DB",
        X = Pos.Right keyQueryFilterTypeFrameView,
        Width = Dim.Percent(15.f),
        Height = Dim.Sized 3
    )

    let dbPickerComboBox = new ComboBox(ustr "0",
        X = Pos.Left dbPickerFrameView + Pos.At 1,
        Y = Pos.Top dbPickerFrameView + Pos.At 1,
        Width = Dim.Width dbPickerFrameView - Dim.Sized 15,
        Height = Dim.Sized 17,
        ReadOnly = true
    )

    dbPickerComboBox.SetSource([|
        for i = 0 to 15 do ustr (string i)
    |])

    let dbPickerCheckBox = new CheckBox(ustr "Query All",
        X = Pos.Right dbPickerComboBox + Pos.At 1,
        Y = Pos.Top dbPickerComboBox
    )

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

    let views: Views = {
        Top = Application.Top
        Window = window
        KeyQueryFrameView = keyQueryFrameView
        KeyQueryTextField = keyQueryTextField
        KeyQueryFilterFrameView = keyQueryFilterFrameView
        KeyQueryFilterTextField = keyQueryFilterTextField
        KeyQueryFilterTypeFrameView = keyQueryFilterTypeFrameView
        KeyQueryFilterTypeComboBox = keyQueryFilterTypeComboBox
        DbPickerFrameView = dbPickerFrameView
        DbPickerComboBox = dbPickerComboBox
        DbPickerCheckBox = dbPickerCheckBox
        KeysFrameView = keysFrameView
        KeysListView = keysListView
        ResultsFrameView = resultsFrameView
        ResultsListView = resultsListView
        CommandFrameView = commandFrameView
        CommandTextField = commandTextField
        ResultFilterFrameView = resultFilterFrameView
        ResultFilterTextField = resultFilterTextField
    }

    views

let setupViewsPosition (views: Views) =
    views.Top {
        views.Window {
            views.KeyQueryFrameView {
                views.KeyQueryTextField
            }
            views.KeyQueryFilterFrameView {
                views.KeyQueryFilterTextField
            }

            views.KeyQueryFilterTypeFrameView
            views.KeyQueryFilterTypeComboBox

            views.DbPickerFrameView
            views.DbPickerComboBox
            views.DbPickerCheckBox

            views.KeysFrameView {
                views.KeysListView
            }
            views.ResultsFrameView {
                views.ResultsListView
            }
            views.CommandFrameView {
                views.CommandTextField
            }
            views.ResultFilterFrameView {
                views.ResultFilterTextField
            }
        }
    } |> ignore

    views.Window.Subviews[0].BringSubviewToFront(views.KeyQueryFilterTypeComboBox)
    views.Window.Subviews[0].BringSubviewToFront(views.DbPickerComboBox)
    views.Window.Subviews[0].BringSubviewToFront(views.DbPickerCheckBox)

    views