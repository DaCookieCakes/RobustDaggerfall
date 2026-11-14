using Engine.Shared.Objects;

namespace Engine.Tests;

public class EntityManagerTests
{
	[Fact]
	public void SpawnedEntitiesGetDifferentValidIds()
	{
		var entities = new EntityManager();

		var goblinEnt = entities.Spawn();
		var ratEnt = entities.Spawn();

		Assert.True(goblinEnt.IsValid());
		Assert.NotEqual(goblinEnt, ratEnt);
		Assert.True(entities.EntityExists(goblinEnt));
	}

	[Fact]
	public void AddedComponentCanBeFetched()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();

		entities.AddComponent(goblinEnt, new DamageableComponent { CurrentHealth = 50 });

		Assert.True(entities.HasComponent<DamageableComponent>(goblinEnt));
		Assert.Equal(50, entities.GetComponent<DamageableComponent>(goblinEnt).CurrentHealth);
	}

	[Fact]
	public void ComponentsBelongToOneEntityOnly()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		var ratEnt = entities.Spawn();

		entities.AddComponent(goblinEnt, new DamageableComponent { CurrentHealth = 50 });
		entities.AddComponent(ratEnt, new DamageableComponent { CurrentHealth = 5 });

		Assert.Equal(50, entities.GetComponent<DamageableComponent>(goblinEnt).CurrentHealth);
		Assert.Equal(5, entities.GetComponent<DamageableComponent>(ratEnt).CurrentHealth);
	}

	[Fact]
	public void ComponentStoredByItsRealTypeEvenWhenPassedAsBaseComponent()
	{
		var entities = new EntityManager();
		var goblin = entities.Spawn();
		Component health = new DamageableComponent();

		entities.AddComponent(goblin, health);

		Assert.True(entities.HasComponent<DamageableComponent>(goblin));
	}

	[Fact]
	public void GettingMissingComponentThrows()
	{
		var entities = new EntityManager();
		var goblin = entities.Spawn();

		Assert.Throws<KeyNotFoundException>(() => entities.GetComponent<DamageableComponent>(goblin));
	}

	[Fact]
	public void TryGetMissingComponentReturnsFalse()
	{
		var entities = new EntityManager();
		var goblin = entities.Spawn();

		Assert.False(entities.TryGetComponent<DamageableComponent>(goblin, out var health));
		Assert.Null(health);
	}

	[Fact]
	public void AddingDuplicateComponentThrows()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		
		entities.AddComponent(goblinEnt, new DamageableComponent());

		Assert.Throws<InvalidOperationException>(() => entities.AddComponent(goblinEnt, new DamageableComponent()));
	}

	[Fact]
	public void AddingToNonexistentEntityThrows()
	{
		var entities = new EntityManager();
		
		var ghostComp = new EntityUid(999);

		Assert.Throws<ArgumentException>(() => entities.AddComponent(ghostComp, new DamageableComponent()));
	}

	[Fact]
	public void RemovedComponentIsGoneAndMarkedDeleted()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		var healthComp = new DamageableComponent();
		entities.AddComponent(goblinEnt, healthComp);

		entities.RemoveComponent<DamageableComponent>(goblinEnt);

		Assert.False(entities.HasComponent<DamageableComponent>(goblinEnt));
		Assert.True(healthComp.Deleted);
	}
	
	[Fact]
	public void RemovedComponentCanBeAddedAgain()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		var healthComp = new DamageableComponent();
		
		entities.AddComponent(goblinEnt, healthComp);

		entities.RemoveComponent<DamageableComponent>(goblinEnt);
		Assert.False(entities.HasComponent<DamageableComponent>(goblinEnt));

		var nHealthComp = new DamageableComponent();
		entities.AddComponent(goblinEnt, nHealthComp);
		
		Assert.True(entities.HasComponent<DamageableComponent>(goblinEnt));
	}

	[Fact]
	public void RemovingMissingComponentThrows()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();

		Assert.Throws<KeyNotFoundException>(() => entities.RemoveComponent<DamageableComponent>(goblinEnt));
	}

	[Fact]
	public void RemovingOneTypeLeavesOthers()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		
		entities.AddComponent(goblinEnt, new DamageableComponent());
		entities.AddComponent(goblinEnt, new MetaDataComponent { Name = "Goblin" });
		entities.RemoveComponent<DamageableComponent>(goblinEnt);

		Assert.Equal("Goblin", entities.GetComponent<MetaDataComponent>(goblinEnt).Name);
	}

	[Fact]
	public void RemovingEntityReturnsTrue()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		var ratEnt = entities.Spawn();
		var healthComp = new DamageableComponent();
		
		entities.AddComponent(goblinEnt, healthComp);
		entities.AddComponent(ratEnt, new MetaDataComponent { Name = "Rat" });
		
		Assert.True(entities.RemoveEntity(goblinEnt));
		Assert.True(healthComp.Deleted);
		Assert.True(entities.EntityExists(ratEnt));
	}
}
