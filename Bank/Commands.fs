module Commands
open Newtonsoft.Json

[<JsonObject(MemberSerialization.Fields)>]
type Command() = class end

type TransferMoney(source : string, target : string, amount : decimal, trxId:string) =
    inherit Command()
    member __.Source = source
    member __.Target = target
    member __.Amount = amount
    member __.TrxId = trxId

type ConfirmReservation(trxId: string) =
    member __.TrxId = trxId

type GetBalance() = 
    inherit Command()
    

type SetBalance(amount : decimal) =
    inherit Command()
    member __.Amount = amount



type PassivateAccount(id : string)  = 
    inherit Command()
    member __.Id = id


type PassivateTrx(id : string)  = 
    inherit Command()
    member __.Id = id