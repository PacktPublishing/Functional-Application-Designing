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

    let updateState (event : Event) =
        match event with
        | :? BalanceSet as e ->
            currentBalance <- e.Amount
            availableBalance <- currentBalance

        | :? AmountReserved as e ->
            match reservations.TryFind e.TrxId with
            | None ->
                reservations <- reservations.Add(e.TrxId, e.Amount)
                if  e.Amount > 0M then
                    availableBalance <- availableBalance - e.Amount
            | _ -> ()

        | :? ReservationConfirmed as e ->
            match reservations.TryFind e.TrxId with
            | Some amount ->
                reservations <- reservations.Remove e.TrxId
                currentBalance <- currentBalance - amount
                if  amount < 0M then
                    availableBalance <- availableBalance - amount
            | _ -> ()
        | _ -> ()

    let tellToTransactionProcess e trxId =
        let transactionProcess = "../TransactionProcess_"
        context.ActorSelection(transactionProcess + trxId).Tell(e)

    let passivateIfApplicable () =
        if(reservations = Map.empty) then
            accountId |> PassivateAccount |> context.Parent.Tell

    let publishSideEffects (event : Event) =
         publish event
         match event with
         | :? TransferRejected as e ->
            tellToTransactionProcess e (e.TrxId)
            passivateIfApplicable()

         | :? AmountReserved as e -> tellToTransactionProcess e (e.TrxId)
         | :? BalanceSet -> passivateIfApplicable()
         | :? ReservationConfirmed as e ->
            tellToTransactionProcess e (e.TrxId)
            passivateIfApplicable()

         | _ -> ()

    let handleEvent (event : Event) =
        account.Persist(event,
            fun e ->
                updateState e
                publishSideEffects e)

    let transferMoney (cmd : TransferMoney) =
        if  cmd.Amount > availableBalance then
            handleEvent <| TransferRejected(cmd.TrxId, "Not enough balance")
        else
            handleEvent <| AmountReserved(cmd.Amount, cmd.TrxId)
        true

    let setBalance (cmd : SetBalance) =
       handleEvent <| BalanceSet cmd.Amount


    let confirmReservation (cmd : ConfirmReservation) =
        let event = ReservationConfirmed cmd.TrxId
        match reservations.TryFind cmd.TrxId with
        | Some _ -> handleEvent event
        | _ -> publishSideEffects event
        true

    let recover (message:obj) =
        match message with
        | :? Event as e -> updateState e
        | _ -> ()

    do
        account.Command transferMoney
        account.Command confirmReservation
        account.Command setBalance
        account.Recover recover

    override __.PersistenceId = "Account_" + accountId

    static member Props accountId =
        Props.Create(fun _ -> AccountActor accountId);


type TransactionProcess (cmd:TransferMoney) as t =
    inherit PersistentFSM<IState, obj, Event>()
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
        base.StartWith(start, null)
        base.When(start,
                fun x ->
                    fun _->
                        match x.FsmEvent with
                        | :? AmountReserved ->
                            goto(targetApproving)
                                .AndThen(fun _ ->
                                    TransferMoney(cmd.Source,cmd.Target,-cmd.Amount,cmd.TrxId) 
                                        |> tellToAccount cmd.Target)
                        | :? TransferRejected ->
                            cmd.TrxId |> PassivateTrx |> context.Parent.Tell
                            stop()

                        | _ -> stay())
        base.When(targetApproving,
            fun x ->
                    fun _->
                        match x.FsmEvent with
                        | :? AmountReserved as e ->
                            goto(sourceCommiting)
                                .AndThen(fun _ ->
                                    ConfirmReservation e.TrxId |>
                                        tellToAccount cmd.Source)
                        | _ -> stay())

        base.When(sourceCommiting,
            fun x ->
                    fun _->
                        match x.FsmEvent with
                        | :? ReservationConfirmed as e ->
                            goto(targetCommiting)
                                .AndThen(fun _ ->
                                    ConfirmReservation e.TrxId |>
                                        tellToAccount cmd.Target)
                        | _ -> stay())
        base.When(targetCommiting,
            fun x ->
                    fun _->
                        match x.FsmEvent with
                        | :? ReservationConfirmed ->
                                cmd.TrxId |> PassivateTrx |> context.Parent.Tell
                                stop()
                        | _ -> stay())
    override __.PersistenceId = "TransactionProcess_" + cmd.TrxId
    override __.ApplyEvent(_, currentData) =

        currentData

    override this.OnRecoveryCompleted() =
        let state = this.StateName
        match state with
        | :? Start ->cmd |> tellToAccount cmd.Source
        | :? TargetApproving -> cmd |> tellToAccount  cmd.Target
        | :? SourceCommiting ->
            ConfirmReservation cmd.TrxId
                |> tellToAccount cmd.Source
        | :? TargetCommiting ->
            ConfirmReservation cmd.TrxId
                |> tellToAccount cmd.Target
        | _ -> ()

    static member Props cmd =
        Props.Create(fun _ -> TransactionProcess cmd);

