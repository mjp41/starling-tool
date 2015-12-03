module Starling.Pretty.Misc

open Microsoft
open Starling
open Starling.Var
open Starling.AST
open Starling.Collator
open Starling.Model
open Starling.Modeller
open Starling.Errors.Modeller
open Starling.Errors.Var
open Starling.Pretty.AST
open Starling.Pretty.Types

/// Pretty-prints a collated script.
let printCollatedScript cs =
    VSep ([ vsep <| List.map (printViewProto >> String) cs.CVProtos
            vsep <| List.map (uncurry (printScriptVar "global") >> String) cs.CGlobals
            vsep <| List.map (uncurry (printScriptVar "local") >> String) cs.CLocals
            vsep <| List.map (printConstraint >> String) cs.CConstraints
            VSep (List.map (printMethod >> String) cs.CMethods, Separator) ],
          Separator)

/// Pretty-prints variable conversion errors.
let printVarMapError ve =
    match ve with
    | VMEDuplicate vn -> "variable '" + vn + "' is defined multiple times"
    | VMENotFound vn -> "variable '" + vn + "' not in environment"

/// Pretty-prints expression conversion errors.
let printExprError ee =
    match ee with
    | EEBadAST (ast, reason) ->
        "cannot convert " + printExpression ast
                          + " to Z3: " + reason
    | EEVar (ast, vme) ->
        "bad variable usage in " + printExpression ast
                                 + ": " + printVarMapError vme
    | EEVarNotBoolean lv ->
        "lvalue '" + printLValue lv
                   + "' is not a suitable type for its use in a boolean expression"
    | EEVarNotArith lv ->
        "lvalue '" + printLValue lv
                   + "' is not a suitable type for use in an arithmetic expression"

/// Pretty-prints view conversion errors.
let printViewError ve =
    match ve with
    | VEBadExpr (view, ee) ->
        "bad expression in '" + printView view
                      + "': " + printExprError ee
    | VEUnsupported (view, reason) ->
        "view '" + printView view + "' not supported: " + reason

/// Pretty-prints viewdef conversion errors.
let printViewDefError ve =
    match ve with
    | VDENoSuchView name ->
        "no view prototype for " + name
    | VDEBadParamCount (name, expected, actual) ->
        let exps = sprintf "%d" expected
        let acts = sprintf "%d" actual
        "view '" + name + "' expects " + exps
                        + "params, but is given " + acts
    | VDEBadVars vme ->
        "view variable inconsistency: " + printVarMapError vme
    | VDEGlobalVarConflict vme ->
        "view variables conflict with globals: " + printVarMapError vme

/// Pretty-prints constraint conversion errors.
let printConstraintError ce =
    match ce with
    | CEView ve -> printViewDefError ve
    | CEExpr ee -> printExprError ee

/// Pretty-prints axiom errors.
let printAxiomError ae =
    match ae with
    | AEBadGlobal le -> "error resolving global: " + printVarMapError le
    | AEBadLocal le -> "error resolving local: " + printVarMapError le
    | AEBadExpr ee -> "bad expression in axiom: " + printExprError ee
    | AEBadView ve -> "bad view in axiom: " + printViewError ve
    | AETypeMismatch (expected, badvar, got) ->
        "type error: " + printLValue badvar
                       + " is of type " + printType got
                       + ", but should be of type " + printType expected
    | AEUnsupportedAtomic (atom, reason) ->
        "cannot use " + printAtomicAction atom
                      + " in an axiom: " + reason
    | AEUnsupportedCommand (cmd, reason) ->
        "cannot use " + printCommand 0 cmd
                      + " in an axiom: " + reason

/// Pretty-prints view prototype conversion errors
let printViewProtoError vpe =
    match vpe with
    | VPEDuplicateParam (vp, param) ->
        "view proto " + printViewProto vp
                      + " has duplicate param " + param

/// Pretty-prints model conversion errors.
let printModelError ce =
    match ce with
    | MEConstraint ce -> printConstraintError ce
    | MEVar ve -> printVarMapError ve
    | MEAxiom ae -> printAxiomError ae
    | MEVProto vpe -> printViewProtoError vpe

