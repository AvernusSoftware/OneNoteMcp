using OneNoteMcp.Core.Exceptions;
using OneNoteMcp.Core.Hierarchy;
using OneNoteMcp.Core.Interop;
using System.Xml.Linq;

namespace OneNoteMcp.Tests;

[TestFixture]
public class SubpagePlacementTests
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    private static XElement Page(string id, string name, int? pageLevel = null, params XAttribute[] extra)
    {
        XElement page = new(One + "Page", new XAttribute("ID", id), new XAttribute("name", name));
        if (pageLevel is not null)
        {
            page.SetAttributeValue("pageLevel", pageLevel.Value);
        }

        foreach (XAttribute attribute in extra)
        {
            page.Add(attribute);
        }

        return page;
    }

    private static XElement Section(string id, params XElement[] pages) =>
        new(One + "Section", new XAttribute("ID", id), pages);

    private static List<XElement> PagesOf(XElement section) => [.. section.Elements(One + "Page")];

    [Test]
    public void Inserts_as_first_subpage_of_a_top_level_parent()
    {
        XElement section = Section("{sec}", Page("A", "A"));

        (XElement result, int childLevel) = SubpagePlacement.PlaceUnderParent(section, "A", "New", "New");

        List<XElement> pages = PagesOf(result);
        Assert.That(childLevel, Is.EqualTo(1));
        Assert.That(pages.Select(p => (string?)p.Attribute("ID")), Is.EqualTo(["A", "New"]));
        Assert.That((string?)pages[1].Attribute("pageLevel"), Is.EqualTo("1"));
    }

    [Test]
    public void Inserts_after_parents_existing_subpages()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "A"),
            Page("B", "B", pageLevel: 1),
            Page("C", "C", pageLevel: 1),
            Page("D", "D"));

        (XElement result, int childLevel) = SubpagePlacement.PlaceUnderParent(section, "A", "New", "New");

        List<string?> order = PagesOf(result).Select(p => (string?)p.Attribute("ID")).ToList();
        Assert.That(childLevel, Is.EqualTo(1));
        Assert.That(order, Is.EqualTo(["A", "B", "C", "New", "D"]));
    }

    [Test]
    public void Nesting_under_a_subpage_creates_a_sub_subpage()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "A"),
            Page("B", "B", pageLevel: 1),
            Page("C", "C"));

        (XElement result, int childLevel) = SubpagePlacement.PlaceUnderParent(section, "B", "New", "New");

        List<string?> order = PagesOf(result).Select(p => (string?)p.Attribute("ID")).ToList();
        Assert.That(childLevel, Is.EqualTo(2));
        Assert.That(order, Is.EqualTo(["A", "B", "New", "C"]));
    }

    [Test]
    public void Throws_when_parent_is_already_at_max_level()
    {
        XElement section = Section("{sec}", Page("A", "A", pageLevel: 2));

        Assert.Throws<OneNoteException>(() => SubpagePlacement.PlaceUnderParent(section, "A", "New", "New"));
    }

    [Test]
    public void Throws_when_parent_is_not_in_this_section()
    {
        XElement section = Section("{sec}", Page("A", "A"));

        Assert.Throws<OneNoteException>(() => SubpagePlacement.PlaceUnderParent(section, "Missing", "New", "New"));
    }

    [Test]
    public void Preexisting_pages_are_cloned_verbatim()
    {
        XElement section = Section(
            "{sec}",
            Page("A", "A", null, new XAttribute("lastModifiedTime", "2020-01-01T00:00:00.000Z"), new XAttribute("isCurrentlyViewed", "true")));

        (XElement result, _) = SubpagePlacement.PlaceUnderParent(section, "A", "New", "New");

        XElement a = PagesOf(result)[0];
        Assert.That((string?)a.Attribute("lastModifiedTime"), Is.EqualTo("2020-01-01T00:00:00.000Z"));
        Assert.That((string?)a.Attribute("isCurrentlyViewed"), Is.EqualTo("true"));
    }

    [Test]
    public void New_page_is_excluded_from_the_input_before_being_reinserted()
    {
        XElement section = Section("{sec}", Page("A", "A"), Page("New", "stale name"));

        (XElement result, int childLevel) = SubpagePlacement.PlaceUnderParent(section, "A", "New", "New");

        List<XElement> pages = PagesOf(result);
        Assert.That(pages.Count(p => (string?)p.Attribute("ID") == "New"), Is.EqualTo(1));
        Assert.That(pages.Select(p => (string?)p.Attribute("ID")), Is.EqualTo(["A", "New"]));
        Assert.That((string?)pages[1].Attribute("name"), Is.EqualTo("New"));
        Assert.That((string?)pages[1].Attribute("pageLevel"), Is.EqualTo(childLevel.ToString()));
    }
}
