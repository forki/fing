﻿// Copyright (c) 2010, Nathan Sanders
// Licence: New BSD. See accompanying documentation.


/// code specific to types retrieved from FSharp.Powerpack.Metadata
module FSharpTypes
open Microsoft.FSharp.Compiler.SourceCodeServices
open Types

let rec debinarize t =
  let rec debinarise = function
  | Arrow [t; Arrow ts] -> match debinarize (Arrow ts) with
                           | Arrow ts -> Some (Arrow (debinarize t::ts))
                           | _ -> failwith "oh no"
  | _ -> None
  Types.map debinarise id t

let isArray (e:FSharpType) =
  let name = e.TypeDefinition.DisplayName
  // it appears that DisplayName can either be array or []
  // I think this is inconsistency on the part of either the Powerpack or (more likely)
  // the F# runtime in allowing Set.ofArray : array<'t> -> Set<'t>
  // while all the Array functions are eg : ('a -> 'b) -> 'a[] -> 'b[]
  // OH WELL. Should work now
  name = "array" || name.StartsWith "[" && name.EndsWith "]"
/// e must be the FSharpType of an array
let dimensions (e:FSharpType) = 
  match e.TypeDefinition.DisplayName with
  | "array" -> 1
  | brackets -> brackets.Length - 1
let tryFindConstraint (param:FSharpGenericParameter) (p,f) =
  match param.Constraints |> Seq.tryFind p with
  | Some(x) -> Some (f x)
  | None -> None
let rec optionsum = function
| [] -> None
| Some(x)::xs -> Some(x)
| None::xs -> optionsum xs
(* FSharpType -> Typ, no longer a Ty<System.Type,string> 
TODO: cvtParam is not done, it really only does type defaulting as yet
*)
let rec cvt (e:FSharpType) =
  if e.IsTupleType then
    Tuple (e.GenericArguments |> Seq.map cvt |> List.ofSeq)
  elif e.IsFunctionType then
      Arrow (e.GenericArguments |> Seq.map cvt |> List.ofSeq)
  elif e.IsGenericParameter then
    cvtParam e.GenericParameter // TODO: cvtParam is very much not done
  elif e |> isArray then // It only has in defaulting so far
    Array(dimensions e, e.GenericArguments |> Seq.map cvt |> Seq.head)
  else
    match e.TypeDefinition |> canonicalType, e.GenericArguments |> Seq.map cvt |> List.ofSeq with
    | t,[] -> t
    | Generic(t,_),args -> Generic(t,args)
    | t,args -> Generic(t, args)
and cvtParam (param:FSharpGenericParameter) =
  if Seq.isEmpty param.Constraints then
    Var (Normal param.Name)
  else
    match param.Constraints 
          |> Seq.tryFind (fun c -> c.IsDefaultsToConstraint 
                                   && c.DefaultsToConstraintData.DefaultsToTarget.HasTypeDefinition) with
    | Some def -> def.DefaultsToConstraintData.DefaultsToTarget.TypeDefinition |> canonicalType
    | None -> Var (Normal param.Name)
    // param.Constraints |> Seq.map whenify |> Seq.fold SOMETHING param
(* Gets something stable from an FSharpEntity so that we can see if two are identical *)
and canonicalType (e:FSharpEntity) =
  if e.IsFSharpAbbreviation then
    cvt e.AbbreviatedType
  else
    // the dealias here is a hack because
    // unit-of-measure types do not have IsAbbreviation set.
    // TODO: I have no idea how to make this work once real alias detection
    // is implemented, because it will (probably) be based on IsAbbreviation.
    Id e.DisplayName |> ParsedTypes.dealias // e.ReflectionType |> string
and whenify (param:FSharpGenericParameter) (con:FSharpGenericParameterConstraint) =
  // NOTE: This is missing several important (but non-syntactic) kinds of constraints
  // particuarly the defaults constraint
  // Also: I have no way to compose whenify's results  in cvtParam. Most types with constraints
  // have multiple constraints, and these are also duplicated for each occurrence of the type
  if con.IsSupportsNullConstraint then
    Null (Normal param.Name)
  elif con.IsReferenceTypeConstraint then
    NotStruct (Normal param.Name)
  elif con.IsNonNullableValueTypeConstraint then
    Struct (Normal param.Name)
  elif con.IsRequiresDefaultConstructorConstraint then
    DefaultConstructor (Normal param.Name)
  elif con.IsEnumConstraint then
    Enum(Normal param.Name, cvt con.EnumConstraintTarget)
  elif con.IsDelegateConstraint then
    Delegate(Normal param.Name, cvt con.DelegateConstraintData.DelegateTupledArgumentType, cvt con.DelegateConstraintData.DelegateReturnType)
  elif con.IsCoercesToConstraint then
    Subtype(Normal param.Name, cvt con.CoercesToTarget)
  elif con.IsMemberConstraint then
    Sig(Structural param.Name,
        Id con.MemberConstraintData.MemberName,
        Arrow (List.ofSeq (Seq.map cvt con.MemberConstraintData.MemberArgumentTypes) @ [cvt con.MemberConstraintData.MemberReturnType]), 
        Function)
  else
    failwith "basically this style of data structure just sucks"
