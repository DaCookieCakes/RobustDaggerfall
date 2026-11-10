namespace Engine.Shared.Objects;

/// <summary>
///		The life-stages of a component.
/// </summary>
public enum ComponentLifeStage : byte
{
	/// <summary>
	///		Has just been allocated.
	/// </summary>
	PreAdd = 0,
	
	/// <summary>
	///		Being added to an entity.
	/// </summary>
	Adding,
	
	/// <summary>
	///		Has been added to an entity.
	/// </summary>
	Added,
	
	/// <summary>
	///		Being initialized.
	/// </summary>
	Initializing,
	
	/// <summary>
	///		Has been initialized.
	/// </summary>
	Initialized,
	
	/// <summary>
	///		Being started up.
	/// </summary>
	Starting,
	
	/// <summary>
	///		Has started up.
	/// </summary>
	Running,
	
	/// <summary>
	///		Being shut down.
	/// </summary>
	Stopping,
	
	/// <summary>
	///		Has been shut down.
	/// </summary>
	Stopped,
	
	/// <summary>
	///		Currently being removed from an entity.
	/// </summary>
	Removing,
	
	/// <summary>
	///		Has been removed from an entity, and is being deleted.
	/// </summary>
	Deleted
}