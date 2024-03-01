module PoshRedisViewer.Redis

open System
open System.Diagnostics
open StackExchange.Redis
open En3Tho.FSharp.Extensions
open En3Tho.FSharp.ComputationExpressions.Tasks

type StackExchangeRedisResult = RedisResult

let inline toString x = x.ToString()
let inline toStringOrEmpty x = if Object.ReferenceEquals(x, null) then "" else x.ToString()

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
            match redisResult.Resp2Type with
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
            | ResultType.Array ->
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

    let connect (user: string option) (password: string option) (endPoint: string) = redisTask {
        let connectionOptions = ConfigurationOptions()
        connectionOptions.EndPoints.Add endPoint

        user |> Option.iter ^ fun user -> connectionOptions.User <- user
        password |> Option.iter ^ fun password -> connectionOptions.Password <- password

        return! ConnectionMultiplexer.ConnectAsync(connectionOptions)
    }

    let getKeys (multiplexer: IConnectionMultiplexer) (database: KeySearchDatabase) (pattern: string) = redisTask {
        try
            // always 1 endpoint in this version
            let server = multiplexer.GetServer(multiplexer.GetEndPoints()[0])

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

    let execCommand (multiplexer: IConnectionMultiplexer) database (command: string) = redisTask {
        try
            let database = multiplexer.GetDatabase database
            let commandAndArgs = command.Split(" ", StringSplitOptions.RemoveEmptyEntries)
            let command = commandAndArgs[0]
            let args = commandAndArgs[1..] |> Array.map box

            let! result = database.ExecuteAsync(command, args)
            return RedisResult.fromStackExchangeRedisResult result
        with
        | e ->
            return RedisError e
    }

    let getKeyType (multiplexer: IConnectionMultiplexer) database (key: string) = redisTask {
        let key = RedisKey key
        let database = multiplexer.GetDatabase database
        return! database.KeyTypeAsync(key).AsResult()
    }

    let getKeyValue (multiplexer: IConnectionMultiplexer) database (key: string) = redisTask {
        match! getKeyType multiplexer database key with
        | Ok keyType ->
            let key = RedisKey key
            let database = multiplexer.GetDatabase database
            match keyType with
            | RedisType.Hash ->
                let! hashFields = database.HashGetAllAsync key
                return
                    hashFields
                    |> Array.map ^ fun hashField ->
                        { Field = toString hashField.Name; Value = toString hashField.Value }
                    |> RedisHash

            | RedisType.Set ->
                let! setMembers = database.SetMembersAsync key
                return
                    setMembers
                    |> Array.map toString
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
                    |> Array.mapi ^ fun i value ->
                        { Index = i; Value = toString value }
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