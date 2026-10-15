using OneNoteMcp.Core.Interop;

namespace OneNoteMcp.Tests;

internal sealed class FakeComObjects(InlineStaThreadRunner sta) : IComObjects
{
    public List<FakeApplication> Created { get; } = [];

    public List<object> Released { get; } = [];

    public List<bool> ReleasedOnSta { get; } = [];

    public Action<FakeApplication> OnCreate { get; set; } = _ => { };

    public IApplication CreateApplication()
    {
        FakeApplication application = new();
        OnCreate(application);
        Created.Add(application);
        return application;
    }

    public void Release(object comObject)
    {
        Released.Add(comObject);
        ReleasedOnSta.Add(sta.IsRunningWork);
    }
}
