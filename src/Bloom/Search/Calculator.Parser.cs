using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Bloom.Core;

namespace Bloom.Search;

/// The calculator's expression parser (numbers, Korean amounts, operators, functions, units).
internal static partial class Calculator
{
    // ── parser ──────────────────────────────────────────────────────────────────────────────────
    private enum T { Num, Ident, Op, End }

    private readonly struct Tok
    {
        public readonly T Type;
        public readonly double Num;
        public readonly string S;
        public readonly bool Korean, BaseLit;

        public Tok(T type, double num = 0, string s = "", bool korean = false, bool baseLit = false)
        {
            Type = type;
            Num = num;
            S = s;
            Korean = korean;
            BaseLit = baseLit;
        }

        public bool Is(string op) => Type == T.Op && S == op;
    }

    private struct V
    {
        public double X;
        public bool Pct;
    }

    private sealed class Parser
    {
        private readonly List<Tok> toks;
        private int i;
        private bool interesting;
        public bool IntegerLiteral;

        public Parser(string s) => toks = Lex(s, out IntegerLiteral);

        public bool Parse(out double value, out string? unit, out bool isInteresting)
        {
            value = 0;
            unit = null;
            isInteresting = false;
            if (toks.Count == 0) return false;
            // a leading currency symbol: "$100"
            if (toks[0].Type == T.Ident && Units.Currency(toks[0].S) is not null && toks.Count > 1 && toks[1].Type != T.Ident)
            {
                unit = toks[0].S;
                i = 1;
            }
            V v = Expr();
            // a trailing unit or currency: "10 km", "100 usd", "5만원"
            if (Peek.Type == T.Ident && unit is null)
            {
                string u = Peek.S;
                if (i + 1 < toks.Count && toks[i + 1].Type == T.Ident && Units.Find(u + toks[i + 1].S) is not null)
                {
                    u += toks[i + 1].S;
                    i++;
                }
                if (Units.Find(u) is not null || Units.Currency(u) is not null)
                {
                    unit = u;
                    i++;
                }
            }
            if (Peek.Type != T.End) return false;
            value = v.Pct ? v.X / 100 : v.X;
            isInteresting = interesting || unit is not null;
            return !double.IsNaN(value) || interesting;
        }

        private Tok Peek => i < toks.Count ? toks[i] : new Tok(T.End);

        private V Expr()
        {
            V a = Term();
            while (Peek.Is("+") || Peek.Is("-"))
            {
                bool plus = Peek.Is("+");
                i++;
                interesting = true;
                V b = Term();
                double bx = b.Pct ? a.X * b.X / 100 : b.X;
                a = new V { X = plus ? a.X + bx : a.X - bx };
            }
            return a;
        }

        private bool StartsOperand()
        {
            var t = Peek;
            if (t.Type == T.Num || t.Is("(")) return true;
            if (t.Type != T.Ident) return false;
            if (Consts.ContainsKey(t.S)) return true;
            // a function only when called ("min(…)", "sqrt 16"); a bare "min" is the unit
            if (!Funcs.ContainsKey(t.S) || i + 1 >= toks.Count) return false;
            var next = toks[i + 1];
            return next.Is("(") || next.Type == T.Num;
        }

        private V Term()
        {
            V a = Unary();
            while (true)
            {
                string? op = null;
                if (Peek.Is("*") || Peek.Is("/")) op = Peek.S;
                else if (Peek.Type == T.Ident && Peek.S is "mod" or "의" or "of") op = Peek.S;
                if (op is not null)
                {
                    i++;
                    interesting = true;
                    V b = Unary();
                    double bx = b.Pct ? b.X / 100 : b.X;
                    double ax = a.Pct ? a.X / 100 : a.X;
                    a = op switch
                    {
                        "/" => new V { X = ax / bx },
                        "mod" => new V { X = ax % bx },
                        _ => new V { X = ax * bx },
                    };
                }
                else if (StartsOperand())
                {
                    interesting = true; // implicit multiplication: 2pi, 3(4+5)
                    V b = Unary();
                    a = new V { X = (a.Pct ? a.X / 100 : a.X) * (b.Pct ? b.X / 100 : b.X) };
                }
                else break;
            }
            return a;
        }

