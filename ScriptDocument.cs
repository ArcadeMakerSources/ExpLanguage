using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace Exp;

public class ScriptDocument
{
    public string Name { get; set; }
    public string Script { get; }
    internal TextSpan[] CodeSpans { get; }
    internal string[] Includes { get; }


    private ScriptDocument(string script, string name)
    {
        this.Name = name;
        this.Script = script;
        Includes = ReadIncludes(out int endinc);
        this.Script = script.Substring(endinc);
        CodeSpans = Spanner.GetTextSpans(this.Script);
        foreach (var span in CodeSpans)
            span.doc = this;
    }

    public static ScriptDocument FromString(string script, string name)
    {
        return new ScriptDocument(script, name);
    }

    public static ScriptDocument FromFile(string path)
    {
        return new ScriptDocument(File.ReadAllText(path), path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path);
    }

    public static ScriptDocument[] FromFiles(string[] paths)
    {
        var docs = new ScriptDocument[paths.Length];
        for (int i = 0; i < paths.Length; i++)
            docs[i] = FromFile(paths[i]);
        return docs;
    }

    private static readonly char[] includeOvers = [' ', '\n', '\t'];
    private string[] ReadIncludes(out int endinc)
    {
        int cursor = -1;
        List<string> ns = [];
        while (cursor < Script.Length)
        {
            char c = Script[++cursor];
            if (c == ' ' || c == '\n' || c == '\t')
                continue;

            if (Script.Substring(cursor, 8).Equals("#include"))
            {
                cursor += 9;
                string inc = "";
                while (cursor < Script.Length && !includeOvers.Contains(Script[cursor]))
                    inc += Script[cursor++];
                ns.Add(inc);
            }
            else
            {
                break;
            }
        }
        endinc = cursor;
        return ns.ToArray();
    }
}