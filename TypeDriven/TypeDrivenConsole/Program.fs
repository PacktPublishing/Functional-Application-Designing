// Learn more about F# at http://fsharp.org

open Domain
open Domain.Result

let p = Price.create(10,999)
let p2 = Price.create(10,99)  
printfn "%A" (p = p2)
let product = (Product.create "Phone") <!> p

match product with
| Ok {price = p } -> 
    printf "Product price with tax: %A" 
        (p <**> 1.15M |> Price.toValue)
        
| Error err -> printf "%A" <| err 


  
