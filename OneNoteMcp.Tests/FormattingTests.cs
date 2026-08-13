using OneNoteMcp.Core.Markdown;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OneNoteMcp.Tests;

[TestFixture]
public class FormattingTests
{
    private const string PageId = "{ABC}{1}{B0}";
    private static readonly XNamespace One = PageXml.Ns;

    private static XElement Build(string markdown) =>
        XElement.Parse(MarkdownToOneNoteXml.BuildPageXml(PageId, "T", markdown, PageXml.Agent));

    private static XElement StyleDef(XElement page, string name) =>
        page.Elements(One + "QuickStyleDef")
            .Single(d => (string?)d.Attribute("name") == name);

    [Test]
    public void Body_text_is_Segoe_UI_11()
    {
        XElement paragraph = StyleDef(Build("text"), "p");

        Assert.Multiple(() =>
        {
            Assert.That((string?)paragraph.Attribute("font"), Is.EqualTo("Segoe UI"));
            Assert.That((string?)paragraph.Attribute("fontSize"), Is.EqualTo("11.0"));
        });
    }

    [Test]
    public void Every_style_except_code_uses_Segoe_UI()
    {
        XElement page = Build("# H\n\ntext\n\n> quote\n\n```\ncode\n```");

        foreach (XElement def in page.Elements(One + "QuickStyleDef"))
        {
            string? name = (string?)def.Attribute("name");
            string expected = name == "code" ? "Consolas" : "Segoe UI";

            Assert.That((string?)def.Attribute("font"), Is.EqualTo(expected), $"style '{name}'");
        }
    }

    [Test]
    public void Headings_reserve_a_blank_line_above_themselves()
    {
        List<XElement> headings = [.. Build("# One\n\n## Two\n\n### Three\n\n#### Four\n\n##### Five\n\n###### Six")
            .Element(One + "Outline")!
            .Element(One + "OEChildren")!
            .Elements(One + "OE")];

        Assert.That(headings, Has.Count.EqualTo(6));

        for (int level = 1; level <= 6; level++)
        {
            Assert.That(
                (string?)headings[level - 1].Attribute("spaceBefore"),
                Is.EqualTo("11.0"),
                $"h{level} must carry the leading gap");
        }
    }

    [Test]
    public void No_quick_style_carries_the_gap_itself()
    {
        XElement page = Build("# One\n\ntext\n\n> quote\n\n```\ncode\n```");

        foreach (XElement def in page.Elements(One + "QuickStyleDef"))
        {
            Assert.That(
                (string?)def.Attribute("spaceBefore"),
                Is.EqualTo("0.0"),
                $"style '{(string?)def.Attribute("name")}'");
        }
    }

    [Test]
    public void The_gap_above_a_heading_is_not_an_empty_paragraph()
    {
        XElement page = Build("Intro text.\n\n## Section\n\nBody.");

        List<string> texts = [.. page.Element(One + "Outline")!
            .Element(One + "OEChildren")!
            .Elements(One + "OE")
            .Select(oe => string.Concat(oe.Elements(One + "T").Select(t => t.Value)))];

        Assert.That(texts, Is.EqualTo(["Intro text.", "Section", "Body."]));
    }

    [Test]
    public void Body_and_quote_paragraphs_carry_no_leading_gap()
    {
        IEnumerable<XElement> paragraphs = Build("text\n\n> quote")
            .Element(One + "Outline")!
            .Element(One + "OEChildren")!
            .Elements(One + "OE");

        foreach (XElement oe in paragraphs)
        {
            Assert.That((string?)oe.Attribute("spaceBefore"), Is.Null);
        }
    }

    [TestCase(1, "h1")]
    [TestCase(2, "h2")]
    [TestCase(3, "h3")]
    [TestCase(4, "h4")]
    [TestCase(5, "h5")]
    [TestCase(6, "h6")]
    public void Heading_styles_are_not_bold(int level, string styleName)
    {
        XElement page = Build(new string('#', level) + " Heading");
        Assert.That((string?)StyleDef(page, styleName).Attribute("bold"), Is.EqualTo("false"));
    }

    [Test]
    public void A_top_level_list_sits_one_level_in()
    {
        XElement body = Build("- one\n- two").Element(One + "Outline")!.Element(One + "OEChildren")!;

        XElement carrier = body.Elements(One + "OE").First();
        Assert.That(carrier.Element(One + "List"), Is.Null, "the carrier is not itself a bullet");

        XElement? items = carrier.Element(One + "OEChildren");
        Assert.That(items, Is.Not.Null, "bullets must hang off the carrier");
        Assert.That(items!.Elements(One + "OE").Count(), Is.EqualTo(2));
        Assert.That(
            items.Elements(One + "OE").All(oe => oe.Element(One + "List") is not null),
            Is.True,
            "both items keep their bullet");
    }

