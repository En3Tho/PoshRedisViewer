module PoshRedisViewer.AsyncEnumerable
open System
open System.Threading
open System.Threading.Tasks
open En3Tho.FSharp.Extensions
open En3Tho.FSharp.ComputationExpressions.Tasks
open System.Collections.Generic

let toResizeArray (enumerable: IAsyncEnumerable<'a>) = vtask {
    let result = ResizeArray()
    use enumerator = enumerable.GetAsyncEnumerator()

    let mutable goNext = true
    while goNext do
        match! enumerator.MoveNextAsync() with
        | true ->
            result.Add enumerator.Current
        | _ ->
            goNext <- false

    return result
}

let dispose2 (enumerator1: #IAsyncEnumerator<'a>)(enumerator2: #IAsyncEnumerator<'b>) = uvtask {
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
        member this.Current = invalidOp "Current should not be used with empty async enumerable"
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