using OneNoteMcp.Core.Markdown;
using System.Text.RegularExpressions;

namespace OneNoteMcp.Tests;

[TestFixture]
public class RoundTripTests
{
    private static string RoundTrip(string markdown) =>
        OneNoteXmlParser.ToMarkdown(
            MarkdownToOneNoteXml.BuildPageXml("{ID}{1}{B0}", "Doc", markdown, PageXml.Agent),
            includeFrontMatter: false);

    private static string Body(string markdown)
    {
        string md = RoundTrip(markdown);
        string withoutTitle = Regex.Replace(md, @"\A# Doc\n+", string.Empty);
        return Regex.Replace(withoutTitle, @"\n{2,}", "\n\n").Trim();
    }

    [Test]
    public void Paragraph_survives()
    {
        Assert.That(Body("Just a sentence."), Is.EqualTo("Just a sentence."));
    }

    [Test]
    public void Inline_emphasis_survives()
    {
        Assert.That(
            Body("Some **bold** and *italic* and ~~struck~~ text."),
            Is.EqualTo("Some **bold** and *italic* and ~~struck~~ text."));
    }

    [Test]
    public void Inline_code_survives()
    {
        Assert.That(Body("call `Foo()` now"), Is.EqualTo("call `Foo()` now"));
    }

    [Test]
    public void Link_survives()
    {
        Assert.That(
            Body("see [example](https://example.com) please"),
            Is.EqualTo("see [example](https://example.com) please"));
    }

    [TestCase("# One")]
    [TestCase("## Two")]
    [TestCase("### Three")]
    [TestCase("#### Four")]
    public void Heading_level_is_preserved(string markdown)
    {
        Assert.That(Body(markdown), Is.EqualTo(markdown));
    }

    [Test]
    public void Flat_bullet_list_survives()
    {
        Assert.That(Body("- one\n- two\n- three"), Is.EqualTo("- one\n- two\n- three"));
    }

    [Test]
    public void Nested_bullet_list_keeps_its_indentation()
    {
        Assert.That(Body("- one\n  - two\n    - three"), Is.EqualTo("- one\n  - two\n    - three"));
    }

    [Test]
    public void Numbered_list_survives()
    {
        Assert.That(Body("1. one\n2. two"), Is.EqualTo("1. one\n2. two"));
    }

    [Test]
    public void Task_list_survives_with_completion_state()
    {
        Assert.That(Body("- [ ] open\n- [x] shut"), Is.EqualTo("- [ ] open\n- [x] shut"));
    }

    [Test]
    public void Table_survives()
    {
        const string table = "| a | b |\n| --- | --- |\n| 1 | 2 |";
        Assert.That(Body(table), Is.EqualTo(table));
    }

    [Test]
    public void Code_block_content_survives_even_though_the_fence_does_not()
    {
        string body = Body("```cs\nvar x = 1;\nvar y = 2;\n```");

        Assert.That(body, Does.Contain("var x = 1;"));
        Assert.That(body, Does.Contain("var y = 2;"));
    }

    [Test]
    public void A_paragraph_after_a_list_is_not_absorbed_into_it()
    {
        Assert.That(Body("- item\n\nAfter the list."), Is.EqualTo("- item\n\nAfter the list."));
    }

    [Test]
    public void A_paragraph_after_a_code_block_is_not_absorbed_into_it()
    {
        string body = Body("```\ncode\n```\n\nAfter the code.");
        Assert.That(body, Does.Match(@"code\n\nAfter the code\."));
    }

    [Test]
    public void Special_characters_survive_without_corrupting_the_xml()
    {
        string body = Body("a & b < c > d");
        Assert.That(body, Does.Contain("a & b < c > d"));
    }

    [Test]
    public void A_document_using_every_feature_round_trips_without_loss_of_structure()
    {
        const string markdown = """
            Intro with **bold** and a [link](https://example.com).

            ## Section

            - alpha
              - beta

            1. first
            2. second

            - [ ] todo
            - [x] done

            | Language | Year |
            | --- | --- |
            | C# | 2000 |
            """;

        string body = Body(markdown);

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("Intro with **bold** and a [link](https://example.com)."));
            Assert.That(body, Does.Contain("## Section"));
            Assert.That(body, Does.Contain("- alpha"));
            Assert.That(body, Does.Contain("  - beta"));
            Assert.That(body, Does.Contain("1. first"));
            Assert.That(body, Does.Contain("2. second"));
            Assert.That(body, Does.Contain("- [ ] todo"));
            Assert.That(body, Does.Contain("- [x] done"));
            Assert.That(body, Does.Contain("| Language | Year |"));
            Assert.That(body, Does.Contain("| C# | 2000 |"));
        });
    }
}
