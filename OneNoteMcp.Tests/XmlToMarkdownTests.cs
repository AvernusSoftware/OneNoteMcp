using OneNoteMcp.Core.Exceptions;
using OneNoteMcp.Core.Markdown;
using System.Xml.Linq;

namespace OneNoteMcp.Tests;

[TestFixture]
public class XmlToMarkdownTests
{
    private static readonly XNamespace One = PageXml.Ns;

    private static string Convert(string body, string? extraDefs = null) =>
        OneNoteXmlParser.ToMarkdown(PageXml.Page(body, extraDefs: extraDefs), includeFrontMatter: false);

    private static string StyleNameAfterRoundTrip(string body)
    {
        string md = System.Text.RegularExpressions.Regex.Replace(Convert(body), @"\A# Test page\n+", string.Empty);
        XElement page = XElement.Parse(MarkdownToOneNoteXml.BuildPageXml("{ID}{1}{B0}", "T", md, PageXml.Agent));

        Dictionary<string, string> styleByIndex = page.Elements(One + "QuickStyleDef")
            .ToDictionary(d => (string)d.Attribute("index")!, d => (string)d.Attribute("name")!);

        XElement oe = page.Element(One + "Outline")!.Descendants(One + "OE")
            .First(o => o.Element(One + "T") is { } t && t.Value.Length > 0);
        return styleByIndex[(string?)oe.Attribute("quickStyleIndex") ?? "0"];
    }

    [Test]
    public void Emits_front_matter_with_id_title_and_timestamps()
    {
        string md = OneNoteXmlParser.ToMarkdown(PageXml.Page(PageXml.Oe("hi"), title: "My notes"));

        Assert.That(md, Does.StartWith("---\n"));
        Assert.That(md, Does.Contain("title: \"My notes\""));
        Assert.That(md, Does.Contain("last_modified: 2026-01-02T11:00:00.000Z"));
        Assert.That(md, Does.Contain("created: 2026-01-01T10:00:00.000Z"));
    }

    [Test]
    public void Renders_page_title_as_top_level_heading()
    {
        string md = Convert(PageXml.Oe("body"));
        Assert.That(md, Does.StartWith("# Test page"));
    }

    [Test]
    public void Plain_text_survives_unchanged()
    {
        Assert.That(Convert(PageXml.Oe("just text")), Does.Contain("just text"));
    }

    [TestCase(1, "h1", "# ")]
    [TestCase(2, "h2", "## ")]
    [TestCase(3, "h3", "### ")]
    public void Heading_level_comes_from_the_quick_style_name(int index, string style, string marker)
    {
        string md = Convert(PageXml.Oe("Section", $"quickStyleIndex=\"{index}\""));

        Assert.That(md, Does.Contain(marker + "Section"), $"style {style} should render as '{marker}'");
    }

    [Test]
    public void Heading_indexes_are_not_assumed_to_be_positional()
    {
        string page = $$"""
            <?xml version="1.0"?>
            <one:Page xmlns:one="{{PageXml.Ns}}" ID="{X}" name="T">
              <one:QuickStyleDef index="42" name="h2" font="Calibri" fontSize="14.0"/>
              <one:Outline><one:OEChildren>
                <one:OE quickStyleIndex="42"><one:T><![CDATA[Deep]]></one:T></one:OE>
              </one:OEChildren></one:Outline>
            </one:Page>
            """;

        Assert.That(OneNoteXmlParser.ToMarkdown(page, includeFrontMatter: false), Does.Contain("## Deep"));
    }

    [TestCase("font-weight:bold", "**bold**")]
    [TestCase("font-style:italic", "*bold*")]
    [TestCase("text-decoration:line-through", "~~bold~~")]
    [TestCase("font-family:Consolas", "`bold`")]
    public void Inline_styles_become_markdown_markers(string style, string expected)
    {
        string md = Convert(PageXml.Oe($"<span style='{style}'>bold</span>"));
        Assert.That(md, Does.Contain(expected));
    }

    [Test]
    public void Underline_falls_back_to_html_since_markdown_has_no_syntax_for_it()
    {
        Assert.That(
            Convert(PageXml.Oe("<span style='text-decoration:underline'>u</span>")),
            Does.Contain("<u>u</u>"));
    }

    [Test]
    public void Nested_spans_combine_their_formatting()
    {
        string md = Convert(PageXml.Oe(
            "<span style='font-weight:bold'><span style='font-style:italic'>x</span></span>"));

        Assert.That(md, Does.Contain("***x***").Or.Contain("**​*x*​**").Or.Contain("**" + "*x*" + "**"));
    }

