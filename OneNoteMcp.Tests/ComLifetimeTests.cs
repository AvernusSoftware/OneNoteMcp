using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OneNoteMcp.Core.Configuration;
using OneNoteMcp.Core.Interop;
using OneNoteMcp.Core.Models;
using OneNoteMcp.Core.Services;
using System.Runtime.InteropServices;

namespace OneNoteMcp.Tests;

[TestFixture]
public class ComLifetimeTests
{
    private const int RpcSServerUnavailable = unchecked((int)0x800706BA);

    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(10);

    private InlineStaThreadRunner _sta = null!;
    private FakeComObjects _com = null!;
    private FakeTimeProvider _time = null!;
    private OneNoteComService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _sta = new InlineStaThreadRunner();
        _com = new FakeComObjects(_sta);
        _time = new FakeTimeProvider();
        _service = CreateService(idleReleaseSeconds: 10);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        _sta.Dispose();
    }

    private OneNoteComService CreateService(int idleReleaseSeconds) =>
        new(_sta, _com, _time, Options.Create(new OneNoteOptions { IdleReleaseSeconds = idleReleaseSeconds }), Options.Create(PageXml.Agent));

    private void UseZeroIdleDelay()
    {
        _service.Dispose();
        _service = CreateService(idleReleaseSeconds: 0);
    }

    private Task<HierarchyNode[]> AnyOperation() => _service.GetHierarchyAsync(HierarchyScope.Notebooks);

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

        HierarchyNode[] nodes = await AnyOperation();

        Assert.Multiple(() =>
        {
            Assert.That(nodes, Is.Empty);
            Assert.That(_com.Created, Has.Count.EqualTo(2));
            Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
        });
    }

    [Test]
    public async Task OneNote_is_released_once_the_idle_delay_passes_without_another_operation()
    {
        await AnyOperation();

        _time.Advance(IdleDelay);

        Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
    }

    [Test]
    public async Task OneNote_is_kept_until_the_idle_delay_has_fully_passed()
    {
        await AnyOperation();

        _time.Advance(IdleDelay - TimeSpan.FromTicks(1));

        Assert.That(_com.Released, Is.Empty);
    }

    [Test]
    public async Task Another_operation_within_the_idle_delay_postpones_the_release()
    {
        await AnyOperation();
        _time.Advance(TimeSpan.FromSeconds(9));
        await AnyOperation();
        _time.Advance(TimeSpan.FromSeconds(9));

        Assert.Multiple(() =>
        {
            Assert.That(_com.Created, Has.Count.EqualTo(1));
            Assert.That(_com.Released, Is.Empty);
        });

        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
    }

    [Test]
    public async Task The_next_operation_after_a_release_activates_OneNote_again()
    {
        await AnyOperation();
        _time.Advance(IdleDelay);

        await AnyOperation();

        Assert.Multiple(() =>
        {
            Assert.That(_com.Created, Has.Count.EqualTo(2));
            Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
        });
    }

    [Test]
    public async Task A_failed_operation_still_releases_OneNote_after_the_idle_delay()
    {
        _com.OnCreate = application => application.GetHierarchyFailures.Enqueue(new InvalidOperationException("boom"));

        Assert.ThrowsAsync<InvalidOperationException>(AnyOperation);
        _time.Advance(IdleDelay);

        Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
    }

    [Test]
    public async Task OneNote_is_released_on_the_sta_thread()
    {
        await AnyOperation();

        _time.Advance(IdleDelay);

        Assert.That(_com.ReleasedOnSta, Is.EqualTo(new[] { true }));
    }

    [Test]
    public async Task With_a_zero_idle_delay_OneNote_is_released_as_soon_as_the_operation_ends()
    {
        UseZeroIdleDelay();

        await AnyOperation();

        Assert.Multiple(() =>
        {
            Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
            Assert.That(_com.ReleasedOnSta, Is.EqualTo(new[] { true }));
        });
    }

    [Test]
    public async Task With_a_zero_idle_delay_a_multi_step_operation_activates_and_releases_once()
    {
        UseZeroIdleDelay();

        await _service.CreatePageAsync("{section}", "Title", string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(_com.Created, Has.Count.EqualTo(1));
            Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
        });
    }

    [Test]
    public async Task Disposing_releases_OneNote_that_is_still_held()
    {
        await AnyOperation();

        _service.Dispose();

        Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
    }

    [Test]
    public async Task Disposing_after_an_idle_release_does_not_release_again()
    {
        await AnyOperation();
        _time.Advance(IdleDelay);

        _service.Dispose();

        Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
    }

    [Test]
    public async Task The_idle_timer_does_not_fire_after_disposal()
    {
        await AnyOperation();
        _service.Dispose();

        _time.Advance(IdleDelay);

        Assert.That(_com.Released, Is.EqualTo(new object[] { _com.Created[0] }));
    }

    [Test]
    public async Task Reading_the_current_page_releases_the_window_objects_it_obtained()
    {
        await _service.GetCurrentPageAsync();

        FakeWindows windows = _com.Created[0].Windows;
        Assert.That(_com.Released, Is.EqualTo(new object[] { windows.CurrentWindow, windows }));
    }

    [Test]
    public async Task The_window_objects_are_released_even_when_OneNote_has_no_active_window()
    {
        _com.OnCreate = application => application.Windows.CurrentWindow.CurrentPageIdFailure = new COMException("no active window");

        await _service.GetCurrentPageAsync();

        FakeWindows windows = _com.Created[0].Windows;
        Assert.That(_com.Released, Is.EqualTo(new object[] { windows.CurrentWindow, windows }));
    }
}
