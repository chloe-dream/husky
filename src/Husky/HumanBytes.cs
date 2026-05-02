using System.Globalization;

namespace Husky;

internal static class HumanBytes
{
    public static string Format(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        int i = -1;
        do
        {
            value /= 1024;
            i++;
        } while (value >= 1024 && i < units.Length - 1);

        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[i];
    }
}