    [Test]
    public void Unquoted_attribute_values_do_not_break_inline_parsing()
    {
        string md = Convert(PageXml.Oe("<span style='font-weight:bold' lang=en-US>bold</span>"));
        Assert.That(md, Does.Contain("**bold**"));
    }

    [Test]
    public void Links_become_markdown_links()
    {
        string md = Convert(PageXml.Oe("""see <a href="https://example.com">example</a>"""));
        Assert.That(md, Does.Contain("[example](https://example.com)"));
    }

    [Test]
    public void Unclosed_br_is_tolerated_and_becomes_a_line_break()
    {
        string md = Convert(PageXml.Oe("one<br>two"));
        Assert.That(md, Does.Contain("one"));
        Assert.That(md, Does.Contain("two"));
    }

    [Test]
    public void Nbsp_entity_is_decoded_rather_than_breaking_the_parse()
    {
        string md = Convert(PageXml.Oe("a&nbsp;b"));
        Assert.That(md, Does.Contain("a b").Or.Contain("a b"));
    }

    [Test]
    public void Bullets_nest_by_two_spaces_per_level()
    {
        string body = PageXml.Bullet("one", PageXml.Bullet("two", PageXml.Bullet("three")));
        string md = Convert(body);

        Assert.That(md, Does.Contain("- one"));
        Assert.That(md, Does.Contain("  - two"));
        Assert.That(md, Does.Contain("    - three"));
    }

    [Test]
    public void Numbered_lists_are_sequential()
    {
        string md = Convert(PageXml.Number("first") + PageXml.Number("second"));

        Assert.That(md, Does.Contain("1. first"));
        Assert.That(md, Does.Contain("2. second"));
    }

    [Test]
    public void ToDo_tags_become_checkboxes_reflecting_completion()
    {
        string md = Convert(
            PageXml.ToDo("open", completed: false) + PageXml.ToDo("shut", completed: true),
            extraDefs: PageXml.ToDoTagDef);

        Assert.That(md, Does.Contain("- [ ] open"));
        Assert.That(md, Does.Contain("- [x] shut"));
    }

    [Test]
    public void Non_todo_tags_are_surfaced_rather_than_dropped()
    {
        string md = Convert(
            """<one:OE><one:Tag index="1"/><one:T><![CDATA[call Bob]]></one:T></one:OE>""",
            extraDefs: """<one:TagDef index="1" type="12" symbol="17" name="Important"/>""");

        Assert.That(md, Does.Contain("Important"));
        Assert.That(md, Does.Contain("call Bob"));
    }

    [Test]
    public void Table_with_header_row_renders_as_a_github_table()
    {
        string body = $"""
            <one:OE><one:Table bordersVisible="true" hasHeaderRow="true">
              <one:Columns>
                <one:Column index="0" width="100"/><one:Column index="1" width="100"/>
              </one:Columns>
              <one:Row>
                <one:Cell><one:OEChildren>{PageXml.Oe("Language")}</one:OEChildren></one:Cell>
                <one:Cell><one:OEChildren>{PageXml.Oe("Year")}</one:OEChildren></one:Cell>
              </one:Row>
              <one:Row>
                <one:Cell><one:OEChildren>{PageXml.Oe("C#")}</one:OEChildren></one:Cell>
                <one:Cell><one:OEChildren>{PageXml.Oe("2000")}</one:OEChildren></one:Cell>
              </one:Row>
            </one:Table></one:OE>
            """;

        string md = Convert(body);

        Assert.That(md, Does.Contain("| Language | Year |"));
        Assert.That(md, Does.Contain("| --- | --- |"));
        Assert.That(md, Does.Contain("| C# | 2000 |"));
    }

    [Test]
    public void Cell_text_ending_in_r_or_b_is_not_truncated()
    {
        string body = $"""
            <one:OE><one:Table hasHeaderRow="true">
              <one:Columns><one:Column index="0" width="100"/></one:Columns>
              <one:Row><one:Cell><one:OEChildren>{PageXml.Oe("Year")}</one:OEChildren></one:Cell></one:Row>
            </one:Table></one:OE>
            """;

        Assert.That(Convert(body), Does.Contain("| Year |"));
    }

    [Test]
    public void Table_without_header_row_still_gets_one_so_it_renders()
    {
        string body = $"""
            <one:OE><one:Table>
              <one:Columns><one:Column index="0" width="100"/></one:Columns>
              <one:Row><one:Cell><one:OEChildren>{PageXml.Oe("only")}</one:OEChildren></one:Cell></one:Row>
            </one:Table></one:OE>
            """;

        string md = Convert(body);

        Assert.That(md, Does.Contain("| --- |"));
        Assert.That(md, Does.Contain("| only |"));
    }

