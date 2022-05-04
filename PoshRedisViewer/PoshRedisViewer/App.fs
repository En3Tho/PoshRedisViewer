module PoshRedisViewer.App

open StackExchange.Redis
open Terminal.Gui

let run(multiplexer: IConnectionMultiplexer) =
    Application.Init()
    |> UI.makeViews
    |> UI.setupViewsPosition
    |> UILogic.setupViewsLogic multiplexer
    |> fun views ->
        let handler = UILogic.makeErrorHandler views
        Application.Run(views.Top, handler)

let shutdown() = Application.Shutdown()