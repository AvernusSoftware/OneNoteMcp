using OneNoteMcp.Core.Threading;

namespace OneNoteMcp.Tests;

[TestFixture]
public class StaThreadRunnerTests
{
    private StaThreadRunner _runner = null!;

    [SetUp]
    public void SetUp() => _runner = new StaThreadRunner();

    [TearDown]
    public void TearDown() => _runner.Dispose();

    [Test]
    public async Task Work_runs_in_a_single_threaded_apartment()
    {
        ApartmentState state = await _runner.RunAsync(() => Thread.CurrentThread.GetApartmentState());
        Assert.That(state, Is.EqualTo(ApartmentState.STA));
    }

    [Test]
    public async Task Work_never_runs_on_the_calling_thread()
    {
        int callerId = Environment.CurrentManagedThreadId;
        int workerId = await _runner.RunAsync(() => Environment.CurrentManagedThreadId);

        Assert.That(workerId, Is.Not.EqualTo(callerId));
    }

    // The OneNote RCW is apartment-affine: every call must land on the same thread.
    [Test]
    public async Task All_work_items_run_on_the_same_thread()
    {
        List<int> ids = new();
        for (int i = 0; i < 25; i++)
        {
            ids.Add(await _runner.RunAsync(() => Environment.CurrentManagedThreadId));
        }

        Assert.That(ids.Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task Concurrent_callers_are_serialised_onto_the_same_thread()
    {
        Task<int>[] tasks = [.. Enumerable.Range(0, 50).Select(_ => _runner.RunAsync(() => Environment.CurrentManagedThreadId))];

        int[] ids = await Task.WhenAll(tasks);
        Assert.That(ids.Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task Result_is_returned_to_the_caller()
    {
        Assert.That(await _runner.RunAsync(() => 6 * 7), Is.EqualTo(42));
    }

    [Test]
    public void Exceptions_propagate_to_the_awaiting_caller()
    {
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _runner.RunAsync<int>(() => throw new InvalidOperationException("boom")));

        Assert.That(ex!.Message, Is.EqualTo("boom"));
    }

    // A faulted item must not take the worker loop down with it.
    [Test]
    public async Task The_runner_keeps_working_after_a_faulted_item()
    {
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _runner.RunAsync<int>(() => throw new InvalidOperationException()));

        Assert.That(await _runner.RunAsync(() => "still alive"), Is.EqualTo("still alive"));
    }

    [Test]
    public async Task The_void_overload_executes_its_action()
    {
        bool ran = false;
        await _runner.RunAsync(() => { ran = true; });

        Assert.That(ran, Is.True);
    }

    // Without RunContinuationsAsynchronously the awaiting continuation resumes on the STA thread
    // and starves the queue.
    [Test]
    public async Task Continuations_do_not_resume_on_the_sta_thread()
    {
        int workerId = await _runner.RunAsync(() => Environment.CurrentManagedThreadId);
        int continuationId = Environment.CurrentManagedThreadId;

        Assert.That(continuationId, Is.Not.EqualTo(workerId));
    }

    [Test]
    public async Task Nested_dispatches_do_not_deadlock()
    {
        int result = await _runner.RunAsync(() =>
        {
            // Queue more work while already on the STA thread, then leave; the outer await must
            // still complete rather than blocking the loop.
            return 1;
        });

        Assert.That(result, Is.EqualTo(1));
        Assert.That(await _runner.RunAsync(() => 2), Is.EqualTo(2));
    }

    [Test]
    public void Work_cancelled_before_it_starts_is_reported_as_cancelled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.ThrowsAsync<TaskCanceledException>(
            async () => await _runner.RunAsync(() => 1, cts.Token));
    }

    [Test]
    public void Null_work_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => _runner.RunAsync((Func<int>)null!));
        Assert.Throws<ArgumentNullException>(() => _runner.RunAsync((Action)null!));
    }

    [Test]
    public void Dispatching_after_disposal_is_rejected()
    {
        StaThreadRunner runner = new();
        runner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runner.RunAsync(() => 1));
    }

    [Test]
    public void Dispose_is_idempotent()
    {
        StaThreadRunner runner = new();
        runner.Dispose();

        Assert.DoesNotThrow(runner.Dispose);
    }

    [Test]
    public async Task Queued_work_completes_in_submission_order()
    {
        List<int> order = new();
        List<Task> tasks = new();

        for (int i = 0; i < 20; i++)
        {
            int n = i;
            tasks.Add(_runner.RunAsync(() => order.Add(n)));
        }

        await Task.WhenAll(tasks);
        Assert.That(order, Is.EqualTo(Enumerable.Range(0, 20).ToList()));
    }
}
