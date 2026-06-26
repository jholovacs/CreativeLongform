using CreativeLongform.Application.Agent;
using CreativeLongform.Application.Generation;
using CreativeLongform.Application.Options;
using CreativeLongform.Domain.Entities;

namespace CreativeLongform.Application.Tests;

public sealed class AgentSessionFactoryTests
{
    private static AgentBookContext EmptyBookContext()
    {
        var bookId = Guid.NewGuid();
        var book = new Book { Id = bookId, Title = "Test", Synopsis = "" };
        var chapter = new Chapter { Id = Guid.NewGuid(), BookId = bookId, Book = book, Order = 0 };
        var scene = new Scene { Id = Guid.NewGuid(), ChapterId = chapter.Id, Chapter = chapter, Title = "S1", Order = 0 };
        return new AgentBookContext(
            AgentLoreCatalog.Create(book, [], [], [], []),
            AgentSceneContextCatalog.Create(book, scene, [scene]));
    }

    [Fact]
    public void Build_pipeline_and_correction_share_verification_when_quality_enabled()
    {
        var opts = new OllamaOptions { QualityGateEnabled = true };
        var bookContext = EmptyBookContext();

        var pipeline = AgentSessionFactory.Build(new AgentSessionBuildRequest
        {
            Kind = AgentSessionKind.PipelinePostDraft,
            OllamaOptions = opts,
            BookContext = bookContext,
            BookDirectiveBlock = "book",
            SceneInstructionsBlock = "Scene brief.",
            ParagraphCount = 3,
            StateBeforeJson = "{}",
            AuthorizedCastBlock = "",
            QualityReviewMinScore = 55,
            SkipQualityGate = false,
            Delegates = EmptyDelegates()
        });

        var correction = AgentSessionFactory.Build(new AgentSessionBuildRequest
        {
            Kind = AgentSessionKind.AuthorCorrection,
            OllamaOptions = opts,
            BookContext = bookContext,
            BookDirectiveBlock = "book",
            SceneInstructionsBlock = "Scene brief.",
            ParagraphCount = 3,
            StateBeforeJson = "{}",
            AuthorizedCastBlock = "",
            QualityReviewMinScore = 55,
            UserCorrectionMission = "Sharpen dialogue",
            Delegates = EmptyDelegates()
        });

        Assert.Equal(AgentSessionKind.PipelinePostDraft, pipeline.SessionKind);
        Assert.Equal(AgentSessionKind.AuthorCorrection, correction.SessionKind);
        Assert.True(pipeline.RequireQualityBeforeFinish);
        Assert.True(correction.RequireQualityBeforeFinish);
        Assert.NotNull(pipeline.RunQualityAsync);
        Assert.NotNull(correction.RunQualityAsync);
        Assert.NotNull(pipeline.RunComplianceAsync);
        Assert.Equal("Sharpen dialogue", correction.UserCorrectionMission);
    }

    [Fact]
    public void Build_skips_quality_when_gate_disabled_or_run_skips()
    {
        var bookContext = EmptyBookContext();

        var disabled = AgentSessionFactory.Build(new AgentSessionBuildRequest
        {
            Kind = AgentSessionKind.PipelinePostDraft,
            OllamaOptions = new OllamaOptions { QualityGateEnabled = false },
            BookContext = bookContext,
            BookDirectiveBlock = "",
            SceneInstructionsBlock = "",
            ParagraphCount = 1,
            StateBeforeJson = "{}",
            AuthorizedCastBlock = "",
            Delegates = EmptyDelegates()
        });

        var skipped = AgentSessionFactory.Build(new AgentSessionBuildRequest
        {
            Kind = AgentSessionKind.PipelinePostDraft,
            OllamaOptions = new OllamaOptions { QualityGateEnabled = true },
            BookContext = bookContext,
            BookDirectiveBlock = "",
            SceneInstructionsBlock = "",
            ParagraphCount = 1,
            StateBeforeJson = "{}",
            AuthorizedCastBlock = "",
            SkipQualityGate = true,
            Delegates = EmptyDelegates()
        });

        Assert.Null(disabled.RunQualityAsync);
        Assert.False(disabled.RequireQualityBeforeFinish);
        Assert.Null(skipped.RunQualityAsync);
        Assert.False(skipped.RequireQualityBeforeFinish);
    }

    [Fact]
    public void ComputeMaxTurns_scales_with_paragraph_count()
    {
        var opts = new OllamaOptions { AgenticEditMaxTurns = 16 };
        Assert.Equal(16, AgentSessionFactory.ComputeMaxTurns(opts, 5));
        Assert.True(AgentSessionFactory.ComputeMaxTurns(opts, 20) > 16);
    }

    [Fact]
    public void Build_propagates_word_targets()
    {
        var bookContext = EmptyBookContext();
        var session = AgentSessionFactory.Build(new AgentSessionBuildRequest
        {
            Kind = AgentSessionKind.PipelinePostDraft,
            OllamaOptions = new OllamaOptions(),
            BookContext = bookContext,
            BookDirectiveBlock = "",
            SceneInstructionsBlock = "",
            ParagraphCount = 5,
            StateBeforeJson = "{}",
            AuthorizedCastBlock = "",
            MinWordsTarget = 1200,
            MaxWordsTarget = 1800,
            Delegates = EmptyDelegates()
        });

        Assert.Equal(1200, session.MinWordsTarget);
        Assert.Equal(1800, session.MaxWordsTarget);
    }

    private static AgentSessionDelegates EmptyDelegates() => new()
    {
        RunComplianceAsync = (_, _) => Task.FromResult(new ComplianceVerdict { Pass = true }),
        RunQualityAsync = (_, _) => Task.FromResult(new QualityVerdict { Score = 80 }),
        InvokeWriterAsync = (_, _) => Task.FromResult("ok"),
        InvokeEditorAsync = (_, _) => Task.FromResult("ok"),
        InvokeCorrectorAsync = (_, _) => Task.FromResult("ok")
    };
}
