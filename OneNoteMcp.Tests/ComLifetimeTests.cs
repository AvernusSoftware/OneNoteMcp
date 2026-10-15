using Microsoft.Extensions.Options;
using OneNoteMcp.Core.Interop;
using OneNoteMcp.Core.Models;
using OneNoteMcp.Core.Services;
using System.Runtime.InteropServices;

namespace OneNoteMcp.Tests;

[TestFixture]
public class ComLifetimeTests
{
    private const int RpcSServerUnavailable = unchecked((int)0x800706BA);

    private InlineStaThreadRunner _sta = null!;
    private FakeComObjects _com = null!;
    private OneNoteComService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _sta = new InlineStaThreadRunner();
        _com = new FakeComObjects(_sta);
        _service = new OneNoteComService(_sta, _com, Options.Create(PageXml.Agent));
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        _sta.Dispose();
    }

    [Test]
    public void OneNote_is_not_activated_before_the_first_operation()
    {
        Assert.That(_com.Created, Is.Empty);
    }

    [Test]
    public async Task A_multi_step_operation_activates_once()
    {
        await _service.CreatePageAsync("{section}", "Title", string.Empty);

        Assert.That(_com.Created, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_disconnect_on_the_first_attempt_releases_the_dead_object_and_reactivates_once()
    {
        _com.OnCreate = application =>
        {
            if (_com.Created.Count == 0)
            {
                application.GetHierarchyFailures.Enqueue(new COMException("disconnected", RpcSServerUnavailable));
            }
        };

        HierarchyNode[] nodes = await _service.GetHierarchyAsync(HierarchyScope.Notebooks);

        Assert.Multiple(() =>
        {
            Assert.That(nodes, Is.Empty);
            Assert.That(_com.Created, Has.Count.EqualTo(2));
            Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
        });
    }
}
