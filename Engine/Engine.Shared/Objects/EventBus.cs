namespace Engine.Shared.Objects;

/// <summary>
///		This is the method shape for a directed-event handler.
///		I.E. an entity, its component, and the event.
///		The event is passed by ref, so a handler can change it, and later handlers will see the change.
/// </summary>
public delegate void ComponentEventHandler<in TComp, TEvent>(EntityUid uid, TComp component, ref TEvent args)
	where TComp : Component;

/// <summary>
///		Delivers events to the components of one entity.
/// </summary>
public sealed class EventBus
{
	private readonly EntityManager _entities;

	/// <summary>
	///		Event type as: List->Subscription->Event.
	/// </summary>
	/// <remarks>
	///		The value is stored as object because each event type has a different list.
	///		<see cref="RaiseLocalEvent"/> casts it back.
	///</remarks>
	private readonly Dictionary<Type, object> _subscriptions = new();

	public EventBus(EntityManager entities)
	{
		_entities = entities;
	}

	/// <summary>
	///		The same handler with its component parameter loosened to plain Component,
	///			so handlers for different component types can share one list.
	/// </summary>
	private delegate void UntypedHandler<TEvent>(EntityUid uid, Component component, ref TEvent args);

	/// <summary>
	///		One subscription: which component type it's for, and what to call.
	/// </summary>
	private sealed record Subscription<TEvent>(Type ComponentType, UntypedHandler<TEvent> Handler);

	/// <summary>
	///		Calls <paramref name="handler"/> whenever an Event is raised on an entity that has a Component.
	///		Handlers run in the order they were subscribed.
	/// </summary>
	/// <exception cref="InvalidOperationException">This component type already has a handler for this event.</exception>
	public void SubscribeLocalEvent<TComp, TEvent>(ComponentEventHandler<TComp, TEvent> handler)
		where TComp : Component
	{
		ArgumentNullException.ThrowIfNull(handler);

		var subs = GetOrCreateSubscriptions<TEvent>();

		// This allows one handler per component/event pair, which keeps the "who handles this?" unambiguous.
		if (subs.Exists(s => s.ComponentType == typeof(TComp)))
			throw new InvalidOperationException(
				$"{typeof(TComp).Name} already has a handler for {typeof(TEvent).Name}.");

		// Wrap the typed handler: cast the component back to TComp, then call the real handler.
		// The parameter types have to be written out because of the ref parameter.
		subs.Add(new Subscription<TEvent>(
			typeof(TComp),
			(uid, component, ref args) => handler(uid, (TComp) component, ref args)));
	}

	/// <summary>
	///		Sends an event to every component on <paramref name="uid"/> that has a handler for it.
	///		Changes handlers make to <paramref name="args"/> are visible to later handlers and to the caller.
	/// </summary>
	public void RaiseLocalEvent<TEvent>(EntityUid uid, ref TEvent args)
	{
		if (!_subscriptions.TryGetValue(typeof(TEvent), out var stored))
			return;

		var subs = (List<Subscription<TEvent>>) stored;

		// Why we use a for loop rather than a foreach;
		//	If a handler subscribes something new mid-event, a foreach would throw.
		//	So subscriptions added this way are reached later in this same loop.
		for (var i = 0; i < subs.Count; i++)
		{
			var sub = subs[i];
			if (!_entities.TryGetComponent(uid, sub.ComponentType, out var component) || component.Deleted)
				continue;

			sub.Handler(uid, component, ref args);
		}
	}

	/// <summary>
	///		Same as the Ref RaiseLocalEvent, but without needing to see other handler changes.
	/// </summary>
	public void RaiseLocalEvent<TEvent>(EntityUid uid, TEvent args)
	{
		RaiseLocalEvent(uid, ref args);
	}

	private List<Subscription<TEvent>> GetOrCreateSubscriptions<TEvent>()
	{
		if (!_subscriptions.TryGetValue(typeof(TEvent), out var stored))
		{
			stored = new List<Subscription<TEvent>>();
			_subscriptions[typeof(TEvent)] = stored;
		}

		return (List<Subscription<TEvent>>) stored;
	}
}