        private V Unary()
        {
            if (Peek.Is("-"))
            {
                i++;
                V v = Unary();
                return new V { X = -v.X, Pct = v.Pct };
            }
            if (Peek.Is("+"))
            {
                i++;
                return Unary();
            }
            return Power();
        }

        private V Power()
        {
            V b = Postfix();
            if (Peek.Is("^"))
            {
                i++;
                interesting = true;
                V e = Unary();
                return new V { X = Math.Pow(b.Pct ? b.X / 100 : b.X, e.Pct ? e.X / 100 : e.X) };
            }
            return b;
        }

        private V Postfix()
        {
            V v = Primary();
            while (true)
            {
                if (Peek.Is("!"))
                {
                    i++;
                    interesting = true;
                    v = new V { X = Factorial(v.X) };
                }
                else if (Peek.Is("%"))
                {
                    i++;
                    interesting = true;
                    v = new V { X = v.X, Pct = true };
                }
                else if (Peek.Type == T.Ident && Peek.S is "°" or "deg" or "도")
                {
                    i++;
                    v = new V { X = v.X * Math.PI / 180 };
                }
                else return v;
            }
        }

        private V Primary()
        {
            var t = Peek;
            if (t.Type == T.Num)
            {
                i++;
                if (t.Korean || t.BaseLit) interesting = true;
                return new V { X = t.Num };
            }
            if (t.Is("("))
            {
                i++;
                V v = Expr();
                if (Peek.Is(")")) i++;
                interesting = true;
                return v;
            }
            if (t.Type == T.Ident)
            {
                if (Consts.TryGetValue(t.S, out double c))
                {
                    i++;
                    interesting = true;
                    return new V { X = c };
                }
                if (Funcs.TryGetValue(t.S, out var f))
                {
                    i++;
                    interesting = true;
                    var args = new List<double>();
                    if (Peek.Is("("))
                    {
                        i++;
                        if (!Peek.Is(")"))
                        {
                            while (true)
                            {
                                V a = Expr();
                                args.Add(a.Pct ? a.X / 100 : a.X);
                                if (Peek.Is(",")) i++;
                                else break;
                            }
                        }
                        if (Peek.Is(")")) i++;
                    }
                    else
                    {
                        V a = Power(); // "sqrt 16"
                        args.Add(a.X);
                    }
                    return new V { X = f(args) };
                }
            }
            throw new FormatException("operand expected");
        }

        private static double Factorial(double x)
        {
            if (x < 0 || x > 170 || Math.Abs(x - Math.Round(x)) > 1e-9) return double.NaN;
            double r = 1;
            for (int k = 2; k <= (int)Math.Round(x); k++) r *= k;
            return r;
        }

        private static readonly Dictionary<string, double> Consts = new()
        {
            ["pi"] = Math.PI,
            ["π"] = Math.PI,
            ["e"] = Math.E,
            ["tau"] = Math.Tau,
            ["phi"] = (1 + Math.Sqrt(5)) / 2,
        };

        private static double A(List<double> a, int k) => k < a.Count ? a[k] : double.NaN;

