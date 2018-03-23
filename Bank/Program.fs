open Domain
open Akka.Actor
open Commands
open Microsoft.Extensions.Configuration;
open System.IO
open System
open Akka.Configuration
open System.Reflection
open System.Threading

[<EntryPoint>]
let main _ =
    let dllPath = Assembly.GetEntryAssembly().Location
    let dir = FileInfo(dllPath).Directory
    let config =
        ConfigurationBuilder()
            .SetBasePath(dir.FullName)
            .AddJsonFile("appsettings.json")
            .Build();
    let s = config.["akka"]
    let c = ConfigurationFactory.ParseString(s)
    let props = CommandHandler.Props ()
    let sys = ActorSystem.Create("Bank",c)
    let commandHandler = sys.ActorOf(props,"CommandHandler")
    commandHandler.Tell(SetBalance("1",2000M))
    commandHandler.Tell(SetBalance("2",3000M))
    commandHandler.Tell(TransferMoney("1","2",100M,Guid.NewGuid().ToString()))

    commandHandler.Tell(TransferMoney("1","2",1950M,Guid.NewGuid().ToString()))

    Console.ReadKey() |> ignore
    let z = commandHandler
    Console.WriteLine(z.Path)
    0