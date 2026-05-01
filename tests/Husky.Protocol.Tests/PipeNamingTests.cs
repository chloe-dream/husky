using Husky.Protocol;

namespace Husky.Protocol.Tests;

public sealed class PipeNamingTests
{
    [Fact]
    public void Generate_starts_with_prefix()
    {
        string name = PipeNaming.Generate();

        Assert.StartsWith(PipeNaming.Prefix, name);
    }

    [Fact]
    public void Generate_appends_a_36_character_guid()
    {
        string name = PipeNaming.Generate();
        string guidPart = name[PipeNaming.Prefix.Length..];

        Assert.Equal(36, guidPart.Length);
        Assert.True(Guid.TryParseExact(guidPart, "D", out _));
    }

    [Fact]
    public void Generate_uses_lower_case_hex()
    {
        string name = PipeNaming.Generate();
        string guidPart = name[PipeNaming.Prefix.Length..];

        Assert.Equal(guidPart, guidPart.ToLowerInvariant());
    }

    [Fact]
    public void Generate_returns_a_fresh_name_per_call()
    {
        string a = PipeNaming.Generate();
        string b = PipeNaming.Generate();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Environment_variable_names_match_spec()
    {
        Assert.Equal("HUSKY_PIPE", HuskyEnvironment.PipeNameVariable);
        Assert.Equal("HUSKY_APP_NAME", HuskyEnvironment.AppNameVariable);
    }

    [Fact]
    public void Protocol_version_is_one()
    {
        Assert.Equal(1, ProtocolVersion.Current);
    }

    [Fact]
    public void Message_types_match_spec_strings()
    {
        Assert.Equal("hello", MessageTypes.Hello);
        Assert.Equal("welcome", MessageTypes.Welcome);
        Assert.Equal("heartbeat", MessageTypes.Heartbeat);
        Assert.Equal("ping", MessageTypes.Ping);
        Assert.Equal("pong", MessageTypes.Pong);
        Assert.Equal("shutdown", MessageTypes.Shutdown);
        Assert.Equal("shutdown-ack", MessageTypes.ShutdownAck);
        Assert.Equal("shutdown-progress", MessageTypes.ShutdownProgress);
    }
}
