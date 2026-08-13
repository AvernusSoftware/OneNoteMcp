using OneNoteMcp.Core.Markdown;
using System.Xml.Linq;

namespace OneNoteMcp.Tests;

[TestFixture]
public class MarkdownToXmlTests
{
    private const string PageId = "{ABC}{1}{B0}";
    private static readonly XNamespace One = PageXml.Ns;

    private static XElement Build(string markdown, string title = "T") =>
        XElement.Parse(MarkdownToOneNoteXml.BuildPageXml(PageId, title, markdown, PageXml.Agent));

    private static IEnumerable<XElement> Oes(XElement page) =>
        page.Descendants(One + "OE");

    [Test]
    public void Page_carries_the_supplied_id_and_namespace()
    {
        XElement page = Build("text");

        Assert.That(page.Name, Is.EqualTo(One + "Page"));
        Assert.That((string?)page.Attribute("ID"), Is.EqualTo(PageId));
    }

    [Test]
    public void Page_children_follow_the_schema_order()
    {
        XElement page = Build("- [ ] task");
        List<string> order = [.. page.Elements().Select(e => e.Name.LocalName)];

        int lastTagDef = order.LastIndexOf("TagDef");
        int firstStyle = order.IndexOf("QuickStyleDef");
        int title = order.IndexOf("Title");
        int outline = order.IndexOf("Outline");

        Assert.That(lastTagDef, Is.LessThan(firstStyle), "TagDef must precede QuickStyleDef");
        Assert.That(firstStyle, Is.LessThan(title), "QuickStyleDef must precede Title");
        Assert.That(title, Is.LessThan(outline), "Title must precede the body");
    }

    [Test]
    public void Title_is_written_into_the_title_element()
    {
        XElement page = Build("body", title: "Shopping list");
        XElement title = page.Element(One + "Title")!;

        Assert.That(title.Descendants(One + "T").Single().Value, Is.EqualTo("Shopping list"));
    }

    [Test]
    public void Text_runs_are_wrapped_in_cdata()
    {
        string xml = MarkdownToOneNoteXml.BuildPageXml(PageId, "T", "hello", PageXml.Agent);
        Assert.That(xml, Does.Contain("<![CDATA["));
    }

    [Test]
    public void Angle_brackets_in_content_are_encoded_not_left_raw()
    {
        XElement page = Build("use <script> carefully");
        string text = page.Descendants(One + "T").Last().Value;

        Assert.That(text, Does.Contain("&lt;script&gt;"));
        Assert.That(text, Does.Not.Contain("<script>"));
    }

    [TestCase(1, "h1")]
    [TestCase(2, "h2")]
    [TestCase(6, "h6")]
    public void Headings_reference_the_matching_quick_style(int level, string styleName)
    {
        XElement page = Build(new string('#', level) + " Heading");

        string styleIndex = page.Elements(One + "QuickStyleDef")
            .Single(d => (string?)d.Attribute("name") == styleName)
            .Attribute("index")!.Value;

        XElement heading = Oes(page).Single(o => o.Element(One + "T")?.Value == "Heading");
        Assert.That((string?)heading.Attribute("quickStyleIndex"), Is.EqualTo(styleIndex));
    }

    [TestCase("**b**", "font-weight:bold")]
    [TestCase("*i*", "font-style:italic")]
    [TestCase("~~s~~", "text-decoration:line-through")]
    [TestCase("`c`", "font-family:Consolas")]
    public void Inline_emphasis_becomes_a_styled_span(string markdown, string expectedStyle)
    {
        XElement page = Build(markdown);
        Assert.That(page.Descendants(One + "T").Last().Value, Does.Contain(expectedStyle));
    }

    [Test]
    public void Links_become_anchors()
    {
        XElement page = Build("[example](https://example.com)");
        Assert.That(
            page.Descendants(One + "T").Last().Value,
            Does.Contain("""<a href="https://example.com">example</a>"""));
    }

    [Test]
    public void Bullet_lists_use_one_list_bullet()
    {
        XElement page = Build("- one\n- two");
        List<XElement> bullets = [.. page.Descendants(One + "Bullet")];

        Assert.That(bullets, Has.Count.EqualTo(2));
    }

    [Test]
    public void Bullet_does_not_carry_a_font_attribute()
    {
        XElement page = Build("- one");
        Assert.That(page.Descendants(One + "Bullet").Single().Attribute("font"), Is.Null);
    }

    [Test]
    public void Number_carries_numberFormat_and_nothing_onenote_rejects()
    {
        XElement page = Build("1. one");
        XElement number = page.Descendants(One + "Number").Single();

        Assert.Multiple(() =>
        {
            Assert.That((string?)number.Attribute("numberFormat"), Is.EqualTo("##."));
            Assert.That(number.Attribute("startAt"), Is.Null, "startAt is rejected by OneNote");
            Assert.That(number.Attribute("fontSize"), Is.Null, "fontSize is rejected by OneNote");
            Assert.That(number.Attribute("font"), Is.Null);
        });
    }

