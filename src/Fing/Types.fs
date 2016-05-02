﻿// Copyright (c) 2010, Nathan Sanders
// Licence: New BSD. See accompanying documentation.
module Types

open Util

/// type aliases. Probably there is an existing list of these
/// but for now I have just hard coded a few of my favourites
let aliases = 
    Map.ofList [ "int", "Int32" //"System.Int32"
                 "obj", "Object"
                 "seq", "IEnumerable" // "System.Collections.Generic.IEnumerable`1[T]"
                 "bool", "Boolean" // "System.Boolean"
                 "string", "String" // "System.String"
                 "char", "Char" // "System.Char"
                 // TODO: I don't know whether lower case 'map' is a correct alias
                 //"Set", "Set" // "Microsoft.FSharp.Collections.FSharpSet`1[T]"
                 //"map", "Map" // "Microsoft.FSharp.Collections.FSharpMap`2[TKey,TValue]"
                 "option", "Option" //"Microsoft.FSharp.Collections.FSharpOption`1[T]"
                 "list", "List" // "Microsoft.FSharp.Collections.FSharpList`1[T]"
                 "float", "Double" // "System.Double"
                 "double", "Double" //"System.Double"
                 "option", "Option" //"Microsoft.FSharp.Core.FSharpOption`1[T]"
                 "unit", "Unit" // "Microsoft.FSharp.Core.Unit"
                                ]

let revMap = 
    Map.toSeq
    >> Seq.map (fun (k, v) -> (v, k))
    >> Map.ofSeq

let unaliases = revMap aliases


type Typar = 
    | Anonymous
    | Normal of string
    | Structural of string
    | Choice of list<Typar>

type Property = 
    | Function
    | Get
    | Set
    | GetSet

type Typ = 
    | Arrow of list<Typ>
    | Tuple of list<Typ>
    | Var of Typar
    | Id of string
    | NamedArg of string * Typ * bool
    | Generic of Typ * list<Typ>
    | Array of int * Typ
    | Constraint of When * Typ

and When = 
    | Null of Typar
    | Struct of Typar
    | NotStruct of Typar
    | DefaultConstructor of Typar
    | Enum of Typar * Typ
    | Delegate of Typar * Typ * Typ
    | Subtype of Typar * Typ
    | Sig of Typar * Typ * Typ * Property
    | TyparConstraint of list<When> // where Typ = Constraint


let rec format = 
    function 
    | Arrow ts -> "(" + String.concat " -> " (Seq.map format ts) + ")"
    | Tuple ts -> "(" + String.concat " * " (Seq.map format ts) + ")"
    | Var v -> formatTypar v
    | Id name when Map.containsKey name unaliases -> Map.find name unaliases
    | Id name -> name
    | NamedArg(name, t, opt) -> 
        (if opt then "?"
         else "") + name + ":" + format t
    | Generic(t, ts) -> 
        format t + "<" + String.concat "," (Seq.map format ts) + ">"
    | Array(n, t) -> format t + "[" + String.replicate (n - 1) "," + "]"
    // this ad-hockery doesn't work because the ignored _ of Constraint can
    // contain nested constraints, which have to be separated from the normal
    // 
    | Constraint(TyparConstraint cons, Generic(t, ts)) -> 
        sprintf "%s<%s when %s>" (format t) 
            (String.concat "," (Seq.map format ts)) 
            (String.concat " and " (Seq.map formatConstraint cons))
    // OLD NOTES:
    // this almost works, except what you really want is to just extract
    // the list of constraints, ignoring the variables they apply to
    // (you already know that from the containing Sig)
    // so REALLY the Right Thing is to change the type so that
    // TyparConstraint returns a single list of constraints,
    // and no Vars to which it applies, since it applies equally to
    // all the vars of the sig it's a part of
    // TODO: Change the types, the parser and everything else to handle this
    | Constraint(con, t) -> format t + " when " + formatConstraint con

and formatTypar = 
    function 
    | Anonymous -> "_"
    | Normal name -> "'" + name
    | Structural name -> "^" + name
    | Choice typars -> 
        "(" + String.concat " or " (Seq.map formatTypar typars) + ")"

