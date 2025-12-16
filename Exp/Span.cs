
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;

namespace Exp.Spans;

interface IKeyword<T>
{
    static abstract string Keyword { get; }
}

interface IKeyword : IKeyword<string>;

abstract class Span
{
    internal string Text { get; }

    internal virtual string FullText
    {
        get => Text;
    }

    internal Span(string text)
    {
        this.Text = text;
    }
}

class WordSpan : Span
{
    internal WordSpan(string text) : base(text) { }
}

abstract class OperatorSpan : Span
{
    internal OperatorSpan(string op) : base(op) { }
    internal abstract object Apply(object target, object val);
    internal abstract bool TwoSides { get; }
    protected static string TypeOrNull(object obj)
    {
        if (obj == null)
            return "null";
        else
            return obj.GetType().ToString();
    }
}

class PrintWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "print";
    internal PrintWordSpan() : base(Keyword) { }
}

class LenofWordSpan : WordSpan, IKeyword<string>
{
    public static string Keyword { get; } = "lenof";
    internal LenofWordSpan() : base(Keyword) { }
}

class NumberSpan : Span
{
    internal double Number;
    internal NumberSpan(double num) : base(num.ToString())
    {
        this.Number = num;
    }
    internal NumberSpan(string text) : base(text)
    {
        this.Number = Convert.ToDouble(text);
    }
}

class StringSpan : Span
{
    private readonly bool escaped;
    internal override string FullText
    {
        get => (escaped ? "@" : "") + "\"" + Text + "\"";
    }
    internal StringSpan(string text, bool escaped = false) : base(text)
    {
        this.escaped = escaped;
    }
}

class CharSpan : Span
{
    internal override string FullText => $"'{Text.Replace("\n", @"\n")}'";
    internal CharSpan(char c) : base("" + c) { }
}

class NamespaceWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "namespace";
    internal string Namespace { get; }
    internal NamespaceWordSpan(string ns) : base(Keyword)
    {
        this.Namespace = ns;
    }
    internal override string FullText => $"{Keyword} {Namespace}:";
}

class UsingWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "using";
    internal string Namespace { get; }
    internal UsingWordSpan(string ns) : base(Keyword)
    {
        this.Namespace = ns;
    }
    internal override string FullText => $"{Keyword} {Namespace}";
}

class ExternWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "extern";
    internal string RefName { get; }
    internal Type Type { get; }
    internal ExternWordSpan(string refName, Type type) : base(Keyword)
    {
        this.RefName = refName;
        this.Type = type;
    }
    internal override string FullText => $"{Keyword} {RefName} = \"{Type}\"";
}

class TypeOfWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "typeof";
    internal Instance Value { get; }
    private TypeOfWordSpan() : base(Keyword) { }
    internal TypeOfWordSpan(ExternWordSpan ext) : this()
    {
        Value = ext.Type.AsExtern();
    }
    internal TypeOfWordSpan(ClassDefSpan cls) : this()
    {
        Value = cls.ExpType;
    }
    internal override string FullText => Keyword + " " + Value.def.Namespace + "::" + Value.Vars.Find(v => v.Name == "name").Value;
}

class NamespaceSpecificationSpan : WordSpan
{
    internal NamespaceSpecificationSpan() : base("::") { }
}

class SetWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "var";
    internal SetWordSpan() : base(Keyword) { }
}

class StaticWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "static";
    internal StaticWordSpan() : base(Keyword) { }
}

class PlusPlusOperatorSpan : OperatorSpan
{
    internal PlusPlusOperatorSpan() : base("++") { }
    internal override bool TwoSides => false;
    internal override object Apply(object target, object _ = null)
    {
        if (target is double d)
            return ++d;
        Compiler.Activated.Throw($"Cannot apply {Text} on {TypeOrNull(target)}.");
        return null;
    }
}

