module PoshRedisViewer.Redis

open System
open System.Collections.Generic
open FSharp.Control.Tasks
open StackExchange.Redis
open En3Tho.FSharp.Extensions.Core

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
            | _ -> goNext <- false
        return result
    }

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

module RedisReader =
    let connect (user: string option) (password: string option) (endPoint: string) = task {
        let connectionOptions = ConfigurationOptions()
        connectionOptions.EndPoints.Add endPoint

        user |> Option.iter ^ fun user -> connectionOptions.User <- user
        password |> Option.iter ^ fun password -> connectionOptions.Password <- password

        return! ConnectionMultiplexer.ConnectAsync(connectionOptions)
    }

    let getKeys (multiplexer: IConnectionMultiplexer) database (pattern: string) = task {
        try
            let server = multiplexer.GetServer(multiplexer.Configuration)
            let! keys = server.KeysAsync(database, RedisValue(pattern)) |> AsyncEnumerable.toResizeArray
            return
                keys
                |> Seq.map toString
                |> Seq.toArray
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
        try
            let! keyType = database.KeyTypeAsync key
            return Ok keyType
        with
        | exn ->
            return Error exn
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
