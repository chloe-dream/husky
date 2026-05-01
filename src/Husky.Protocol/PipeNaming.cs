namespace Husky.Protocol;

public static class PipeNaming
{
    public const string Prefix = "husky-";

    public static string Generate() => $"{Prefix}{Guid.NewGuid():D}";
}
