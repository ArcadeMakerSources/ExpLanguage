using System;
using System.Linq;
using System.Collections.Generic;
using Exp.Spans;

namespace Exp;

public static class CSBasicTypes
{
    public static Instance Int(double value)
    {
        return ((int)value).AsExtern();
    }

    public static Instance Float(double value)
    {
        return ((float)value).AsExtern();
    }

    public static Instance Long(double value)
    {
        return ((long)value).AsExtern();
    }

    public static Instance Uint(double value)
    {
        return ((uint)value).AsExtern();
    }

    public static Instance Byte(double value)
    {
        return ((byte)value).AsExtern();
    }

    internal static Instance AsExtern(this object obj)
    {
        Instance inst = new Instance(ClassDefSpan.ExternTypeValueDef);
        inst.Vars[0].Value = Compiler.StringToExpString(obj.GetType().ToString());
        inst.Vars[1].Value = obj;
        return inst;
    }

    internal static Array MinArray(Instance arr)
    {
        if (arr == null || !arr.IsArray)
            Compiler.Activated.Throw($"Invalid argument: {nameof(arr)} must be an array.");
        if (arr.ArrayValues.Length == 0)
            return arr.ArrayValues;

        Type highest = (arr.Vars[0]?.Value as Instance)?.Vars[1]?.Value as Type; // exp Array.csType.instance

        if (highest == null)
        {
            // determine the item that extends the fewest number of types
            int hdepth = -1, i = -1;
            foreach (var item in arr.ArrayValues)
            {
                i++;

                int DepthOf(Type type)
                {
                    int d = 0;
                    while (type != typeof(object))
                    {
                        d++;
                        type = type.BaseType;
                    }
                    return d;
                }

                Type t = item.GetType();
                int d = DepthOf(t);
                if (d < hdepth || hdepth < 0)
                {
                    highest = t;
                    hdepth = d;
                }
            }
        }

        // validate that all items extend this item
        foreach (var item in arr.ArrayValues)
        {
            bool Extends(Type c, Type p)
            {
                if (p != null && c == p)
                    return true;
                while (c != null)
                {
                    c = c.BaseType;
                    if (p != null && c == p)
                        return true;
                }
                return false;
            }

            if (!Extends(item.GetType(), highest))
                return arr.ArrayValues;
        }

        var res = Array.CreateInstance(highest, arr.ArrayValues.Length);
        Array.Copy(arr.ArrayValues, 0, res, 0, res.Length);
        return res;
    }
}

public class Test
{
    public static void Met(object[] i) => $"Great o[] {i.Length}".Print();
    public static void Met(Test[] i) => $"Great t[] {i.Length}".Print();
    public Test() { }
}

public class Test1 : Test
{
    public Test1() { }
}