        private static readonly Dictionary<string, Func<List<double>, double>> Funcs = new()
        {
            ["sqrt"] = a => Math.Sqrt(A(a, 0)),
            ["√"] = a => Math.Sqrt(A(a, 0)),
            ["cbrt"] = a => Math.Cbrt(A(a, 0)),
            ["abs"] = a => Math.Abs(A(a, 0)),
            ["round"] = a => a.Count > 1 ? Math.Round(A(a, 0), (int)A(a, 1)) : Math.Round(A(a, 0), MidpointRounding.AwayFromZero),
            ["floor"] = a => Math.Floor(A(a, 0)),
            ["ceil"] = a => Math.Ceiling(A(a, 0)),
            ["ln"] = a => Math.Log(A(a, 0)),
            ["log"] = a => a.Count > 1 ? Math.Log(A(a, 0), A(a, 1)) : Math.Log10(A(a, 0)),
            ["log2"] = a => Math.Log2(A(a, 0)),
            ["exp"] = a => Math.Exp(A(a, 0)),
            ["sin"] = a => Math.Sin(A(a, 0)),
            ["cos"] = a => Math.Cos(A(a, 0)),
            ["tan"] = a => Math.Tan(A(a, 0)),
            ["asin"] = a => Math.Asin(A(a, 0)),
            ["acos"] = a => Math.Acos(A(a, 0)),
            ["atan"] = a => Math.Atan(A(a, 0)),
            ["min"] = a => a.Count == 0 ? double.NaN : Min(a),
            ["max"] = a => a.Count == 0 ? double.NaN : Max(a),
            ["pow"] = a => Math.Pow(A(a, 0), A(a, 1)),
        };

        private static double Min(List<double> a)
        {
            double m = a[0];
            foreach (double x in a) m = Math.Min(m, x);
            return m;
        }

        private static double Max(List<double> a)
        {
            double m = a[0];
            foreach (double x in a) m = Math.Max(m, x);
            return m;
        }

        // ── lexer ──
        private static List<Tok> Lex(string s, out bool integerLiteral)
        {
            integerLiteral = false;
            var list = new List<Tok>();
            var funcDepth = new Stack<bool>();
            int p = 0;
            while (p < s.Length)
            {
                char c = s[p];
                if (char.IsWhiteSpace(c))
                {
                    p++;
                    continue;
                }
                if (c == '0' && p + 1 < s.Length && (s[p + 1] is 'x' or 'X' or 'b' or 'B' or 'o' or 'O'))
                {
                    int radix = char.ToLowerInvariant(s[p + 1]) switch { 'x' => 16, 'b' => 2, _ => 8 };
                    int q = p + 2;
                    while (q < s.Length && Uri.IsHexDigit(s[q])) q++;
                    if (q > p + 2)
                    {
                        list.Add(new Tok(T.Num, System.Convert.ToInt64(s[(p + 2)..q], radix), baseLit: true));
                        integerLiteral = true;
                        p = q;
                        continue;
                    }
                }
                if (char.IsDigit(c) || (c == '.' && p + 1 < s.Length && char.IsDigit(s[p + 1])))
                {
                    double n = ReadNumber(s, ref p, funcDepth.Count > 0 && funcDepth.Peek());
                    int save = p;
                    while (save < s.Length && s[save] == ' ') save++;
                    if (save < s.Length && "천백십만억조".Contains(s[save]))
                    {
                        p = save;
                        list.Add(new Tok(T.Num, ReadKorean(s, ref p, n), korean: true));
                    }
                    else list.Add(new Tok(T.Num, n));
                    continue;
                }
                if ("+-*/^%!(),".Contains(c))
                {
                    if (c == '(')
                    {
                        bool isFunc = list.Count > 0 && list[^1].Type == T.Ident && Funcs.ContainsKey(list[^1].S);
                        funcDepth.Push(isFunc);
                    }
                    else if (c == ')' && funcDepth.Count > 0) funcDepth.Pop();
                    list.Add(new Tok(T.Op, s: c.ToString()));
                    p++;
                    continue;
                }
                if (c is '$' or '€' or '£' or '¥' or '₩')
                {
                    list.Add(new Tok(T.Ident, s: c.ToString()));
                    p++;
                    continue;
                }
                if (c is '°' or 'π' or '√')
                {
                    // °C / °F stay one identifier
                    if (c == '°' && p + 1 < s.Length && char.IsLetter(s[p + 1]))
                    {
                        list.Add(new Tok(T.Ident, s: "°" + char.ToLowerInvariant(s[p + 1])));
                        p += 2;
                        continue;
                    }
                    list.Add(new Tok(T.Ident, s: c.ToString()));
                    p++;
                    continue;
                }
                if (char.IsLetter(c) || c == '²' || c == '_')
                {
                    int q = p;
                    bool hangul = Hangul.IsSyllable(c);
                    while (q < s.Length && (hangul ? Hangul.IsSyllable(s[q]) : char.IsLetterOrDigit(s[q]) && !Hangul.IsSyllable(s[q]) || s[q] is '²' or '_' or '/'))
                    {
                        if (s[q] == '/' && !(q > p && q + 1 < s.Length && char.IsLetter(s[q + 1]) && Units.Find(s[p..q].ToLowerInvariant() + "/" + Word(s, q + 1)) is not null)) break;
                        q++;
                    }
                    string word = s[p..q].ToLowerInvariant();
                    // a Korean postposition stuck to a unit: "100달러를" → 달러 + 를 (ignored)
                    if (hangul && word.Length > 1 && word[^1] is '를' or '을' or '로' or '은' or '는' && Units.Currency(word[..^1]) is not null) word = word[..^1];
                    list.Add(new Tok(T.Ident, s: word));
                    p = q;
                    continue;
                }
                throw new FormatException("unexpected " + c);
            }
            return list;
        }