and formatConstraint = 
    function 
    | Null v -> formatTypar v + " : null"
    | Struct v -> formatTypar v + " : struct"
    | NotStruct v -> formatTypar v + " : not struct"
    | DefaultConstructor v -> formatTypar v + " : (new : unit -> 'T)"
    | Enum(v, t) -> sprintf "%s : enum<%s>" (formatTypar v) (format t)
    | Delegate(v, arg, res) -> 
        sprintf "%s : delegate<%s,%s>" (formatTypar v) (format arg) (format res)
    | Subtype(v, t) -> formatTypar v + " :> " + format t
    | Sig(v, name, t, prop) -> 
        let chompchomp (s : string) = 
            if s.Length > 2 then s.[1..s.Length - 2]
            else s
        sprintf "%s : (%s : %s%s)" (formatTypar v) (format name) 
            (format t |> chompchomp) (formatProperty prop)
    | TyparConstraint cons -> 
        String.concat " and " (Seq.map formatConstraint cons)

and formatProperty = 
    function 
    | Function -> ""
    | Get -> " with get"
    | Set -> " with set"
    | GetSet -> " with get,set"

let map f lookup = 
    let rec combiner t = 
        match f t with
        | Some t' -> t'
        | None -> 
            match t with
            | Var v -> Var v
            | Arrow types -> Arrow(List.map combiner types)
            | Tuple types -> Tuple(List.map combiner types)
            | Id id -> Id id
            | NamedArg(n, t, opt) -> NamedArg(n, combiner t, opt)
            | Generic(t, types) -> Generic(combiner t, List.map combiner types)
            | Array(n, t) -> Array(n, combiner t)
            | Constraint(var, t) -> 
                let t' = combiner t
                Constraint(constraintCombiner var, t')
    
    // TODO: constraintCombiner is not really done and definitely not tested
    and constraintCombiner = 
        function 
        | Null var -> Null(lookup var)
        | Struct var -> Struct(lookup var)
        | NotStruct var -> NotStruct(lookup var)
        | DefaultConstructor var -> DefaultConstructor(lookup var)
        // TODO: not sure combiner is the Right Thing .. 
        // even if it is, I might need to spawn a copy of indices good only in this scope
        | Enum(var, t) -> Enum(lookup var, combiner t)
        | Delegate(var, t, t') -> Delegate(lookup var, combiner t, combiner t')
        | Subtype(var, t) -> Subtype(lookup var, combiner t)
        | Sig(var, t, t', prop) -> 
            Sig(lookup var, combiner t, combiner t', prop)
        | TyparConstraint cons -> 
            TyparConstraint(List.map constraintCombiner cons)
    
    combiner

let fold next combine unit = 
    let rec transformer t = 
        match next t with
        | Some x -> x
        | None -> 
            match t with
            | Var _ -> unit
            | Arrow types -> combine (List.map transformer types)
            | Tuple types -> combine (List.map transformer types)
            | Id _ -> unit
            | NamedArg(_, t, _) -> transformer t
            | Generic(t, types) -> 
                combine (transformer t :: List.map transformer types)
            | Array(_, t) -> transformer t
            | Constraint(var, t) -> 
                let x = transformer t
                x // short-circuit Constraint processing for now;
    // callers will have to provide next/combine/unit functions for it as well
    transformer

// WHOA! Hardcoding aliases into a map called 'aliases' is a terrible idea!
// TODO: Fix this to search FSharp.Core for aliases and store them.
let dealias = 
    let dealias' = 
        function 
        | Id id when Map.containsKey id aliases -> Some(Id(Map.find id aliases))
        | _ -> None
    map dealias' id // might need to use something besides id someday

/// an infinite stream of possible variable names - for nicely named de Bruijn indices
#nowarn "40" // INFINITE SEQ IS INFINITE

let rec names = // and that's OK.
                
    let letters = [| 'a'..'z' |] |> Array.map string
    seq { 
        yield! letters
        for n in names do
            for l in letters do
                yield n + l
    }
    

// this is not really de Bruijn indexing but it is similar
// in particular, I use state to make multi-parameter things like Arrow and Tuple
// less painful to write; it's mapM in the State monad instead of fold over tuples.
// also I don't use numbers, so that the output is a syntactically valid F# type.
let index t = 
    let nextIndex = 
        let names = names.GetEnumerator()
        names.MoveNext() |> ignore // ignore: INFINITE SEQ.MoveNext is always true.
        fun () -> 
            let name = names.Current
            names.MoveNext() |> ignore
            name
    
    let indices = ref Map.empty
    
    let rec update v = 
        match Map.tryFind v !indices with
        | Some i -> i
        | None -> 
            let i = 
                match v with
                | Normal _ | Anonymous -> Normal(nextIndex())
                | Structural _ -> Structural(nextIndex())
                | Choice vars -> Choice(List.map update vars)
            indices.Value <- Map.add v i indices.Value
            i
    
    let updateVar = function 
        | Var v -> Some(Var(update v))
        | _ -> None
    
    let lookup v = Map.find v indices.Value
    map updateVar lookup t