/// Pretty-prints a singular generic view.
let printGenView ppars v =
    hsep [ String v.VName
           parened (HSep (List.map ppars v.VParams, String ",")) ]

/// Pretty-prints Z3 expressions.
let printZ3Exp (expr: #Z3.Expr) = String (expr.ToString ())

/// Pretty-prints a singular view assertion.
let printModelView = printGenView printZ3Exp

/// Pretty-prints a type-name parameter.
let printParam (ty, name) =
    hsep [String <| printType ty
          String name]

/// Pretty-prints a singular view definition.
let printModelViewDef = printGenView printParam

/// Pretty-prints a multiset of views.
let printModelViews vs =
    squared (HSep (List.map printModelView vs, String ","))

/// Pretty-prints a multiset of viewdefs.
let printModelViewDefs vs =
    squared (HSep (List.map printModelViewDef vs, String ","))

/// Pretty-prints TVars.
let printTVar tvar =
    ssurround "(Z3:"
              ")"
              (HSep ( [ printZ3Exp tvar.VarExpr
                        printZ3Exp tvar.VarPreExpr
                        printZ3Exp tvar.VarPostExpr
                        printZ3Exp tvar.VarFrameExpr ], String ","))

/// Pretty-prints model variables.
let printModelVar nvar =
    let name, var = nvar
    HSep ( [ String name
             (match var with
              | IntVar tv -> hsep [ String "int"
                                    printTVar tv ]
              | BoolVar tv -> hsep [ String "bool"
                                     printTVar tv ] ) ],
           String ":")

/// Pretty-prints a conditional view.
let rec printCondView cv =
    match cv with
    | CITEView (i, t, e) ->
        hsep [ String "if"
               printZ3Exp i
               String "then"
               printCondViewList t
               String "else"
               printCondViewList e ]
    | CSetView v -> printModelView v

/// Pretty-prints a list of cond-views.
and printCondViewList cvs =
    ssurround "[| "
              " |]"
              (HSep (List.map printCondView cvs, String ";"))

/// Pretty-prints a guarded view.
let printGuarView gv =
    ssurround "("
              ")"
              (HSep ([ printZ3Exp gv.GCond
                       printModelView gv.GView
                     ], String ","))

/// Pretty-prints a list of guar-views.
let printGuarViewList cvs =
    ssurround "<| "
              " |>"
              (HSep (List.map printGuarView cvs, String ";"))

/// Pretty-prints something wrapped in a general condition pair.
let printInConditionPair pcond cpair inner =
    Surround (hsep [ pcond cpair.Pre ; Nop ],
              inner,
              hsep [ Nop ; pcond cpair.Post ])

/// Lifts a pretty-printer to optional values.
let printOption pp ov =
    match ov with
    | None -> String "(none)"
    | Some v -> pp v

/// Pretty-prints a fetch prim.
let printFetchPrim ty dest src mode =
    hsep [ String ("fetch<" + ty + ">")
           parened (hsep [ printOption (printLValue >> String) dest
                           String "="
                           String (printLValue src)
                           String (printFetchMode mode) ] ) ]

/// Pretty-prints a CAS prim.
let printCASPrim ty dest src set =
    hsep [ String ("cas<" + ty + ">")
           parened (HSep ( [String (printLValue dest)
                            String (printLValue src)
                            printZ3Exp set],
                           String ",")) ]

/// Pretty-prints a local-set prim.
let printLocalPrim ty dest src =
    hsep [ String ("lset<" + ty + ">")
           parened (hsep [String (printLValue dest)
                          String "="
                          printZ3Exp src] ) ]

/// Pretty-prints a prim.
let printPrim prim =
    match prim with
    | ArithFetch (dest, src, mode) -> printFetchPrim "arith" dest src mode
    | BoolFetch (dest, src) -> printFetchPrim "bool" (Some dest) src Direct
    | ArithCAS (dest, src, set) -> printCASPrim "arith" dest src set
    | BoolCAS (dest, src, set) -> printCASPrim "bool" dest src set
    | ArithLocalSet (dest, src) -> printLocalPrim "arith" dest src
    | BoolLocalSet (dest, src) -> printLocalPrim "bool" dest src
    | PrimId -> String "id"
    | PrimAssume expr -> hsep [ String "assume"
                                braced (printZ3Exp expr) ]

/// Pretty-prints a Hoare triple
let printHoare pcond pinner axiom = printInConditionPair pcond
                                                         axiom.Conditions
                                                         (angled (pinner axiom.Inner))

/// Pretty-prints a model axiom.
let printFlatAxiom = printHoare printCondViewList printPrim

/// Pretty-prints a model axiom.
let printFullAxiom = printHoare printGuarViewList printPrim

/// Pretty-prints a semantically translated axiom.
let printSemAxiom (ax: SemAxiom) =
    printHoare printGuarViewList
               (printZ3Exp >> Indent >> Seq.singleton >> Seq.toList >> vsep)
               ax

/// Pretty-prints a part-axiom at the given indent level.
let rec printPartAxiom axiom =
    match axiom with
    | PAAxiom ax -> printFlatAxiom ax
    | PAWhile (isDo, expr, outer, inner) ->
        vsep [ hsep [ String "begin"
                      String (if isDo then "do-while" else "while")
                      String (expr.ToString ()) ]
               printInConditionPair printCondViewList
                                    outer
                                    (vsep [ String "begin block"
                                            ivsep <| List.map printPartAxiom inner.Inner
                                            String "end block" ] )
               String "end" ]
    | PAITE (expr, outer, inTrue, inFalse) ->
        vsep [ hsep [ String "begin if"
                      String (expr.ToString ()) ]
               printInConditionPair printCondViewList
                                    outer
                                    (vsep [ String "begin true"
                                            ivsep <| List.map printPartAxiom inTrue.Inner
                                            String "end true; begin false"
                                            ivsep <| List.map printPartAxiom inFalse.Inner
                                            String "end false" ] )
               String "end" ]

/// Pretty-prints a model constraint.
let printModelConstraint c =
    keyMap [ ("View", printModelViewDefs c.CViews)
             ("Z3", c.CZ3.ToString () |> String) ]

/// Pretty-prints a model view prototype.
let printModelViewProto (vn, vps) =
    // TODO(CaptainHayashi): this is a bit of a cop-out!
    printViewProto { VPName = vn
                     VPPars = vps } |> String

/// Pretty-prints a model given an axiom printer.
let printModel axpp model =
    headed "Model"
           [ headed "Globals" <| List.map printModelVar (Map.toList model.Globals)
             Separator
             headed "Locals" <| List.map printModelVar (Map.toList model.Locals)
             Separator
             headed "Views" <| List.map printModelViewProto (Map.toList model.VProtos)
             Separator
             headed "Constraints" <| List.map printModelConstraint model.DefViews
             Separator
             headed "Axioms" <| List.map axpp model.Axioms ]

/// Pretty-prints a model with partially resolved axioms.
let printPartModel = printModel printPartAxiom

/// Pretty-prints a model with flattened but not fully resolved axioms.
let printFlatModel = printModel printFlatAxiom

/// Pretty-prints a model with fully resolved axioms.
let printFullModel = printModel printFullAxiom

/// Pretty-prints a model with semantically translated axioms.
let printSemModel = printModel printSemAxiom

/// Pretty-prints a framed axiom.
let printFramedAxiom ax =
    vsep [curry Header "Axiom" <| Indent (printSemAxiom ax.Axiom)
          curry Header "Frame" <| Indent (printGuarViewList ax.Frame) ]

/// Pretty-prints a list by headering each by its number.
let printNumHeaderedList pp =
    Seq.ofList
    >> Seq.mapi (fun i x -> Header (sprintf "%d" (i + 1),
                                    Indent (pp x)))
    >> Seq.toList
    >> vsep

/// Pretty-prints a list of framed axioms.
let printFramedAxioms = printNumHeaderedList printFramedAxiom

/// Pretty-prints an unreified term.
let printTerm tm =
    vsep [curry Header "Action" <| Indent (printZ3Exp tm.TAction)
          curry Header "Pre" <| Indent (printGuarViewList tm.TPre)
          curry Header "Post" <| Indent (printGuarViewList tm.TPost) ]

/// Pretty-prints a list of terms.
let printTerms = printNumHeaderedList printTerm

