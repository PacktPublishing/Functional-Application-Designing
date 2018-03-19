module States

type IState = Akka.Persistence.Fsm.PersistentFSM.IFsmState

type Start() =
    interface IState with
        member __.Identifier = "Start"

type SourceCommiting() =
    interface IState with
        member __.Identifier = "SourceCommiting"

type TargetApproving() =
    interface IState with
        member __.Identifier = "TargetApproving"
        
type TargetCommiting() =
    interface IState with
        member __.Identifier = "TargetCommiting"

type TrxCompleting() =
    interface IState with
        member __.Identifier = "TrxCompleting"