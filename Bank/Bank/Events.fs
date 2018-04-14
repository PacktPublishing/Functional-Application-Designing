module Events
open Newtonsoft.Json
open Commands

[<JsonObject(MemberSerialization.Fields)>]
type Event() = class end

type AmountReserved(amount : decimal, trxId : string) =
    inherit Event()
    member __.TrxId = trxId
    member __.Amount = amount
    private new () = AmountReserved(0M,"")

type ReservationConfirmed(trxId : string) =
    inherit Event()
    member __.TrxId = trxId
    private new () = ReservationConfirmed("")


type TransferRejected(trxId : string, reason : string) =
    inherit Event()
    member __.TrxId = trxId
    member __.Reason = reason
    private new () = TransferRejected("","")


type BalanceSet( amount : decimal) =
    inherit Event()
    member __.Amount = amount
    private new() = BalanceSet(0M)

type AccountCreated(accountId : string) =
    inherit Event()
    member __.AccountId = accountId
    private new() = AccountCreated("")


type TrxCreated(cmd:TransferMoney) =
    inherit Event()
    member __.Command = cmd
    private new() = TrxCreated(Unchecked.defaultof<TransferMoney>)


type AccountPassivated (id : string) =
    inherit Event()
    member __.Id = id
    private new() = AccountPassivated("")

type TrxPassivated (id : string) =
    inherit Event()
    member __.Id = id
    private new() = TrxPassivated("")