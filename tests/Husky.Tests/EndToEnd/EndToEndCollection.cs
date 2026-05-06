namespace Husky.Tests.EndToEnd;

/// <summary>
/// xUnit collection that serialises all E2E tests. Each end-to-end test
/// boots a real launcher process, a FakeHttpServer, and a TestApp child;
/// running several of them in parallel with the rest of the suite chews
/// up enough CPU and disk that the launcher's 60-second boot-poll-plus-
/// extract-plus-start budget can blow under contention. Within this
/// collection xUnit runs the tests serially (one at a time) — they still
/// run in parallel with tests from *other* collections.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EndToEndCollection
{
    public const string Name = "EndToEnd";
}
