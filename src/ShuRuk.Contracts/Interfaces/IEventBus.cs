namespace ShuRuk.Contracts.Interfaces;

public interface IEventBus
{
    /// <summary>
/// Publishes an event to registered handlers.
/// </summary>
/// <param name="eventData">The event instance to publish.</param>
void Publish<TEvent>(TEvent eventData) where TEvent : class;

    /// <summary>
/// Registers a handler for events of the specified type.
/// </summary>
/// <param name="handler">The handler to invoke when an event of the specified type is published.</param>
void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

    /// <summary>
/// Removes a handler for events of the specified type.
/// </summary>
/// <param name="handler">The handler to remove.</param>
void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

    /// <summary>
/// Retrieves recently published events of the specified type.
/// </summary>
/// <param name="count">The maximum number of events to retrieve.</param>
/// <returns>The most recent events of the specified type.</returns>
IReadOnlyList<TEvent> GetRecentEvents<TEvent>(int count = 10) where TEvent : class;
}