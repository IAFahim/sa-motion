using System.Text;

namespace SA.Balance.Tuning;

static class TuningReport
{
    public static string Format(BalanceParams p)
    {
        var text = new StringBuilder();
        text.AppendLine("name,value");
        for (var i = 0; i < ParamSpace.Count; i++)
            text.AppendLine(ParamSpace.Name(i) + "," + ParamSpace.Get(in p, i).ToString("R"));
        return text.ToString();
    }
}