class MinusMinusOperatorSpan : OperatorSpan
{
    internal MinusMinusOperatorSpan() : base("--") { }
    internal override bool TwoSides => false;
    internal override object Apply(object target, object _ = null)
    {
        if (target is double d)
            return --d;
        Compiler.Activated.Throw($"Cannot apply {Text} on {TypeOrNull(target)}.");
        return null;
    }
}

class SetPlusOperatorSpan : OperatorSpan
{
    internal SetPlusOperatorSpan() : base("+=") { }
    internal override bool TwoSides => true;
    internal override object Apply(object target, object input)
    {
        if (target is double l1 && input is double r1)
            return l1 + r1;
        else if (target is string l2 && input is double r2)
            return l2 + r2;
        else if (target is string l3 && input is string r3)
            return l3 + r3;
        Compiler.Activated.Throw($"Cannot add {TypeOrNull(input)} to {TypeOrNull(target)}.");
        return null;
    }
}

class SetMinusOperatorSpan : OperatorSpan
{
    internal SetMinusOperatorSpan() : base("-=") { }
    internal override bool TwoSides => true;
    internal override object Apply(object target, object input)
    {
        if (target is double l1 && input is double r1)
            return l1 - r1;
        Compiler.Activated.Throw($"Cannot subtract {TypeOrNull(input)} from {TypeOrNull(target)}.");
        return null;
    }
}

class QuestionMarkSpan : WordSpan
{
    internal QuestionMarkSpan() : base("?") { }
}

class NullCoalescingSpan : WordSpan
{
    internal NullCoalescingSpan() : base("??") { }
}

class PrivateWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "private";
    internal PrivateWordSpan() : base(Keyword) { }
}

class BaseArrayWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "basearray";
    internal BaseArrayWordSpan() : base(Keyword) { }
}

class ConstWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "const";
    internal ConstWordSpan() : base(Keyword) { }
}

class IsWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "is";
    internal IsWordSpan() : base(Keyword) { }
}

class TrueWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "true";
    internal TrueWordSpan() : base(Keyword) { }
}

class FalseWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "false";
    internal FalseWordSpan() : base(Keyword) { }
}

class NotSymbolSpan : WordSpan
{
    internal NotSymbolSpan() : base("!") { }
}

// a item that has its own vars list
public interface IVarSystem
{
    List<Variable> Vars { get; }
}

interface IContext : IVarSystem
{
    IVarSystem OuterVarSystem { get; }
    TextSpan[] InnerSource { get; set; }
    IContext Context { get; set; }
}

interface ILoopContext : IContext
{
    bool Break { get; set; }
    bool Continue { get; set; }
    string Counter { get; set; }
    string Id { get; set; }
}

interface IDefination
{
    string Name { get; }
    string Namespace { get; set; }
}

abstract class ConditionSpan : WordSpan, IContext
{
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; }
    public TextSpan[] InnerSource { get; set; }
    internal TextSpan[] Condition { get; }
    public IContext Context { get; set; }
    internal bool ConditionWasTrue { get; set; }
    internal override string FullText => Text + ' ' + Condition + "\n{\n\t" + InnerSource + "\n}";
    internal ConditionSpan(string text, TextSpan[] condition, TextSpan[] innerSource, IVarSystem varSystem) : base(text)
    {
        this.InnerSource = innerSource;
        this.Condition = condition;
        this.OuterVarSystem = varSystem;
    }
}

class IfConditionSpan : ConditionSpan, IKeyword
{
    public static string Keyword { get; } = "if";
    internal IfConditionSpan(TextSpan[] condition, TextSpan[] innerSource, IVarSystem varSystem) : base(Keyword, condition, innerSource, varSystem) { }
}

class ElseConditionSpan : WordSpan, IContext, IKeyword
{
    public static string Keyword { get; } = "else";
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; }
    public TextSpan[] InnerSource { get; set; }
    public IContext Context { get; set; }

    internal override string FullText => $"{Keyword}\n{{\n\t{InnerSource.ToString(" ")}\n}}";
    internal ElseConditionSpan(TextSpan[] innerSource, IVarSystem varSystem) : base(Keyword)
    {
        this.InnerSource = innerSource;
        this.OuterVarSystem = varSystem;
    }
}

