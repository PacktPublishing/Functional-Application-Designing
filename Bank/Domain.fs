module Domain
open Akka.Persistence
open Akka.Persistence.Fsm
open Commands
open Events
open Akka.Actor
open States
open System

type AccountActor (accountId) as account =
    inherit ReceivePersistentActor () 
    let mutable currentBalance = 0M
    let mutable availableBalance = 0M
    let mutable reservations : Map<string, decimal> = Map.empty
    let context = ReceivePersistentActor.Context
    let publish = context.System.EventStream.Publish
    let sender = account.Sender
    let self = account.Self

    let updateState (event : Event) =
        match event with

        | :? BalanceSet as e -> 
            currentBalance <- e.Amount
            availableBalance <- currentBalance

        | :? AmountReserved as e -> 
            match reservations.TryFind e.TrxId with
            | None ->
                reservations <- reservations.Add(e.TrxId, e.Amount)
                if e.Amount > 0M then
                    availableBalance <- availableBalance - e.Amount
            | _ -> ()

        | :? ReservationConfirmed as e -> 
            match reservations.TryFind e.TrxId with
            | Some amount ->
                reservations <- reservations.Remove e.TrxId
                currentBalance <- currentBalance + amount
                availableBalance <- currentBalance
            | _ -> ()
        | _ -> ()

    let tellToTransactionProcess e trxId =
        let transactionProcess = "../TransactionProcess_"
        context.ActorSelection(transactionProcess + trxId).Tell(e)
  
    let handleEvent (event : Event) = 
        account.Persist(event, fun e -> updateState e; publish e)
        match event with
        | :? TransferRejected as e -> 
             accountId |> PassivateAccount |> context.Parent.Tell
        | :? AmountReserved as e -> tellToTransactionProcess e (e.TrxId)
        | :? ReservationConfirmed as e -> 
            accountId |> PassivateAccount |> context.Parent.Tell
            tellToTransactionProcess e (e.TrxId)
        | _ -> ()
    let transferMoney (cmd : TransferMoney) = 
        if  cmd.Amount > availableBalance then
            handleEvent <| TransferRejected(cmd.TrxId, "Not enough balance")
        else
            handleEvent <| AmountReserved(cmd.Amount, cmd.TrxId)
        true
    let confirmReservation (cmd : ConfirmReservation) = 
        match reservations.TryFind(cmd.TrxId) with
        | Some _ -> 
            handleEvent <| ReservationConfirmed cmd.TrxId
        | _ -> ()
        true

    let setBalance (cmd : SetBalance) = 
       handleEvent <| BalanceSet cmd.Amount
    let getBalance (_ : GetBalance) = 
       sender.Tell(currentBalance, self)
      
    let confirmReservation (cmd : ConfirmReservation) = 
        match reservations.TryFind cmd.TrxId with
        | Some _ -> 
            handleEvent <| ReservationConfirmed cmd.TrxId
        | _ -> ()
        true

    let recover (message:obj) = 
        match message with
        | :? Event as e -> handleEvent e
        | _ -> ()

    do 
        account.Command transferMoney
        account.Command confirmReservation
        account.Command setBalance
        account.Recover recover

    override __.PersistenceId = "Account_" + accountId

    static member Props accountId = 
        Props.Create(fun _ -> AccountActor accountId);
    

type TransactionProcess (sourceId, targetId , amount, trxId) as t =
    inherit PersistentFSM<IState ,obj, Event>()
    let stay = t.Stay
    let goto = t.GoTo
    let stop : unit -> _ = t.Stop
    let start = Start()
    let context = TransactionProcess.Context
    let sourceCommiting : IState = upcast SourceCommiting()
    let targetCommiting : IState = upcast TargetCommiting() 
    let targetApproving : IState = upcast TargetApproving()

    
    let tellToAccount accountId e =
        let account = "../Account_"
        context.ActorSelection(account + accountId).Tell(e)
    
    do
        base.StartWith(start,null)
        base.When(start, 
                fun x -> 
                    fun _-> 
                        match x.FsmEvent with
                        | :? AmountReserved as e ->
                            goto(targetApproving).Applying(e)
                                .AndThen(fun _ -> 
                                    TransferMoney(sourceId, targetId, -e.Amount , trxId) |>
                                        tellToAccount targetId)
                        | _ -> stay())
        base.When(targetApproving,
            fun x -> 
                    fun _-> 
                        match x.FsmEvent with
                        | :? AmountReserved as e ->
                            goto(sourceCommiting).Applying(e)
                                .AndThen(fun _ ->
                                    ConfirmReservation e.TrxId |>
                                        tellToAccount sourceId)
                        | _ -> stay())

        base.When(sourceCommiting,
            fun x -> 
                    fun _-> 
                        match x.FsmEvent with
                        | :? ReservationConfirmed as e ->
                            goto(targetCommiting).Applying(e)
                                .AndThen(fun _ ->   
                                    ConfirmReservation e.TrxId |>
                                        tellToAccount targetId)
                        | _ -> stay())
        base.When(targetCommiting,
            fun x -> 
                    fun _-> 
                        match x.FsmEvent with
                        | :? ReservationConfirmed as e ->
                            stop().Applying(e).AndThen(fun _ -> 
                                trxId |> PassivateTrx |> context.Parent.Tell)
                        | _ -> stay())
    override __.PersistenceId = "TransactionProcess_" + trxId
    override __.ApplyEvent(_, currentData) = 

        currentData

    override this.OnRecoveryCompleted() =
        let state = this.StateName
        match state with 
        | :? Start -> 
             TransferMoney(sourceId, targetId, amount , trxId) 
                |> tellToAccount sourceId
        | :? TargetApproving ->
             TransferMoney(sourceId, targetId, -amount , trxId)
                |> tellToAccount  targetId
        | :? SourceCommiting ->
            ConfirmReservation trxId 
                |> tellToAccount sourceId
        | :? TargetCommiting ->
            ConfirmReservation trxId 
                |> tellToAccount targetId
        | _ -> ()

    static member Props (sourceId, targetId , amount, trxId) = 
        Props.Create(fun _ -> 
            new TransactionProcess(sourceId, targetId, amount, trxId));

