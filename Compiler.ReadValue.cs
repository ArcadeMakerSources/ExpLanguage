using System;
using System.Linq;
using System.Collections.Generic;
using Exp.Spans;

namespace Exp;

public partial class Compiler
{
    internal static readonly string[] opSymb = new string[] { "+", "-", "*", "/", "%", "=", "<", ">", "<=", ">=", "&", "|" };

    private object ReadValue(out TextSpan[] src, Span firstSpan = null, bool allowUnknownVars = false)
    {
        int cbr = spansCursor;

        object ReadSingle(out bool wasval)
        {
            wasval = false;
            object value = null;
            Span span = firstSpan ?? ReadSpan(spaces: true);
            firstSpan = null;
            if (span is null)
                return null;

            if (span is NumberSpan num)
                value = num.Number;
            else if (span is StringSpan)
                value = span.Text;
            else if (span is CharSpan)
                value = span.Text[0];
            else if (span is TrueWordSpan)
                value = true;
            else if (span is FalseWordSpan)
                value = false;
            else if (span is NotSymbolSpan)
                value = !(ReadValue<bool>());
            else if (span is NullWordSpan)
                value = null;
            else if (span is FuncDefSpan funcpntr)
                value = funcpntr;
            else if (span is LenofWordSpan)
            {
                object val = ReadValue();
                if (val is Instance inststr && inststr.def == ClassDefSpan.ExpStringDef)
                    value = (inststr.Vars[0].Value as Instance).ArrayValues.Length;
                else if (val is Instance inst && inst.IsArray)
                    value = inst.ArrayValues.Length;
                else if (!allowUnknownVars)
                    Throw("Only arrays and strings can be read here.");
            }
            else if (span is OpeningBracketSpan)
                value = span;
            else if (span is ArrayOpenerSpan)
            {
                object[] vals = ReadParamList(true, true, false);
                value = new Instance(ClassDefSpan.ExpArrayDef, vals);
            }
            else if (span is InstInitSpan init)
            {
                value = ReadInstInitSpan(init);
            }
            else if (span is WordSpan word)
            {
                value = ReadNamedValueOrPointer(out bool isArrPntr, out int arrPntrInd, word, allowUnknownVars);
                if (value is Variable pointer)
                    value = isArrPntr ? ((Instance)pointer.Value).ArrayValues[arrPntrInd] : pointer.Value;
            }
            else if (span is WhiteSpaceSpan)
                return ReadSingle(out wasval);
            else
                Throw($"Something went wrong (value: {span.GetType().Name}).");

            if (value is int integer)
                value = (double)integer;

            // "is" check
            if (Spoiler() is IsWordSpan)
            {
                ReadSpan();

                bool not = false;

                if (Spoiler().FullText.Equals("not"))
                {
                    not = true;
                    ReadSpan();
                }

                // type to check
                string clsName = ReadWord().FullText;

                // throw if type not exist
                const string number = "number";
                string[] builtins = [number, "string", "char", "bool", "function"];
                if (!builtins.Contains(clsName))
                {
                    if (!allowUnknownVars && Classes.FirstOrDefault(c => c.Name.Equals(clsName)) == null)
                        Throw($"Unknown type '{clsName}'.");
                }

                // check
                if (value is Instance ins)
                    value = ins.def.Name.Equals(clsName);
                else if (value is double)
                    value = clsName.Equals(number);
                else if (value is string)
                    value = clsName.Equals(builtins[1]);
                else if (value is bool)
                    value = clsName.Equals(builtins[2]);
                else if (value is char)
                    value = clsName.Equals(builtins[3]);
                else if (value is FuncDefSpan)
                    value = clsName.Equals(builtins[4]);
                else
                    throw new Exception($"Unsupported value ({value.GetType()}).");
                if (not)
                    value = !(bool)value;
            }

            wasval = true;
            return value;
        }

        // values operations
        object val;
        bool valread;

        var nums = new List<object>();
        var ops = new List<string>();

        // fill arrays
        do
        {
            val = ReadSingle(out valread);
            if (val is int i32)
                val = (double)i32;

            if (val is OpeningBracketSpan)
            {
                List<TextSpan> sub = [];
                Span span = ReadSpan();
                while (span is not ClosingBracketSpan)
                {
                    sub.Add(SourceSpans[spansCursor - 1]);
                    span = ReadSpan();
                }

                object subRes = Run<object>(sub.ToArray(), allowUnknownVars: allowUnknownVars);
                val = subRes;
            }

            Span spoiler = Spoiler();
            if (spoiler != null && opSymb.Contains(spoiler.FullText))
            {
                nums.Add(val);
                ops.Add(ReadSpan().FullText);
            }
            else
            {
                nums.Add(val);
                break;
            }
        } while (valread);