        private static string Word(string s, int from)
        {
            int q = from;
            while (q < s.Length && char.IsLetter(s[q])) q++;
            return s[from..q].ToLowerInvariant();
        }

        private static double ReadNumber(string s, ref int p, bool inFuncArgs)
        {
            var sb = new StringBuilder();
            while (p < s.Length)
            {
                char c = s[p];
                if (char.IsDigit(c) || c == '.')
                {
                    sb.Append(c);
                    p++;
                }
                else if (c == ',' && !inFuncArgs && IsGroup(s, p))
                {
                    p++; // thousands separator
                }
                else if ((c is 'e' or 'E') && p + 1 < s.Length && (char.IsDigit(s[p + 1]) || (s[p + 1] is '+' or '-' && p + 2 < s.Length && char.IsDigit(s[p + 2]))))
                {
                    sb.Append('e');
                    p++;
                    if (s[p] is '+' or '-') sb.Append(s[p++]);
                    while (p < s.Length && char.IsDigit(s[p])) sb.Append(s[p++]);
                }
                else break;
            }
            return double.Parse(sb.ToString(), NumberStyles.Float, Inv);
        }

        /// ",234" followed by a non-digit (or the end) is a group separator.
        private static bool IsGroup(string s, int comma)
        {
            for (int k = 1; k <= 3; k++)
                if (comma + k >= s.Length || !char.IsDigit(s[comma + k])) return false;
            return comma + 4 >= s.Length || !char.IsDigit(s[comma + 4]);
        }

        /// "3억 5천만", "1.5만", "2천" — digits with Korean place units.
        private static double ReadKorean(string s, ref int p, double first)
        {
            double total = 0, section = 0, current = first;
            while (p < s.Length)
            {
                char u = s[p];
                double small = u switch { '천' => 1000, '백' => 100, '십' => 10, _ => 0 };
                double big = u switch { '만' => 1e4, '억' => 1e8, '조' => 1e12, _ => 0 };
                if (small > 0)
                {
                    section += (current == 0 ? 1 : current) * small;
                    current = 0;
                    p++;
                }
                else if (big > 0)
                {
                    section += current;
                    current = 0;
                    total += (section == 0 ? 1 : section) * big;
                    section = 0;
                    p++;
                }
                else break;
                int q = p;
                while (q < s.Length && s[q] == ' ') q++;
                if (q < s.Length && char.IsDigit(s[q]))
                {
                    p = q;
                    current = ReadNumber(s, ref p, false);
                    int r = p;
                    while (r < s.Length && s[r] == ' ') r++;
                    if (r < s.Length && "천백십만억조".Contains(s[r])) p = r;
                    else break;
                }
                else if (q < s.Length && "천백십만억조".Contains(s[q])) p = q;
                else break;
            }
            return total + section + current;
        }
    }
}