type CommandHandler () as commandHandler =
    inherit ReceivePersistentActor () 
    let context = ReceivePersistentActor.Context
    let publish = context.System.EventStream.Publish
    let mutable accounts : Map<string, IActorRef> = Map.empty
    let mutable transactions : Map<string, IActorRef> = Map.empty

    let mutable accountPassivationQueue : Map<string,int>  = Map.empty

    let updateState (event : Event) =
        match event with
        | :? AccountCreated as e -> 
            let actor = context.ActorOf( AccountActor.Props(e.AccountId),"Account_" + e.AccountId) 
            accounts <- accounts.Add(e.AccountId, actor)
            accountPassivationQueue <- accountPassivationQueue.Add(e.AccountId,1)
        | :? TrxCreated as e -> 
             let trxDetails =  e.Source,e.Target, e.Amount, e.TrxId
             let actor = context.ActorOf( TransactionProcess.Props(trxDetails),"TransactionProcess_" + e.TrxId) 
             transactions <- transactions.Add(e.TrxId, actor)
        | :? TrxPassivated as e ->
            context.Sender.GracefulStop(TimeSpan.FromSeconds(1.0)).Wait()
            transactions <- transactions.Remove e.Id
        | :? AccountPassivated as e ->
            let id = e.Id
            let count  = accountPassivationQueue.[id] - 1
            if count = 0 then
                context.Sender.GracefulStop(TimeSpan.FromSeconds(1.0)).Wait()
                accountPassivationQueue <- accountPassivationQueue.Remove id
                accounts <- accounts.Remove id
            else 
                accountPassivationQueue <-
                    accountPassivationQueue 
                    |> Map.remove id 
                    |> Map.add id count
        | _ -> ()
            
    let handleEvent (event : Event) = 
        commandHandler.Persist(event, fun e -> updateState e; publish e)

    let getAccount accountId = 
        match accounts.TryFind accountId with
        | Some actor -> Some actor
        | None -> 
              (accountId |> AccountCreated)  |> handleEvent 
              None
    let incrementPassivationQueue accountId =
         accountPassivationQueue <- 
                let count = accountPassivationQueue.[accountId]
                accountPassivationQueue 
                |> Map.remove accountId 
                |> Map.add (accountId) (count + 1)
     
    let getTrx (cmd : TransferMoney) = 
        match transactions.TryFind cmd.TrxId with
        | Some actor -> Some actor
        | None -> 
            let trxDetails = cmd.Source, cmd.Target, cmd.Amount, cmd.TrxId
            trxDetails |> TrxCreated |> handleEvent
            None

    let transferMoney (cmd:TransferMoney) = 
        match cmd.Source |> getAccount with
        | None -> context.Self.Tell cmd
        | Some sourceAccount ->
            match cmd.Target |> getAccount with
            | Some _ ->
                match cmd |> getTrx with
                | Some _ ->
                    incrementPassivationQueue cmd.Source
                    incrementPassivationQueue cmd.Target
                    sourceAccount.Tell cmd
                | None ->  context.Self.Tell cmd
            |None -> context.Self.Tell cmd
        true
    let passivateAccount (cmd : PassivateAccount) = 
        handleEvent <| AccountPassivated(cmd.Id)
       
    do 
        commandHandler.Recover(updateState)
        commandHandler.Command(transferMoney)
    override __.PersistenceId = "CommandHandler"
    static member Props () = Props.Create(fun _ -> new CommandHandler());
    




 