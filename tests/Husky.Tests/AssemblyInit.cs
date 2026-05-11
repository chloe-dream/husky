using System.Runtime.CompilerServices;

namespace Husky.Tests;

/// <summary>
/// Assembly-level setup that runs once before any test executes.
/// Currently raises the ThreadPool minimum worker / completion-port
/// thread counts so the test process has slack on small CI runners
/// (windows-latest is a 2-vCPU VM, so the default min == CPU count is
/// just 2). End-to-end tests spawn child processes, run FakeHttpServer
/// loops, and drive async pipes simultaneously across multiple xUnit
/// collections — under that load a 2-thread floor lets the ThreadPool's
/// slow-grow heuristic stall HTTP-handler tasks and async I/O
/// continuations for seconds, which surfaced as a download hanging
/// at 0% and as a stderr collection arriving empty.
/// </summary>
internal static class AssemblyInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        ThreadPool.GetMinThreads(out int workers, out int io);
        ThreadPool.SetMinThreads(
            Math.Max(workers, 64),
            Math.Max(io, 64));
    }
}
