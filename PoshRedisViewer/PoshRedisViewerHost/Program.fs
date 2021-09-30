open System
open System.Threading.Tasks
open En3Tho.FSharp.Extensions.Core
open PoshRedisViewer
open PoshRedisViewer.Redis

[<EntryPoint>]
let main argv =

    let multiplexer =
        "localhost:6379"
        |> RedisReader.connect None None
        |> Task.RunSynchronously

    UI.runApp(multiplexer)
    UI.shutdown()
    if not Console.IsInputRedirected then
        Console.Write("\u001b[?1h\u001b[?1003l") // fixes an issue with certain terminals, same as ocgv
    0