    [Test]
    public void Images_become_placeholders_carrying_the_callback_id()
    {
        string body = """
            <one:OE><one:Image format="png" alt="diagram">
              <one:CallbackID callbackID="{CB-1}"/>
            </one:Image></one:OE>
            """;

        Assert.That(Convert(body), Does.Contain("![diagram](onenote-object:{CB-1})"));
    }

    [Test]
    public void Ink_is_noted_rather_than_silently_dropped()
    {
        Assert.That(Convert("<one:OE><one:InkDrawing/></one:OE>"), Does.Contain("ink drawing"));
    }

    [Test]
    public void Attachments_become_placeholders_carrying_their_file_name()
    {
        string body = """<one:OE><one:InsertedFile pathCache="x" preferredName="report.pdf" objectID="{F-1}"/></one:OE>""";

        Assert.That(Convert(body), Does.Contain("[attachment: report.pdf](onenote-object:{F-1})"));
    }

    [Test]
    public void An_attachment_without_an_id_renders_without_an_empty_link()
    {
        string md = Convert("""<one:OE><one:InsertedFile preferredName="report.pdf"/></one:OE>""");

        Assert.That(md, Does.Contain("[attachment: report.pdf]"));
        Assert.That(md, Does.Not.Contain("onenote-object:)"));
    }

    [Test]
    public void An_attachment_alone_in_a_paragraph_is_not_mistaken_for_an_indent_carrier()
    {
        string body = """
            <one:OE><one:InsertedFile preferredName="report.pdf"/>
              <one:OEChildren><one:OE><one:T><![CDATA[below]]></one:T></one:OE></one:OEChildren>
            </one:OE>
            """;

        string md = Convert(body);

        Assert.That(md, Does.Contain("[attachment: report.pdf]"));
        Assert.That(md, Does.Contain("below"));
    }

    [Test]
    public void A_list_is_separated_from_the_paragraph_that_follows_it()
    {
        string md = Convert(PageXml.Bullet("item") + PageXml.Oe("after"));

        Assert.That(md, Does.Contain("- item\n\nafter"));
    }

    [TestCase("# not a heading")]
    [TestCase("- not a bullet")]
    [TestCase("1. not an ordered item")]
    [TestCase("> not a quote")]
    [TestCase("---")]
    public void A_paragraph_that_only_looks_like_a_block_marker_stays_a_paragraph_on_round_trip(string text)
    {
        Assert.That(StyleNameAfterRoundTrip(PageXml.Oe(text)), Is.EqualTo("p"));
    }

    [Test]
    public void A_bullet_item_whose_text_looks_like_a_heading_is_not_promoted_on_round_trip()
    {
        string md = Convert(PageXml.Bullet("# not a heading"));

        XElement page = XElement.Parse(MarkdownToOneNoteXml.BuildPageXml("{ID}{1}{B0}", "T", md, PageXml.Agent));
        XElement item = page.Descendants(One + "OE").Single(o => o.Element(One + "List") is not null);

        Assert.That(item.Element(One + "T")!.Value, Does.Contain("not a heading"));
    }

    [Test]
    public void A_heading_whose_own_text_starts_with_a_hash_is_left_unescaped()
    {
        string md = Convert(PageXml.Oe("# already a heading", @"quickStyleIndex=""1"""));
        Assert.That(md, Does.Contain("# # already a heading"));
    }

    [Test]
    public void Inline_code_containing_a_backtick_keeps_its_own_delimiter_from_closing_early()
    {
        string md = Convert(PageXml.Oe("before <span style='font-family:Consolas'>a`b</span> after"));
        Assert.That(md, Does.Contain("``a`b``"));
    }

    [Test]
    public void Empty_page_produces_just_the_title()
    {
        string md = Convert(string.Empty);
        Assert.That(md.Trim(), Is.EqualTo("# Test page"));
    }

    [Test]
    public void Xml_without_a_page_element_is_rejected_clearly()
    {
        OneNoteException? ex = Assert.Throws<OneNoteException>(() => OneNoteXmlParser.ToMarkdown("<root/>"));
        Assert.That(ex!.Message, Does.Contain("one:Page"));
    }

    [Test]
    public void Blank_or_null_xml_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => OneNoteXmlParser.ToMarkdown("   "));
    }
}