class WhileConditionSpan : ConditionSpan, ILoopContext, IKeyword
{
    public static string Keyword { get; } = "while";
    public bool Break { get; set; }
    public bool Continue { get; set; }
    public string Counter { get; set; }
    public string Id { get; set; }
    internal override string FullText
    {
        get
        {
            string s = $"{Keyword} {Condition}";
            if (Id != null || Counter != null)
                s += " : ";
            if (Id != null)
                s += "id " + Id;
            if (Id != null && Counter != null)
                s += " , ";
            if (Counter != null)
                s += "counter " + Counter;
            s += $"\n{{\n\t{InnerSource.ToString(" ")}\n}}";
            return s;
        }
    }
    internal WhileConditionSpan(TextSpan[] condition, TextSpan[] innerSource, IVarSystem varSystem) : base(Keyword, condition, innerSource, varSystem) { }
}

class ForLoopSpan : ConditionSpan, ILoopContext, IKeyword
{
    public static string Keyword { get; } = "for";
    public bool Break { get; set; }
    public bool Continue { get; set; }
    public string Counter { get; set; }
    internal TextSpan[] InitExe { get; }
    internal TextSpan[] StepExe { get; }
    public string Id { get; set; }
    internal override string FullText
    {
        get
        {
            string s = $"{Keyword} {InitExe} ; {Condition} ; {StepExe}";
            if (Id != null || Counter != null)
                s += " : ";
            if (Id != null)
                s += "id " + Id;
            if (Id != null && Counter != null)
                s += " , ";
            if (Counter != null)
                s += "counter " + Counter;
            s += $"\n{{\n\t{InnerSource.ToString(" ")}\n}}";
            return s;
        }
    }
    internal ForLoopSpan(TextSpan[] initExe, TextSpan[] condition, TextSpan[] stepExe, TextSpan[] innerSource, IVarSystem varSystem) : base(Keyword, condition, innerSource, varSystem)
    {
        this.InitExe = initExe;
        this.StepExe = stepExe;
    }
}

class ForEachLoopSpan : WordSpan, ILoopContext, IKeyword
{
    public static string Keyword { get; } = "foreach";
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; }
    public TextSpan[] InnerSource { get; set; }
    public IContext Context { get; set; }
    public bool Break { get; set; }
    public bool Continue { get; set; }
    public string Counter { get; set; }
    internal TextSpan[] ArrReadText { get; }
    internal string VarName { get; }
    public string Id { get; set; }
    internal override string FullText
    {
        get
        {
            string s = $"{Keyword} {VarName} in {ArrReadText}";
            if (Id != null || Counter != null)
                s += " : ";
            if (Id != null)
                s += "id " + Id;
            if (Id != null && Counter != null)
                s += " , ";
            if (Counter != null)
                s += "counter " + Counter;
            s += $"\n{{\n\t{InnerSource.ToString(" ")}\n}}";
            return s;
        }
    }

    internal ForEachLoopSpan(string varName, TextSpan[] arrReadText, TextSpan[] innerSource, IVarSystem varSystem) : base(Keyword)
    {
        this.VarName = varName;
        this.ArrReadText = arrReadText;
        this.InnerSource = innerSource;
        this.OuterVarSystem = varSystem;
    }
}


class BreakWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "break";
    internal BreakWordSpan() : base(Keyword) { }
}

class ContinueWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "continue";
    internal ContinueWordSpan() : base(Keyword) { }
}

class OpeningBracketSpan : WordSpan
{
    internal OpeningBracketSpan() : base("(") { }
}

class ArrayOpenerSpan : WordSpan
{
    internal ArrayOpenerSpan() : base("[") { }
}

