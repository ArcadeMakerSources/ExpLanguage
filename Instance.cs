using System;
using System.Linq;
using System.Collections.Generic;
using Exp.Spans;

namespace Exp;

class Instance : IVarSystem
{
    internal readonly ClassDefSpan def;
    public List<Variable> Vars { get; }
    internal bool IsArray { get; }
    internal object[] ArrayValues { get; }

    internal Instance(ClassDefSpan def, object[] arrVals = null)
    {
        this.def = def;
        this.Vars = [];
        this.IsArray = arrVals != null;
        this.ArrayValues = arrVals;

        foreach (var prop in def.Props)
        {
            //object val = prop.InitValueReadText == null ? null : comp.Run<object>(prop.InitValueReadText);
            Vars.Add(new Variable(prop.Name, null, def, prop.Private, prop.Const));
        }
    }

    public override string ToString()
    {
        return def == ClassDefSpan.ExpStringDef ? Compiler.ExpStringToString(this) : def.Name;
    }
}
