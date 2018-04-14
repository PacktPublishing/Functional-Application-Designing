module Domain
open System

type UnsafeProduct ={
    name : string
    price : decimal
}

[<CustomEquality; CustomComparison>]
type Price = 
    private | Price of decimal
    static member (+) (Price price1, Price price2) = 
        Price(price1 + price2)
    static member (-) (Price price1 , Price price2) = 
        Price(price1 - price2)
    static member (<**>) (Price price1, d:decimal) = 
        Decimal.Round(price1 * d, 2) |> Price
    static member (<//>) (Price price, d:decimal) = 
         Decimal.Round(price /d  , 2) |> Price
    static member (<**>) (d:decimal, price) = 
        price * d
 
    interface System.IComparable with
        member x.CompareTo y = 
            match y with
            | :? Price  as y -> 
                let (Price p1) = x  
                let (Price p2) = y
                compare p1 p2
            | _ -> failwith "cannot compare"
    override x.Equals(yobj) = 
        match yobj with
        | :? Price  as y  -> 
            let (Price p1) = x  
            let (Price p2) = y
            p1 = p2
        | _ -> false
    override x.GetHashCode() =
        let (Price p) = x
        hash p
     
type Product = {
    name : string
    price : Price
}
//Result<a->b>-> Result<a> -> Result<b>
//int -> int -> decimal
//apply map
//Result<int> -> Result<int->decimal>>
//apply apply
//Result<int> -> Result<int> -> Result<decimal>
module Result =
    let apply fRes xRes = 
        match fRes, xRes  with
        | Ok f, Ok x -> f x |> Ok
        | _ ,  Error e  -> Error e
        | Error e , _ -> Error e
        
    let (<!>) = Result.map
    let (<*>) = apply

module Price =
    let private validIntegerForPrice = function
       | integer when  integer >= 0 -> Ok integer
       | _ -> Result.Error "price cannot be negative."

    let private validIntegerForDecimal = function
        | decimal when  decimal >= 0 && decimal < 100 -> Ok decimal 
        | _ -> Result.Error "decimal points must be between 0 and 99 for a price"
    let private convert integer decimals
            = (decimal integer) + (decimal decimals) / decimal 100
    
    let toValue (Price decimal) = decimal

    open Result
    let create (integer, decimals) =
        Price <!> 
            (convert <!> 
                (integer |> validIntegerForPrice) 
                <*> ( decimals |> validIntegerForDecimal))


    let create2 (integer, decimals) =
        let validInteger = integer |> validIntegerForPrice
        let validDecimal = decimals |> validIntegerForDecimal
        //let result = convert validInteger validDecimal
        let liftedConvert = Ok convert
       // let result = liftedConvert validInteger validDecimal
        let firstParameterApplied = liftedConvert <*> validInteger
        let secondParamgerApplied = firstParameterApplied <*> validDecimal
        // a-> b -> c
        //E<a->b->c>    : Applied return
        //E<a> -> E<b->c> : Applied apply

        // a->b->c
        //E<a>->E<b->c> : Applied map
        //Result.map Price secondParamgerApplied
        Price <!> secondParamgerApplied

        //failwith "not implemented yet"
        


module Product = 
    let create name price = { name = name ; price = price}


        

        
            


    