type CommandHandler () as commandHandler =
    inherit ReceivePersistentActor ()
    let context = ReceivePersistentActor.Context
    let publish = context.System.EventStream.Publish
    let mutable transactions : Map<string, TransferMoney> = Map.empty

    let mutable accountPassivationQueue : Map<string,int> = Map.empty
    let mutable accounts : Map<string, IActorRef>  = Map.empty


    let updateState (event : Event) =
        match event with
        | :? TrxCreated as e ->
             transactions <- transactions.Add(e.Command.TrxId,e.Command)
        | :? TrxPassivated as e ->
            transactions <- transactions.Remove e.Id
        | _ -> ()

    let handleEvent (event : Event) action =
        commandHandler.Persist(event, fun e -> updateState e; action(); publish e)

    let incrementPassivationQueue accountId =
         accountPassivationQueue <-
                let count = accountPassivationQueue.[accountId]
                accountPassivationQueue
                |> Map.remove accountId
                |> Map.add (accountId) (count + 1)

    let createAccount accountId =
        match accountPassivationQueue.TryFind accountId with
        | Some _ -> incrementPassivationQueue accountId; accounts.[accountId]
        | None ->
            let actor = context.ActorOf( AccountActor.Props(accountId), "Account_" + accountId)
            accountPassivationQueue <- accountPassivationQueue.Add(accountId,1)
            accounts<- accounts.Add(accountId, actor)
            actor

    let createTrx (cmd : TransferMoney) =
        if not(transactions.ContainsKey(cmd.TrxId)) then
            handleEvent (cmd |> TrxCreated )
                (fun () ->  context.ActorOf(TransactionProcess.Props(cmd)
                    , "TransactionProcess_" + cmd.TrxId) |> ignore)

    let transferMoney (cmd:TransferMoney) =
        cmd.Source |> createAccount |> ignore
        cmd.Target |> createAccount |> ignore
        cmd |> createTrx
        true

    let setBalance (cmd:SetBalance) =
        (cmd.AccountId |> createAccount).Tell cmd
        true

    let passivateAccount (cmd : PassivateAccount) =
         let id = cmd.Id
         let count  = accountPassivationQueue.[id] - 1
         if count = 0 then
            context.Sender.GracefulStop(TimeSpan.FromSeconds(1.0)).Wait()
            accountPassivationQueue <- accountPassivationQueue.Remove id
            accounts <- accounts.Remove(id)
         else
            accountPassivationQueue
                <- accountPassivationQueue
                    |> Map.remove id
                    |> Map.add id count

    let passivateTrx (cmd : PassivateTrx) =
        handleEvent (TrxPassivated(cmd.Id)) (fun () ->
            context.Sender.GracefulStop(TimeSpan.FromSeconds(1.0)).Wait())


    let recover (e:obj) =
        match e with
        | :? RecoveryCompleted ->
             transactions |> Map.iter (fun _ v -> transferMoney v |> ignore)
        | :? Event as ev -> updateState ev
        | _ ->()

    do
        commandHandler.Recover(recover)
        commandHandler.Command(transferMoney)
        commandHandler.Command(setBalance)
        commandHandler.Command(passivateAccount)
        commandHandler.Command(passivateTrx)

    override __.PersistenceId = "CommandHandler"
    static member Props () = Props.Create(fun _ -> new CommandHandler());