class ArrayCloserSpan : WordSpan
{
    internal ArrayCloserSpan() : base("]") { }
}

class ClosingBracketSpan : WordSpan
{
    internal ClosingBracketSpan() : base(")") { }
}

class SourceOpenerSpan : WordSpan
{
    internal SourceOpenerSpan() : base("{") { }
}

class SourceCloserSpan : WordSpan
{
    internal SourceCloserSpan() : base("}") { }
}
class CommaSpan : WordSpan
{
    internal CommaSpan() : base(",") { }
}

class SemicolonSpan : WordSpan
{
    internal SemicolonSpan() : base(";") { }
}

class DotSpan : WordSpan
{
    internal DotSpan() : base(".") { }
}

class SetSymbolSpan : WordSpan
{
    internal SetSymbolSpan() : base("=") { }
}

class WhiteSpaceSpan : Span
{
    internal WhiteSpaceSpan(string txt) : base(txt) { }
}

class CommentSpan : Span
{
    private readonly bool multiLine;
    internal CommentSpan(string comment, bool multiLine = false) : base(comment)
    {
        this.multiLine = multiLine;
    }

    internal override string FullText => multiLine ? ("/*" + Text + "*/") : ("//" + Text);
}

class FuncDefSpan : WordSpan, IContext, IDefination, IKeyword
{
    public static string Keyword { get; } = "func";
    public string Namespace { get; set; }
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; set; }
    public TextSpan[] InnerSource { get; set; }
    public IContext Context { get; set; }

    public string Name { get; }
    internal bool Private { get; set; }
    internal bool Static { get; set; }
    internal ArgumentSpan[] Args { get; }
    internal bool Return { get; set; } = false;
    internal object Returns { get; set; }

    internal ClassDefSpan DefinedAt { get; }

    internal FuncDefSpan(string name, ArgumentSpan[] args, TextSpan[] innerSource, ClassDefSpan definedAt) : base(Keyword)
    {
        this.Name = name;
        this.Args = args;
        this.InnerSource = innerSource;
        this.DefinedAt = definedAt;
    }

    protected FuncDefSpan(string text, string name, ArgumentSpan[] args, TextSpan[] innerSource, ClassDefSpan definedAt, bool useless_bool) : base(text)
    {
        this.Name = name;
        this.Args = args;
        this.InnerSource = innerSource;
        this.DefinedAt = definedAt;
    }

    internal override string FullText
    {
        get
        {
            string argsStr = "";
            foreach (var a in Args)
                argsStr += a.ToString() + " , ";
            if (Args.Length > 0)
                argsStr = argsStr.Substring(0, argsStr.Length - 3);
            string s = Static ? "static " : "";
            s += $"{Keyword} {Name} ( {argsStr} )\n{{\n\t{InnerSource.ToString(" ")}\n}}";
            return s;
        }
    }

    public override string ToString() => Text;
}

class ConstructorDefSpan : FuncDefSpan, IKeyword
{
    public static new string Keyword { get; } = "constructor";
    internal ConstructorDefSpan(ArgumentSpan[] args, TextSpan[] innerSource, ClassDefSpan definedAt, Compiler toThrowWith) : base(text: Keyword, definedAt == null ? null : $"{definedAt.Name}.ctor", args, innerSource, definedAt, false)
    {
        if (definedAt == null)
            toThrowWith.Throw("Constructor must be defined inside a class.");
    }

    internal override string FullText
    {
        get
        {
            string argsStr = "";
            foreach (var a in Args)
                argsStr += a.ToString() + " , ";
            if (Args.Length > 0)
                argsStr = argsStr.Substring(0, argsStr.Length - 3);
            string s = "";
            s = $"{Keyword} ({argsStr})\n{{\n\t{InnerSource.ToString(" ")}\n}}";
            return s;
        }
    }
}

class ArgumentSpan : WordSpan
{
    internal string Name { get; }
    internal bool NotNull { get; }
    internal ArgumentSpan(string name, bool notNull = false) : base(name)
    {
        this.Name = name;
        this.NotNull = notNull;
    }

