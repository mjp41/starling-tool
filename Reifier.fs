/// The part of the Starling process that performs the backend-agnostic
/// (in theory) part of reification.
module Starling.Reifier

open Starling.Collections
open Starling.Core.Expr
open Starling.Core.Model
open Starling.Core.Command
open Starling.Core.GuardedView

/// Calculate the multiset of ways that this View matches the pattern in dv and add to the assumulator.
let reifySingleDef view accumulator (dv : ViewDef<DView>) : ViewSet  = 

    let rec matchMultipleViews (pattern : DFunc list) (view : GFunc list) accumulator result =
        match pattern with
        | [] ->
                //Pull out the set of guards used in this match, and add to the set
                let guars, views =
                    result
                    |> List.map gFuncTuple
                    |> List.unzip
                Multiset.add accumulator 
                    { // Then, separately add them into a ReView.
                    Cond = mkAnd guars
                    Item = List.rev views }
        | p :: pattern ->
            let rec matchSingleView (view : GFunc list) rview accumulator =
               match view with
               | [] -> accumulator
               | v :: view ->
                  let accumulator =
                    if p.Name = v.Item.Name && p.Params.Length = v.Item.Params.Length then
                        matchMultipleViews pattern (rview @ view) accumulator (v::result)
                    else
                        accumulator
                  matchSingleView view (v :: rview) accumulator
            matchSingleView view [] accumulator

    matchMultipleViews dv.View view accumulator []

/// Reifies an dvs entire view application.
let reifyView (dvs : ViewDef<DView> List)  vap : ViewSet = 
    let goal = Multiset.toFlatList vap
    Seq.fold (reifySingleDef goal) Multiset.empty dvs

/// Reifies all of the views in a term.
let reifyTerm dvs = 
    (* For the goal, we need only calculate D(r), not |_r_|.
     * This means we need not do anything with the goal.
     *)
    mapTerm id (reifyView dvs) id

/// Reifies all of the terms in a model's axiom list.
let reify : Model<PTerm<GView, OView>, DView> -> Model<PTerm<ViewSet, OView>, DView> =
    fun ms -> 
        mapAxioms (reifyTerm ms.ViewDefs) ms
