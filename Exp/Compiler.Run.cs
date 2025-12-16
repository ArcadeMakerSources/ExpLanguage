using System;
using System.Linq;
using System.Collections.Generic;
using Exp.Spans;
using System.Reflection;

namespace Exp;

public partial class Compiler
{
    IContext currentContext = null;
    readonly IVarSystem _currentVarSystem_; // set to this at constructor
    IVarSystem CurrentVarSystem
    {
        get => currentContext ?? _currentVarSystem_;
    }
    string currNs;

    public void Run()
    {
        // first run static constructors
        foreach (var clas in Classes)
        {
            foreach (var ctor in clas.Funcs.Where(f => f is ConstructorDefSpan && f.Static))
                Run(ctor.InnerSource, ctor);
        }

        IContext context = null;

        try
        {
            Run(context);
        }
        catch (ScriptThrowingException)
        {
            Throw(ExpStringToString(throwing.Vars[0].Value as Instance));
        }
    }

    internal void Run(IContext context = null, bool single = false, bool neutral = false)
    {
        currentContext = context;

        //if (BuiltinFuncs(context))
        //    return;

        WordSpan cmd = null, prevCmd = null;
        while (spansCursor < SourceSpans.Length)
        {
            if (cmd != null && single)
                break;
            prevCmd = cmd;

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
            cmd = ReadWord();
            if (cmd == null)
                continue;

            if (cmd is IContext toSetCtx)
                toSetCtx.Context = context;

            void RunContext(IContext rctx)
            {
                Run(rctx.InnerSource, rctx);
            }

            if (cmd is ConditionSpan cond)
            {
                if (neutral)
                    continue;

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
                        RunContext(cond);
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
                        RunContext(loops);

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
                if (neutral)
                    continue;

                if (prevCmd is ConditionSpan conditionSpan && (!conditionSpan.ConditionWasTrue) && conditionSpan is not ForLoopSpan)
                {
                    RunContext(elses);
                }
            }
            else if (cmd is ForEachLoopSpan fe)
            {
                if (neutral)
                    continue;

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
                        RunContext(fe);
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
                    if (loopc.Id != null && loopc.Id == spoiler)
                        ReadSpan();

                    if (neutral)
                        continue;

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
                if (neutral)
                    continue;

                try
                {
                    RunContext(trys);
                }
                catch (ScriptThrowingException)
                {
                    if (trys.Catch != null)
                    {
                        // init exception var
                        if (trys.Catch.VarName != null)
                            SetVar(trys.Catch.VarName, throwing, specificVS: trys.Catch);

                        // check "when"
                        bool toCatch = true;
                        if (trys.Catch.When != null)
                            toCatch = Run<bool>(trys.Catch.When.Condition, trys.Catch);

                        if (toCatch)
                        {
                            throwing = null;
                            RunContext(trys.Catch);
                        }
                    }
                }
                finally
                {
                    if (trys.Finally != null)
                    {
                        throwing = null;
                        RunContext(trys.Finally);
                    }
                }

                // we want try without catch to work like a try with an empty catch
                throwing = trys.Catch != null ? throwing : null;
            }
            else if (cmd is ThrowWordSpan)
            {
                ReadThrowStmt(readThrowKeyword: false, neutral: neutral);
            }
            else if (cmd is SectionWordSpan section)
            {
                if (neutral)
                    continue;

                try
                {
                    RunContext(section);
                }
                catch (ScriptThrowingException)
                {
                    var ex = throwing;
                    throwing = null;
                    if (FindParentContext<TryWordSpan>(section) is TryWordSpan tryb && tryb.Catch != null)
                    {
                        if (tryb.Catch.VarName != null)
                            SetVar(tryb.Catch.VarName, ex, specificVS: tryb.Catch);
                        if (tryb.Catch.When == null || Run<bool>(tryb.Catch.When.Condition, tryb.Catch))
                        {
                            RunContext(tryb.Catch);
                        }
                    }
                }
            }
            else if (cmd is PrintWordSpan)
            {
                object str = ReadValue(allowUnknownVars: neutral);

                if (neutral)
                    continue;

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
                object value = ReadValue(allowUnknownVars: neutral);

                if (neutral)
                    continue;

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
                        func.Returns = ReadValue(allowUnknownVars: neutral);

                        if (neutral)
                        {
                            func.Returns = null;
                            continue;
                        }

                        func.Return = true;
                    }

                    if (breakFrom != null)
                        breakFrom.Break = true;
                    break;
                }
                else
                    Throw("No enclosing function of which to return.");
            }
            else if (cmd is IDefination or NamespaceWordSpan or UsingWordSpan or ExternWordSpan)
            {
                if (context != null)
                    Throw($"{cmd.Text} word is not expected in this context.");
                continue;
            }
            else
            {
                // set an existing variable or call a func
                object obj = ReadNamedValueOrPointer(out bool isArrPntr, out int arrPntrInd, cmd, allowUnknownVars: neutral, allowFuncsToNotReturn: true);

                Variable pntr = null;
                if (obj is Variable || (neutral && Spoiler() is SetSymbolSpan))
                {
                    pntr = obj as Variable;
                    // take care of operations (++, --, +=, -=, etc.)
                    if (Spoiler() is OperatorSpan op)
                    {
                        ReadSpan();

                        object opInput = op.TwoSides ? ReadValue(allowUnknownVars: neutral) : null;

                        if (!neutral)
                        {
                            if (isArrPntr)
                            {
                                var arr = pntr.Value as Instance;
                                arr.ArrayValues[arrPntrInd] = op.Apply(arr.ArrayValues[arrPntrInd], opInput);
                            }
                            else
                            {
                                pntr.Value = op.Apply(pntr.Value, opInput);
                            }
                        }
                    }
                    else
                    {
                        // this is a variable set (not init) statement
                        Read<SetSymbolSpan>();
                        var value = ReadValue(allowUnknownVars: neutral);

                        if (neutral)
                            continue;

                        if (isArrPntr)
                            ((Instance)pntr.Value).ArrayValues[arrPntrInd] = value;
                        else
                            pntr.Value = value;
                    }
                }
                // else, it was a function and it was just been called, so do nothing
            }
        }

        if (throwing != null && context == null)
            Throw(ExpStringToString(throwing.Vars[0].Value as Instance));
    }

    public void Run(TextSpan[] src)
    {
        //try
        //{
        IContext context = null;
        Run(src, context);
        //}
        //catch (ScriptThrowingException)
        //{
        //    Throw(ExpStringToString(throwing.Vars[0].Value as Instance));
        //}
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

        ScriptThrowingException toThrow = null;
        try
        {
            Run(context);
        }
        catch (ScriptThrowingException ex)
        {
            toThrow = ex;
        }
        finally
        {

            //Throw(ExpStringToString(throwing.Vars[0].Value as Instance));

            currentContext = ctx;
            SourceSpans = sourceSpans_backup;
            lastSpan = ls;
            cursor = cur;
            spansCursor = spcur;
            contextLoc = 0;
            if (toThrow != null)
                throw toThrow;
        }
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

    public void Run(string src)
    {
        //try
        //{
        Run(Spanner.GetAllTextSpans(src));
        //}
        //catch (ScriptThrowingException)
        //{
        //    Throw(ExpStringToString(throwing.Vars[0].Value as Instance));
        //}
    }

    private void ReadThrowStmt(bool readThrowKeyword = true, bool neutral = false, bool thr = true)
    {
        if (readThrowKeyword)
            Read<ThrowWordSpan>();

        Instance ex = ReadValue<Instance>(allowUnknownVars: neutral);

        if (neutral)
            return;

        if (ex == null || !ex.def.Name.Equals("Exception"))
            Throw("Only instances of type Exception can be thrown.");

        if (thr)
        {
            throwing = ex;
            throw new ScriptThrowingException();
        }
    }

    private void CollectDefs(TextSpan[] code = null)
    {
        code ??= SourceSpans;

        int cursor_bu = cursor, spcur_bu = spansCursor;
        TextSpan[] sourceSpans_bu = SourceSpans;
        SourceSpans = code;
        Span lastSpan_bu = lastSpan;
        bool collected = false, codeStart = false;
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
                    ClassDefSpan enumcls = new ClassDefSpan(enm.Name, [], []);
                    enumcls.Vars.AddRange(vars);
                    definations.Add(enumcls);
                }
                else
                    definations.Add(def);
                collected = true;
                codeStart = true;
            }
            else if (span is NamespaceWordSpan nss)
            {
                if (collected || docNs != null)
                    Throw("Namespace naming can appear only once in a doc and before first defination.");
                currNs = nss.Namespace;
                docNs = currNs;
                codeStart = true;
            }
            else if (span is UsingWordSpan use)
            {
                if (codeStart)
                    Throw("Using / Extern directives must appear before any other code.");
                if (currUsings.Contains(use.Namespace))
                    Throw($"The namespace '{use.Namespace}' is already being used in this document.");
                currUsings.Add(use.Namespace);
            }
            else if (span is ExternWordSpan ext)
            {
                if (codeStart)
                    Throw("Using / Extern directives must appear before any other code.");

                externs.Add(ext);
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
            currUsings = [];
            currNs = null;
        }
    }

    private object FuncCall(Instance inst, string fname, IContext context, out bool isFuncCall, FuncDefSpan[] funcs = null, object[] parameters = null)
    {
        parameters = parameters ?? ReadParamList();

        // find the function
        var func = (funcs ?? this.Funcs).FirstOrDefault(f => fname.Equals(f.Name) && parameters.Length == f.Args.Length) ?? GetVar(fname, context ?? (IVarSystem)inst ?? this) as FuncDefSpan;
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

        if (!BuiltinFuncs(func))
            Run(func.InnerSource, func);

        // delete args vars
        for (int i = 0; i < func.Args.Length; i++)
        {
            //if (del[i])
            //    DeleteVar(func.Args[i].Name);
        }

        isFuncCall = true;

        var returns = func.Return ? func.Returns : Void.Return;
        func.Return = false;
        func.Returns = null;
        return returns;
    }

    private object InvokeExtern(Type type, object inst, string method, object[] args)
    {
        // naming rule violation: funcs starts with lowercase letters
        method = method[0].ToString().ToUpper() + method[1..];

        MethodInfo methodi = null;
        string typesStr = null;
        try
        {
            methodi = type.GetMethod(method, GetArgTypesForExternInvocation(args, out typesStr)) ?? throw new Exception("Method not found");
        }
        catch (Exception ex)
        {
            Throw($"Could not find external method '{type}.{method}{typesStr}' (Error message: {ex.Message}).");
        }

        return InvokeExtern(type, inst, methodi, args);
    }

    private object NewExtern(Type type, object[] args)
    {
        ConstructorInfo ctor = null;
        string typesStr = null;
        try
        {
            ctor = type.GetConstructor(GetArgTypesForExternInvocation(args, out typesStr)) ?? throw new Exception("Constructor not found");
        }
        catch (Exception ex)
        {
            Throw($"Could not find external constructor{typesStr} for type '{type}' (Error message: {ex.Message}).");
        }

        return InvokeExtern(type, null, ctor, args);
    }

    private Type[] GetArgTypesForExternInvocation(object[] args, out string typesStr)
    {
        typesStr = "(";
        Type[] types = new Type[args.Length];
        for (int i = 0; i < types.Length; i++)
        {
            void Convert(object[] args, Type[] types, int i)
            {
                types[i] = args[i]?.GetType() ?? typeof(object);

                if (args[i] is Instance expinst)
                {
                    if (expinst.def == ClassDefSpan.ExpStringDef)
                    {
                        args[i] = ExpStringToString(expinst);
                        types[i] = typeof(string);
                    }
                    else if (expinst.def == ClassDefSpan.ExpArrayDef)
                    {
                        Type[] _ = new Type[expinst.ArrayValues.Length];
                        for (int j = 0; j < expinst.ArrayValues.Length; j++)
                            Convert(expinst.ArrayValues, _, j);
                        args[i] = CSBasicTypes.MinArray(expinst);
                        types[i] = args[i].GetType();
                    }
                    else if (expinst.def == ClassDefSpan.ExternTypeValueDef)
                    {
                        args[i] = expinst.Vars[1].Value;
                        types[i] = args[i].GetType();
                    }
                }
            }

            Convert(args, types, i);
            typesStr += types[i].ToString() + (i < args.Length - 1 ? ", " : "");
        }

        typesStr += ")";
        return types;
    }

    private object InvokeExtern(Type type, object inst, MethodBase method, object[] args)
    {
        try
        {
            // invoke
            object result = method is ConstructorInfo ctor ? ctor.Invoke(args) : method.Invoke(inst, args);

            // a func to convert cs value to exp value
            object ConvertToExp(object result)
            {
                if (result is string csstr)
                    result = StringToExpString(csstr);
                else if (result is Array csarr)
                {
                    for (int i = 0; i < csarr.Length; i++)
                        csarr.SetValue(ConvertToExp(csarr.GetValue(i)), i);
                    result = new Instance(ClassDefSpan.ExpArrayDef, (object[])csarr);
                }
                else try
                    {
                        result = (double)result;
                    }
                    catch { }
                if (!(result is bool or char or double or Instance or null))
                {
                    result = result.AsExtern();
                }

                return result;
            }

            return ConvertToExp(result);
        }
        catch (TargetInvocationException ex)
        {
            Instance expex = new Instance(ClassDefSpan.ExpExceptionDef);
            expex.Vars[0].Value = StringToExpString(ex.InnerException.Message);
            throwing = expex;
            throw new ScriptThrowingException();
        }
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
                else if (func.Static && func.DefinedAt == ClassDefSpan.ExpTypeDef && func.Name == "get")
                {
                    var arg = func.Vars[0].Value as Instance;
                    if (arg == null)
                        Throw($"Invalid Argument: {func.Args[0].Name} must be a strong type.");
                    else
                    {
                        func.Returns = arg.def.ExpType;
                        func.Return = true;
                        bin = true;
                    }
                }
            }
            else if (func.Namespace == "reflection")
            {
                if (func.Name == "getProperties")
                {
                    Instance ToExpPropertyInst(Variable v)
                    {
                        ClassDefSpan expPropDef = null;
                        definations.FirstOrDefault(d => d is ClassDefSpan expPropDef && d.Namespace == "reflection" && d.Name == "Property");
                        if (expPropDef == null)
                            Throw("reflection::Property class not found.");
                        return new Instance(expPropDef);
                    }

                    List<Instance> props = [];

                    Instance input = (Instance)func.Vars[0].Value;
                    foreach (var prop in input.Vars)
                        props.Add(ToExpPropertyInst(prop));

                    func.Return = true;
                    func.Returns = new Instance(ClassDefSpan.ExpArrayDef, props.ToArray());
                    bin = true;
                }
            }
        }

        skipBuiltinFuncs = false;
        return bin;
    }
}

// when a func returns instance of this, it mea0ns it didn't return a value
class Void
{
    internal static Void Return { get; }
    private Void() { }
    static Void() => Return = new Void();
}
