using System.Text.Json;
using Husky.Client;
using Husky.Protocol;

namespace Husky.Client.Tests;

public sealed class HuskyClientUpdateProtocolTests
{
    private static readonly IReadOnlyList<string> ManualCaps = [Capabilities.ManualUpdates, Capabilities.ShutdownProgress];

    [Fact]
    public async Task CheckForUpdateAsync_returns_info_when_status_says_available()
    {
        await using ConnectedHandshake handshake =
            await ConnectedHandshake.PerformAsync(launcherCapabilities: ManualCaps);

        Task<HuskyUpdateInfo?> checkTask = handshake.Client.CheckForUpdateAsync();

        MessageEnvelope? request = await handshake.Harness.ServerReader.ReadAsync();
        Assert.Equal(MessageTypes.UpdateCheck, request!.Type);
        Assert.NotNull(request.Id);

        await SendUpdateStatusAsync(
            handshake.Harness.ServerWriter,
            replyTo: request.Id,
            new UpdateStatusPayload(
                Available: true,
                CurrentVersion: "1.0.0",
                NewVersion: "1.1.0",
                DownloadSizeBytes: 1234567));

        HuskyUpdateInfo? info = await checkTask;
        Assert.NotNull(info);
        Assert.Equal("1.0.0", info!.CurrentVersion);
        Assert.Equal("1.1.0", info.NewVersion);
        Assert.Equal(1234567L, info.DownloadSizeBytes);
    }

    [Fact]
    public async Task CheckForUpdateAsync_returns_null_when_no_update_available()
    {
        await using ConnectedHandshake handshake =
            await ConnectedHandshake.PerformAsync(launcherCapabilities: ManualCaps);

        Task<HuskyUpdateInfo?> checkTask = handshake.Client.CheckForUpdateAsync();

        MessageEnvelope? request = await handshake.Harness.ServerReader.ReadAsync();
        await SendUpdateStatusAsync(
            handshake.Harness.ServerWriter,
            replyTo: request!.Id,
            new UpdateStatusPayload(Available: false, CurrentVersion: "1.0.0"));

        HuskyUpdateInfo? info = await checkTask;
        Assert.Null(info);
    }

    [Fact]
    public async Task CheckForUpdateAsync_throws_when_launcher_lacks_capability()
    {
        // No launcherCapabilities → SupportsManualUpdates is false.
        await using ConnectedHandshake handshake = await ConnectedHandshake.PerformAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await handshake.Client.CheckForUpdateAsync());
    }

    [Fact]
    public async Task RequestUpdateAsync_sends_update_now_message()
    {
        await using ConnectedHandshake handshake =
            await ConnectedHandshake.PerformAsync(launcherCapabilities: ManualCaps);

        await handshake.Client.RequestUpdateAsync();

        MessageEnvelope? envelope = await handshake.Harness.ServerReader.ReadAsync();
        Assert.Equal(MessageTypes.UpdateNow, envelope!.Type);
    }

    [Fact]
    public async Task RequestUpdateAsync_throws_when_launcher_lacks_capability()
    {
        await using ConnectedHandshake handshake = await ConnectedHandshake.PerformAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await handshake.Client.RequestUpdateAsync());
    }

    [Fact]
    public async Task SetUpdateModeAsync_round_trips_through_ack_and_updates_property()
    {
        await using ConnectedHandshake handshake =
            await ConnectedHandshake.PerformAsync(launcherCapabilities: ManualCaps);

        Task switchTask = handshake.Client.SetUpdateModeAsync(HuskyUpdateMode.Manual);

        MessageEnvelope? request = await handshake.Harness.ServerReader.ReadAsync();
        Assert.Equal(MessageTypes.SetUpdateMode, request!.Type);
        SetUpdateModePayload? requestPayload = request.Data!.Value
            .Deserialize(HuskyJsonContext.Default.SetUpdateModePayload);
        Assert.Equal(UpdateModes.Manual, requestPayload!.Mode);

        await SendUpdateModeAckAsync(
            handshake.Harness.ServerWriter,
            replyTo: request.Id,
            mode: UpdateModes.Manual);

        await switchTask;
        Assert.Equal(HuskyUpdateMode.Manual, handshake.Client.UpdateMode);
    }

    [Fact]
    public async Task UpdateAvailable_event_fires_on_unsolicited_push()
    {
        await using ConnectedHandshake handshake =
            await ConnectedHandshake.PerformAsync(launcherCapabilities: ManualCaps);

        TaskCompletionSource<HuskyUpdateInfo> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        handshake.Client.UpdateAvailable += (_, info) => tcs.TrySetResult(info);

        UpdateAvailablePayload payload = new(
            CurrentVersion: "1.0.0",
            NewVersion: "1.2.0",
            DownloadSizeBytes: 99_999);
        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.UpdateAvailablePayload);

        await handshake.Harness.ServerWriter.WriteAsync(new MessageEnvelope
        {
            Type = MessageTypes.UpdateAvailable,
            Data = data,
        });

        HuskyUpdateInfo info = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("1.2.0", info.NewVersion);
        Assert.Equal(99_999L, info.DownloadSizeBytes);
    }

    private static async Task SendUpdateStatusAsync(
        MessageWriter writer, string? replyTo, UpdateStatusPayload payload)
    {
        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.UpdateStatusPayload);
        await writer.WriteAsync(new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString("D"),
            ReplyTo = replyTo,
            Type = MessageTypes.UpdateStatus,
            Data = data,
        });
    }

    private static async Task SendUpdateModeAckAsync(
        MessageWriter writer, string? replyTo, string mode)
    {
        UpdateModeAckPayload payload = new(Mode: mode);
        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.UpdateModeAckPayload);
        await writer.WriteAsync(new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString("D"),
            ReplyTo = replyTo,
            Type = MessageTypes.UpdateModeAck,
            Data = data,
        });
    }
}
