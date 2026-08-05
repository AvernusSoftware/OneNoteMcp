using OneNoteMcp.Core.Links;
using OneNoteMcp.Core.Models;

namespace OneNoteMcp.Tests;

[TestFixture]
public class OneNoteLinkResolverTests
{
    private static HierarchyNode Notebook(string name, params HierarchyNode[] children) => new()
    {
        Id = "nb-" + name,
        Name = name,
        Type = HierarchyNodeType.Notebook,
        Children = [.. children],
    };

    private static HierarchyNode Section(string id, string name, params HierarchyNode[] pages) => new()
    {
        Id = id,
        Name = name,
        Type = HierarchyNodeType.Section,
        Children = [.. pages],
    };

    private static HierarchyNode Page(string id, string name, params HierarchyNode[] children) => new()
    {
        Id = id,
        Name = name,
        Type = HierarchyNodeType.Page,
        Children = children.Length == 0 ? null : [.. children],
    };

    [Test]
    public void Unique_exact_title_match_wins_with_no_ids_at_all()
    {
        HierarchyNode[] tree = new[]
        {
            Notebook("IoT - założenia", Section("{SEC}", "5.3", Page("{PAGE-1}", "BL260652 Cron"))),
        };

        ParsedOneNoteLink link = new() { PageTitle = "BL260652 Cron" };

        OneNoteLinkResolver.ResolveResult result = OneNoteLinkResolver.Resolve(tree, link);

        Assert.That(result.Match, Is.Not.Null);
        Assert.That(result.Match!.Page.Id, Is.EqualTo("{PAGE-1}"));
        Assert.That(result.MatchReason, Is.EqualTo("exact title match"));
    }

    [Test]
    public void Ambiguous_title_is_narrowed_by_section_file_name()
    {
        HierarchyNode[] tree = new[]
        {
            Notebook(
                "Notebook A",
                Section("{SEC-A}", "5.3", Page("{PAGE-A}", "Cron config"))),
            Notebook(
                "Notebook B",
                Section("{SEC-B}", "5.4", Page("{PAGE-B}", "Cron config"))),
        };

        ParsedOneNoteLink link = new() { PageTitle = "Cron config", SectionFileName = "5.4" };

        OneNoteLinkResolver.ResolveResult result = OneNoteLinkResolver.Resolve(tree, link);

        Assert.That(result.Match, Is.Not.Null);
        Assert.That(result.Match!.Page.Id, Is.EqualTo("{PAGE-B}"));
    }

    [Test]
    public void Still_ambiguous_title_with_no_disambiguator_reports_near_misses_not_a_match()
    {
        HierarchyNode[] tree = new[]
        {
            Notebook("Notebook A", Section("{SEC-A}", "5.3", Page("{PAGE-A}", "Cron config"))),
            Notebook("Notebook B", Section("{SEC-B}", "5.4", Page("{PAGE-B}", "Cron config"))),
        };

        ParsedOneNoteLink link = new() { PageTitle = "Cron config" };

        OneNoteLinkResolver.ResolveResult result = OneNoteLinkResolver.Resolve(tree, link);

        Assert.That(result.Match, Is.Null);
        Assert.That(result.NearMisses, Has.Count.EqualTo(2));
    }

    [Test]
    public void Falls_back_to_page_id_embedded_in_the_hierarchy_id_when_the_title_does_not_match()
    {
        Guid guid = Guid.Parse("FEDF2512-FA44-423D-A903-12CB845B0FFA");

        HierarchyNode[] tree = new[]
        {
            Notebook(
                "IoT - założenia",
                Section("{SEC}{1}{B0}", "5.3", Page($"{{SEC}}{{1}}{{{guid:N}}}", "Renamed since the link was copied"))),
        };

        ParsedOneNoteLink link = new() { PageTitle = "Old title that no longer matches", PageId = guid };

        OneNoteLinkResolver.ResolveResult result = OneNoteLinkResolver.Resolve(tree, link);

        Assert.That(result.Match, Is.Not.Null);
        Assert.That(result.MatchReason, Is.EqualTo("page id embedded in hierarchy id"));
    }

    [Test]
    public void Finds_an_indented_sub_page()
    {
        HierarchyNode[] tree = new[]
        {
            Notebook(
                "IoT - założenia",
                Section(
                    "{SEC}",
                    "5.3",
                    Page("{PARENT}", "Parent page", Page("{CHILD}", "BL260652 Cron")))),
        };

        ParsedOneNoteLink link = new() { PageTitle = "BL260652 Cron" };

        OneNoteLinkResolver.ResolveResult result = OneNoteLinkResolver.Resolve(tree, link);

        Assert.That(result.Match, Is.Not.Null);
        Assert.That(result.Match!.Page.Id, Is.EqualTo("{CHILD}"));
        Assert.That(result.Match.SectionName, Is.EqualTo("5.3"));
    }

    [Test]
    public void No_match_and_no_near_miss_for_an_unrelated_title()
    {
        HierarchyNode[] tree = new[]
        {
            Notebook("IoT - założenia", Section("{SEC}", "5.3", Page("{PAGE-1}", "Something else entirely"))),
        };

        ParsedOneNoteLink link = new() { PageTitle = "Completely different topic" };

        OneNoteLinkResolver.ResolveResult result = OneNoteLinkResolver.Resolve(tree, link);

        Assert.That(result.Match, Is.Null);
        Assert.That(result.NearMisses, Is.Empty);
    }
}