        if (nums.Count == 0)
            Throw($"A value was expected.");

        // calculate math-first operations
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i] == "*" || ops[i] == "/" || ops[i] == "%")
            {
                nums[i] = MakeOperation(nums[i], ops[i], nums[i + 1]);
                nums.RemoveAt(i + 1);
                ops.RemoveAt(i);
                i--;
            }
        }

        // calculate math-late operations
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i] == "+" || ops[i] == "-")
            {
                nums[i] = MakeOperation(nums[i], ops[i], nums[i + 1]);
                nums.RemoveAt(i + 1);
                ops.RemoveAt(i);
                i--;
            }
        }

        // calculate all the rest, except for & and |
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i] != "&" && ops[i] != "|")
            {
                nums[i] = MakeOperation(nums[i], ops[i], nums[i + 1]);
                nums.RemoveAt(i + 1);
                ops.RemoveAt(i);
                i--;
            }
        }

        // calculate the final result
        val = nums[0];
        for (int i = 1; i < nums.Count; i++)
        {
            val = MakeOperation(val, ops[i - 1], nums[i]);
        }

        object MakeOperation(object left, string symbol, object right)
        {
            if (allowUnknownVars)
                return 0d;

            if (left is Instance inst && inst.def == ClassDefSpan.ExpStringDef)
                left = ExpStringToString(inst);
            if (right is Instance inst1 && inst1.def == ClassDefSpan.ExpStringDef)
                right = ExpStringToString(inst1);

            object value = left;
            static string ErrName(object obj) => obj == null ? "NULL" : (obj is Instance inst ? inst.def.Name : obj.GetType().Name.Replace("Double", "number"));

            if (symbol == "+")
            {
                //ReadSpan(); // reads '+'
                object add = right;

                if (value is double l1 && add is double r1)
                    value = l1 + r1;
                else if (value is string l2 && add is string r2)
                    value = l2 + r2;
                else if (value is double l3 && add is string r3)
                    value = "" + l3 + r3;
                else if (value is string l4 && add is double r4)
                    value = l4 + r4;
                else if (value is char l8 && add is string r8)
                    value = l8 + r8;
                else if (value is string l9 && add is char r9)
                    value = l9 + r9;
                else if (value is string l5 && add is null)
                    value = l5 + "NULL";
                else if (value == null && add is string r6)
                    value = "NULL" + r6;
                else if (value is char l7 && add is char r7)
                    value = l7 + r7;
                else if (value is bool l10 && add is string r10)
                    value = l10 + r10;
                else if (value is string l11 && add is bool r11)
                    value = l11 + r11;
                else
                    Throw($"Cannot add {ErrName(add)} to {ErrName(value)}.");
            }
            else if (symbol == "-")
            {
                //ReadSpan(); // reads '-'
                object subtract = right;

                if (value is double d1 && subtract is double d2)
                    value = d1 - d2;
                else if (value is char c5 && subtract is char c6)
                    value = c5 - c6;
                else if (value is double d3 && subtract is char c3)
                    value = d3 - c3;
                else if (value is char c4 && subtract is double d4)
                    value = c4 - d4;
                else
                    Throw($"Cannot subtract {ErrName(subtract)} from {ErrName(value)}.");
            }
            else if (symbol == "*")
            {
                //ReadSpan(); // reads '*'
                object multi = right;

                if (value is double d1 && multi is double d2)
                {
                    value = d1 * d2;
                }
                else
                    Throw($"Cannot multiply {ErrName(value)} by {ErrName(multi)}.");
            }
            else if (symbol == "/")
            {
                //ReadSpan(); // reads '/'
                object divide = right;

                if (value is double d1 && divide is double d2)
                {
                    if (d2 == 0)
                        Throw("Divide by zero.");
                    value = d1 / d2;
                }
                else
                    Throw($"Cannot divide {ErrName(value)} by {ErrName(divide)}.");
            }
            else if (symbol == "=")
            {
                //ReadSpan(); // reads '='
                object compare = right;
                if (value != null)
                    value = value.Equals(compare);
                else
                    value = compare == null;
            }
            else if (symbol == "!=")
            {
                //ReadSpan(); // reads '!='
                object compare = right;
                if (value != null)
                    value = !value.Equals(compare);
                else
                    value = compare != null;
            }
            else if (symbol == ">")
            {
                //ReadSpan(); // reads '>'
                object compare = right;
                if (value is double d1 && compare is double d2)
                    value = d1 > d2;
                else
                    Throw($"Cannot compare {ErrName(value)} to {ErrName(compare)}.");
            }
            else if (symbol == ">=")
            {
                //ReadSpan(); // reads '>='
                object compare = right;
                if (value is double d1 && compare is double d2)
                    value = d1 >= d2;
                else
                    Throw($"Cannot compare {ErrName(value)} from {ErrName(compare)}.");
            }
            else if (symbol == "<")
            {
                //ReadSpan(); // reads '<'
                object compare = right;
                if (value is double d1 && compare is double d2)
                    value = d1 < d2;
                else
                    Throw($"Cannot compare {ErrName(value)} to {ErrName(compare)}.");
            }
            else if (symbol == "<=")
            {
                //ReadSpan(); // reads '>'
                object compare = right;
                if (value is double d1 && compare is double d2)
                    value = d1 <= d2;
                else
                    Throw($"Cannot compare {ErrName(value)} to {ErrName(compare)}.");
            }
            else if (symbol == "&")
            {
                //ReadSpan(); // reads '&'
                object compare = right;
                if (value is bool b1 && compare is bool b2)
                    value = b1 && b2;
                else
                    Throw($"The & symbol must appear between 2 booleans.");
            }
            else if (symbol == "|")
            {
                //ReadSpan(); // reads '|'
                object compare = right;
                if (value is bool b1 && compare is bool b2)
                    value = b1 || b2;
                else
                    Throw($"The | symbol must appear between 2 booleans.");
            }
            return value;
        }

        src = new TextSpan[spansCursor - cbr];
        for (int i = cbr; i < spansCursor; i++)
            src[i - cbr] = SourceSpans[i];

        if (val is string s)
            val = StringToExpString(s);

        return val;
    }

    private object ReadValue(bool allowUnknownVars = false)
    {
        return ReadValue(out TextSpan[] _, null, allowUnknownVars);
    }

    private T ReadValue<T>(bool allowUnknownVars = false)
    {
        return ReadValue<T>(out TextSpan[] _, allowUnknownVars);
    }

    private T ReadValue<T>(out TextSpan[] src, bool allowUnknownVars = false)
    {
        object v = ReadValue(out src, null, allowUnknownVars);

        if (allowUnknownVars)
            return default;

        if (v is not T)
            Throw($"A {typeof(T).Name} value was expected (received: {v}).");
        return (T)v;
    }

    private Instance ReadInstInitSpan(InstInitSpan init)
    {
        // get the class span
        ClassDefSpan cls = Classes.FirstOrDefault(c => c.Name.Equals(init.DefName));
        if (cls == null)
            Throw($"Unknown class '{init.DefName}'.");

        // read param list
        var param = ReadParamList();

        // if no constructor is defined, skip the ctor search
        if (cls.Funcs.OfType<ConstructorDefSpan>().Any())
        {
            // if there's a constructor with this num of params, create the instance and call the constructor
            var func = cls.Funcs.FirstOrDefault(f => f is ConstructorDefSpan && f.Args.Length == param.Length);
            if (func == null)
                Throw($"'{cls.Name}' does not implement a constructor with this number of parameters.");

            // built in classes:
            // array
            bool isArr = cls.Name.Equals("Array");
            object[] arr = null;
            if (isArr)
            {
                if (param[0] is not double)
                    Throw("A round possitive number was expected as array length.");
                int len = (int)((double)param[0]);
                arr = new object[len];
            }

            // create instance and call constructor
            Instance value = new Instance(cls, arr);
            FuncCall(value, func.Name, currentContext, out bool _, cls.Funcs, param);
            return value;
        }
        else
        {
            if (param.Length > 0)
                Throw("There are parameters attached, but no constructor is implemented for this type.");
            return new Instance(cls);
        }
    }

    private object ReadNamedValueOrPointer(out bool isArrPointer, out int arrPointerIndex, WordSpan firstSpan = null, bool allowUnknownVars = false)
    {
        isArrPointer = false;
        arrPointerIndex = 0;
        bool firstTime = true;
        object val = null;
        Instance inst = null;
        ClassDefSpan clas = null;
        while (true)
        {
            var span = firstTime ? (firstSpan ?? ReadWord()) : ReadWord();

            string word = span.FullText;
            IVarSystem vs = (IVarSystem)inst ?? (IVarSystem)clas;
            vs ??= currentContext;

            // if it's a func, call it
            bool isFunc = false;
            if (!allowUnknownVars)
            {
                var funcs = inst == null ? (clas == null ? GetFuncLs(currentContext) : clas.Funcs) : inst.def.Funcs;
                if (funcs != null && funcs.FirstOrDefault(f => word.Equals(f.Name)) != null)
                    val = FuncCall(inst, word, currentContext, out isFunc, funcs);
            }
            else
            {
                if (Spoiler() is OpeningBracketSpan)
                {
                    ReadParamList(allowUnknownVars: true);
                    val = 0d;
                    isFunc = true;
                }
            }

            // if it's a var, get it (if not, it will throw for us)
            if (!isFunc)
            {
                val = allowUnknownVars ? 0d : GetPointer(word, vs);
                if (val is Variable varpntr && varpntr.Value is FuncDefSpan funcpntr && Spoiler() is OpeningBracketSpan)
                    val = FuncCall(inst, funcpntr, currentContext, out isFunc);
                if (val == null && span is ThisWordSpan thisWordSpan)
                {
                    if (FindParentVarSystem<Instance>(CurrentVarSystem) is Instance thiss)
                        val = thiss;
                    else
                        Throw($"'{thisWordSpan.Text}' word can only appear inside a class.");
                }
            }

            // if it's not a var, check static var
            if (val == null && firstTime)
            {
                val = Classes.FirstOrDefault(cl => cl.Name.Equals(word));
            }


            // check if it's an array ref, if it is - read index
            CheckArrayIndex(val is Variable pntr ? pntr.Value : val, out isArrPointer, out arrPointerIndex, allowUnknownVars);

            // maybe it's an instance and it is followed by a dot
            Span spoiler = Spoiler();

            // if spoiler is '.', relate it as an instance
            if (spoiler is DotSpan)
            {
                if (val is Variable pointer)
                    val = isArrPointer ? ((Instance)pointer.Value).ArrayValues[arrPointerIndex] : pointer.Value;

                if (val is Instance ival)
                {
                    inst = ival;
                    ReadSpan();
                }
                else if (allowUnknownVars)
                {
                    val = StringToExpString("");
                    ReadSpan();
                }
                else if (val is ClassDefSpan cls)
                {
                    clas = cls;
                    ReadSpan();
                }
                else
                    Throw($"Unexpected '.': '{word}' is not an Instance reference.");
            }
            else if (val == null && !isFunc && !allowUnknownVars)
            {
                string beforeDot = inst == null ? (clas == null ? "" : (clas.Name + '.')) : (inst.def.Name + '.');
                Throw($"Unknown item '{beforeDot}{word}'.");
            }

            // else, break to return the value
            else
            {
                break;
            }
            firstTime = false;
        }

        return val;
    }


    internal static Instance StringToExpString(string s)
    {
        var carr = new object[s.Length];
        for (int c = 0; c < s.Length; c++)
            carr[c] = s[c];
        var expCarr = new Instance(ClassDefSpan.ExpArrayDef, carr);
        var exp = new Instance(ClassDefSpan.ExpStringDef, [expCarr]);
        exp.Vars[0].Value = expCarr;
        return exp;
    }

    internal static string ExpStringToString(Instance exp)
    {
        var oarr = (exp.Vars[0].Value as Instance).ArrayValues;
        string s = "";
        foreach (object c in oarr)
            s += (char)c;
        return s;
    }

    private FuncDefSpan[] GetFuncLs(IContext context)
    {
        var func = FindParentContext<FuncDefSpan>(context);
        if (func == null || func.DefinedAt == null)
            return this.funcs;
        return func.DefinedAt.Funcs;
    }

    private object CheckArrayIndex(object value, out bool isArrInd, out int index, bool allowUnknownVars = false)
    {
        isArrInd = false;
        index = -1;
        if (Spoiler() is ArrayOpenerSpan)
        {
            ReadSpan();
            index = (int)ReadValue<double>(allowUnknownVars);

            if (allowUnknownVars)
            {
                isArrInd = true;
            }
            else if (value is Instance inst && inst.IsArray)
            {
                if (index >= inst.ArrayValues.Length || index < 0)
                    Throw($"index is out of range (index: {index}, array length: {inst.ArrayValues.Length}).");

                value = inst.ArrayValues[index];
                isArrInd = true;
            }
            else
                Throw($"Only arrays can be followed by '['.");
            Read<ArrayCloserSpan>();
        }
        return value;
    }
}
