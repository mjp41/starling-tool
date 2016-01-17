﻿/// The Z3 backend driver.
module Starling.Z3.Backend

open Microsoft
open Chessie.ErrorHandling
open Starling
open Starling.Model
open Starling.Utils
open Starling.Pretty.Misc

(*
 * Request and response types
 *)

/// Type of requests to the Z3 backend.
type Request =
    /// Only translate the term views; return `Output.Translate`.
    | Translate
    /// Translate and combine term Z3 expressions; return `Output.Combine`.
    | Combine
    /// Translate, combine, and run term Z3 expressions; return `Output.Sat`.
    | Sat

/// Type of responses from the Starling frontend.
[<NoComparison>]
type Response =
    /// Output of the term translation step only.
    | Translate of Model<ZTerm>
    /// Output of the final Z3 terms only.
    | Combine of Model<Microsoft.Z3.BoolExpr>
    /// Output of satisfiability reports for the Z3 terms.
    | Sat of Microsoft.Z3.Status list

(*
 * Error types
 *)

/// Type of errors generated by the Z3 backend.
type Error =
    /// A translation error occurred, given as a Translator.Error.
    | Translator of Starling.Errors.Z3.Translator.Error

(*
 * Pretty-printing
 *)

/// Pretty-prints a response.
let printResponse =
    function
    | Response.Translate {Axioms = t} -> printNumHeaderedList (printTerm printZ3Exp printZ3Exp) t
    | Response.Combine {Axioms = z} -> printNumHeaderedList printZ3Exp z
    | Response.Sat s -> Starling.Pretty.Misc.printSats s

/// Pretty-prints an error.
let printError =
    function
    | Error.Translator e -> Starling.Pretty.Errors.printZ3TranslatorError e

(*
 * Driver functions
 *)

/// Shorthand for the parser stage of the frontend pipeline.
let translate ctx = Translator.reifyZ3 ctx >> mapMessages Error.Translator
/// Shorthand for the collation stage of the frontend pipeline.
let combine = Translator.combineTerms >> lift
/// Shorthand for the modelling stage of the frontend pipeline.
let sat = Run.run >> lift

/// Runs the Starling Z3 backend.
/// Takes two arguments: the first is the `Response` telling the backend what
/// to output; the second is the reified model to process with Z3.
let run resp =
    use ctx = new Z3.Context()
    match resp with
    | Request.Translate -> translate ctx >> lift Response.Translate
    | Request.Combine -> translate ctx >> combine ctx >> lift Response.Combine
    | Request.Sat -> translate ctx >> combine ctx >> sat ctx >> lift Response.Sat
