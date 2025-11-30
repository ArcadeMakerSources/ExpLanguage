using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using Exp.Spans;

namespace Exp
{
    public class Variable
    {
        internal string Name { get; }
        internal object _val;
        internal object Value
        {
            get => _val;
            set
            {
                if (Const && this.Value != null)
                    Compiler.Activated.Throw("This var is marked as constant and cannot be set.");
                this._val = value;
            }
        }
        internal bool Private { get; }
        internal bool Const { get; }
        internal WordSpan SettingSpan { get; }

        internal Variable(string name, object value, WordSpan settingSpan, bool prvt = false, bool cons = false)
        {
            this.Name = name;
            this.Value = value;
            this.SettingSpan = settingSpan;
            this.Private = prvt;
            this.Const = cons;
        }
    }

    class Property
    {
        internal string Name { get; }
        internal bool Const { get; }
        internal bool Private { get; }
        internal bool BaseArray { get; }
        internal TextSpan[] InitValueReadText;

        internal Property(bool cons, string name, bool prvt, bool baseArr, TextSpan[] initVal = null)
        {
            this.Const = cons;
            this.Name = name;
            this.Private = prvt;
            this.BaseArray = baseArr;
            this.InitValueReadText = initVal;
        }
    }

    public partial class Compiler : IVarSystem
    {
        private string source;
        private TextSpan[] _sourceSpans_;
        private TextSpan[] SourceSpans
        {
            get => _sourceSpans_;
            set
            {
                _sourceSpans_ = value;
                source = "";
                foreach (var ss in value)
                    source += ss.text;
            }
        }

        private List<ScriptDocument> docs = [];
        private string[] currIncludes = [];
        private int cursor = 0, contextLoc = 0;
        private int spansCursor = 0, spansContextLoc = 0;

        public List<Variable> Vars { get; } = [];
        internal FuncDefSpan[] funcs => definations.OfType<FuncDefSpan>().ToArray();
        internal ClassDefSpan[] Classes => definations.OfType<ClassDefSpan>().Where(c => c.Namespace == null || currIncludes.Contains(c.Namespace)).ToArray();
        internal readonly List<IDefination> definations = [];

        private Instance throwing = null;
        public static Compiler Activated { get; private set; }
        public Compiler(ScriptDocument source, params ScriptDocument[] imports)
        {
            Activated = this;
            this.source = source.Script;
            currIncludes = source.Includes;
            currIncludes[0] = "system";
            if (!currIncludes[0].Equals("system"))
                throw new Exception();
            SourceSpans = source.CodeSpans;
            docs.AddRange(imports);
            this._currentVarSystem_ = this;

            CollectDefs(imports);
            CollectDefs();
        }

