open Domain
open Akka.Actor
open Commands
open Microsoft.Extensions.Configuration;
open System.IO
open System
open Akka.Configuration
open System.Reflection
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
    commandHandler.Tell(TransferMoney("1","2",100M,"1_2"))
    Console.ReadKey() |> ignore
    0