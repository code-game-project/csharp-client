namespace CodeGame;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Base class of all event data objects.
/// </summary>
public abstract class EventData { }
/// <summary>
/// Base class of all command data objects.
/// </summary>
public abstract class CommandData { }

internal interface IEventCallbacks
{
    public void Call(string dataJson);
    public void RemoveCallback(Guid id);
}

internal class Event<T> where T : EventData
{
    public string Name { get; set; }
    public T Data { get; set; }

    [JsonConstructor]
    public Event(string name, T data)
    {
        this.Name = name;
        this.Data = data;
    }
}

internal class Command<T> where T : CommandData
{
    public string Name { get; set; }
    public T Data { get; set; }

    [JsonConstructor]
    public Command(string name, T data)
    {
        this.Name = name;
        this.Data = data;
    }
}

internal class EventCallbacks<T> : IEventCallbacks where T : EventData
{
    private Dictionary<Guid, Action<T>> callbacks = new Dictionary<Guid, Action<T>>();
    private Dictionary<Guid, Func<T, Task>> asyncCallbacks = new Dictionary<Guid, Func<T, Task>>();

    public Guid AddCallback(Action<T> cb, bool once = false)
    {
        var id = Guid.NewGuid();
        if (once)
        {
            callbacks.Add(id, (data) =>
            {
                cb(data);
                RemoveCallback(id);
            });
        }
        else
        {
            callbacks.Add(id, cb);
        }
        return id;
    }

    public Guid AddCallback(Func<T, Task> cb, bool once = false)
    {
        var id = Guid.NewGuid();
        if (once)
        {
            asyncCallbacks.Add(id, async (data) =>
            {
                await cb(data);
                RemoveCallback(id);
            });
        }
        else
        {
            asyncCallbacks.Add(id, cb);
        }
        return id;
    }

    public void Call(string dataJson)
    {
        if (callbacks.Count == 0 && asyncCallbacks.Count == 0) return;
        var data = JsonSerializer.Deserialize<Event<T>>(dataJson, Api.JsonOptions);
        if (data == null) return;
        foreach (var entry in callbacks)
        {
            entry.Value(data.Data);
        }
        foreach (var entry in asyncCallbacks)
        {
            entry.Value(data.Data).Wait();
        }
    }

    public void RemoveCallback(Guid id)
    {
        callbacks.Remove(id);
        asyncCallbacks.Remove(id);
    }
}
