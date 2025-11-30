# ExpLanguage
A new programming language project

To run a console API for running scripts:

1. save the basic types codes in a file, for example system.txt. The code for this file is:

```csharp
namespace system

class string (const chars private basearray) 
{
    constructor (carr notnull) 
    { 
        if carr is not Array 
        { 
            throw new Exception ("Argument must be an array. ") 
        } 
        foreach c in carr 
        { 
            if c is not char 
            { 
                throw new Exception ("Argument must be an array of chars.") 
            } 
        } 
        chars = carr 
    }
    
    constructor() 
    { 
        chars = new Array(0)
    }
    
    func charAt(index notnull) 
    {
        return chars [ index ]
    }
    
    func length() 
    {
        return lenof chars
    }
    
    func substr(stind, len) 
    {
        var str = new string() 
        for var i = stind; i < length(); i = i + 1 : counter ind
        {
            str = str + chars[i]
            if ind = len - 1
            {
                return str
            }
        }
    }
    
    func toNum( ) 
    {
        var num = 0
        const strlen = length() 
        for var m = 1 var d = 0; d < strlen; d = d + 1 m = m * 10
        {
            var dgt = charAt(strlen - 1 - d) - '0'
            num = num + dgt * m
        }
        return num 
    }
}

class Exception (msg) 
{
    constructor (m notnull) 
    {
        msg = m
    }
}

class Array () 
{
    constructor (len notnull) 
    {
        // BUILTIN FUNCTION
    }
    
    func length() 
    {
        return lenof this
    }
    
    func map (mod) 
    {
        const n = new Array ( lenof this )
        foreach i in this : counter ind
        {
            n [ ind ] = mod ( i )
        }
        return n
    }
    
    func copy ( ) 
    {
        return map(func(item) { return item } ) 
    }
}

class List (array private basearray) 
{
    constructor ( arr notnull ) 
    {
        array = arr
    }
    
    constructor() 
    {
        array = new Array (0) 
    }
    
    func add(val) 
    {
        const n = new Array(array.length() + 1)
        for var i = 0; i < count(); i = i + 1
        {
            n [i] = array[i]
        }
        n[n.length() - 1] = val
        array = n
    }
    
    func remove(index notnull) 
    {
        const n = new Array (array.length() - 1) 
        const len = array.length() 
        for var i = 0; i < index; i = i + 1
        {
            n[i] = array[i]
        }
        for var i = index + 1 ; i < len ; i = i + 1
        {
            n[i - 1] = array[i]
        }
        
        array = n
    }
    
    func setAt(i notnull, val) 
    {
        array[i] = val
    }
    
    func get(i notnull) 
    {
        if i is not number | i > count() - 1 | i < 0
        {
            throw new Exception ( "Argument out of range." ) 
        }
        return array[i] 
    }
    
    func first(cond) 
    {
        if cond is not function
        {
            throw new Exception("This function takes a function as parameter.") 
        }
        foreach item in array
        {
            if cond(item) = true
            {
                return item
            }
        }
    }
    
    func findAll(cond) 
    {
        if cond is not function
        {
            throw new Exception ("This function takes a function as parameter.") 
        }
        const all = new List() 
        foreach item in array
        {
            if cond(item) = true
            {
                all.add(item) 
            }
        }
        return all
    }
    
    func count() 
    {
        return lenof array
    }
        
    func toString()
    {
        var s = "["
        foreach v in array : counter i
        {
            s = s + v
            if i < count ( ) - 1
            {
                s = s + ", "
            }
        }
        return s + "]"
    }
    
    func print()
    {
        print toString()
    }
}

class Date (year, month, day, hour, minute)
{
    constructor(y, m, d, h, min)
    {
        year = y
        month = m
        day = d
        hour = h
        minute = min
    }
    
    constructor()
    {
        
    }
    
    func setToNow()
    {
        // BUILTIN FUNCTION
    }
    
    static func now ()
    {
        const n = new Date()
        n.setToNow()
        return n
    }
    
    func toString ()
    {
        var h = hour
        if (h < 10)
        {
            h = "0" + h
        }
        var m = minute
        if (m < 10)
        {
            m = "0" + m
        }
        return day + "." + month + "." + year + " " + h + ":" + m
    }
    
    func print()
    {
        print toString()
    }
}
```

2. save the code you want to run in another file, e.g. debug.txt. This file must start with `#include system`. For example:
   ```cs
   #include system
   print "Setup is complete (" + Date.now().toString() + ").\n"
   ```

3. Add program.cs looks like this:
```cs
using Exp;

public static class Program
{
    public static void Main(string[] args)
    {
        var system = ScriptDocument.FromFile(@"C:\path_to\system.txt");
        var code = ScriptDocument.FromFile(@"C:\path_to\debug.txt");
        Console.WriteLine(code.Script);

        Compiler compiler = new Compiler(code, system);

        try
        {
            Console.WriteLine("\n-------- Compiler START -------------------------\n");
            compiler.Run();
            Console.WriteLine("\n\n-------- Compiler END ---------------------------\n\n");

            bool f = true;
            while (f)
            {
                compiler.Run(Console.ReadLine());
                Console.WriteLine();
            }
        }
        catch (ExpException ex)
        {
            Console.WriteLine($"\n\nExp Error ({ex.Line}, {ex.Col}): " + ex.Message);
        }
    }
}
```



I didn't write any other documentation for this project so i'm aware of that it will be really hard to get in to this project, but feel free to ask any question, as many as you have.