    [TestCase("- one\n- two")]
    [TestCase("1. one\n2. two")]
    [TestCase("- [ ] open\n- [x] shut")]
    public void Bullets_numbers_and_todos_are_all_indented(string markdown)
    {
        XElement body = Build(markdown).Element(One + "Outline")!.Element(One + "OEChildren")!;

        Assert.That(body.Elements(One + "OE").First().Element(One + "OEChildren"), Is.Not.Null);
    }

    [TestCase("- one\n- two")]
    [TestCase("1. one\n2. two")]
    [TestCase("- [ ] open\n- [x] shut")]
    public void Every_top_level_list_gains_a_trailing_gap(string markdown)
    {
        XElement body = Build(markdown).Element(One + "Outline")!.Element(One + "OEChildren")!;
        List<XElement> top = [.. body.Elements(One + "OE")];

        Assert.That(top, Has.Count.EqualTo(2), "the carrier plus a trailing blank line");
        Assert.That(top[1].Element(One + "OEChildren"), Is.Null, "the trailing element is a plain blank line");
        Assert.That(string.Concat(top[1].Elements(One + "T").Select(t => t.Value)), Is.Empty);
    }

    [Test]
    public void A_nested_list_gains_no_second_carrier()
    {
        XElement body = Build("- one\n  - two").Element(One + "Outline")!.Element(One + "OEChildren")!;

        XElement items = body.Elements(One + "OE").First().Element(One + "OEChildren")!;
        XElement first = items.Elements(One + "OE").First();
        XElement inner = first.Element(One + "OEChildren")!;

        Assert.That(
            inner.Elements(One + "OE").Single().Element(One + "List"),
            Is.Not.Null,
            "the nested bullet must be the direct child, with no carrier in between");
    }

    [Test]
    public void A_nested_sub_list_gains_no_trailing_gap_of_its_own()
    {
        XElement body = Build("- one\n  - two\n- three").Element(One + "Outline")!.Element(One + "OEChildren")!;

        List<XElement> items = [.. body.Elements(One + "OE").First().Element(One + "OEChildren")!.Elements(One + "OE")];
        Assert.That(items, Has.Count.EqualTo(2), "'one' and 'three', with no blank line spliced between them");
    }

    [Test]
    public void Table_cells_gain_no_carrier_or_spacer()
    {
        XElement page = Build("| h |\n| --- |\n| - item |\n| `code` |");

        foreach (XElement cell in page.Descendants(One + "Cell"))
        {
            List<XElement> oes = [.. cell.Element(One + "OEChildren")!.Elements(One + "OE")];

            Assert.That(oes, Has.Count.EqualTo(1), "one paragraph per cell");
            Assert.That(oes[0].Element(One + "OEChildren"), Is.Null, "no carrier inside a cell");
        }
    }

    [Test]
    public void A_top_level_table_sits_one_level_in()
    {
        XElement body = Build("| a |\n| --- |\n| 1 |").Element(One + "Outline")!.Element(One + "OEChildren")!;

        XElement carrier = body.Elements(One + "OE").Single(oe => oe.Descendants(One + "Table").Any());
        XElement? indented = carrier.Element(One + "OEChildren");

        Assert.That(indented, Is.Not.Null, "the table must hang off the carrier");
        Assert.That(indented!.Elements(One + "OE").Single().Element(One + "Table"), Is.Not.Null);
    }

    [Test]
    public void A_table_is_separated_from_its_surroundings_by_a_blank_line()
    {
        List<XElement> body = [.. Build("Before.\n\n| a |\n| --- |\n| 1 |\n\nAfter.")
            .Element(One + "Outline")!
            .Element(One + "OEChildren")!
            .Elements(One + "OE")];

        List<string> texts = [.. body.Select(oe => string.Concat(oe.Elements(One + "T").Select(t => t.Value)))];

        Assert.That(texts, Is.EqualTo(["Before.", string.Empty, string.Empty, "After."]));

        int tableAt = body.FindIndex(oe => oe.Descendants(One + "Table").Any());
        Assert.Multiple(() =>
        {
            Assert.That(tableAt, Is.EqualTo(1));
            Assert.That(body[tableAt - 1].Descendants(One + "Table"), Is.Empty);
            Assert.That(body[tableAt + 1].Descendants(One + "Table"), Is.Empty);
        });
    }

    [Test]
    public void A_table_directly_after_a_heading_gains_only_one_leading_blank_line()
    {
        List<XElement> body = [.. Build("## Heading\n\n| a |\n| --- |\n| 1 |")
            .Element(One + "Outline")!
            .Element(One + "OEChildren")!
            .Elements(One + "OE")];

        List<string> texts = [.. body.Select(oe => string.Concat(oe.Elements(One + "T").Select(t => t.Value)))];

        Assert.That(texts, Is.EqualTo(["Heading", string.Empty, string.Empty]));
    }

    [Test]
    public void A_table_at_the_start_of_a_block_gains_no_leading_blank_line()
    {
        List<XElement> body = [.. Build("| a |\n| --- |\n| 1 |\n\nAfter.")
            .Element(One + "Outline")!
            .Element(One + "OEChildren")!
            .Elements(One + "OE")];

        List<string> texts = [.. body.Select(oe => string.Concat(oe.Elements(One + "T").Select(t => t.Value)))];

        Assert.That(texts, Is.EqualTo([string.Empty, string.Empty, "After."]));
    }