    [Test]
    public void Nested_lists_produce_nested_oechildren()
    {
        XElement page = Build("- one\n  - two\n    - three");

        int depth = 0;
        XElement? node = page.Element(One + "Outline")!.Element(One + "OEChildren");
        while (node is not null)
        {
            depth++;
            node = node.Element(One + "OE")?.Element(One + "OEChildren");
        }

        Assert.That(depth, Is.EqualTo(4), "three list levels plus the indent carrier");
    }

    [Test]
    public void Task_items_become_tags_and_the_tag_definition_is_emitted_once()
    {
        XElement page = Build("- [ ] open\n- [x] shut");

        List<XElement> defs = [.. page.Elements(One + "TagDef")];
        List<XElement> tags = [.. page.Descendants(One + "Tag")];

        Assert.Multiple(() =>
        {
            Assert.That(defs, Has.Count.EqualTo(1), "exactly one TagDef regardless of item count");
            Assert.That((string?)defs[0].Attribute("type"), Is.EqualTo("3"), "type 3 is the to-do tag");
            Assert.That(tags, Has.Count.EqualTo(2));
            Assert.That((string?)tags[0].Attribute("completed"), Is.EqualTo("false"));
            Assert.That((string?)tags[1].Attribute("completed"), Is.EqualTo("true"));
        });
    }

    [Test]
    public void No_tag_definition_is_emitted_when_there_are_no_tasks()
    {
        Assert.That(Build("- plain bullet").Elements(One + "TagDef"), Is.Empty);
    }

    [Test]
    public void Task_items_do_not_also_get_a_bullet()
    {
        XElement page = Build("- [ ] task");
        Assert.That(page.Descendants(One + "Bullet"), Is.Empty);
    }

    [Test]
    public void Fenced_code_becomes_one_monospace_element_per_line()
    {
        XElement page = Build("```csharp\nvar x = 1;\nvar y = 2;\n```");

        string codeStyleIndex = page.Elements(One + "QuickStyleDef")
            .Single(d => (string?)d.Attribute("name") == "code")
            .Attribute("index")!.Value;

        List<XElement> codeLines = [.. Oes(page).Where(o => (string?)o.Attribute("quickStyleIndex") == codeStyleIndex)];

        Assert.That(codeLines, Has.Count.EqualTo(2));
        Assert.That(codeLines[0].Element(One + "T")!.Value, Is.EqualTo("var x = 1;"));
    }

    [Test]
    public void Tables_produce_matching_column_and_cell_counts()
    {
        XElement page = Build("| a | b | c |\n| --- | --- | --- |\n| 1 | 2 | 3 |");
        XElement table = page.Descendants(One + "Table").Single();

        Assert.Multiple(() =>
        {
            Assert.That(table.Element(One + "Columns")!.Elements(One + "Column").Count(), Is.EqualTo(3));
            Assert.That(table.Elements(One + "Row").Count(), Is.EqualTo(2));
            Assert.That((string?)table.Attribute("hasHeaderRow"), Is.EqualTo("true"));

            foreach (XElement row in table.Elements(One + "Row"))
            {
                Assert.That(row.Elements(One + "Cell").Count(), Is.EqualTo(3));
            }
        });
    }

    [Test]
    public void List_element_precedes_the_text_run()
    {
        XElement page = Build("- item");
        XElement oe = Oes(page).First(o => o.Element(One + "List") is not null);
        List<string> names = [.. oe.Elements().Select(e => e.Name.LocalName)];

        Assert.That(names.IndexOf("List"), Is.LessThan(names.IndexOf("T")));
    }

    [Test]
    public void Tag_element_precedes_the_text_run()
    {
        XElement page = Build("- [x] done");
        XElement oe = Oes(page).First(o => o.Element(One + "Tag") is not null);
        List<string> names = [.. oe.Elements().Select(e => e.Name.LocalName)];

        Assert.That(names.IndexOf("Tag"), Is.LessThan(names.IndexOf("T")));
    }

    [Test]
    public void Nested_children_come_after_the_items_own_text()
    {
        XElement page = Build("- parent\n  - child");
        XElement oe = Oes(page).First(o => o.Element(One + "OEChildren") is not null);
        List<string> names = [.. oe.Elements().Select(e => e.Name.LocalName)];

        Assert.That(names.IndexOf("T"), Is.LessThan(names.IndexOf("OEChildren")));
    }

    [Test]
    public void Empty_markdown_still_produces_a_valid_page_with_an_outline()
    {
        XElement page = Build(string.Empty);

        Assert.That(page.Element(One + "Outline"), Is.Not.Null);
        Assert.That(page.Descendants(One + "OE").Any(), Is.True);
    }

    [Test]
    public void Blank_page_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => MarkdownToOneNoteXml.BuildPageXml(" ", "t", "b", PageXml.Agent));
    }

    [Test]
    public void Generated_xml_is_well_formed_for_a_document_using_every_feature()
    {
        const string markdown = """
            # Title

            Text with **bold**, *italic*, `code` & <angle> and a [link](https://x.test).

            - bullet
              - nested

            1. one
            2. two

            - [ ] todo
            - [x] done

            | a | b |
            | --- | --- |
            | 1 | 2 |

            ```cs
            var x = 1;
            ```

            > quoted

            ---
            """;

        Assert.DoesNotThrow(() => XDocument.Parse(MarkdownToOneNoteXml.BuildPageXml(PageId, "All", markdown, PageXml.Agent)));
    }
}
