using Engine.Shared.Objects;

namespace Engine.Tests;

// TODO: Replace these with proper systems later.
public class EventBusTests
{
	private static void TakeDamage(EntityUid uid, DamageableComponent damageable, ref DamageEvent args)
	{
		damageable.CurrentHealth -= args.Amount;
	}

	private static void BlockDamage(EntityUid uid, DamageBlockerComponent seal, ref DamageEvent args)
	{
		if (args.DamageType != seal.VulnerableTo)
			args.Amount = 0;
	}

	[Fact]
	public void EventReachesComponentWithHandler()
	{
		var entities = new EntityManager();
		var bus = new EventBus(entities);
		bus.SubscribeLocalEvent<DamageableComponent, DamageEvent>(TakeDamage);

		var door = entities.Spawn();
		var health = new DamageableComponent { CurrentHealth = 50 };
		entities.AddComponent(door, health);

		var ev = new DamageEvent(20, "Slashing");
		bus.RaiseLocalEvent(door, ref ev);

		Assert.Equal(30, health.CurrentHealth);
	}

	[Fact]
	public void EventSkipsEntityWithoutTheComponent()
	{
		var entities = new EntityManager();
		var bus = new EventBus(entities);
		var called = false;
		bus.SubscribeLocalEvent((EntityUid _, DamageableComponent _, ref DamageEvent _) => called = true);

		var rock = entities.Spawn();
		entities.AddComponent(rock, new MetaDataComponent { Name = "Rock" });

		var ev = new DamageEvent(20, "Slashing");
		bus.RaiseLocalEvent(rock, ref ev);

		Assert.False(called);
	}

	[Fact]
	public void EventOnlyReachesTheTargetEntity()
	{
		var entities = new EntityManager();
		var bus = new EventBus(entities);
		bus.SubscribeLocalEvent<DamageableComponent, DamageEvent>(TakeDamage);

		var goblin = entities.Spawn();
		var rat = entities.Spawn();
		var goblinHealth = new DamageableComponent { CurrentHealth = 50 };
		var ratHealth = new DamageableComponent { CurrentHealth = 50 };
		entities.AddComponent(goblin, goblinHealth);
		entities.AddComponent(rat, ratHealth);

		var ev = new DamageEvent(20, "Slashing");
		bus.RaiseLocalEvent(goblin, ref ev);

		Assert.Equal(30, goblinHealth.CurrentHealth);
		Assert.Equal(50, ratHealth.CurrentHealth);
	}

	[Fact]
	public void EarlierHandlerCanChangeEventForLaterOnes()
	{
		var entities = new EntityManager();
		var bus = new EventBus(entities);
		
		bus.SubscribeLocalEvent<DamageBlockerComponent, DamageEvent>(BlockDamage);
		bus.SubscribeLocalEvent<DamageableComponent, DamageEvent>(TakeDamage);

		var door = entities.Spawn();
		var health = new DamageableComponent { CurrentHealth = 50 };
		entities.AddComponent(door, health);
		entities.AddComponent(door, new DamageBlockerComponent { VulnerableTo = "Fire" });

		var slash = new DamageEvent(20, "Slashing");
		bus.RaiseLocalEvent(door, ref slash);
		Assert.Equal(50, health.CurrentHealth);
		Assert.Equal(0, slash.Amount);

		var fire = new DamageEvent(20, "Fire");
		bus.RaiseLocalEvent(door, ref fire);
		Assert.Equal(30, health.CurrentHealth);
	}

	[Fact]
	public void RaisingWithNoSubscribersDoesNothing()
	{
		var entities = new EntityManager();
		var bus = new EventBus(entities);
		var door = entities.Spawn();

		var ev = new DamageEvent(20, "Slashing");
		bus.RaiseLocalEvent(door, ref ev);

		Assert.Equal(20, ev.Amount);
	}

	[Fact]
	public void ComponentRemovedByEarlierHandlerIsSkipped()
	{
		var entities = new EntityManager();
		var bus = new EventBus(entities);
		
		bus.SubscribeLocalEvent((EntityUid uid, DamageBlockerComponent _, ref DamageEvent _) 
			=> entities.RemoveComponent<DamageableComponent>(uid));
		bus.SubscribeLocalEvent<DamageableComponent, DamageEvent>(TakeDamage);

		var door = entities.Spawn();
		var health = new DamageableComponent { CurrentHealth = 50 };
		entities.AddComponent(door, health);
		entities.AddComponent(door, new DamageBlockerComponent());

		var ev = new DamageEvent(20, "Slashing");
		bus.RaiseLocalEvent(door, ref ev);

		Assert.Equal(50, health.CurrentHealth);
	}

	[Fact]
	public void SubscribingTwiceForSamePairThrows()
	{
		var entities = new EntityManager();
		var bus = new EventBus(entities);
		
		bus.SubscribeLocalEvent<DamageableComponent, DamageEvent>(TakeDamage);

		Assert.Throws<InvalidOperationException>(() =>
			bus.SubscribeLocalEvent<DamageableComponent, DamageEvent>(TakeDamage));
	}
	
	[Fact]
	public void BreakingDoorComponentPasses()
	{
		// Register the EntMan and EvBus.
		var entMan = new EntityManager();
		var evBus = new EventBus(entMan);

		// Subscribe the events.
		// Damageable gets the Damage Event, and Breakable gets the BreakEvent.
		evBus.SubscribeLocalEvent((EntityUid uid, DamageableComponent damageableComp, ref DamageEvent args) =>
		{
			damageableComp.CurrentHealth -= args.Amount;
			if (damageableComp.CurrentHealth <= 0)
			{
				evBus.RaiseLocalEvent(uid, new BreakEvent());
			}
		});
		
		evBus.SubscribeLocalEvent((EntityUid _, BreakableComponent breakableComp, ref BreakEvent _) => 
			breakableComp.Broken = true);

		// Register the entity and its components.
		var doorEnt = entMan.Spawn();
		var breakableComp = new BreakableComponent();
		var damageableComp = new DamageableComponent { CurrentHealth = 50 };
		entMan.AddComponent(doorEnt, breakableComp);
		entMan.AddComponent(doorEnt, damageableComp);

		// Raise the events - two damage events to add up to 50,
		//	and check for if its broken each time.
		var ev = new DamageEvent(25, "Slashing");
		evBus.RaiseLocalEvent(doorEnt, ref ev);
		Assert.False(entMan.GetComponent<BreakableComponent>(doorEnt).Broken);
		
		evBus.RaiseLocalEvent(doorEnt, ref ev);
		Assert.True(entMan.GetComponent<BreakableComponent>(doorEnt).Broken);
	}
}