    [Test]
    public void Code_lines_carry_the_code_quick_style()
    {
        XElement page = Build("```\nfirst\nsecond\n```");
        string? codeIndex = (string?)StyleDef(page, "code").Attribute("index");

        List<string> lines = [.. page.Descendants(One + "OE")
            .Where(oe => (string?)oe.Attribute("quickStyleIndex") == codeIndex)
            .Select(oe => string.Concat(oe.Elements(One + "T").Select(t => t.Value)))];

        Assert.That(lines, Is.EqualTo(["first", "second"]));
    }

    [Test]
    public void Code_is_separated_from_its_surroundings_by_a_blank_line()
    {
        XElement page = Build("Before.\n\n```\ncode\n```\n\nAfter.");
        string? codeIndex = (string?)StyleDef(page, "code").Attribute("index");

        XElement body = page.Element(One + "Outline")!.Element(One + "OEChildren")!;
        List<XElement> oes = [.. body.Elements(One + "OE")];

        List<string> texts = [.. oes.Select(oe => string.Concat(oe.Elements(One + "T").Select(t => t.Value)))];

        Assert.That(texts, Is.EqualTo(["Before.", string.Empty, "code", string.Empty, "After."]));

        int codeAt = oes.FindIndex(oe => (string?)oe.Attribute("quickStyleIndex") == codeIndex);
        Assert.Multiple(() =>
        {
            Assert.That(codeAt, Is.EqualTo(2));
            Assert.That((string?)oes[codeAt - 1].Attribute("quickStyleIndex"), Is.Not.EqualTo(codeIndex));
            Assert.That((string?)oes[codeAt + 1].Attribute("quickStyleIndex"), Is.Not.EqualTo(codeIndex));
        });
    }

    [Test]
    public void Code_at_the_start_of_a_block_gains_no_leading_blank_line()
    {
        XElement body = Build("```\ncode\n```\n\nAfter.")
            .Element(One + "Outline")!
            .Element(One + "OEChildren")!;

        List<string> texts = [.. body.Elements(One + "OE").Select(oe => string.Concat(oe.Elements(One + "T").Select(t => t.Value)))];

        Assert.That(texts, Is.EqualTo(["code", string.Empty, "After."]));
    }

    [TestCase("| a |\n| --- |\n| 1 |\n\n| b |\n| --- |\n| 2 |", 1, "table then table")]
    [TestCase("| a |\n| --- |\n| 1 |\n\n```\ncode\n```", 1, "table then code")]
    [TestCase("```\ncode\n```\n\n| a |\n| --- |\n| 1 |", 1, "code then table")]
    [TestCase("```\none\n```\n\n```\ntwo\n```", 1, "code then code")]
    [TestCase("- item\n\n```\ncode\n```", 1, "list then code")]
    [TestCase("- item\n\n| a |\n| --- |\n| 1 |", 1, "list then table")]
    public void Adjacent_blocks_that_each_want_a_gap_share_a_single_blank_line(string markdown, int expectedBlanksAtSeam, string scenario)
    {
        List<XElement> body = [.. Build(markdown).Element(One + "Outline")!.Element(One + "OEChildren")!.Elements(One + "OE")];

        int blankRun = body
            .SkipWhile(oe => !IsBlank(oe))
            .TakeWhile(IsBlank)
            .Count();

        Assert.That(blankRun, Is.EqualTo(expectedBlanksAtSeam), scenario);

        bool IsBlank(XElement oe) =>
            oe.Element(One + "OEChildren") is null
            && string.Concat(oe.Elements(One + "T").Select(t => t.Value)).Length == 0;
    }

    [Test]
    public void Inline_code_stays_monospace_without_becoming_a_block()
    {
        XElement page = Build("Use `dotnet build` here.");
        string run = page.Descendants(One + "T").Last().Value;

        Assert.That(run, Does.Contain("font-family:Consolas"));
        Assert.That(run, Does.Contain("dotnet build"));
    }

    [TestCase("- one\n- two")]
    [TestCase("- one\n  - two")]
    [TestCase("1. one\n2. two")]
    [TestCase("- [ ] open\n- [x] shut")]
    public void Indented_lists_read_back_unchanged(string markdown)
    {
        Assert.That(BodyOf(markdown), Is.EqualTo(markdown));
    }

    [Test]
    public void A_paragraph_after_an_indented_list_is_still_separated()
    {
        Assert.That(BodyOf("- item\n\nAfter."), Is.EqualTo("- item\n\nAfter."));
    }

    private static string BodyOf(string markdown)
    {
        string md = OneNoteXmlParser.ToMarkdown(
            MarkdownToOneNoteXml.BuildPageXml(PageId, "Doc", markdown, PageXml.Agent),
            includeFrontMatter: false);

        return Regex.Replace(md, @"\A# Doc\n+", string.Empty).Trim();
    }
}
