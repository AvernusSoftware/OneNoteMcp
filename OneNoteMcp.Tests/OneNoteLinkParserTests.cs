using OneNoteMcp.Core.Links;

namespace OneNoteMcp.Tests;

[TestFixture]
public class OneNoteLinkParserTests
{
    private const string SectionId = "39645F4C-976F-4F4B-856F-92B2A3A2B8F4";
    private const string PageId = "FEDF2512-FA44-423D-A903-12CB845B0FFA";
    private const string Title = "🔨 BL260652;[IoT] Obsługa wyrażeń Cron w konfiguracji Sygnału";

    private const string OneNoteUri =
        "onenote:https://eqsystem.sharepoint.com/sites/MOMTeam/Biblioteka%20dokumentw%202/" +
        "Za%C5%82o%C5%BCenia/IoT%20-%20za%C5%82o%C5%BCenia/Skrypty%20modu%C5%82owe%20i%20struktura" +
        "%20Xprimer/5.3.one#🔨%20BL260652;%5bIoT%5d%20Obs%C5%82uga%20wyra%C5%BCe%C5%84%20Cron%20w" +
        "%20konfiguracji%20Sygna%C5%82u&section-id={39645F4C-976F-4F4B-856F-92B2A3A2B8F4}" +
        "&page-id={FEDF2512-FA44-423D-A903-12CB845B0FFA}&end";

    private const string SharePointDocUrl =
        "https://eqsystem.sharepoint.com/sites/MOMTeam/_layouts/Doc.aspx?sourcedoc=" +
        "{A74BB997-4CC8-4883-8829-B643E8EF359E}&wd=target%28Skrypty%20modu%C5%82owe%20i%20struktura" +
        "%20Xprimer%2F5.3.one%7C39645F4C-976F-4F4B-856F-92B2A3A2B8F4%2F%F0%9F%94%A8%20BL260652%3B" +
        "%5BIoT%5D%20Obs%C5%82uga%20wyra%C5%BCe%C5%84%20Cron%20w%20konfiguracji%20Sygna%C5%82u%7C" +
        "FEDF2512-FA44-423D-A903-12CB845B0FFA%2F%29&wdpartid={CF1A7EDA-A472-0C03-209E-CC85C5DF82DC}" +
        "{1}&wdsectionfileid={39645F4C-976F-4F4B-856F-92B2A3A2B8F4}&end";

    [Test]
    public void Parses_onenote_uri_ids_and_title()
    {
        ParsedOneNoteLink? parsed = OneNoteLinkParser.TryParse(OneNoteUri);

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.SectionId, Is.EqualTo(Guid.Parse(SectionId)));
            Assert.That(parsed.PageId, Is.EqualTo(Guid.Parse(PageId)));
            Assert.That(parsed.PageTitle, Is.EqualTo(Title));
        });
    }

    [Test]
    public void Parses_sharepoint_doc_url_ids_title_and_section_file_name()
    {
        ParsedOneNoteLink? parsed = OneNoteLinkParser.TryParse(SharePointDocUrl);

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.SectionId, Is.EqualTo(Guid.Parse(SectionId)));
            Assert.That(parsed.PageId, Is.EqualTo(Guid.Parse(PageId)));
            Assert.That(parsed.PageTitle, Is.EqualTo(Title));
            Assert.That(parsed.SectionFileName, Is.EqualTo("5.3"));
        });
    }

    [Test]
    public void Finds_the_link_when_pasted_alongside_other_text()
    {
        string pasted = "check this out please:\n" + SharePointDocUrl + "\nalso see\n" + OneNoteUri;

        ParsedOneNoteLink? parsed = OneNoteLinkParser.TryParse(pasted);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.PageId, Is.EqualTo(Guid.Parse(PageId)));
    }

    [Test]
    public void Returns_null_for_unrelated_text()
    {
        Assert.That(OneNoteLinkParser.TryParse("just a normal sentence, no link here"), Is.Null);
    }

    [Test]
    public void Returns_null_for_empty_input()
    {
        Assert.That(OneNoteLinkParser.TryParse(""), Is.Null);
    }
}
