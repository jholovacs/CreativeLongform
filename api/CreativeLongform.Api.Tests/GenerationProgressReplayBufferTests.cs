using CreativeLongform.Api.Services;

namespace CreativeLongform.Api.Tests;

public sealed class GenerationProgressReplayBufferTests
{
    [Fact]
    public void GetReplay_returns_events_recorded_before_join()
    {
        var runId = Guid.NewGuid();
        var buffer = new GenerationProgressReplayBuffer();
        var payload = new { runId, step = "PreState", detail = "Starting", elapsedMs = 0L };

        buffer.Record(runId, "StepStarted", payload);
        buffer.Record(runId, "LlmStarted", payload);

        var replay = buffer.GetReplay(runId);
        Assert.Equal(2, replay.Count);
        Assert.Equal("StepStarted", replay[0].EventName);
        Assert.Equal("LlmStarted", replay[1].EventName);
    }

    [Fact]
    public void RemoveRun_clears_replay_after_finish()
    {
        var runId = Guid.NewGuid();
        var buffer = new GenerationProgressReplayBuffer();
        buffer.Record(runId, "RunFinished", new { runId, step = "Succeeded" });
        buffer.RemoveRun(runId);

        Assert.Empty(buffer.GetReplay(runId));
    }
}
