using System.Net.Http;

namespace Husky.Tests.Fixtures;

public sealed class FakeHttpServerTests
{
    [Fact]
    public async Task Server_serves_files_from_the_root_directory()
    {
        using TempDirectory root = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "hello.txt"), "hi");

        await using FakeHttpServer server = FakeHttpServer.Start(root.Path);

        using HttpClient client = new();
        string body = await client.GetStringAsync(server.Url("hello.txt"));

        Assert.Equal("hi", body);
    }

    [Fact]
    public async Task Server_returns_404_for_unknown_paths()
    {
        using TempDirectory root = TempDirectory.Create();
        await using FakeHttpServer server = FakeHttpServer.Start(root.Path);

        using HttpClient client = new();
        HttpResponseMessage response = await client.GetAsync(server.Url("missing.txt"));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Server_can_run_alongside_other_instances_on_separate_ports()
    {
        using TempDirectory root = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "x.txt"), "ok");

        await using FakeHttpServer a = FakeHttpServer.Start(root.Path);
        await using FakeHttpServer b = FakeHttpServer.Start(root.Path);

        Assert.NotEqual(a.Address.Port, b.Address.Port);

        using HttpClient client = new();
        Assert.Equal("ok", await client.GetStringAsync(a.Url("x.txt")));
        Assert.Equal("ok", await client.GetStringAsync(b.Url("x.txt")));
    }
}
