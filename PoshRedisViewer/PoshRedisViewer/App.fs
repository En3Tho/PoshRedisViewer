module PoshRedisViewer.App

open System.Threading
open En3Tho.FSharp.ComputationExpressions.GenericTaskBuilder.Tasks.SemaphoreSlimTask
open En3Tho.FSharp.ComputationExpressions.GenericTaskBuilder.Tasks.SynchronizationContextTask
open PoshRedisViewer.UIUtil
open StackExchange.Redis
open Terminal.Gui

let run(multiplexer: IConnectionMultiplexer) =
    Application.Init()

    UITask.Builder <- SynchronizationContextTaskBuilder(SynchronizationContext.Current)
    RedisTask.Builder <- SemaphoreSlimTaskBuilder(SemaphoreSlim(1))
    Clipboard.MiniClipboard <- MiniClipboard(Application.Driver.Clipboard)

    UI.makeViews()
    |> UI.setupViewsPosition
    |> UILogic.setupViewsLogic multiplexer
    |> fun views ->
        let handler = UILogic.makeErrorHandler views
        Application.Run(views.Top, handler)

let shutdown() = Application.Shutdown()