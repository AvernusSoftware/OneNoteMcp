using OneNoteMcp.Core.Markdown;
using System.Xml.Linq;

namespace OneNoteMcp.Tests;

[TestFixture]
public class AttachmentXmlTests
{
    private const string PageId = "{ABC}{1}{B0}";
    private const string FilePath = @"D:\docs\report.pdf";
    private static readonly XNamespace One = PageXml.Ns;

    private static XElement Build(string? caption = null, string? objectId = null, XElement? existingPage = null)
    {
        PageSchema schema = existingPage is null ? PageSchema.ForNewPage() : PageSchema.ForExistingPage(existingPage);
        return XElement.Parse(AttachmentXml.BuildOutlineXml(PageId, FilePath, "report.pdf", caption, PageXml.Agent, schema, objectId));
    }

    private static XElement File(XElement block) => block.Descendants(One + "InsertedFile").Single();

    [Test]
    public void The_file_is_carried_by_an_inserted_file_element_inside_a_paragraph()
    {
        XElement file = File(Build());

        Assert.That(file.Parent?.Name, Is.EqualTo(One + "OE"));
        Assert.That((string?)file.Attribute("pathSource"), Is.EqualTo(FilePath));
        Assert.That((string?)file.Attribute("preferredName"), Is.EqualTo("report.pdf"));
    }

    [Test]
    public void The_fragment_targets_the_page_it_was_built_for()
    {
        XElement block = Build();

        Assert.That(block.Name, Is.EqualTo(One + "Page"));
        Assert.That((string?)block.Attribute("ID"), Is.EqualTo(PageId));
    }

    [Test]
    public void Appending_leaves_the_outline_untargeted_so_onenote_creates_a_new_block()
    {
        XElement outline = Build().Element(One + "Outline")!;

        Assert.That(outline.Attribute("objectID"), Is.Null);
    }

    [Test]
    public void Replacing_targets_the_existing_outline_by_id()
    {
        XElement outline = Build(objectId: "{B1}").Element(One + "Outline")!;

        Assert.That((string?)outline.Attribute("objectID"), Is.EqualTo("{B1}"));
    }

    [Test]
    public void Without_a_caption_the_block_is_a_single_paragraph()
    {
        Assert.That(Build().Descendants(One + "OE").Count(), Is.EqualTo(1));
    }

    [Test]
    public void A_caption_becomes_a_second_paragraph_below_the_file()
    {
        List<XElement> paragraphs = [.. Build(caption: "Q3 figures").Descendants(One + "OE")];

        Assert.That(paragraphs, Has.Count.EqualTo(2));
        Assert.That(paragraphs[0].Element(One + "InsertedFile"), Is.Not.Null);
        Assert.That(paragraphs[1].Element(One + "T")?.Value, Is.EqualTo("Q3 figures"));
    }

    [Test]
    public void A_caption_is_html_encoded()
    {
        XElement block = Build(caption: "a < b & c");

        Assert.That(block.Descendants(One + "T").Single().Value, Is.EqualTo("a &lt; b &amp; c"));
    }

    [Test]
    public void The_style_definition_the_block_references_travels_with_it()
    {
        XElement block = Build(objectId: "{B1}", existingPage: XElement.Parse(
            $@"<one:Page xmlns:one=""{PageXml.Ns}"" ID=""{PageId}""><one:QuickStyleDef index=""7"" name=""p"" font=""Segoe UI"" fontSize=""11.0""/></one:Page>"));

        string? referenced = (string?)block.Descendants(One + "OE").First().Attribute("quickStyleIndex");

        Assert.That(referenced, Is.EqualTo("7"), "the page's own index for 'p' should be reused");
        Assert.That(block.Elements(One + "QuickStyleDef").Select(d => (string?)d.Attribute("index")), Does.Contain("7"));
    }

    [Test]
    public void The_outline_element_is_not_stamped_directly()
    {
        XElement outline = Build().Element(One + "Outline")!;

        Assert.That(outline.Attribute("author"), Is.Null);
    }

    [Test]
    public void A_path_with_xml_significant_characters_survives()
    {
        string xml = AttachmentXml.BuildOutlineXml(PageId, @"D:\a & b\<odd>.txt", "<odd>.txt", null, PageXml.Agent, PageSchema.ForNewPage());
        XElement file = XElement.Parse(xml).Descendants(One + "InsertedFile").Single();

        Assert.That((string?)file.Attribute("pathSource"), Is.EqualTo(@"D:\a & b\<odd>.txt"));
        Assert.That((string?)file.Attribute("preferredName"), Is.EqualTo("<odd>.txt"));
    }
}
