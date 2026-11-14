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

		entities.AddComponent(goblinEnt, new HealthComponent { Current = 50 });

		Assert.True(entities.HasComponent<HealthComponent>(goblinEnt));
		Assert.Equal(50, entities.GetComponent<HealthComponent>(goblinEnt).Current);
	}

	[Fact]
	public void ComponentsBelongToOneEntityOnly()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		var ratEnt = entities.Spawn();

		entities.AddComponent(goblinEnt, new HealthComponent { Current = 50 });
		entities.AddComponent(ratEnt, new HealthComponent { Current = 5 });

		Assert.Equal(50, entities.GetComponent<HealthComponent>(goblinEnt).Current);
		Assert.Equal(5, entities.GetComponent<HealthComponent>(ratEnt).Current);
	}

	[Fact]
	public void ComponentStoredByItsRealTypeEvenWhenPassedAsBaseComponent()
	{
		var entities = new EntityManager();
		var goblin = entities.Spawn();
		Component health = new HealthComponent();

		entities.AddComponent(goblin, health);

		Assert.True(entities.HasComponent<HealthComponent>(goblin));
	}

	[Fact]
	public void GettingMissingComponentThrows()
	{
		var entities = new EntityManager();
		var goblin = entities.Spawn();

		Assert.Throws<KeyNotFoundException>(() => entities.GetComponent<HealthComponent>(goblin));
	}

	[Fact]
	public void TryGetMissingComponentReturnsFalse()
	{
		var entities = new EntityManager();
		var goblin = entities.Spawn();

		Assert.False(entities.TryGetComponent<HealthComponent>(goblin, out var health));
		Assert.Null(health);
	}

	[Fact]
	public void AddingDuplicateComponentThrows()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		
		entities.AddComponent(goblinEnt, new HealthComponent());

		Assert.Throws<InvalidOperationException>(() => entities.AddComponent(goblinEnt, new HealthComponent()));
	}

	[Fact]
	public void AddingToNonexistentEntityThrows()
	{
		var entities = new EntityManager();
		
		var ghostComp = new EntityUid(999);

		Assert.Throws<ArgumentException>(() => entities.AddComponent(ghostComp, new HealthComponent()));
	}

	[Fact]
	public void RemovedComponentIsGoneAndMarkedDeleted()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		var healthComp = new HealthComponent();
		entities.AddComponent(goblinEnt, healthComp);

		entities.RemoveComponent<HealthComponent>(goblinEnt);

		Assert.False(entities.HasComponent<HealthComponent>(goblinEnt));
		Assert.True(healthComp.Deleted);
	}
	
	[Fact]
	public void RemovedComponentCanBeAddedAgain()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		var healthComp = new HealthComponent();
		
		entities.AddComponent(goblinEnt, healthComp);

		entities.RemoveComponent<HealthComponent>(goblinEnt);
		Assert.False(entities.HasComponent<HealthComponent>(goblinEnt));

		var nHealthComp = new HealthComponent();
		entities.AddComponent(goblinEnt, nHealthComp);
		
		Assert.True(entities.HasComponent<HealthComponent>(goblinEnt));
	}

	[Fact]
	public void RemovingMissingComponentThrows()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();

		Assert.Throws<KeyNotFoundException>(() => entities.RemoveComponent<HealthComponent>(goblinEnt));
	}

	[Fact]
	public void RemovingOneTypeLeavesOthers()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		
		entities.AddComponent(goblinEnt, new HealthComponent());
		entities.AddComponent(goblinEnt, new NameComponent { Name = "Goblin" });
		entities.RemoveComponent<HealthComponent>(goblinEnt);

		Assert.Equal("Goblin", entities.GetComponent<NameComponent>(goblinEnt).Name);
	}

	[Fact]
	public void RemovingEntityReturnsTrue()
	{
		var entities = new EntityManager();
		
		var goblinEnt = entities.Spawn();
		var ratEnt = entities.Spawn();
		var healthComp = new HealthComponent();
		
		entities.AddComponent(goblinEnt, healthComp);
		entities.AddComponent(ratEnt, new NameComponent { Name = "Rat" });
		
		Assert.True(entities.RemoveEntity(goblinEnt));
		Assert.True(healthComp.Deleted);
		Assert.True(entities.EntityExists(ratEnt));
	}
}
