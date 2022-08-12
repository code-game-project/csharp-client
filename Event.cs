namespace CodeGame.Client;

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
    public Task Call(string dataJson);
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
    private Dictionary<Guid, Func<T, Task>> callbacks = new Dictionary<Guid, Func<T, Task>>();

    public Guid AddCallback(Action<T> cb, bool once = false)
    {
        return AddCallback(async (data) => await Task.Run(() => cb(data)), once);
    }

    public Guid AddCallback(Func<T, Task> cb, bool once = false)
    {
        var id = Guid.NewGuid();
        if (once)
        {
            callbacks.Add(id, async (data) =>
            {
                await cb(data);
                RemoveCallback(id);
            });
        }
        else
        {
            callbacks.Add(id, cb);
        }
        return id;
    }

    public async Task Call(string dataJson)
    {
        if (callbacks.Count == 0 && callbacks.Count == 0) return;
        var data = JsonSerializer.Deserialize<Event<T>>(dataJson, Api.JsonOptions);
        if (data == null) return;
        foreach (var entry in callbacks)
        {
            await entry.Value(data.Data);
        }
    }

    public void RemoveCallback(Guid id)
    {
        callbacks.Remove(id);
        callbacks.Remove(id);
    }
}
