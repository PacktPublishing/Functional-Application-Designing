module Events
open Newtonsoft.Json

[<JsonObject(MemberSerialization.Fields)>]
type Event() = class end

type AmountReserved(amount : decimal, trxId : string) =
    inherit Event()
    member __.TrxId = trxId
    member __.Amount = amount
    

type ReservationConfirmed(trxId : string) =
    inherit Event()
    member __.TrxId = trxId


type TransferRejected(trxId : string, reason : string) =
    inherit Event()
    member __.TrxId = trxId
    member __.Reason = reason


type BalanceSet( amount : decimal) =
    inherit Event()
    member __.Amount = amount

type AccountCreated(accountId : string) =
    inherit Event()
    member __.AccountId = accountId

type TrxCreated(source : string, target : string , amount : decimal, trxId : string) =
    inherit Event()
    member __.Source = source
    member __.Target = target
    member __.TrxId = trxId
    member __.Amount = amount


type AccountPassivated (id : string) =
    inherit Event()
    member __.Id = id
    
type TrxPassivated (id : string) =
    inherit Event()
    member __.Id = id
