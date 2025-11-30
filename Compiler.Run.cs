using System;
using System.Linq;
using System.Collections.Generic;
using Exp.Spans;

namespace Exp;

public partial class Compiler
{
    IContext currentContext = null;
    IVarSystem _currentVarSystem_; // set to this at constructor
    IVarSystem CurrentVarSystem
    {
        get => currentContext ?? _currentVarSystem_;
    }
    string currNs;

    public void Run()
    {
        IContext context = null;
        Run(context);
    }

    internal void Run(IContext context = null)
    {
        currentContext = context;

        if (BuiltinFuncs(context))
            return;

        WordSpan prevCmd = null;
        while (spansCursor < SourceSpans.Length)
        {
            // break or continue if needed
            ILoopContext firstBroken = null;
            int ShouldBreak(IContext con) // 0 No, 1 Continue, 2 Break
            {
                if (throwing != null)
                    return 2;
                while (con != null)
                {
                    if (con is ILoopContext lc)
                    {
                        firstBroken = lc;
                        if (lc.Break)
                            return 2;
                        if (lc.Continue)
                            return 1;
                    }
                    else if (con is FuncDefSpan func && func.Return)
                        return 2;
                    con = con.Context;
                }
                firstBroken = null;
                return 0;
            }

            int res = ShouldBreak(context);

            if (res == 2)
                break;
            if (res == 1)
            {
                if (firstBroken == context)
                    firstBroken.Continue = false;
                break;
            }

            // make next cmd
            WordSpan cmd = ReadWord();
            if (cmd == null)
                continue;

            if (cmd is ConditionSpan cond)
            {
                cond.Context = context;

                // init counter if set
                bool delCounter = false;
                if (cond is ILoopContext counterc)
                    delCounter = VarExists(counterc.Counter);
                Action TickCounter = null;
                if (cond is ILoopContext lc && lc.Counter != null)
                {
                    SetVar(lc.Counter, 0d, cond, lc);
                    TickCounter = () =>
                    {
                        if (lc.Counter != null)
                            SetVar(lc.Counter, GetVar<double>(lc.Counter, false, lc) + 1, null, lc);
                    };
                }

                // if statement / while loop
                if (cond is IfConditionSpan || cond is WhileConditionSpan)
                {
                    while (Run<bool>(cond.Condition))
                    {
                        Run(cond.InnerSource, cond);
                        cond.ConditionWasTrue = true;

                        if (cmd is IfConditionSpan || (ShouldBreak(cond) == 2))
                            break;

                        TickCounter?.Invoke();
                    }
                }

                // for loop
                else if (cond is ForLoopSpan loops)
                {
                    for (Run(loops.InitExe, loops); Run<bool>(cond.Condition, loops); Run(loops.StepExe, loops))
                    {
                        Run(loops.InnerSource, loops);

                        if (ShouldBreak(loops) == 2)
                            break;

                        TickCounter?.Invoke();
                    }
                }

                // delete counter if set
                if (delCounter && cond is ILoopContext cc)
                    DeleteVar(cc.Counter, cc);
            }

            // else statement
            else if (cmd is ElseConditionSpan elses)
            {
                if (prevCmd is ConditionSpan conditionSpan && (!conditionSpan.ConditionWasTrue) && conditionSpan is not ForLoopSpan)
                {
                    Run(elses.InnerSource, elses);
                }
            }
            else if (cmd is ForEachLoopSpan fe)
            {
                fe.Context = context;
                var arr = Run<Instance>(fe.ArrReadText);

                // base array
                while (arr != null && !arr.IsArray && arr.def.BaseArrayProp is Property bap)
                    arr = (arr.Vars.FirstOrDefault(v => v.Name.Equals(bap.Name)) is Variable vv ? vv.Value as Instance : null);

                if (arr == null)
                    Throw("Array is null.");
                if (!arr.IsArray)
                    Throw($"An array or basearray defining instance was expected (type read: {arr})");
                int counter = 0;
                try
                {
                    foreach (var obj in arr.ArrayValues)
                    {
                        if (fe.Counter != null)
                            SetVar(fe.Counter, counter++, fe);
                        SetVar(fe.VarName, obj, fe);
                        Run(fe.InnerSource, fe);
                        if (fe.Break)
                            break;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Throw("Invalid operation while in foreach loop: " + ex.Message);
                }
            }
            else if (cmd is BreakWordSpan or ContinueWordSpan)
            {
                // if id is attached, search a loop with this id
                string spoiler = Spoiler()?.FullText;
                IContext search = context;
                while (search != null && !(search is ILoopContext atta && atta.Id == spoiler))
                    search = search.Context;

                // if id not found, relate this as id-less break / continue
                if (search == null)
                {
                    search = context;
                    while (search != null && search is not ILoopContext)
                        search = search.Context;
                }

                if (search is ILoopContext loopc)
                {
                    if (cmd is BreakWordSpan)
                        loopc.Break = true;
                    else if (cmd is ContinueWordSpan)
                        loopc.Continue = true;
                    break;
                }
                else
                    Throw("No enclosing loop out of which to break or continue.");
            }
            else if (cmd is TryWordSpan trys)
            {
                Run(trys.InnerSource, trys);
                if (throwing != null)
                {
                    if (trys.Catch != null)
                    {
                        SetVar(trys.Catch.VarName, throwing, trys.Catch);
                        throwing = null;
                        Run(trys.Catch.InnerSource, trys.Catch);
                    }
                    throwing = null;
                }
            }
            else if (cmd is ThrowWordSpan)
            {
                Instance thr = ReadValue<Instance>();
                if (thr == null || !thr.def.Name.Equals("Exception"))
                    Throw("Only instances of type Exception can be thrown.");
                throwing = thr;
            }
            else if (cmd is PrintWordSpan)
            {
                object str = ReadValue();

                Print(str ?? "NULL");
            }
            else if (cmd is SetWordSpan or ConstWordSpan)
            {
                string vname = ReadWord().Text;

                // if this name is catched, throw an error
                // the bug in this code will be fixed when inner sources will be of Span instead of TextSpan
                /*bool setOnOuterVS = CurrentVarSystem is IContext someContext && VarExists(vname, someContext.OuterVarSystem);
                if (setOnOuterVS || (GetPointer(vname) is Variable exists && exists.SettingSpan != cmd))
                {
                    Throw($"'{vname}': This name is already in use. Pick another name for the variable.");
                }*/

                Read<SetSymbolSpan>();
                object value = ReadValue();
                SetVar(vname, value, cmd, null, cmd is ConstWordSpan);
            }
            else if (cmd is ReturnWordSpan)
            {
                // find the func we are in
                IContext ctx = context;
                ILoopContext breakFrom = null;
                while (ctx != null && ctx is not FuncDefSpan)
                {
                    if (ctx is ILoopContext lc)
                        breakFrom = lc;
                    ctx = ctx.Context;
                }

                // return
                if (ctx is FuncDefSpan func)
                {
                    if (ctx is not ConstructorDefSpan)
                    {
                        func.Returns = ReadValue();
                        func.Return = true;
                    }

                    if (breakFrom != null)
                        breakFrom.Break = true;
                    break;
                }
                else
                    Throw("No enclosing function of which to return.");
            }
            else if (cmd is IDefination or NamespaceWordSpan)
                continue;
            else
            {
                // set an existing variable
                object obj = ReadNamedValueOrPointer(out bool isArrPntr, out int arrPntrInd, cmd);
                if (obj is Variable pntr)
                {
                    // this is a variable set (not init) statement
                    Read<SetSymbolSpan>();
                    var value = ReadValue();

                    if (isArrPntr)
                        ((Instance)(pntr.Value)).ArrayValues[arrPntrInd] = value;
                    else
                        pntr.Value = value;
                }
                // else, it was a function and it was just called, so do nothing
            }

            prevCmd = cmd;
        }

        if (throwing != null && context == null)
            Throw(throwing.Vars[0].Value as string);
    }

    public void Run(TextSpan[] src)
    {
        IContext context = null;
        Run(src, context);
    }

    internal void Run(TextSpan[] src, IContext context)
    {
        contextLoc = cursor;
        TextSpan[] sourceSpans_backup = SourceSpans;
        Span ls = lastSpan;
        IContext ctx = currentContext;
        int cur = this.cursor, spcur = spansCursor;
        cursor = 0;
        spansCursor = 0;
        SourceSpans = src;
        Run(context);
        currentContext = ctx;
        SourceSpans = sourceSpans_backup;
        lastSpan = ls;
        cursor = cur;
        spansCursor = spcur;
        contextLoc = 0;
    }

    private T Run<T>(TextSpan[] src, IContext context, bool allowUnknownVars = false)
    {
        contextLoc = cursor;
        TextSpan[] sourceSpans_backup = SourceSpans;
        Span ls = lastSpan;
        IContext ctx = currentContext;
        int cur = this.cursor, spcur = spansCursor;
        cursor = 0;
        spansCursor = 0;
        SourceSpans = src;
        currentContext = context ?? currentContext;
        T v = ReadValue<T>(allowUnknownVars);
        currentContext = ctx;
        SourceSpans = sourceSpans_backup;
        lastSpan = ls;
        cursor = cur;
        spansCursor = spcur;
        contextLoc = 0;
        return v;
    }

    public T Run<T>(TextSpan[] src, bool allowUnknownVars = false) => Run<T>(src, null, allowUnknownVars);

    public void Run(string src) => Run(Spanner.GetAllTextSpans(src));

    private void CollectDefs(TextSpan[] code = null)
    {
        code ??= SourceSpans;

        int cursor_bu = cursor, spcur_bu = spansCursor;
        TextSpan[] sourceSpans_bu = SourceSpans;
        SourceSpans = code;
        Span lastSpan_bu = lastSpan;
        bool collected = false;
        string docNs = null;

        // find all def spans and load them to the defs list
        spansCursor = 0;
        while (spansCursor < code.Length)
        {
            Span span = ReadSpan();

            if (span is IDefination def)
            {
                def.Namespace = currNs;
                if (def is EnumDefSpan enm)
                {
                    var vars = new Variable[enm.Values.Length];
                    for (int i = 0; i < vars.Length; i++)
                        vars[i] = new Variable(enm.Values[i].Name, enm.Values[i].Value, enm, false, true);
                    ClassDefSpan enumcls = new ClassDefSpan(enm.Name, new Property[0], new FuncDefSpan[0]);
                    enumcls.Vars.AddRange(vars);
                    definations.Add(enumcls);
                }
                else
                    definations.Add(def);
                collected = true;
            }
            else if (span is NamespaceWordSpan nss)
            {
                if (collected || docNs != null)
                    Throw("Namespace naming can appear only once in a doc and before first defination.");
                currNs = nss.Namespace;
                docNs = currNs;
            }
            else if (span == null)
                break;
        }

        cursor = cursor_bu;
        spansCursor = spcur_bu;
        lastSpan = lastSpan_bu;
        SourceSpans = sourceSpans_bu;
    }

    private void CollectDefs(ScriptDocument[] docs)
    {
        foreach (var doc in docs)
        {
            CollectDefs(doc.CodeSpans);
        }
    }

    private object FuncCall(Instance inst, string fname, IContext context, out bool isFuncCall, FuncDefSpan[] funcs = null, object[] parameters = null)
    {
        parameters = parameters ?? ReadParamList();

        // find the function
        var func = (funcs ?? this.funcs).FirstOrDefault(f => fname.Equals(f.Name) && parameters.Length == f.Args.Length) ?? GetVar(fname, context ?? (IVarSystem)inst ?? this) as FuncDefSpan;
        return FuncCall(inst, func, context, out isFuncCall, parameters);
    }

    private object FuncCall(Instance inst, FuncDefSpan func, IContext context, out bool isFuncCall, object[] parameters = null)
    {
        parameters = parameters ?? ReadParamList();

        // find the function
        if (func == null)
        {
            isFuncCall = false;
            return null;
        }
        
        // create vars for args
        bool[] del = new bool[func.Args.Length];
        for (int i = 0; i < func.Args.Length; i++)
        {
            object par = i < parameters.Length ? parameters[i] : null;
            if (func.Args[i].NotNull && par == null)
                Throw($"Value cannot be null (Param: '{func.Args[i].Name}').");
            del[i] = !VarExists(func.Args[i].Name);
            SetVar(func.Args[i].Name, par, func, func);
        }

        // run func
        func.Context = context;
        func.OuterVarSystem = inst ?? CurrentVarSystem;
        Run(func.InnerSource, func);

        // delete args vars
        for (int i = 0; i < func.Args.Length; i++)
        {
            //if (del[i])
            //    DeleteVar(func.Args[i].Name);
        }

        isFuncCall = true;

        var returns = func.Returns;
        func.Return = false;
        func.Returns = null;
        return returns;
    }

    private T FindParentContext<T>(IContext context) where T : IContext
    {
        while (context != null && context.GetType() != typeof(T))
            context = context.Context;

        if (context == null)
            return default;

        return (T)context;
    }

    private T FindParentVarSystem<T>(IVarSystem vs) where T : IVarSystem
    {
        if (vs is IContext context)
        {
            while (context != null && context.GetType() != typeof(T))
            {
                if (context.OuterVarSystem is T found)
                    return found;
                context = context.Context;
            }

            vs = context;
        }

        if (vs is T v)
            return v;
        return default;
    }

    bool skipBuiltinFuncs = false;
    private bool BuiltinFuncs(IContext ctx)
    {
        if (skipBuiltinFuncs)
            return false;
        skipBuiltinFuncs = true;
        bool bin = false;

        if (ctx is FuncDefSpan func)
        {
            if (func.DefinedAt != null)
            {
                if (func.DefinedAt.Name == "Date" && func.Name == "setToNow")
                {
                    string exe;
                    DateTime now = DateTime.Now;
                    exe = "day = " + now.Day + "\n";
                    exe += "month = " + now.Month + "\n";
                    exe += "year = " + now.Year + "\n";
                    exe += "hour = " + now.Hour + "\n";
                    exe += "minute = " + now.Minute + "\n";
                    Run(Spanner.GetAllTextSpans(exe), ctx);
                    bin = true;
                }
            }
        }

        skipBuiltinFuncs = false;
        return bin;
    }
}