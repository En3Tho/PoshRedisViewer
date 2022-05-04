namespace PoshRedisViewerModule

open System
open System.Management.Automation
open System.Threading.Tasks
open PoshRedisViewer
open PoshRedisViewer.Redis
open En3Tho.FSharp.Extensions

[<Cmdlet(VerbsCommon.Get, "RedisViewer")>]
type GetRedisViewerCommand() =
    inherit Cmdlet()

    [<Parameter(Position = 0, Mandatory = true)>]
    [<ValidateNotNullOrEmpty>]
    member val ConnectionString = "" with get, set

    member val User = "" with get, set
    member val Password = "" with get, set

    override this.ProcessRecord() =
        let connectionString = this.ConnectionString
        let user = this.User |> Option.ofString
        let password = this.Password |> Option.ofString

        let dispose() =
            if not Console.IsInputRedirected then
                Console.Write("\u001b[?1h\u001b[?1003l") // fixes an issue with certain terminals, same as ocgv

        use _ = defer dispose

        connectionString
        |> RedisReader.connect user password
        |> Task.RunSynchronously
        |> App.run
        |> App.shutdown