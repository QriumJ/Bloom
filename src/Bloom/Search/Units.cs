using System.Collections.Generic;

namespace Bloom.Search;

internal sealed class UnitDef
{
    public string Name = "", Dim = "", Label = "";
    public double Factor = 1;
}

internal static class Units
{
    private static readonly Dictionary<string, UnitDef> Map = new();
    private static readonly Dictionary<string, string> CurrencyMap = new();

    static Units()
    {
        Add("length", 1e-3, "mm", "mm", "millimeter", "밀리미터", "밀리");
        Add("length", 1e-2, "cm", "cm", "centimeter", "센티미터", "센티");
        Add("length", 1, "m", "m", "meter", "meters", "미터");
        Add("length", 1000, "km", "km", "kilometer", "kilometers", "킬로미터", "키로미터");
        Add("length", 0.0254, "in", "in", "inch", "inches", "인치", "\"");
        Add("length", 0.3048, "ft", "ft", "foot", "feet", "피트");
        Add("length", 0.9144, "yd", "yd", "yard", "yards", "야드");
        Add("length", 1609.344, "mi", "mi", "mile", "miles", "마일");
        Add("length", 1852, "nmi", "nmi", "해리");
        Add("mass", 1e-3, "mg", "mg", "milligram", "밀리그램");
        Add("mass", 1, "g", "g", "gram", "grams", "그램");
        Add("mass", 1000, "kg", "kg", "kilogram", "kilograms", "킬로그램", "킬로");
        Add("mass", 1e6, "t", "t", "ton", "tonne", "톤");
        Add("mass", 453.59237, "lb", "lb", "lbs", "pound", "pounds", "파운드");
        Add("mass", 28.349523125, "oz", "oz", "ounce", "ounces", "온스");
        Add("mass", 600, "근", "근");
        Add("mass", 3.75, "돈", "돈");
        Add("temp", 1, "c", "°C", "°c", "celsius", "섭씨", "도씨");
        Add("temp", 1, "f", "°F", "°f", "fahrenheit", "화씨");
        Add("temp", 1, "k", "K", "kelvin", "켈빈");
        Add("data", 1.0 / 8, "bit", "bit", "bits", "비트");
        Add("data", 1, "b", "B", "byte", "bytes", "바이트");
        Add("data", 1024, "kb", "KB", "kib", "kilobyte", "킬로바이트");
        Add("data", 1024.0 * 1024, "mb", "MB", "mib", "megabyte", "메가바이트", "메가");
        Add("data", 1024.0 * 1024 * 1024, "gb", "GB", "gib", "gigabyte", "기가바이트", "기가");
        Add("data", 1024.0 * 1024 * 1024 * 1024, "tb", "TB", "tib", "terabyte", "테라바이트", "테라");
        Add("time", 1e-3, "ms", "ms", "millisecond", "밀리초");
        Add("time", 1, "s", "s", "sec", "second", "seconds", "초");
        Add("time", 60, "min", "min", "minute", "minutes", "분");
        Add("time", 3600, "h", "h", "hr", "hour", "hours", "시간");
        Add("time", 86400, "d", "일", "day", "days", "일");
        Add("time", 604800, "week", "주", "weeks", "wk", "주");
        Add("time", 2629746, "month", "개월", "months", "개월", "달");
        Add("time", 31556952, "year", "년", "years", "yr", "년");
        Add("speed", 1, "m/s", "m/s", "mps");
        Add("speed", 1 / 3.6, "km/h", "km/h", "kmh", "kph", "시속");
        Add("speed", 0.44704, "mph", "mph");
        Add("speed", 0.514444, "knot", "kn", "knots", "노트");
        Add("area", 1, "m2", "m²", "m²", "sqm", "제곱미터", "㎡");
        Add("area", 1e-4, "cm2", "cm²", "cm²");
        Add("area", 1e6, "km2", "km²", "km²");
        Add("area", 1e4, "ha", "ha", "hectare", "헥타르");
        Add("area", 4046.8564224, "acre", "acre", "acres", "에이커");
        Add("area", 0.09290304, "ft2", "ft²", "ft²", "sqft");
        Add("area", 400.0 / 121, "평", "평");
        Add("volume", 1e-3, "ml", "mL", "milliliter", "밀리리터", "미리");
        Add("volume", 1, "l", "L", "liter", "liters", "litre", "리터");
        Add("volume", 3.785411784, "gal", "gal", "gallon", "gallons", "갤런");
        Add("volume", 0.0295735295625, "floz", "fl oz");
        Add("volume", 0.2365882365, "cup", "cup", "cups", "컵");

        Cur("USD", "usd", "$", "달러", "불", "dollar", "dollars", "미국달러");
        Cur("KRW", "krw", "₩", "원", "won", "원화");
        Cur("JPY", "jpy", "¥", "엔", "yen", "엔화");
        Cur("EUR", "eur", "€", "유로", "euro", "euros");
        Cur("CNY", "cny", "위안", "yuan", "rmb", "위안화");
        Cur("GBP", "gbp", "£", "파운드화");
        Cur("HKD", "hkd", "홍콩달러");
        Cur("TWD", "twd", "대만달러");
        Cur("AUD", "aud", "호주달러");
        Cur("CAD", "cad", "캐나다달러");
        Cur("CHF", "chf", "프랑");
        Cur("SGD", "sgd", "싱가포르달러");
        Cur("THB", "thb", "바트");
        Cur("VND", "vnd", "동");
        Cur("PHP", "php", "페소");
        Cur("INR", "inr", "루피");
        Cur("RUB", "rub", "루블");
        Cur("MXN", "mxn");
        Cur("NZD", "nzd");
    }

    private static void Add(string dim, double factor, string name, string label, params string[] aliases)
    {
        var u = new UnitDef { Name = name, Dim = dim, Factor = factor, Label = label };
        Map[name] = u;
        foreach (string a in aliases) Map[a.ToLowerInvariant()] = u;
    }

    private static void Cur(string code, params string[] aliases)
    {
        foreach (string a in aliases) CurrencyMap[a.ToLowerInvariant()] = code;
    }

    public static UnitDef? Find(string s) => Map.TryGetValue(s.ToLowerInvariant().Replace(" ", ""), out var u) ? u : null;

    public static string? Currency(string s)
    {
        string k = s.ToLowerInvariant().Trim();
        if (CurrencyMap.TryGetValue(k, out string? c)) return c;
        if (k.Length == 3 && Rates.Known(k.ToUpperInvariant())) return k.ToUpperInvariant();
        return null;
    }

    public static string Symbol(string code) => code switch { "USD" => "$", "KRW" => "₩", "JPY" => "¥", "EUR" => "€", "GBP" => "£", _ => "" };

    public static string DimName(string dim) => dim switch
    {
        "length" => Tr("길이", "Length"),
        "mass" => Tr("무게", "Weight"),
        "temp" => Tr("온도", "Temperature"),
        "data" => Tr("데이터 (1 KB = 1024 B)", "Data (1 KB = 1024 B)"),
        "time" => Tr("시간", "Time"),
        "speed" => Tr("속도", "Speed"),
        "area" => Tr("넓이", "Area"),
        "volume" => Tr("부피", "Volume"),
        _ => dim,
    };

    public static double Temp(double v, string from, string to)
    {
        double c = from switch { "f" => (v - 32) * 5 / 9, "k" => v - 273.15, _ => v };
        return to switch { "f" => c * 9 / 5 + 32, "k" => c + 273.15, _ => c };
    }
}