    internal override string FullText
    {
        get
        {
            string s = Name;
            if (NotNull)
                s += " notnull";
            return s;
        }
    }
}

class NotNullWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "notnull";
    internal NotNullWordSpan() : base(Keyword) { }
}

class ReturnWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "return";
    internal ReturnWordSpan() : base(Keyword) { }
}

class NullWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "null";
    internal NullWordSpan() : base(Keyword) { }
}

class ClassDefSpan : WordSpan, IDefination, IVarSystem, IKeyword
{
    internal static ClassDefSpan ExpArrayDef;
    internal static ClassDefSpan ExpStringDef;
    internal static ClassDefSpan ExpExceptionDef;
    internal static ClassDefSpan ExternTypeValueDef;
    internal static ClassDefSpan ExpTypeDef;

    public static string Keyword { get; } = "class";
    public string Namespace { get; set; }
    public string Name { get; }
    public List<Variable> Vars { get; } = []; // static props
    internal Property[] Props { get; }
    internal FuncDefSpan[] Funcs { get; set; }
    internal Property BaseArrayProp => Props.FirstOrDefault(p => p.BaseArray);
    internal Instance ExpType
    {
        get
        {
            if (_expType == null)
            {
                _expType = new Instance(ExpTypeDef ?? throw new Exception("Trying to get type instance before Type def was collected."));
                _expType.Vars[0].Value = Compiler.StringToExpString(Name);
                _expType.Vars[1].Value = Compiler.StringToExpString(Namespace + "::" + Name);
            }
            return _expType;
        }
    }
    private Instance _expType;

    internal ClassDefSpan(string name, Property[] props, FuncDefSpan[] funcs = null) : base(Keyword)
    {
        this.Name = name;
        this.Props = props;
        this.Funcs = funcs;

        if (name == "Array")
            ExpArrayDef = this;
        else if (name == "string")
            ExpStringDef = this;
        else if (name == "Exception")
            ExpExceptionDef = this;
        else if (name == "ExternTypeValue")
            ExternTypeValueDef = this;
        else if (name == "Type")
            ExpTypeDef = this;
    }

    internal override string FullText
    {
        get
        {
            string propsStr = "", funcsStr = "", staticsStr = "";
            foreach (var a in Props)
                propsStr += (a.Const ? "const " : "") + a.Name + (a.Private ? " private" : "") + " , ";
            if (Props.Length > 0)
                propsStr = propsStr.Substring(0, propsStr.Length - 3);

            foreach (var v in Vars)
                staticsStr += $"static " + (v.Private ? "private " : "") + (v.Const ? "const " : "") + v.Name + "\n";

            foreach (var func in Funcs)
                funcsStr += func.FullText + "\n";
            string s = $"{Keyword} {Name} ( {propsStr} )\n{{\n\t{funcsStr}\n}}";
            return s;
        }
    }
}

class EnumDefSpan : WordSpan, IDefination, IKeyword
{
    public static string Keyword { get; } = "enum";
    public string Name { get; }
    public string Namespace { get; set; }
    internal EnumValueSpan[] Values { get; }
    internal EnumDefSpan(string name, EnumValueSpan[] values) : base(Keyword)
    {
        this.Name = name;
        this.Values = values;
    }
    internal override string FullText
    {
        get
        {
            string vals = "";
            int index = 0;
            foreach (var val in Values)
                vals += val.FullText + (++index >= Values.Length ? "" : ",") + "\n";
            return Keyword + $" {Name}\n{{\n\t{vals}\n}}";
        }
    }
}

class EnumValueSpan : WordSpan
{
    internal string Name { get; }
    internal double Value { get; set; }
    internal bool CustomValue { get; }
    internal EnumValueSpan(string name, double value, bool customValue) : base(name)
    {
        this.Name = name;
        this.Value = value;
        this.CustomValue = customValue;
    }

