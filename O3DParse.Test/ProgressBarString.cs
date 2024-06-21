using System.Text;

namespace O3DParse.Test;

public class ProgressBarString
{
    private readonly string progressChars = "123456789#";
    private readonly int progressLen;
    private readonly StringBuilder sb;

    public ProgressBarString(int length)
    {
        progressLen = length;
        sb = new StringBuilder(length);
        SetProgress(0);
    }

    public void SetProgress(float progress)
    {
        sb.Clear();
        float prog = MathF.Min(MathF.Max(progress, 0), 1) * progressLen;
        for (int p = 0; p < progressLen; p++)
        {
            if (prog >= p + 1)
                sb.Append(progressChars[^1]);
            else if (prog <= p)
                sb.Append(' ');
            else
                sb.Append(progressChars[(int)((prog - MathF.Truncate(prog)) * progressChars.Length)]);
        }
    }

    public void SetProgress(int progress, int max)
    {
        SetProgress(progress / (float)max);
    }

    public override string ToString()
    {
        return sb.ToString();
    }
}
