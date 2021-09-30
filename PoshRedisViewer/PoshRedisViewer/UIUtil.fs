module PoshRedisViewer.UIUtil

open System
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open En3Tho.FSharp.Extensions
open NStack
open PoshRedisViewer.Redis
open Terminal.Gui
open FSharp.Control.Tasks

type HistorySlot<'a, 'b> = {
    Key: 'a
    Value: 'b
}

type ResultHistoryCache<'a, 'b when 'a: equality>(capacity: int) =
    let syncRoot = obj()
    let items = ResizeArray<HistorySlot<'a, 'b>>(capacity)
    let mutable index = 0

    member _.Up() =
        lock syncRoot ^ fun() ->
            match items.Count, index - 1 with
            | 0, _ ->
                ValueNone
            | currentCount, newIndex ->
                if uint newIndex >= uint currentCount then
                    ValueNone
                else
                    index <- newIndex
                    ValueSome items.[newIndex]

    member _.Down() =
        lock syncRoot ^ fun() ->
            match items.Count, index + 1 with
            | 0, _ ->
                ValueNone
            | currentCount, newIndex ->
                if uint newIndex >= uint currentCount then
                    ValueNone
                else
                    index <- newIndex
                    ValueSome items.[newIndex]

    member _.Add(key, value) =
        lock syncRoot ^ fun() ->
            if items.Count = 0 then
                ()
            else
                match items.FindIndex(fun slot -> slot.Key = key) with
                | -1 -> ()
                | index ->
                    items.RemoveAt index

            items.Add { Key = key; Value = value }
            if items.Count > capacity then
                items.RemoveAt 0
            index <- items.Count - 1

    member _.TryReadCurrent() =
        lock syncRoot ^ fun() ->
            let index = index
            if uint index >= uint items.Count then
                ValueNone
            else
                ValueSome items.[index]

module rec RedisResult =

    let toStringArray (value: RedisResult) =
        match value with
        | RedisString str ->
            [| str |]
        | RedisList strings ->
            strings
            |> Array.map ^ fun member' -> $"Index: {member'.Index} | Value: {member'.Value}"
        | RedisError e ->
            e.ToString().Split(Environment.NewLine)
        | RedisHash members ->
            members
            |> Array.map ^ fun member' -> $"Field: {member'.Field} | Value: {member'.Value}"
        | RedisNone ->
            [| "None" |]
        | RedisSet strings ->
            strings
        | RedisSortedSet members ->
            members
            |> Array.map ^ fun member' -> $"Score: {member'.Score} | Value : {member'.Value}"
        | RedisStream ->
            [| "RedisStream is not supported" |]
        | RedisMultiResult values ->
            values
            |> Array.map toStringArray
            |> Array.concat

module View =
    let preventCursorUpDownKeyPressedEvents (view: View) =
        view.add_KeyPress(fun keyPressEvent ->
            match keyPressEvent.KeyEvent.Key with
            | Key.CursorUp
            | Key.CursorDown ->
                keyPressEvent.Handled <- true
            | _ -> ()
        )

module Semaphore =
    let runTask (job: Task<'a>) (semaphore: SemaphoreSlim) = task {
        do! semaphore.WaitAsync()
        try
            return! job
        finally
            semaphore.Release() |> ignore
    }

let ustr str = icast<string, ustring> str
module Ustr =
    let toString (ustr: ustring) =
        match ustr with
        | null -> ""
        | _ -> ustr.ToString()

module Key =
    let private copyCommandKey =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Key.CtrlMask ||| Key.C
        else
            Key.CtrlMask ||| Key.Y

    let private is flag (key: Key) = key |> Enum.hasFlag flag |> Option.ofBool

    let (|CopyCommand|_|) key = key |> is copyCommandKey

module StringSource =
    let filter filter (source: string[]) =
        match filter with
        | "" -> source
        | filter ->
            source |> Array.filter ^ fun x -> x.Contains(filter, StringComparison.OrdinalIgnoreCase)