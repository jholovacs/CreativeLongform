using CreativeLongform.Application.Manuscript;
using CreativeLongform.Domain.Entities;

namespace CreativeLongform.Application.Tests;

/// <summary>
/// Tests for <see cref="ManuscriptAssembly"/>: building chapter- and book-level prose from finalized scene <see cref="Scene.ManuscriptText"/>.
/// </summary>
public sealed class ManuscriptAssemblyTests
{
    /// <summary>
    /// <para><b>System under test:</b> <see cref="ManuscriptAssembly.AssembleChapter"/>.</para>
    /// <para><b>Test case:</b> Two ordered scenes with titles and non-empty manuscript bodies.</para>
    /// <para><b>Expected result:</b> Output contains markdown scene headings (<c>## Title</c>) and both bodies.</para>
    /// <para><b>Why it matters:</b> Chapter (and book) manuscript export must preserve scene boundaries and readable structure for editing.</para>
    /// </summary>
    [Fact]
    public void AssembleChapter_Joins_Scenes_With_Headings()
    {
        var scenes = new[]
        {
            new Scene { Order = 1, Title = "Open", ManuscriptText = "Alpha." },
            new Scene { Order = 2, Title = "Turn", ManuscriptText = "Beta." }
        };
        var s = ManuscriptAssembly.AssembleChapter(scenes);
        Assert.Contains("## Open", s);
        Assert.Contains("Alpha.", s);
        Assert.Contains("## Turn", s);
        Assert.Contains("Beta.", s);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="ManuscriptAssembly.AssembleBook"/>.</para>
    /// <para><b>Test case:</b> One chapter containing one scene with manuscript text.</para>
    /// <para><b>Expected result:</b> Output contains chapter heading (<c># Chapter title</c>), scene heading, and body text.</para>
    /// <para><b>Why it matters:</b> Full-book assembly must nest chapter sections so authors can navigate long exports.</para>
    /// </summary>
    [Fact]
    public void AssembleBook_Nests_Chapters()
    {
        var ch = new Chapter
        {
            Order = 1,
            Title = "One",
            Scenes = new List<Scene>
            {
                new() { Order = 1, Title = "A", ManuscriptText = "x" }
            }
        };
        var book = ManuscriptAssembly.AssembleBook(new[] { ch });
        Assert.Contains("# One", book);
        Assert.Contains("## A", book);
        Assert.Contains("x", book);
    }
}
