using OneNoteMcp.Core.Markdown;
using System.Xml.Linq;

namespace OneNoteMcp.Tests;

[TestFixture]
public class AiBlockTests
{
    private const string PageId = "{ABC}{1}{B0}";
    private static readonly XNamespace One = PageXml.Ns;
    private static string Agent => PageXml.Agent.DisplayName;

    private static XElement Outline(string objectId, string? outlineAuthor, params string[] paragraphs)
    {
        string authorAttribute = outlineAuthor is null ? string.Empty : $@" author=""{outlineAuthor}""";
        return XElement.Parse(
            $@"<one:Outline xmlns:one=""{PageXml.Ns}"" objectID=""{objectId}""{authorAttribute}>
                 <one:OEChildren>{string.Concat(paragraphs)}</one:OEChildren>
               </one:Outline>");
    }

    private static string Paragraph(string text, string? author = null)
    {
        string authorAttribute = author is null ? string.Empty : $@" author=""{author}""";
        return $@"<one:OE{authorAttribute}><one:T><![CDATA[{text}]]></one:T></one:OE>";
    }

    [Test]
    public void Block_written_by_the_agent_is_owned()
    {
        XElement outline = Outline("{B1}", Agent, Paragraph("agent text"));

        Assert.That(AiBlocks.IsOwnedBy(outline, Agent), Is.True);
    }

    [Test]
    public void Block_written_by_the_user_is_not_owned()
    {
        XElement outline = Outline("{B1}", "Chimerian", Paragraph("my own note"));

        Assert.That(AiBlocks.IsOwnedBy(outline, Agent), Is.False);
    }

    [Test]
    public void Block_with_no_author_at_all_is_not_owned()
    {
        XElement outline = Outline("{B1}", outlineAuthor: null, Paragraph("who wrote this?"));

        Assert.That(AiBlocks.IsOwnedBy(outline, Agent), Is.False);
    }

    [Test]
    public void Agent_block_the_user_has_typed_into_is_not_owned()
    {
        XElement outline = Outline(
            "{B1}",
            Agent,
            Paragraph("agent text"),
            Paragraph("hand-written addition", author: "Chimerian"));

        Assert.That(AiBlocks.IsOwnedBy(outline, Agent), Is.False);
    }

    [Test]
    public void Paragraphs_the_agent_itself_authored_do_not_count_as_contamination()
    {
        XElement outline = Outline("{B1}", Agent, Paragraph("first", Agent), Paragraph("second", Agent));

        Assert.That(AiBlocks.IsOwnedBy(outline, Agent), Is.True);
    }

    [Test]
    public void Ownership_is_case_sensitive()
    {
        XElement outline = Outline("{B1}", "test agent", Paragraph("text"));

        Assert.That(AiBlocks.IsOwnedBy(outline, Agent), Is.False);
    }

    [Test]
    public void An_empty_agent_name_owns_nothing()
    {
        XElement outline = Outline("{B1}", outlineAuthor: null, Paragraph("text"));

        Assert.That(AiBlocks.IsOwnedBy(outline, string.Empty), Is.False);
    }

    [Test]
    public void Contamination_is_detected_in_nested_paragraphs()
    {
        XElement outline = XElement.Parse(
            $@"<one:Outline xmlns:one=""{PageXml.Ns}"" objectID=""{{B1}}"" author=""{Agent}"">
                 <one:OEChildren>
                   <one:OE><one:T><![CDATA[agent]]></one:T>
                     <one:OEChildren>
                       <one:OE author=""Chimerian""><one:T><![CDATA[mine]]></one:T></one:OE>
                     </one:OEChildren>
                   </one:OE>
                 </one:OEChildren>
               </one:Outline>");

        Assert.That(AiBlocks.IsOwnedBy(outline, Agent), Is.False);
    }

    private static string PageWithBlocks(params string[] outlines) =>
        $@"<?xml version=""1.0""?>
           <one:Page xmlns:one=""{PageXml.Ns}"" ID=""{PageId}"" name=""T"">
             <one:QuickStyleDef index=""0"" name=""p"" font=""Calibri"" fontSize=""11.0""/>
             <one:Title><one:OE><one:T><![CDATA[T]]></one:T></one:OE></one:Title>
             {string.Concat(outlines)}
           </one:Page>";

    [Test]
    public void Agent_blocks_are_marked_with_their_id()
    {
        string xml = PageWithBlocks(Outline("{B1}", Agent, Paragraph("agent text")).ToString());

        string markdown = OneNoteXmlParser.ToMarkdown(xml, agentDisplayName: Agent);

        Assert.That(markdown, Does.Contain("<!-- ai-block: {B1} -->"));
    }

    [Test]
    public void User_blocks_are_not_marked()
    {
        string xml = PageWithBlocks(Outline("{B1}", "Chimerian", Paragraph("my note")).ToString());

        string markdown = OneNoteXmlParser.ToMarkdown(xml, agentDisplayName: Agent);

        Assert.That(markdown, Does.Not.Contain("ai-block"));
        Assert.That(markdown, Does.Contain("my note"), "content must still be readable");
    }

    [Test]
    public void Only_the_agent_block_is_marked_when_a_page_holds_both()
    {
        string xml = PageWithBlocks(
            Outline("{USER}", "Chimerian", Paragraph("mine")).ToString(),
            Outline("{AI}", Agent, Paragraph("theirs")).ToString());

        string markdown = OneNoteXmlParser.ToMarkdown(xml, agentDisplayName: Agent);

        Assert.That(markdown, Does.Contain("<!-- ai-block: {AI} -->"));
        Assert.That(markdown, Does.Not.Contain("{USER}"));
    }

    [Test]
    public void No_markers_are_emitted_when_no_agent_is_supplied()
    {
        string xml = PageWithBlocks(Outline("{B1}", Agent, Paragraph("agent text")).ToString());

        Assert.That(OneNoteXmlParser.ToMarkdown(xml), Does.Not.Contain("ai-block"));
    }

    private static XElement BuildBlock(string markdown, string? objectId = null, XElement? page = null)
    {
        PageSchema schema = page is null ? PageSchema.ForNewPage() : PageSchema.ForExistingPage(page);
        return XElement.Parse(
            MarkdownToOneNoteXml.BuildOutlineXml(PageId, markdown, PageXml.Agent, schema, objectId));
    }

    [Test]
    public void Block_paragraphs_carry_the_agent_identity()
    {
        XElement block = BuildBlock("hello");
        XElement oe = block.Descendants(One + "OE").First();

        Assert.Multiple(() =>
        {
            Assert.That((string?)oe.Attribute("author"), Is.EqualTo(PageXml.Agent.DisplayName));
            Assert.That((string?)oe.Attribute("authorInitials"), Is.EqualTo(PageXml.Agent.Initials));
            Assert.That((string?)oe.Attribute("lastModifiedBy"), Is.EqualTo(PageXml.Agent.DisplayName));
        });
    }

    // OneNote ignores an author set on one:Outline and substitutes the local Office user, so the
    // stamp has to sit on the paragraphs and be propagated up by OneNote itself.
    [Test]
    public void The_outline_element_is_not_stamped_directly()
    {
        XElement outline = BuildBlock("hello").Descendants(One + "Outline").Single();

        Assert.That(outline.Attribute("author"), Is.Null);
    }

    [Test]
    public void Every_paragraph_of_a_multi_block_body_is_stamped()
    {
        XElement block = BuildBlock("# Heading\n\npara\n\n- one\n- two");

        Assert.That(
            block.Descendants(One + "OE").All(oe => (string?)oe.Attribute("author") == Agent),
            Is.True);
    }

    [Test]
    public void An_object_id_targets_an_existing_block_for_replacement()
    {
        XElement outline = BuildBlock("replacement", objectId: "{B1}").Descendants(One + "Outline").Single();

        Assert.That((string?)outline.Attribute("objectID"), Is.EqualTo("{B1}"));
    }

    [Test]
    public void No_object_id_means_a_new_block()
    {
        XElement outline = BuildBlock("fresh").Descendants(One + "Outline").Single();

        Assert.That(outline.Attribute("objectID"), Is.Null);
    }

    [Test]
    public void A_block_fragment_carries_the_page_id_but_no_title()
    {
        XElement page = BuildBlock("text");

        Assert.Multiple(() =>
        {
            Assert.That((string?)page.Attribute("ID"), Is.EqualTo(PageId));
            Assert.That(page.Element(One + "Title"), Is.Null, "a partial update must not rewrite the title");
        });
    }

    private static XElement ExistingPage(string defs) =>
        XElement.Parse($@"<one:Page xmlns:one=""{PageXml.Ns}"" ID=""{PageId}"">{defs}</one:Page>");

    [Test]
    public void An_existing_style_is_reused_rather_than_redefined()
    {
        XElement page = ExistingPage(@"<one:QuickStyleDef index=""7"" name=""p"" font=""Segoe UI"" fontSize=""11.0""/>");
        PageSchema schema = PageSchema.ForExistingPage(page);

        Assert.That(schema.StyleIndex("p"), Is.EqualTo(7));
    }

    [Test]
    public void A_missing_style_is_allocated_an_index_the_page_is_not_using()
    {
        XElement page = ExistingPage(
            @"<one:QuickStyleDef index=""0"" name=""somethingElse"" font=""Segoe UI"" fontSize=""11.0""/>");
        PageSchema schema = PageSchema.ForExistingPage(page);

        Assert.That(schema.StyleIndex("p"), Is.Not.EqualTo(0), "index 0 already means something else here");
    }

    // A reused definition still has to travel with the fragment - see the test below - so what
    // matters is that it goes out as the page wrote it rather than as this converter would.
    [Test]
    public void Redefining_a_style_the_page_already_owns_is_avoided()
    {
        XElement page = ExistingPage(@"<one:QuickStyleDef index=""0"" name=""p"" font=""Comic Sans MS"" fontSize=""20.0""/>");
        XElement def = BuildBlock("plain paragraph", page: page)
            .Elements(One + "QuickStyleDef")
            .Single(d => (string?)d.Attribute("index") == "0");

        Assert.Multiple(() =>
        {
            Assert.That((string?)def.Attribute("font"), Is.EqualTo("Comic Sans MS"));
            Assert.That((string?)def.Attribute("fontSize"), Is.EqualTo("20.0"));
        });
    }

    // Replacing a block drops its paragraphs before the new ones are attached, and OneNote prunes
    // any definition nothing references at that moment. A fragment that left a reused definition
    // out because "the page already has it" would find the index gone by the time its paragraphs
    // landed, and every one of them would fall back to index 0 - the block loses its headings.
    [Test]
    public void A_reused_style_definition_still_travels_with_the_block()
    {
        XElement page = ExistingPage(
            @"<one:QuickStyleDef index=""0"" name=""p"" font=""Segoe UI"" fontSize=""11.0""/>
              <one:QuickStyleDef index=""1"" name=""h1"" font=""Segoe UI"" fontSize=""16.0""/>");

        XElement block = BuildBlock("# Heading\n\nbody", objectId: "{B1}", page: page);

        Assert.That(
            block.Elements(One + "QuickStyleDef").Select(d => (string?)d.Attribute("index")),
            Is.EquivalentTo(new[] { "0", "1" }));
    }

    [Test]
    public void A_reused_to_do_tag_definition_still_travels_with_the_block()
    {
        XElement page = ExistingPage(@"<one:TagDef index=""4"" type=""3"" symbol=""3"" name=""To Do""/>");
        XElement block = BuildBlock("- [ ] task", objectId: "{B1}", page: page);

        Assert.That(
            block.Elements(One + "TagDef").Select(d => (string?)d.Attribute("index")),
            Is.EqualTo(new[] { "4" }));
    }

    [Test]
    public void An_existing_to_do_tag_is_reused()
    {
        XElement page = ExistingPage(@"<one:TagDef index=""4"" type=""3"" symbol=""3"" name=""To Do""/>");
        PageSchema schema = PageSchema.ForExistingPage(page);

        Assert.That(schema.ToDoTagIndex, Is.EqualTo(4));
    }

    [Test]
    public void A_to_do_tag_is_allocated_around_indices_already_in_use()
    {
        XElement page = ExistingPage(@"<one:TagDef index=""0"" type=""1"" symbol=""1"" name=""Important""/>");
        PageSchema schema = PageSchema.ForExistingPage(page);

        Assert.That(schema.ToDoTagIndex, Is.Not.EqualTo(0));
    }

    [Test]
    public void Unused_style_definitions_are_not_pushed_onto_an_existing_page()
    {
        XElement block = BuildBlock("just a paragraph", page: ExistingPage(string.Empty));

        Assert.That(
            block.Elements(One + "QuickStyleDef").Count(),
            Is.LessThanOrEqualTo(1),
            "a one-paragraph block should not add the whole style table to the user's page");
    }
}
