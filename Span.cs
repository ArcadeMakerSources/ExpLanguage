
using System;
using System.Collections.Generic;
using System.Linq;

namespace Exp.Spans;

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

class PrintWordSpan : WordSpan
{
    internal PrintWordSpan() : base("print") { }
}

class LenofWordSpan : WordSpan
{
    internal LenofWordSpan() : base("lenof") { }
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

class NamespaceWordSpan : WordSpan
{
    internal string Namespace { get; }
    internal NamespaceWordSpan(string ns) : base("namespace")
    {
        this.Namespace = ns;
    }
    internal override string FullText => $"{Text} {Namespace}";
}

class SetWordSpan : WordSpan
{
    internal SetWordSpan() : base("var") { }
}

class StaticWordSpan : WordSpan
{
    internal StaticWordSpan() : base("static") { }
}

class PrivateWordSpan : WordSpan
{
    internal PrivateWordSpan() : base("private") { }
}

class BaseArrayWordSpan : WordSpan
{
    internal BaseArrayWordSpan() : base("basearray") { }
}

class ConstWordSpan : WordSpan
{
    internal ConstWordSpan() : base("const") { }
}

class IsWordSpan : WordSpan
{
    internal IsWordSpan() : base("is") { }
}

class TrueWordSpan : WordSpan
{
    internal TrueWordSpan() : base("true") { }
}

class FalseWordSpan : WordSpan
{
    internal FalseWordSpan() : base("false") { }
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
    TextSpan[] InnerSource { get; }
    IContext Context { get; }
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
    public TextSpan[] InnerSource { get; }
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

class IfConditionSpan : ConditionSpan
{
    internal IfConditionSpan(TextSpan[] condition, TextSpan[] innerSource, IVarSystem varSystem) : base("if", condition, innerSource, varSystem) { }
}

class ElseConditionSpan : WordSpan, IContext
{
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; }
    public TextSpan[] InnerSource { get; }
    public IContext Context { get; }

    internal override string FullText => base.FullText + $"\n{{\n\t{InnerSource}\n}}";
    internal ElseConditionSpan(TextSpan[] innerSource, IVarSystem varSystem) : base("else")
    {
        this.InnerSource = innerSource;
        this.OuterVarSystem = varSystem;
    }
}

class WhileConditionSpan : ConditionSpan, ILoopContext
{
    public bool Break { get; set; }
    public bool Continue { get; set; }
    public string Counter { get; set; }
    public string Id { get; set; }
    internal override string FullText
    {
        get
        {
            string s = $"while {Condition}";
            if (Id != null || Counter != null)
                s += " : ";
            if (Id != null)
                s += "id " + Id;
            if (Id != null && Counter != null)
                s += " , ";
            if (Counter != null)
                s += "counter " + Counter;
            s += $"\n{{\n\t{InnerSource}\n}}";
            return s;
        }
    }
    internal WhileConditionSpan(TextSpan[] condition, TextSpan[] innerSource, IVarSystem varSystem) : base("while", condition, innerSource, varSystem) { }
}

class ForLoopSpan : ConditionSpan, ILoopContext
{
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
            string s = $"for {InitExe} ; {Condition} ; {StepExe}";
            if (Id != null || Counter != null)
                s += " : ";
            if (Id != null)
                s += "id " + Id;
            if (Id != null && Counter != null)
                s += " , ";
            if (Counter != null)
                s += "counter " + Counter;
            s += $"\n{{\n\t{InnerSource}\n}}";
            return s;
        }
    }
    internal ForLoopSpan(TextSpan[] initExe, TextSpan[] condition, TextSpan[] stepExe, TextSpan[] innerSource, IVarSystem varSystem) : base("for", condition, innerSource, varSystem)
    {
        this.InitExe = initExe;
        this.StepExe = stepExe;
    }
}

class ForEachLoopSpan : WordSpan, ILoopContext
{
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; }
    public TextSpan[] InnerSource { get; }
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
            string s = $"{Text} {VarName} in {ArrReadText}";
            if (Id != null || Counter != null)
                s += " : ";
            if (Id != null)
                s += "id " + Id;
            if (Id != null && Counter != null)
                s += " , ";
            if (Counter != null)
                s += "counter " + Counter;
            s += $"\n{{\n\t{InnerSource}\n}}";
            return s;
        }
    }

    internal ForEachLoopSpan(string varName, TextSpan[] arrReadText, TextSpan[] innerSource, IVarSystem varSystem) : base("foreach")
    {
        this.VarName = varName;
        this.ArrReadText = arrReadText;
        this.InnerSource = innerSource;
        this.OuterVarSystem = varSystem;
    }
}


class BreakWordSpan : WordSpan
{
    internal BreakWordSpan() : base("break") { }
}

