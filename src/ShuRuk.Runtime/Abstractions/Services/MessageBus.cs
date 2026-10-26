using System.Collections.Concurrent;
using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.Runtime.Abstractions.Services;

// In-process message bus implementing IEventBus / 进程内消息总线，实现IEventBus
// IPC transport (named pipe / AppService) to be added for cross-process / 跨进程IPC传输待补
public sealed class MessageBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<HandlerEntry>> _handlers = new();
    private readonly ConcurrentDictionary<Type, Queue<object>> _recent = new();
    private const int MaxRecent = 100;

    public void Publish<TEvent>(TEvent eventData) where TEvent : class
    {
        var t = typeof(TEvent);
        if (_handlers.TryGetValue(t, out var list))
        {
            HandlerEntry[] snap;
            lock (list) snap = list.ToArray();
            foreach (var e in snap) e.Wrapper(eventData!);
        }
        var q = _recent.GetOrAdd(t, _ => new Queue<object>());
        lock (q) { q.Enqueue(eventData!); while (q.Count > MaxRecent) q.Dequeue(); }
    }

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<HandlerEntry>());
        lock (list) list.Add(new HandlerEntry(handler, o => handler((TEvent)o)));
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var list)) return;
        lock (list) list.RemoveAll(e => e.Original.Equals(handler));
    }

    public IReadOnlyList<TEvent> GetRecentEvents<TEvent>(int count = 10) where TEvent : class
    {
        if (!_recent.TryGetValue(typeof(TEvent), out var q)) return Array.Empty<TEvent>();
        lock (q) return q.ToArray().Take(count).Cast<TEvent>().ToList();
    }

    private sealed class HandlerEntry(Delegate original, Action<object> wrapper)
    {
        public Delegate Original { get; } = original;
        public Action<object> Wrapper { get; } = wrapper;
    }
}