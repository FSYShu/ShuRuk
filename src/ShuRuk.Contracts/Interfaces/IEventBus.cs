namespace ShuRuk.Contracts.Interfaces;

public interface IEventBus
{
    void Publish<TEvent>(TEvent eventData) where TEvent : class;

    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

    IReadOnlyList<TEvent> GetRecentEvents<TEvent>(int count = 10) where TEvent : class;
}