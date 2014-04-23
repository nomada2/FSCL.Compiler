﻿namespace FSCL.Compiler.FunctionPreprocessing

open FSCL.Compiler
open Microsoft.FSharp.Quotations
open System.Collections.Generic
open System.Reflection

[<StepProcessor("FSCL_KERNEL_RETURN_DISCOVERY_PROCESSOR", 
                "FSCL_FUNCTION_PREPROCESSING_STEP",
                Dependencies = [| "FSCL_DYNAMIC_ARRAY_TO_PARAMETER_PREPROCESSING_PROCESSOR" |])>]
type KernelReturnExpressionDiscoveryProcessor() =                
    inherit FunctionPreprocessingProcessor()

    let rec SearchReturnExpression(expr:Expr, returnExpression: Var option ref, engine:FunctionPreprocessingStep) =
        match expr with
        | Patterns.Let(v, value, body) ->
            SearchReturnExpression(body, returnExpression, engine)
        | Patterns.Sequential(e1, e2) ->
            SearchReturnExpression(e2, returnExpression, engine)
        | Patterns.IfThenElse(e, ifb, elseb) ->
            SearchReturnExpression(ifb, returnExpression, engine)
            SearchReturnExpression(elseb, returnExpression, engine)
        | Patterns.Lambda(v, e) ->
            SearchReturnExpression(e, returnExpression, engine)            
        | _ ->
            // This could be a return expression if its type is subtype of return type
            let returnType = expr.Type
            if returnType <> typeof<unit> && engine.FunctionInfo.ReturnType.IsAssignableFrom(returnType) then
                // Verify that return expression is a reference to a parameter
                match expr with
                | Patterns.Var(v) -> 
                    if returnExpression.Value.IsSome then
                        if returnExpression.Value.Value = v then
                            // OK
                            ()
                        else
                            raise (new CompilerException("Cannot return different variables/parameters from within a kernel"))
                    else
                        // CHECK THAT THIS IS A REFERENCE TO A PARAMETER
                        let p = Seq.tryFind(fun (e:FunctionParameter) -> e.Name = v.Name) (engine.FunctionInfo.Parameters)
                        if p.IsNone then
                            raise (new CompilerException("Kernels can only return a reference to a parameter or to a dynamically allocated array"))
                         
                        returnExpression := Some(v)
                | _ ->
                    raise (new CompilerException("Only a reference to a parameter can be returned from within a kernel"))            
            
    override this.Run(info, s, opts) =    
        let engine = s :?> FunctionPreprocessingStep
        let returnVariable:Var option ref = ref None

        if info :? KernelInfo then
            SearchReturnExpression(info.Body, returnVariable , engine)

            // Verify that return expressions are all references
            if returnVariable.Value.IsSome then
                engine.FunctionInfo.CustomInfo.Add("RETURN_VARIABLE", returnVariable.Value.Value)
                // Set return parameter to void
                engine.FunctionInfo.ReturnType <- typeof<unit>
                // Mark parameter returned as ReturnParameter
                let p = engine.FunctionInfo.GetParameter(returnVariable.Value.Value.Name).Value 
                p.IsReturned <- true
        