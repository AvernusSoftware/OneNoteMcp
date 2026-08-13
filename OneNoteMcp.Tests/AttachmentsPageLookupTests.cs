using OneNoteMcp.Core.Exceptions;
using OneNoteMcp.Core.Hierarchy;
using OneNoteMcp.Core.Interop;
using System.Xml.Linq;

namespace OneNoteMcp.Tests;

[TestFixture]
public class AttachmentsPageLookupTests
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    private static XElement Page(string id, string name, int? pageLevel = null)
    {
        XElement page = new(One + "Page", new XAttribute("ID", id), new XAttribute("name", name));
        if (pageLevel is not null)
        {
            page.SetAttributeValue("pageLevel", pageLevel.Value);
        }

        return page;
    }

    private static XElement Section(string id, params XElement[] pages) => new(One + "Section", new XAttribute("ID", id), pages);

    private static string? IdOf(XElement? page) => (string?)page?.Attribute("ID");

    [Test]
    public void Finds_a_subpage_of_the_named_parent()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "Notes"),
            Page("B", "Attachments", pageLevel: 1),
            Page("C", "Other"));

        Assert.That(IdOf(AttachmentsPageLookup.FindPage(section, "Attachments", "A")), Is.EqualTo("B"));
    }

    [Test]
    public void Ignores_a_same_titled_page_belonging_to_a_different_parent()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "First"),
            Page("B", "Attachments", pageLevel: 1),
            Page("C", "Second"),
            Page("D", "Attachments", pageLevel: 1));

        Assert.That(IdOf(AttachmentsPageLookup.FindPage(section, "Attachments", "C")), Is.EqualTo("D"));
    }

    [Test]
    public void Stops_at_the_end_of_the_parents_subtree()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "First"),
            Page("B", "Something", pageLevel: 1),
            Page("C", "Second"),
            Page("D", "Attachments", pageLevel: 1));

        Assert.That(AttachmentsPageLookup.FindPage(section, "Attachments", "A"), Is.Null);
    }

    [Test]
    public void Skips_a_match_nested_one_level_too_deep()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "Notes"),
            Page("B", "Detail", pageLevel: 1),
            Page("C", "Attachments", pageLevel: 2));

        Assert.That(AttachmentsPageLookup.FindPage(section, "Attachments", "A"), Is.Null);
        Assert.That(IdOf(AttachmentsPageLookup.FindPage(section, "Attachments", "B")), Is.EqualTo("C"));
    }

    [Test]
    public void Without_a_parent_only_top_level_pages_match()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "Notes"),
            Page("B", "Attachments", pageLevel: 1),
            Page("C", "Attachments"));

        Assert.That(IdOf(AttachmentsPageLookup.FindPage(section, "Attachments", null)), Is.EqualTo("C"));
    }

    [Test]
    public void Titles_match_ignoring_case_and_surrounding_whitespace()
    {
        XElement section = Section("{sec}", Page("A", "Notes"), Page("B", "  Attachments ", pageLevel: 1));

        Assert.That(IdOf(AttachmentsPageLookup.FindPage(section, "attachments", "A")), Is.EqualTo("B"));
    }

    [Test]
    public void Returns_null_when_nothing_matches()
    {
        XElement section = Section("{sec}", Page("A", "Notes"), Page("B", "Links", pageLevel: 1));

        Assert.That(AttachmentsPageLookup.FindPage(section, "Attachments", "A"), Is.Null);
    }

    [Test]
    public void Throws_when_the_parent_is_not_in_this_section()
    {
        XElement section = Section("{sec}", Page("A", "Notes"));

        Assert.Throws<OneNoteException>(() => AttachmentsPageLookup.FindPage(section, "Attachments", "Missing"));
    }

    [Test]
    public void Throws_when_the_parent_is_already_at_the_deepest_level()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "Notes"),
            Page("B", "Detail", pageLevel: 1),
            Page("C", "Deeper still", pageLevel: 2));

        Assert.Throws<OneNoteException>(() => AttachmentsPageLookup.FindPage(section, "Attachments", "C"));
    }

    [Test]
    public void A_parent_at_the_deepest_level_is_rejected_even_when_the_title_is_free()
    {
        XElement section = Section("{sec}", Page("A", "Notes"), Page("B", "Detail", pageLevel: 2));

        Assert.Throws<OneNoteException>(() => AttachmentsPageLookup.FindPage(section, "Anything at all", "B"));
    }
}
