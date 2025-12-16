using Exp.Spans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exp
{
    public partial class Compiler
    {
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
    }
}