class ContinueWordSpan : WordSpan
{
    internal ContinueWordSpan() : base("continue") { }
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

class FuncDefSpan : WordSpan, IContext, IDefination
{
    public string Namespace { get; set; }
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; set; }
    public TextSpan[] InnerSource { get; }
    public IContext Context { get; set; }

    public string Name { get; }
    internal bool Static { get; set; }
    internal ArgumentSpan[] Args { get; }
    internal bool Return { get; set; } = false;
    internal object Returns { get; set; }

    internal ClassDefSpan DefinedAt { get; }

    internal FuncDefSpan(string name, ArgumentSpan[] args, TextSpan[] innerSource, ClassDefSpan definedAt) : base("func")
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
            s += $"{Text} {Name} ( {argsStr} )\n{{\n\t{InnerSource}\n}}";
            return s;
        }
    }

    public override string ToString() => Text;
}

class ConstructorDefSpan : FuncDefSpan
{
    internal ConstructorDefSpan(ArgumentSpan[] args, TextSpan[] innerSource, ClassDefSpan definedAt, Compiler toThrowWith) : base(text: "constructor", definedAt == null ? null : $"{definedAt.Name}.ctor", args, innerSource, definedAt, false)
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
            s = $"{Text} ( {argsStr} )\n{{\n\t{InnerSource}\n}}";
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

class NotNullWordSpan : WordSpan
{
    internal NotNullWordSpan() : base("notnull") { }
}

class ReturnWordSpan : WordSpan
{
    internal ReturnWordSpan() : base("return") { }
}

class NullWordSpan : WordSpan
{
    internal NullWordSpan() : base("null") { }
}

class ClassDefSpan : WordSpan, IDefination, IVarSystem
{
    internal static ClassDefSpan ExpArrayDef;
    internal static ClassDefSpan ExpStringDef;

    public string Namespace { get; set; }
    public string Name { get; }
    public List<Variable> Vars { get; } = []; // static props
    internal Property[] Props { get; }
    internal FuncDefSpan[] Funcs { get; set; }
    internal Property BaseArrayProp => Props.FirstOrDefault(p => p.BaseArray);

    internal ClassDefSpan(string name, Property[] props, FuncDefSpan[] funcs = null) : base("class")
    {
        this.Name = name;
        this.Props = props;
        this.Funcs = funcs;

        if (name == "Array")
            ExpArrayDef = this;
        else if (name == "string")
            ExpStringDef = this;
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
            string s = $"{Text} {Name} ( {propsStr} )\n{{\n\t{funcsStr}\n}}";
            return s;
        }
    }
}

class EnumDefSpan : WordSpan, IDefination
{
    public string Name { get; }
    public string Namespace { get; set; }
    internal EnumValueSpan[] Values { get; }
    internal EnumDefSpan(string name, EnumValueSpan[] values) : base("enum")
    {
        this.Name = name;
        this.Values = values;
    }
    internal override string FullText
    {
        get
        {
            string vals = "";
            foreach (var val in Values)
                vals += val.FullText + "\n";
            return Text + $" {Name}\n{{\n\t{vals}\n}}";
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

    internal override string FullText => Name + (CustomValue ? $" = {Value}" : "") + " ,";
}

class InstInitSpan : WordSpan
{
    internal string DefName { get; }
    internal InstInitSpan(string defName) : base("new")
    {
        this.DefName = defName;
    }

    internal override string FullText
    {
        get => Text + " " + DefName;
    }
}

class ThisWordSpan : WordSpan
{
    internal ThisWordSpan() : base("this") { }
}

class ThrowWordSpan : WordSpan
{
    internal ThrowWordSpan() : base("throw") { }
}

class TryWordSpan : WordSpan, IContext
{
    public IContext Context { get; set; }
    public List<Variable> Vars { get; } = [];
    public IVarSystem OuterVarSystem { get; }
    internal CatchWordSpan Catch { get; set; }
    public TextSpan[] InnerSource { get; }
    internal TryWordSpan(TextSpan[] innerSource, IVarSystem vs, CatchWordSpan catc = null) : base("try")
    {
        this.InnerSource = innerSource;
        this.Catch = catc;
        this.OuterVarSystem = vs;
    }
    internal override string FullText => $"{Text}\n{{\n\t{InnerSource}\n}}";
}

class CatchWordSpan : WordSpan, IContext
{
    public IContext Context { get; set; }
    public List<Variable> Vars { get; set; } = [];
    public IVarSystem OuterVarSystem { get; }
    internal string VarName { get; }
    public TextSpan[] InnerSource { get; }
    internal CatchWordSpan(string varname, TextSpan[] innerSource, IVarSystem vs) : base("catch")
    {
        this.VarName = varname;
        this.InnerSource = innerSource;
        this.OuterVarSystem = vs;
    }
    internal override string FullText => $"{Text}\n{{\n\t{InnerSource}\n}}";
}
