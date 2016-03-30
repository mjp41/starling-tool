﻿module Starling.Lang.AST

open Starling
open Starling.Collections
open Starling.Core.Model
open Starling.Core.Var.Types

/// <summary>
///     Types used in the AST.
/// </summary>
[<AutoOpen>]
module Types =
    /// A Boolean operator.
    type Bop =
        | Mul // a * b
        | Div // a / b
        | Add // a + b
        | Sub // a - b
        | Gt // a > b
        | Ge // a >= b
        | Le // a < b
        | Lt // a <= b
        | Eq // a == b
        | Neq // a != b
        | And // a && b
        | Or // a || b

    /// An untyped, raw expression.
    /// These currently cover all languages, but this may change later.
    type Expression =
        | True // true
        | False // false
        | Int of int64 // 42
        | LV of LValue // foobaz
        | Bop of Bop * Expression * Expression // a BOP b

    /// An atomic action.
    type Atomic =
        | CompareAndSwap of LValue * LValue * Expression // <CAS(a, b, c)>
        | Fetch of LValue * Expression * FetchMode // <a = b??>
        | Postfix of LValue * FetchMode // <a++> or <a-->
        | Id // <id>
        | Assume of Expression // <assume(e)

    /// A view prototype.
    type ViewProto = Func<(Type * string)>

    /// A view definition.
    type ViewDef =
        | Unit
        | Join of ViewDef * ViewDef
        | Func of Func<string>

    /// An AST func.
    type AFunc = Func<Expression>

    /// A view.
    type View =
        | Unit
        | Join of View * View
        | Func of AFunc
        | If of Expression * View * View

    /// A set of primitives.
    type PrimSet =
        { PreAssigns: (LValue * Expression) list
          Atomics: Atomic list
          PostAssigns: (LValue * Expression) list }

    /// A statement in the command language.
    type Command<'view> =
        /// A set of sequentially composed primitives.
        | Prim of PrimSet
        /// An if-then-else statement.
        | If of Expression
              * Block<'view, Command<'view>>
              * Block<'view, Command<'view>>
        /// A while loop.
        | While of Expression
                 * Block<'view, Command<'view>>
        /// A do-while loop.
        | DoWhile of Block<'view, Command<'view>>
                   * Expression // do { b } while (e)
        /// A list of parallel-composed blocks.
        | Blocks of Block<'view, Command<'view>> list

    /// A combination of a command and its postcondition view.
    and ViewedCommand<'view, 'cmd> =
        { Command : 'cmd // <a := b++>;
          Post : ViewExpr<'view> } // {| a = b |}

    /// A block or method body.
    and Block<'view, 'cmd> =
        { Pre : ViewExpr<'view>
          // Post-condition is that in the last Seq.
          Contents : ViewedCommand<'view, 'cmd> list }

    /// A constraint, binding a view to an optional expression.
    type Constraint =
        { CView : ViewDef
          CExpression : Expression option }

    /// A method.
    type Method<'view, 'cmd> =
        { Signature : Func<string> // main (argv, argc) ...
          Body : Block<'view, 'cmd> } // ... { ... }

    /// A top-level item in a Starling script.
    type ScriptItem =
        | Global of Type * string // global int name;
        | Local of Type * string // local int name;
        | Method of Method<View, Command<View>> // method main(argv, argc) { ... }
        | Search of int // search 0;
        | ViewProto of ViewProto // view name(int arg);
        | Constraint of Constraint // constraint emp => true


/// <summary>
///     Pretty printers for the AST.
/// </summary>
module Pretty =
    open Starling.Core.Pretty
    open Starling.Core.Model.Pretty
    open Starling.Core.Var.Pretty

    /// Pretty-prints lvalues.
    let rec printLValue = function
        | LVIdent i -> String i

    /// Pretty-prints Boolean operations.
    let printBop =
        function
        | Mul -> "*"
        | Div -> "/"
        | Add -> "+"
        | Sub -> "-"
        | Gt -> ">"
        | Ge -> ">="
        | Le -> "<"
        | Lt -> "<="
        | Eq -> "=="
        | Neq -> "!="
        | And -> "&&"
        | Or -> "||"
        >> String

    /// Pretty-prints expressions.
    /// This is not guaranteed to produce an optimal expression.
    let rec printExpression =
        function
        | True -> String "true"
        | False -> String "false"
        | Expression.Int i -> i.ToString() |> String
        | LV x -> printLValue x
        | Bop(op, a, b) ->
            hsep [ printExpression a
                   printBop op
                   printExpression b ]
            |> parened

    /// Pretty-prints views.
    let rec printView =
        function
        | View.Func f -> printFunc printExpression f
        | View.Unit -> String "emp"
        | View.Join(l, r) -> binop "*" (printView l) (printView r)
        | View.If(e, l, r) ->
            hsep [ String "if"
                   printExpression e
                   String "then"
                   printView l
                   String "else"
                   printView r ]

    /// Pretty-prints view definitions.
    let rec printViewDef =
        function
        | ViewDef.Func f -> printFunc String f
        | ViewDef.Unit -> String "emp"
        | ViewDef.Join(l, r) -> binop "*" (printViewDef l) (printViewDef r)

    /// Pretty-prints constraints.
    let printConstraint { CView = v; CExpression = e } =
        hsep [ String "constraint"
               printViewDef v
               String "->"
               e |> Option.map printExpression |> withDefault (String "?") ]
        |> withSemi

    /// Pretty-prints fetch modes.
    let printFetchMode =
        function
        | Direct -> Nop
        | Increment -> String "++"
        | Decrement -> String "--"

    /// Pretty-prints local assignments.
    let printAssign dest src =
        equality (printLValue dest) (printExpression src)

    /// Pretty-prints atomic actions.
    let printAtomic =
        function
        | CompareAndSwap(l, f, t) ->
            func "CAS" [ printLValue l
                         printLValue f
                         printExpression t ]
        | Fetch(l, r, m) ->
            equality (printLValue l) (hjoin [ printExpression r
                                              printFetchMode m ])
        | Postfix(l, m) ->
            hjoin [ printLValue l
                    printFetchMode m ]
        | Id -> String "id"
        | Assume e -> func "assume" [ printExpression e ]

    /// Pretty-prints viewed commands with the given indent level (in spaces).
    let printViewedCommand (pView : 'view -> Command)
                           (pCmd : 'cmd -> Command)
                           ({ Command = c; Post = p } : ViewedCommand<'view, 'cmd>) =
        vsep [ pCmd c ; printViewExpr pView p ]

    /// Pretty-prints blocks with the given indent level (in spaces).
    let printBlock (pView : 'view -> Command)
                   (pCmd : 'cmd -> Command)
                   ({ Pre = p; Contents = c } : Block<'view, 'cmd>)
                   : Command =
        vsep ((p |> printViewExpr pView |> Indent)
              :: List.map (printViewedCommand pView pCmd >> Indent) c)
        |> braced

    /// Pretty-prints methods.
    let printMethod (pView : 'view -> Command)
                    (pCmd : 'cmd -> Command)
                    ({ Signature = s; Body = b } : Method<'view, 'cmd>)
                    : Command =
        hsep [ "method" |> String
               printFunc String s
               printBlock pView pCmd b ]

    /// Pretty-prints commands with the given indent level (in spaces).
    let rec printCommand =
        function
        (* The trick here is to make Prim [] appear as ;, but
           Prim [x; y; z] appear as x; y; z;, and to do the same with
           atomic lists. *)
        | Prim { PreAssigns = ps
                 Atomics = ts
                 PostAssigns = qs } ->
            seq { yield! Seq.map (uncurry printAssign) ps
                  yield (ts
                         |> Seq.map printAtomic
                         |> semiSep |> withSemi |> braced |> angled)
                  yield! Seq.map (uncurry printAssign) qs }
            |> semiSep |> withSemi
        | If(c, t, f) ->
            hsep [ "if" |> String
                   c
                   |> printExpression
                   |> parened
                   t |> printBlock printView printCommand
                   f |> printBlock printView printCommand ]
        | While(c, b) ->
            hsep [ "while" |> String
                   c
                   |> printExpression
                   |> parened
                   b |> printBlock printView printCommand ]
        | DoWhile(b, c) ->
            hsep [ "do" |> String
                   b |> printBlock printView printCommand
                   "while" |> String
                   c
                   |> printExpression
                   |> parened ]
            |> withSemi
        | Blocks bs ->
            bs
            |> List.map (printBlock printView printCommand)
            |> hsepStr "||"

    /// Pretty-prints a view prototype.
    let printViewProto { Name = n; Params = ps } =
        hsep [ "view" |> String
               func n (List.map (fun (t, v) ->
                           hsep [ t |> printType
                                  v |> String ]) ps) ]
        |> withSemi

    /// Pretty-prints a search directive.
    let printSearch i =
        hsep [ String "search"
               sprintf "%d" i |> String ]

    /// Pretty-prints a script variable of the given class.
    let printScriptVar cls t v =
        hsep [ String cls
               printType t
               String v ]
        |> withSemi

    /// Pretty-prints script lines.
    let printScriptLine =
        function
        | Global(t, v) -> printScriptVar "shared" t v
        | Local(t, v) -> printScriptVar "thread" t v
        | Method m -> printMethod printView printCommand m
        | ViewProto v -> printViewProto v
        | Search i -> printSearch i
        | Constraint c -> printConstraint c

    /// Pretty-prints scripts.
    let printScript = List.map printScriptLine >> fun ls -> VSep(ls, vsep [ Nop; Nop ])


(*
 * Expression classification
 *)

/// Active pattern classifying bops as to whether they create
/// arithmetic or Boolean expressions.
let (|ArithOp|BoolOp|) =
    function
    | Mul | Div | Add | Sub -> ArithOp
    | Gt | Ge | Le | Lt -> BoolOp
    | Eq | Neq -> BoolOp
    | And | Or -> BoolOp

/// Active pattern classifying bops as to whether they take in
/// arithmetic, Boolean, or indeterminate operands.
let (|ArithIn|BoolIn|AnyIn|) =
    function
    | Mul | Div | Add | Sub -> ArithIn
    | Gt | Ge | Le | Lt -> ArithIn
    | Eq | Neq -> AnyIn
    | And | Or -> BoolIn

/// Active pattern classifying expressions as to whether they are
/// arithmetic, Boolean, or indeterminate.
let (|BoolExp|ArithExp|AnyExp|) =
    function
    | LV _ -> AnyExp
    | Int _ -> ArithExp
    | True | False -> BoolExp
    | Bop(BoolOp, _, _) -> BoolExp
    | Bop(ArithOp, _, _) -> ArithExp

(*
 * Misc
 *)

/// <summary>
///     Type-constrained version of <c>func</c> for <c>AFunc</c>s.
/// </summary>
/// <parameter name="name">
///   The name of the <c>AFunc</c>.
/// </parameter>
/// <parameter name="pars">
///   The parameters of the <c>AFunc</c>, as a sequence.
/// </parameter>
/// <returns>
///   A new <c>AFunc</c> with the given name and parameters.
/// </returns>
let afunc name (pars : Expression list) : AFunc = func name pars
