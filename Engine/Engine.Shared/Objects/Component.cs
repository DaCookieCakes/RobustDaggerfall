namespace Engine.Shared.Objects;

public abstract partial class Component
{
	public ComponentLifeStage LifeStage { get; internal set; } = ComponentLifeStage.PreAdd;
	
	///	<returns>true if the Component has been removed from its entity</returns>
	public bool Deleted => LifeStage >= ComponentLifeStage.Removing;
}