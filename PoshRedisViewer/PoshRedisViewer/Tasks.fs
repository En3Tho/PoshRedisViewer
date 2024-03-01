namespace PoshRedisViewer

open En3Tho.FSharp.Extensions
open En3Tho.FSharp.ComputationExpressions.GenericTaskBuilder.Tasks.SynchronizationContextTask
open PoshRedisViewer.SemaphoreTask

[<AbstractClass; Sealed>]
type UITask() =
    static member val Builder = nullRef<SynchronizationContextTask> with get, set

[<AbstractClass; Sealed>]
type RedisTask() =

    static member val Builder = nullRef<SemaphoreSlimTaskBuilder> with get, set

[<AbstractClass; Sealed; AutoOpen>]
type Tasks() =

    static member uiTask = UITask.Builder
    static member redisTask = RedisTask.Builder