    internal override string FullText => Name + (CustomValue ? $" = {Value}" : "");
}

class InstInitSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "new";
    internal string DefName { get; }
    internal string SpecificNS { get; }
    internal InstInitSpan(string defName, string nsSpec) : base(Keyword)
    {
        this.DefName = defName;
        this.SpecificNS = nsSpec;
    }

    internal override string FullText
    {
        get => Text + " " + DefName;
    }
}

class ThisWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "this";
    internal ThisWordSpan() : base(Keyword) { }
}

class ThrowWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "throw";
    internal ThrowWordSpan() : base(Keyword) { }
}

class TryWordSpan : WordSpan, IContext, IKeyword
{
    public static string Keyword { get; } = "try";
    public IContext Context { get; set; }
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; }
    internal CatchWordSpan Catch { get; set; }
    internal FinallyWordSpan Finally { get; set; }
    public TextSpan[] InnerSource { get; set; }
    internal TryWordSpan(TextSpan[] innerSource, IVarSystem vs, CatchWordSpan catc, FinallyWordSpan finaly) : base(Keyword)
    {
        this.InnerSource = innerSource;
        this.Catch = catc;
        this.Finally = finaly;
        this.OuterVarSystem = vs;
    }
    internal override string FullText => $"{Keyword}\n{{\n\t{InnerSource.ToString(" ")}\n}}";
}

class CatchWordSpan : WordSpan, IContext, IKeyword
{
    public static string Keyword { get; } = "catch";
    public IContext Context { get; set; }
    public List<Variable> Vars { get; set; } = [];
    public IVarSystem OuterVarSystem { get; }
    internal string VarName { get; }
    internal WhenWordSpan When { get; }
    public TextSpan[] InnerSource { get; set; }
    internal CatchWordSpan(string varname, WhenWordSpan when, TextSpan[] innerSource, IVarSystem vs) : base(Keyword)
    {
        this.VarName = varname;
        this.When = when;
        this.InnerSource = innerSource;
        this.OuterVarSystem = vs;
    }
    internal override string FullText
    {
        get
        {
            string s = $"{Keyword} ";
            if (VarName != null)
                s += VarName;
            if (When != null)
                s += When.FullText;
            s += $"\n{{\n\t{InnerSource.ToString(" ")}\n}}";
            return s;
        }
    }
}

class FinallyWordSpan : WordSpan, IContext, IKeyword
{
    public static string Keyword { get; } = "finally";
    public IContext Context { get; set; }
    public List<Variable> Vars { get; set; } = [];
    public IVarSystem OuterVarSystem { get; }
    public TextSpan[] InnerSource { get; set; }
    internal FinallyWordSpan(TextSpan[] innerSource, IVarSystem vs) : base(Keyword)
    {
        this.InnerSource = innerSource;
        this.OuterVarSystem = vs;
    }
    internal override string FullText => $"{Keyword}\n{{\n\t{InnerSource.ToString(" ")}\n}}";
}

class WhenWordSpan : WordSpan, IKeyword
{
    public static string Keyword { get; } = "when";
    internal TextSpan[] Condition { get; }
    internal WhenWordSpan(TextSpan[] condition) : base(Keyword)
    {
        this.Condition = condition;
    }

    internal override string FullText => $"{Keyword}\n{{\n\t{Condition.ToString(" ")}\n}}";
}

class SectionWordSpan : WordSpan, IContext, IKeyword
{
    public static string Keyword { get; } = "section";
    public IContext Context { get; set; }
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; }
    public TextSpan[] InnerSource { get; set; }

    internal SectionWordSpan(TextSpan[] innerSource, IVarSystem vs) : base(Keyword)
    {
        this.InnerSource = innerSource;
        this.OuterVarSystem = vs;
    }

    internal override string FullText => $"{Keyword}\n{{\n\t{InnerSource.ToString(" ")}\n}}";
}
