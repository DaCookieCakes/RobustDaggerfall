namespace Engine.Shared.Objects;

/// <summary>
///		Creates entities and stores their components.
/// </summary>
public partial class EntityManager
{
	protected int NextEntityUid = EntityUid.FirstUid.Id;

	/// <summary>
	///		All currently existing entities.
	/// </summary>
	protected readonly HashSet<EntityUid> Entities = new();

	/// <summary>
	///		Component storage: one inner dictionary per component type.
	/// </summary>
	private readonly Dictionary<Type, Dictionary<EntityUid, Component>> _components = new();

	/// <summary>
	///		Creates a new entity with no components, and returns its UID.
	/// </summary>
	public EntityUid Spawn()
	{
		var uid = new EntityUid(NextEntityUid++);
		Entities.Add(uid);
		return uid;
	}

	/// <summary>
	///		Whether the entity currently exists.
	/// </summary>
	/// <param name="uid">The UID to check has an entity.</param>
	public bool EntityExists(EntityUid uid) => Entities.Contains(uid);

	/// <summary>
	///		Attaches a component to an entity.
	/// </summary>
	/// <param name="uid">The UID to add a component to.</param>
	/// <param name="component">The component to add.</param>
	/// <exception cref="ArgumentException">The entity doesn't exist.</exception>
	/// <exception cref="InvalidOperationException">The entity already has a component of this type.</exception>
	public void AddComponent<T>(EntityUid uid, T component) where T : Component
	{
		if (!EntityExists(uid))
			throw new ArgumentException($"Entity {uid} is not valid.", nameof(uid));

		ArgumentNullException.ThrowIfNull(component);

		// GetType(), not typeof(T): if the caller's variable is typed as plain Component,
		// then T would be a component and the component would be filed under the wrong type.
		var type = component.GetType();
		var dict = GetOrCreateStorage(type);

		if (!dict.TryAdd(uid, component))
			throw new InvalidOperationException($"Entity {uid} already has a {type.Name}.");

		component.LifeStage = ComponentLifeStage.Running;
	}

	/// <summary>
	///		Whether the entity has a component of type T.
	/// </summary>
	/// <param name="uid">The UID to check for a component.</param>
	public bool HasComponent<T>(EntityUid uid) where T : Component
	{
		return _components.TryGetValue(typeof(T), out var dict) && dict.ContainsKey(uid);
	}

	/// <summary>
	///		Gets the entity's component of type T.
	/// </summary>
	/// <param name="uid">The UID to check for a component.</param>
	/// <exception cref="KeyNotFoundException">The entity has no component of this type.</exception>
	public T GetComponent<T>(EntityUid uid) where T : Component
	{
		if (TryGetComponent<T>(uid, out var component))
			return component;

		throw new KeyNotFoundException($"Entity {uid} has no {typeof(T).Name}.");
	}

	/// <summary>
	///		Gets the entity's component of type T if it has one.
	/// </summary>
	/// <param name="uid">The UID to check for the component.</param>
	/// <param name="component">The Component to check for.</param>
	/// <returns>true if found</returns>
	public bool TryGetComponent<T>(EntityUid uid, 
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? component) where T : Component
	{
		if (_components.TryGetValue(typeof(T), out var dict) && dict.TryGetValue(uid, out var found))
		{
			component = (T) found;
			return true;
		}

		component = null;
		return false;
	}

	/// <summary>
	///		Removes the entity's component of type T, if it has one.
	/// </summary>
	/// <param name="uid">The UID to remove a component from.</param>
	/// <exception cref="KeyNotFoundException">Thrown if a component could not be removed</exception>
	public void RemoveComponent<T>(EntityUid uid) where T : Component
	{
		if (!_components.TryGetValue(typeof(T), out var dict) || !dict.Remove(uid, out var component))
			throw new KeyNotFoundException($"Attempted to remove a component that an entity with UID {uid} did not have");
		
		component.LifeStage = ComponentLifeStage.Deleted;
	}

	/// <summary>
	///		Gets the inner dictionary for a component type, creating it the first time that type is used.
	/// </summary>
	private Dictionary<EntityUid, Component> GetOrCreateStorage(Type type)
	{
		if (!_components.TryGetValue(type, out var dict))
		{
			dict = new Dictionary<EntityUid, Component>();
			_components[type] = dict;
		}

		return dict;
	}

	/// <summary>
	///		Removes an entity from the list and clears all of its components.
	/// </summary>
	/// <param name="uid">The uid to clear.</param>
	/// <returns>True if an entity was removed</returns>
	public bool RemoveEntity(EntityUid uid)
	{
		foreach (var (_, dict) in _components)
		{
			if (dict.Remove(uid, out var comp))
				comp.LifeStage = ComponentLifeStage.Deleted;
		}

		return Entities.Remove(uid);
	}
}
