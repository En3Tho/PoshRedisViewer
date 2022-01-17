module PoshRedisViewer.Redis

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open En3Tho.FSharp.ComputationExpressions.Tasks
open StackExchange.Redis
open En3Tho.FSharp.Extensions

type StackExchangeRedisResult = RedisResult

let inline toString x = x.ToString()

module AsyncEnumerable =
    let toResizeArray (enumerable: IAsyncEnumerable<'a>) = vtask {
        let result = ResizeArray()
        let enumerator = enumerable.GetAsyncEnumerator()

        let mutable goNext = true
        while goNext do
            match! enumerator.MoveNextAsync() with
            | true ->
                result.Add enumerator.Current
            | _ ->
                goNext <- false

        return result
    }

    let dispose2 (enumerator1: #IAsyncEnumerator<'a>)(enumerator2: #IAsyncEnumerator<'b>) = unitvtask {
        let mutable exn1 = null
        let mutable exn2 = null
        try
            do! enumerator1.DisposeAsync()
        with e ->
            exn1 <- e
        try
            do! enumerator2.DisposeAsync()
        with e ->
            exn2 <- e

        match exn1, exn2 with
        | null, null ->
            ()
        | exn, null
        | null, exn ->
            Exception.reraise exn
        | _ ->
            raise ^ AggregateException(exn1, exn2)
    }

    type [<Struct>] private EmptyAsyncEnumerator<'a> =
        interface IAsyncEnumerator<'a> with
            member this.Current = invalidOp "Current should not be used from async enumerable"
            member this.DisposeAsync() = ValueTask()
            member this.MoveNextAsync() = ValueTask.FromResult(false)

    type private EmptyAsyncEnumerable<'a>() =
        static let cachedEnumerator = EmptyAsyncEnumerator<'a>() :> IAsyncEnumerator<'a>
        static let cachedInstance = EmptyAsyncEnumerable<'a>()
        static member Empty = cachedInstance

        interface IAsyncEnumerable<'a> with
            member this.GetAsyncEnumerator(_) = cachedEnumerator

    type private MapAsyncEnumerableEnumerator<'a, 'b>(source1: IAsyncEnumerable<'a>, map: 'a -> 'b) =
        let enumerator = source1.GetAsyncEnumerator()

        interface IAsyncEnumerator<'b> with
            member this.Current = enumerator.Current |> map
            member this.DisposeAsync() = enumerator.DisposeAsync()
            member this.MoveNextAsync() = enumerator.MoveNextAsync()

    type private AppendAsyncEnumerableEnumerator<'a>(source1: IAsyncEnumerable<'a>, source2: IAsyncEnumerable<'a>, cancellationToken: CancellationToken) =
        let mutable enumerator = source1.GetAsyncEnumerator()
        let mutable enumerator2 = source2.GetAsyncEnumerator()
        let mutable state = 0

        interface IAsyncEnumerator<'a> with
            member this.Current =
                match state with
                | 0 ->
                    enumerator.Current
                | _ ->
                    enumerator2.Current

            member this.DisposeAsync() = dispose2 enumerator enumerator2

            member this.MoveNextAsync() = vtask {
                cancellationToken.ThrowIfCancellationRequested()
                match state with
                | 0 ->
                    match! enumerator.MoveNextAsync() with
                    | true -> return true
                    | _ ->
                        state <- 1
                        return! enumerator2.MoveNextAsync()
                | _ ->
                    return! enumerator2.MoveNextAsync()
            }

    type private AppendAsyncEnumerable<'a>(source1: IAsyncEnumerable<'a>, source2: IAsyncEnumerable<'a>) =
        interface IAsyncEnumerable<'a> with
            member this.GetAsyncEnumerator(cancellationToken) = AppendAsyncEnumerableEnumerator<'a>(source1, source2, cancellationToken)

    let empty<'a> = EmptyAsyncEnumerable<'a>.Empty :> IAsyncEnumerable<'a>

    let append source1 source2 =
        AppendAsyncEnumerable<'a>(source1, source2) :> IAsyncEnumerable<'a>

    let map map source = { new IAsyncEnumerable<'b> with member _.GetAsyncEnumerator(_) = MapAsyncEnumerableEnumerator<'a, 'b>(source, map) }

type RedisHashMember = {
    Field: string
    Value: string
}

type RedisSortedSetMember = {
    Score: float
    Value: string
}

type RedisListMember = {
    Index: int
    Value: string
}

type RedisResult =
    | RedisString of string
    | RedisList of RedisListMember array
    | RedisHash of RedisHashMember array
    | RedisSet of string array
    | RedisSortedSet of RedisSortedSetMember array
    | RedisStream
    | RedisError of exn
    | RedisMultiResult of RedisResult array
    | RedisNone

module RedisResult =

    let fromRedisKey (redisKey: RedisKey) =
        RedisString (toString redisKey)

    let rec fromStackExchangeRedisResult (redisResult: StackExchangeRedisResult) =
        if redisResult.IsNull then
            RedisResult.RedisNone
        else
        match redisResult.Type with
        | ResultType.SimpleString ->
            RedisString (toString redisResult)
        | ResultType.None ->
            RedisNone
        | ResultType.Error ->
            RedisError (Exception(toString redisResult))
        | ResultType.Integer ->
            RedisString (toString redisResult)
        | ResultType.BulkString ->
            RedisString (toString redisResult)
        | ResultType.MultiBulk ->
            let results = ecast<_, StackExchangeRedisResult[]> redisResult
            RedisMultiResult (results |> Array.map fromStackExchangeRedisResult)
        | _ ->
            RedisError (Exception("Unknown type of Enum"))

type KeySearchDatabase =
    | Single of Database: int
    | Range of From: int * To: int

module KeyFormatter =
    let getFormattedKeyString database redisKey = $"{database}. {toString redisKey}"

    let getDatabaseFromHeader (formattedKey: string) =
        let indexOfDot = formattedKey.IndexOf('.')
        match indexOfDot with
        | -1 ->
            Debug.Fail("Should not be happening")
            0
        | _ ->
            Int32.Parse(formattedKey.AsSpan(0, indexOfDot))

    let trimDatabaseHeader (formattedKey: string) =
        let indexOfDot = formattedKey.IndexOf('.')
        match indexOfDot with
        | -1 ->
            Debug.Fail("Should not be happening")
            formattedKey
        | _ ->
            formattedKey.Substring(indexOfDot + 2)

    let getDatabaseAndOriginalKeyFromFormattedKeyString (formattedKey: string) =
        getDatabaseFromHeader formattedKey, trimDatabaseHeader formattedKey

module RedisReader =

    let connect (user: string option) (password: string option) (endPoint: string) = task {
        let connectionOptions = ConfigurationOptions()
        connectionOptions.EndPoints.Add endPoint

        user |> Option.iter ^ fun user -> connectionOptions.User <- user
        password |> Option.iter ^ fun password -> connectionOptions.Password <- password

        return! ConnectionMultiplexer.ConnectAsync(connectionOptions)
    }

    let getKeys (multiplexer: IConnectionMultiplexer) (database: KeySearchDatabase) (pattern: string) = task {
        try
            let server = multiplexer.GetServer(multiplexer.Configuration)

            let getKeys database =
                server.KeysAsync(database, RedisValue pattern)
                |> AsyncEnumerable.map (KeyFormatter.getFormattedKeyString database)

            let! keys =
                match database with
                | Single database ->
                    getKeys database
                | Range(from, to') ->
                    seq {
                        for database = from to to' do
                            getKeys database
                    }
                    |> Seq.fold AsyncEnumerable.append AsyncEnumerable.empty

                |> AsyncEnumerable.toResizeArray

            return
                keys.ToArray()
                |> RedisSet
        with
        | e ->
            return RedisError e
    }

    let execCommand (multiplexer: IConnectionMultiplexer) database (command: string) = task {
        try
            let database = multiplexer.GetDatabase database
            let commandAndArgs = command.Split(" ", StringSplitOptions.RemoveEmptyEntries)
            let command = commandAndArgs.[0]
            let args = commandAndArgs.[1..] |> Array.map box

            let! result = database.ExecuteAsync(command, args)
            return RedisResult.fromStackExchangeRedisResult result
        with
        | e ->
            return RedisError e
    }

    let getKeyType (multiplexer: IConnectionMultiplexer) database (key: string) = task {
        let key = RedisKey key
        let database = multiplexer.GetDatabase database
        return! database.KeyTypeAsync(key).AsResult()
    }

    let getKeyValue (multiplexer: IConnectionMultiplexer) database (key: string) = task {
        match! getKeyType multiplexer database key with
        | Ok keyType ->
            let key = RedisKey key
            let database = multiplexer.GetDatabase database
            match keyType with
            | RedisType.Hash ->
                let! hashFields = database.HashGetAllAsync key
                return
                    hashFields
                    |> Seq.map ^ fun hashField ->
                        { Field = toString hashField.Name; Value = toString hashField.Value }
                    |> Seq.toArray
                    |> RedisHash

            | RedisType.Set ->
                let! setMembers = database.SetMembersAsync key
                return
                    setMembers
                    |> Seq.map toString
                    |> Seq.toArray
                    |> RedisSet

            | RedisType.String ->
                let! str = database.StringGetAsync key
                return
                    toString str
                    |> RedisString

            | RedisType.List ->
                let! listMembers = database.ListRangeAsync key
                return
                    listMembers
                    |> Seq.mapi (fun i value -> { Index = i; Value = toString value })
                    |> Seq.toArray
                    |> RedisList

            | RedisType.SortedSet ->
                let! setMembers = database.SortedSetScanAsync(key) |> AsyncEnumerable.toResizeArray
                return
                    setMembers
                    |> Seq.map ^ fun setMember ->
                        { Score = setMember.Score; Value = toString setMember.Element }
                    |> Seq.toArray
                    |> RedisSortedSet

             | RedisType.None ->
                return RedisNone

            | RedisType.Stream ->
                return RedisError (NotSupportedException("Redis streams are not supported"))

            | RedisType.Unknown ->
                return RedisError (Exception("Unknown entity"))

            | _  ->
                return RedisError (Exception("Unrecognized enum value"))
        | Error exn ->
            return RedisError exn
    }