        private Span lastSpan;
        Span ReadSpan(bool spoiler = false, bool spaces = false, ClassDefSpan def = null)
        {
            if (spansCursor >= SourceSpans.Length)
                return null;

            TextSpan textSpan = SourceSpans[spansCursor++];
            string text = textSpan.text;
            cursor += text.Length;

            // skip spaces and comments
            if (string.IsNullOrWhiteSpace(textSpan.text) || textSpan.type == SpanType.Space || textSpan.type == SpanType.Comment || textSpan.type == SpanType.MultiLineComment)
                return ReadSpan(spoiler, spaces, def);

            Span span = null;
            if (textSpan.type == SpanType.Number)
            {
                try
                {
                    double d = Convert.ToDouble(text);
                    span = new NumberSpan(d);
                }
                catch (FormatException)
                {
                    Throw("Bad number format.");
                    throw; // else we'll have "Use of unassigned local variable 'span'"
                }
            }
            else if (textSpan.type == SpanType.String)
                span = new StringSpan(text[1..^1].Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\"));
            else if (textSpan.type == SpanType.EscapedString)
                span = new StringSpan(text[2..^1], escaped: true);
            else if (textSpan.type == SpanType.Char)
                span = new CharSpan(text[1..^1].Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\")[0]);
            else if (textSpan.type == SpanType.Normal)
            {
                if (text == "true")
                    span = new TrueWordSpan();
                else if (text == "false")
                    span = new FalseWordSpan();
                else if (text == "if" || text == "while" || text == "for" || text == "foreach" || text == "else")
                {
                    bool forloop = text == "for", foreachloop = text == "foreach", elsec = text == "else";
                    TextSpan[] condition = null, arrReadText = null, initExe = null, stepExe = null, innerSource = null; // they cannot be null, because later we use .ToArray() on them
                    string idAttr = null, counterAttr = null, varname = null;

                    if (!spoiler) // else, the spoiler check would cause UnknownVariable exception if the condition contains a variable that it's init will complete AFTER the spoiler check
                    {
                        if (foreachloop)
                        {
                            // get var name to set
                            varname = ReadWord().FullText;

                            // expect "in" word
                            ReadWord("in");

                            // we want to get all the text of the array reading, but to actually read it in Run()
                            ReadValue(out arrReadText, null, true);
                        }
                        else
                        {
                            // if for loop, read initExe
                            if (forloop)
                            {
                                int cbr = spansCursor;
                                while (true)
                                {
                                    if (cursor >= source.Length)
                                        Throw("Endless for loop.");

                                    Span ispan = ReadSpan(spaces: true);
                                    if (ispan is SemicolonSpan)
                                        break;
                                }
                                initExe = new TextSpan[spansCursor - cbr - 1]; // -1 bc of the ';'
                                for (int i = cbr; i < spansCursor - 1; i++)
                                    initExe[i - cbr] = SourceSpans[i];
                            }

                            if (!elsec)
                            {
                                // read condition
                                ReadValue<bool>(out condition, allowUnknownVars: true);
                                if (forloop)
                                    Read<SemicolonSpan>();
                            }

                            // if for loop, read stepExe
                            if (forloop)
                            {
                                int cbr = spansCursor;
                                while (true)
                                {
                                    if (cursor >= source.Length)
                                        Throw("Endless for loop.");

                                    if (Spoiler().FullText == ":")
                                        break;

                                    Span ispan = ReadSpan(spaces: true);
                                    if (ispan is SourceOpenerSpan)
                                        break;
                                }
                                stepExe = new TextSpan[spansCursor - cbr - 1]; // -1 bc of the '{'
                                for (int i = cbr; i < spansCursor - 1; i++)
                                    stepExe[i - cbr] = SourceSpans[i];
                            }
                        }
                        // condition attributes
                        string[] attr = ["id", "counter"];

                        if (!elsec && Spoiler().FullText == ":")
                        {
                            ReadSpan(); // reads ':'

                            // condition define
                            while (true)
                            {
                                WordSpan word = ReadWord();
                                if (word.FullText == attr[0])
                                {
                                    if (idAttr != null)
                                        Throw(attr[0] + " is already defined.");
                                    else
                                        idAttr = ReadWord().FullText;
                                }
                                else if (word.FullText == attr[1])
                                {
                                    if (counterAttr != null)
                                        Throw(attr[1] + " is already defined.");
                                    else
                                        counterAttr = ReadWord().FullText;
                                }
                                else
                                {
                                    Throw($"Unexpected word '{word}'. Condition attributes are: {attr}");
                                }

                                // if ',' repeat, else break
                                Span next = ReadSpan();
                                if (next is SourceOpenerSpan)
                                    break;
                                else if (next is not CommaSpan)
                                    Throw($"Unexpected '{next.Text}'. Only ',' and '{{' can appear here.");
                            }
                        }
                        else if (!forloop)
                        {
                            Read<SourceOpenerSpan>();
                        }

                        // read inner source
                        innerSource = ReadInnerSource(readOpener: false);
                    }

                    if (text == "if")
                        span = new IfConditionSpan(condition, innerSource, CurrentVarSystem);
                    else if (text == "while")
                        span = new WhileConditionSpan(condition, innerSource, CurrentVarSystem);
                    else if (forloop)
                        span = new ForLoopSpan(initExe, condition, stepExe, innerSource, CurrentVarSystem);
                    else if (foreachloop)
                        span = new ForEachLoopSpan(varname, arrReadText, innerSource, CurrentVarSystem);
                    else if (elsec)
                        span = new ElseConditionSpan(innerSource, CurrentVarSystem);

                    if (span is ILoopContext loop)
                    {
                        loop.Id = idAttr;
                        loop.Counter = counterAttr;
                    }
                }
                else if (text == "break")
                    span = new BreakWordSpan();
                else if (text == "continue")
                    span = new ContinueWordSpan();
                else if (text == "func" || text == "constructor")
                {
                    string name = null;
                    bool ctor = text == "constructor";

                    // read function name
                    // a constructor and a var func (set f = func ()) don't have a name
                    if (!ctor && Spoiler() is not OpeningBracketSpan)
                        name = ReadWord().FullText;

                    // read args
                    Read<OpeningBracketSpan>();

                    var args = new List<ArgumentSpan>();
                    Span argsp = ReadWord();
                    if (argsp is not ClosingBracketSpan)
                    {
                        while (true)
                        {
                            // check notnull
                            bool notnull = false;
                            if (Spoiler() is NotNullWordSpan)
                            {
                                notnull = true;
                                ReadWord();
                            }

                            // create the span
                            args.Add(new ArgumentSpan(argsp.FullText, notnull));
                            argsp = ReadSpan();
                            if (argsp is CommaSpan)
                                argsp = ReadWord();
                            else if (argsp is ClosingBracketSpan)
                                break;
                            else
                                Throw($"Unexpected '{argsp.Text}'. Only ',' or ')' are expected here.");
                        }
                    }

                    // read inner source
                    TextSpan[] innerSource = ReadInnerSource();

                    // create the func span
                    if (!spoiler)
                    {
                        if (ctor)
                            span = new ConstructorDefSpan(args.ToArray(), innerSource, def, this);
                        else
                            span = new FuncDefSpan(name, args.ToArray(), innerSource, def);
                    }
                }
                else if (text == "class")
                {
                    bool basearrSet = false;
                    List<Property> props = [];
                    List<FuncDefSpan> funcs = [];

                    // read class name
                    string name = ReadWord().FullText;

                    // read properties
                    Read<OpeningBracketSpan>();

                    Span propsp = ReadSpan();
                    if (propsp is not ClosingBracketSpan)
                    {
                        while (true)
                        {
                            bool prvt = false, basearr = false, cons = false;

                            // before pname
                            if (propsp is ConstWordSpan)
                            {
                                cons = true;
                                propsp = ReadWord();
                            }

                            // read param name
                            string pname = propsp.FullText;

                            // after pname
                            propsp = ReadSpan();
                            if (propsp is PrivateWordSpan)
                            {
                                prvt = true;
                                propsp = ReadWord();
                            }
                            if (propsp is BaseArrayWordSpan)
                            {
                                if (basearrSet)
                                    Throw("Only 1 property can be marked as basearray.");
                                else
                                    basearrSet = true;
                                basearr = true;
                                propsp = ReadWord();
                            }
                            TextSpan[] val = null;
                            if (propsp is SetSymbolSpan)
                            {
                                ReadValue(out val, allowUnknownVars: true);
                                propsp = ReadWord();
                            }

                            props.Add(new Property(cons, pname, prvt, basearr, val));

                            if (propsp is CommaSpan)
                                propsp = ReadWord();
                            else if (propsp is ClosingBracketSpan)
                                break;
                            else
                                Throw($"Unexpected '{propsp.Text}'. Only ',' or ')' are expected here.");
                        }
                    }

                    // func to read static set (static name = value) (this func will be called after reading the static word)
                    Variable ReadStaticSet(Span fsp)
                    {
                        bool prvt = false, constant = false;
                        if (fsp is PrivateWordSpan)
                        {
                            prvt = true;
                            fsp = ReadWord();
                        }
                        if (fsp is ConstWordSpan)
                        {
                            constant = true;
                            fsp = ReadWord();
                        }
                        string name = fsp.FullText;
                        object val = null;
                        WordSpan settingSpan = null;
                        if (Spoiler() is SetSymbolSpan set)
                        {
                            ReadSpan();
                            val = ReadValue();
                            settingSpan = set;
                        }

                        return new Variable(name, val, settingSpan, prvt, cons: constant);
                    }

                    // collect funcs and statics
                    Read<SourceOpenerSpan>();

                    // create the class span to use it as def parameter in ReadSpan()
                    var cls = new ClassDefSpan(name, props.ToArray());

                    while (true)
                    {
                        if (cursor >= source.Length)
                            Throw("Endless function.");

                        Span ispan = ReadSpan(spaces: false, def: cls);
                        bool statdef = false;
                        if (ispan is StaticWordSpan)
                        {
                            statdef = true;
                            ispan = ReadSpan(spaces: false, def: cls);
                        }

                        if (ispan is SourceCloserSpan)
                            break;
                        if (ispan is FuncDefSpan func)
                        {
                            func.Static = statdef;
                            funcs.Add(func);
                        }
                        else if (statdef)
                            cls.Vars.Add(ReadStaticSet(ispan));
                        else
                            Throw($"Unexpected span in a class ('{ispan.Text}').");
                    }

                    cls.Funcs = funcs.ToArray();
                    span = cls;
                }
                else if (text == "new")
                {
                    // read class name
                    string defname = ReadWord().FullText;

                    span = new InstInitSpan(defname);
                }
                else if (text == "enum")
                {
                    // read enum name
                    string ename = ReadWord().FullText;

                    // read content
                    List<EnumValueSpan> evalues = [];
                    Read<SourceOpenerSpan>();

                    Span next;
                    double counter = -1;
                    do
                    {
                        if (cursor >= source.Length)
                            Throw("Endless enum.");

                        next = ReadSpan();

                        if (next is SourceCloserSpan)
                            break;

                        string vname;
                        double val = -1;
                        bool specific = false;

                        // read value name
                        vname = next.FullText;
                        next = ReadSpan();

                        // read specific value
                        val = ++counter;
                        if (next is SetSymbolSpan)
                        {
                            val = Read<NumberSpan>().Number;
                            specific = true;
                            next = ReadSpan();
                        }

                        evalues.Add(new EnumValueSpan(ename, val, specific));

                        // expect ',' or '}'
                        if (next is SourceCloserSpan)
                            break;
                        if (next is not CommaSpan)
                            Throw($"Unexpected span in enum content. Only ',' or '}}' is expected here (Span received: '{next}').");
                    }
                    while (true);

                    span = new EnumDefSpan(ename, evalues.ToArray());
                }
                else if (text == "notnull")
                    span = new NotNullWordSpan();
                else if (text == "return")
                    span = new ReturnWordSpan();
                else if (text == "this")
                    span = new ThisWordSpan();
                else if (text == "null")
                    span = new NullWordSpan();
                else if (text == "lenof")
                    span = new LenofWordSpan();
                else if (text == "try" || text == "catch")
                {
                    string catchVarName = null;
                    bool catc = text == "catch";

                    // if catch, read varname
                    if (catc)
                        catchVarName = ReadWord().FullText;

                    // read inner source
                    TextSpan[] innerSource = ReadInnerSource();

                    // create the span
                    if (catc)
                        span = new CatchWordSpan(catchVarName, innerSource, CurrentVarSystem);
                    else
                    {
                        CatchWordSpan toAttach = null;
                        if (Spoiler() is CatchWordSpan)
                        {
                            toAttach = Read<CatchWordSpan>();
                        }
                        span = new TryWordSpan(innerSource, CurrentVarSystem, toAttach);
                    }
                }
                else if (text == "throw")
                    span = new ThrowWordSpan();
                else if (text == "namespace")
                {
                    string ns = ReadWord().FullText;
                    span = new NamespaceWordSpan(ns);
                }
                else if (text == "print")
                    span = new PrintWordSpan();
                else if (text == "var")
                    span = new SetWordSpan();
                else if (text == "const")
                    span = new ConstWordSpan();
                else if (text == "static")
                    span = new StaticWordSpan();
                else if (text == "private")
                    span = new PrivateWordSpan();
                else if (text == "basearray")
                    span = new BaseArrayWordSpan();
                else if (text == "is")
                    span = new IsWordSpan();
                else
                    span = new WordSpan(text);
            }
            else if (textSpan.type == SpanType.Symbol)
            {
                if (text == "!")
                    span = new NotSymbolSpan();
                else if (text == "=")
                    span = new SetSymbolSpan();
            }
            else if (textSpan.type == SpanType.DotCom)
            {
                if (text == ".")
                    span = new DotSpan();
                else if (text == ",")
                    span = new CommaSpan();
                else if (text == ";")
                    span = new SemicolonSpan();
            }
            else if (textSpan.type == SpanType.Brace)
            {
                if (text == "(")
                    span = new OpeningBracketSpan();
                else if (text == ")")
                    span = new ClosingBracketSpan();
                else if (text == "[")
                    span = new ArrayOpenerSpan();
                else if (text == "]")
                    span = new ArrayCloserSpan();
                else if (text == "{")
                    span = new SourceOpenerSpan();
                else if (text == "}")
                    span = new SourceCloserSpan();
            }
            else if (textSpan.type == SpanType.Comment)
                span = new CommentSpan(text[2..]);
            else if (textSpan.type == SpanType.MultiLineComment)
                span = new CommentSpan(text[2..^2], multiLine: true);
            else if (textSpan.type == SpanType.Space)
                span = new WhiteSpaceSpan(text);
            span ??= new WordSpan(text);

            lastSpan = span;
            return span;
        }

        private WordSpan ReadWord(string specific = null)
        {
            Span span = ReadSpan();
            if (span == null && specific == null)
                return null;
            if ((!(span is WordSpan)) || (specific != null && span.Text != specific))
                Throw($"A word was expected (span received: {span.GetType().Name}[{span.FullText}]).");
            return span as WordSpan;
        }

        private T Read<T>() where T : Span
        {
            Span span = ReadSpan();
            if (span is null)
                throw new Exception("span was null.");
            if (!(span is T))
                Throw($"A {typeof(T).Name} was expected (span received: {span.GetType().Name}[{span.FullText}]).");
            return span as T;
        }

        private object[] ReadParamList(bool arrayBrackets = false, bool openerWasAlreadyRead = false, bool allowUnknownVars = false)
        {
            if (!openerWasAlreadyRead)
            {
                if (arrayBrackets)
                    Read<ArrayOpenerSpan>();
                else
                    Read<OpeningBracketSpan>();
            }

            var spoiler = Spoiler();
            if (arrayBrackets ? spoiler is ArrayCloserSpan : spoiler is ClosingBracketSpan)
            {
                ReadSpan();
                return new object[0];
            }
            var parameters = new List<object>();
            while (true)
            {
                // parse the value
                object val = ReadValue(out TextSpan[] _, allowUnknownVars: allowUnknownVars);
                parameters.Add(val);

                Span symb = Spoiler();
                if (arrayBrackets ? symb is ArrayCloserSpan : symb is ClosingBracketSpan)
                {
                    ReadSpan();
                    break;
                }
                else if (symb is CommaSpan)
                {
                    ReadSpan();
                }
                else
                    Throw($"Unexpected span in parameter list (Span: '{symb}').");
            }
            return parameters.ToArray();
        }

        private TextSpan[] ReadInnerSource(bool readOpener = true)
        {
            if (readOpener)
                Read<SourceOpenerSpan>();

            int cbr = spansCursor;
            // read inner source until }
            while (true)
            {
                if (cursor >= source.Length)
                    Throw("Endless condition.");

                Span ispan = ReadSpan(spaces: false);

                if (ispan is SourceCloserSpan)
                    break;
            }

            TextSpan[] innerSource = new TextSpan[spansCursor - cbr - 1]; // -1 bc of the '}'
            for (int i = cbr; i < spansCursor - 1; i++)
                innerSource[i - cbr] = SourceSpans[i];

            return innerSource;
        }

        private Span Spoiler(int skip = 0)
        {
            Span last = lastSpan;
            int cur = cursor;
            int spcur = spansCursor;
            Span span = null;
            for (int i = 0; i <= skip; i++)
                span = ReadSpan(true);
            cursor = cur;
            spansCursor = spcur;
            lastSpan = last;
            return span;
        }

        private Variable GetPointer(string name, out IVarSystem foundAt, IVarSystem specificVs = null)
        {
            Variable pointer = null;

            // a function to scan a single VS
            Variable Scan(IVarSystem vs)
            {
                if (vs is null)
                    throw new ArgumentNullException(nameof(vs));
                if (vs.Vars is null)
                    throw new NullReferenceException(nameof(vs.Vars));
                if (name is null)
                    return null;
                return vs.Vars.FirstOrDefault(v => name.Equals(v.Name));
            }

            // scan the current VS inner, then in its outers, then outers' outers and so on
            // if specific VS is attached, start from it
            IVarSystem vs = specificVs ?? CurrentVarSystem;
            while (vs != null)
            {
                pointer = Scan(vs);
                if (pointer == null && vs is IContext ctx)
                {
                    vs = ctx.OuterVarSystem;
                }
                else
                    break;
            }

            if (pointer != null)
            {
                foundAt = vs;
                ValidateAccess(pointer, vs, CurrentVarSystem);
            }
            else
                foundAt = null;

            return pointer;
        }

        private Variable GetPointer(string name, IVarSystem specificVs = null) => GetPointer(name, out IVarSystem _, specificVs);

        private void ValidateAccess(Variable v, IVarSystem setAt, IVarSystem from)
        {
            if (!v.Private)
                return;
            while (from != null)
            {
                if (setAt == from || (from is FuncDefSpan func && setAt is Instance inst && func.DefinedAt == inst.def))
                    return;
                if (from is FuncDefSpan func1 && setAt is ClassDefSpan def && func1.DefinedAt == def)
                    return;
                if (from is IContext ctx)
                    from = (IVarSystem)ctx.Context;
                else
                    break;
            }
            Throw("Access to this variable is denied from this VS / Context");
        }

        private void SetVar(string name, object value, WordSpan settingVar = null, IVarSystem specificVS = null, bool createConst = false)
        {
            var v = GetPointer(name, out IVarSystem foundAt, specificVS);
            if (v != null)
            {
                ValidateAccess(v, foundAt, CurrentVarSystem);
                if (settingVar != null) // meaning this is var init
                    v._val = value; // skip constant set block
                else
                    v.Value = value;
            }
            else
            {
                (specificVS ?? CurrentVarSystem).Vars.Add(new Variable(name, value, settingVar, cons: createConst));
            }
        }

        private void SetArrVar(string name, int index, object value, IVarSystem specificVS = null)
        {
            var v = GetPointer(name, specificVS);
            if (v.Value is Instance inst && inst.IsArray)
            {
                if (index < 0 || index >= inst.ArrayValues.Length)
                    Throw($"index is out of range (index: {index}, array length: {inst.ArrayValues.Length}).");
            }
            else
                Throw($"'{name}' is not an array.");
        }

        private object GetVar(string name, IVarSystem specificVS)
        {
            var pointer = GetPointer(name, out IVarSystem foundAt, specificVS);
            if (pointer != null)
            {
                ValidateAccess(pointer, foundAt, CurrentVarSystem);
                return pointer.Value;
            }

            Throw($"Unknown variable '{name}'.");
            throw null;
        }

        private T GetVar<T>(string name, bool allowNull = true, IVarSystem specificVS = null)
        {
            object val = GetVar(name, specificVS);
            if (val == null)
            {
                if (allowNull)
                    return default;
                Throw($"{name} is null.");
            }
            if (val is T obj)
                return obj;
            Throw(name + $" is {val.GetType()}, but {typeof(T)} was expected.");
            throw null;
        }

        private bool VarExists(string name, IVarSystem specificVS = null)
        {
            return GetPointer(name, specificVS) != null;
        }

        private void DeleteVar(string name, IVarSystem specificVS = null)
        {
            GetPointer(name, out IVarSystem vs, specificVS);
            vs?.Vars.RemoveAll(v => v.Name.Equals(name));
        }

        internal void Throw(string msg, bool beforeCurrentSpan = true)
        {
            string source = this.source, sourceName = "UnknownSource";
            int loc = 0;
            if (SourceSpans.Length > 0 && spansCursor > 0)
            {
                loc = SourceSpans[spansCursor - 1].location;
                source = SourceSpans[spansCursor - 1].doc?.Script ?? source;
                sourceName = SourceSpans[spansCursor - 1].doc?.Name;
            }
            
            if (beforeCurrentSpan && lastSpan != null)
                loc -= lastSpan.FullText.Length;

            int line = 1, col = 0;
            for (int i = 0; i < loc; i++)
            {
                col++;
                if (source[i] == '\n')
                {
                    line++;
                    col = 0;
                }
            }

            msg += $"\nVS: {CurrentVarSystem}\ncontext: {currentContext}";
            if (currentContext is FuncDefSpan func)
                msg += $" ({func.Name})";
            throw new ExpException(sourceName, line, col, msg);
        }

        private static void Print(object s)
        {
            s.Print();
        }

        private static void Println(object s)
        {
            s.Println();
        }
    }

    public class ExpException : Exception
    {
        public int Line { get; }
        public int Col { get; }
        public ExpException(string sourceName, int line, int col, string msg) : base($"Exp Error ({sourceName}: {line}, {col}): {msg}")
        {
            this.Line = line;
            this.Col = col;
        }
    }

    public static class Extensions
    {
        public static bool IsDigit(this char c)
        {
            return c >= '0' && c <= '9';
        }

        public static void Print(this object s, object plus = null)
        {
            Console.Write(s.ToString() + (plus ?? ""));
        }

        public static void Println(this object s, object plus = null)
        {
            if (s is TextSpan[] spans)
            {
                Println("[");
                foreach (var span in spans)
                    span.Print(", ");
                Println("]");
            }
            else
                Console.WriteLine(s.ToString() + (plus ?? ""));
        }

        public static int CountOf(this string src, char c, int startIndex = 0, int endIndex = -1, bool ignoreCase = false)
        {
            if (src.Length == 0)
                return 0;

            if (endIndex < 0)
                endIndex = src.Length - 1;
            if (startIndex < 0 || startIndex >= src.Length || endIndex < startIndex || endIndex >= src.Length)
                throw new IndexOutOfRangeException("start or end index is out of range");
            int count = 0;
            for (int i = startIndex; i <= endIndex; i++)
            {
                char cc = src[i];
                if (ignoreCase ? cc.EqualsIgnoreCase(c) : cc == c)
                    count++;
            }
            return count;
        }

        public static bool EqualsIgnoreCase(this char c, char value)
        {
            return c.ToString().Equals(value.ToString(), StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
